namespace Elasticstretch.Logging.Filebeat;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using System.Buffers;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;

/// <summary>
/// A provider of loggers for use with Filebeat.
/// </summary>
/// <remarks>
/// Log entries are documents satisfying the Elastic Common Schema, written to the filesystem as UTF-8 newline-delimited JSON.
/// </remarks>
[ProviderAlias("Filebeat")]
public class FilebeatLoggerProvider : ILoggerProvider, IAsyncDisposable
{
    static readonly byte[] Delimiter = Encoding.UTF8.GetBytes(Environment.NewLine);
    static readonly JsonEncodedText EcsVersion = JsonEncodedText.Encode(ElasticSchema.Version);

    // Use same fallback as Host
    // https://github.com/dotnet/runtime/blob/7a0b4f99a3e90d24e152e5e077839971e7678cfd/src/libraries/Microsoft.Extensions.Hosting/src/HostBuilder.cs#L240
    static readonly string FallbackAppName = Assembly.GetEntryAssembly()?.GetName()?.Name ?? "dotnet";

    static readonly FileStreamOptions StreamOptions =
        new()
        {
            Mode = FileMode.Append,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
            BufferSize = 0,
        };

    readonly IOptionsMonitor<FileLoggingOptions> fileOptions;
    readonly IHostEnvironment? environment;

    readonly BufferBlock<IElasticEntry> buffer = new();
    readonly LogLocal<IElasticEntry> scopes = new();

    Task? flushing;

    /// <summary>
    /// Initializes the provider.
    /// </summary>
    /// <param name="fileOptions">The file logging options.</param>
    /// <param name="environment">The host environment, if any.</param>
    public FilebeatLoggerProvider(IOptionsMonitor<FileLoggingOptions> fileOptions, IHostEnvironment? environment = null)
    {
        this.fileOptions = fileOptions;
        this.environment = environment;
    }

    /// <inheritdoc/>
    public virtual ILogger CreateLogger(string categoryName)
    {
        lock (buffer)
        {
            if (buffer.Completion.IsCompleted)
            {
                throw GetException();
            }

            if (flushing == null)
            {
                flushing = SerializeLoopAsync();

                flushing.ContinueWith(
                    x => ((IDataflowBlock)buffer).Fault(x.Exception!),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }
        }

        var fields = CreateEntry();
        WriteStatic(fields, categoryName);

        return new FilebeatLogger(this, categoryName, fields);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        Dispose(disposing: false);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Writes static document fields for all log entries.
    /// </summary>
    /// <param name="writer">The field writer.</param>
    /// <param name="category">The log category name.</param>
    protected virtual void WriteStatic(IElasticFieldWriter writer, string category)
    {
        ArgumentNullException.ThrowIfNull(writer, nameof(writer));

        writer.Begin(ElasticSchema.Fields.LogLogger).WriteStringValue(category);
        writer.Begin(ElasticSchema.Fields.EcsVersion).WriteStringValue(EcsVersion);
    }

    /// <summary>
    /// Writes document fields for a log scope.
    /// </summary>
    /// <typeparam name="TState">The scope state type.</typeparam>
    /// <param name="writer">The field writer.</param>
    /// <param name="category">The log category name.</param>
    /// <param name="state">The log scope state.</param>
    protected virtual void WriteScope<TState>(IElasticFieldWriter writer, string category, TState state)
        where TState : notnull
    {
    }

    /// <summary>
    /// Writes document fields for a log entry.
    /// </summary>
    /// <typeparam name="TState">The entry state type.</typeparam>
    /// <param name="writer">The field writer.</param>
    /// <param name="entry">The log entry.</param>
    protected virtual void WriteEntry<TState>(IElasticFieldWriter writer, in LogEntry<TState> entry)
    {
        ArgumentNullException.ThrowIfNull(writer, nameof(writer));

        writer.Begin(ElasticSchema.Fields.Timestamp).WriteStringValue(DateTimeOffset.Now);
        writer.Begin(ElasticSchema.Fields.LogLevel).WriteNumberValue((int)entry.LogLevel);
        writer.Begin(ElasticSchema.Fields.Message).WriteStringValue(entry.Formatter(entry.State, entry.Exception));

        if (entry.EventId != default)
        {
            var eventField = writer.Begin(ElasticSchema.Fields.Event);

            eventField.WriteStartObject();
            eventField.WriteNumber(ElasticSchema.Fields.Code, entry.EventId.Id);

            if (entry.EventId.Name != null)
            {
                eventField.WriteString(ElasticSchema.Fields.Action, entry.EventId.Name);
            }

            eventField.WriteEndObject();
        }

        WriteExceptions(writer, entry.Exception);
    }

    /// <summary>
    /// Writes document fields for a logged exception.
    /// </summary>
    /// <remarks>
    /// Applies to each exception included in a log entry, including inner exceptions.
    /// </remarks>
    /// <param name="writer">The field writer.</param>
    /// <param name="exception">The logged exception.</param>
    protected virtual void WriteException(IElasticFieldWriter writer, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(writer, nameof(writer));
        ArgumentNullException.ThrowIfNull(exception, nameof(exception));

        var errorField = writer.Begin(ElasticSchema.Fields.Error);

        errorField.WriteStartObject();
        errorField.WriteString(ElasticSchema.Fields.Message, exception.Message);
        errorField.WriteString(ElasticSchema.Fields.Type, exception.GetType().ToString());

        if (exception.StackTrace != null)
        {
            errorField.WriteString(ElasticSchema.Fields.StackTrace, exception.StackTrace);
        }

        errorField.WriteEndObject();
    }

    /// <summary>
    /// Writes document fields for a log property.
    /// </summary>
    /// <remarks>
    /// Applies to log entry and scope state implementing <see cref="IReadOnlyList{T}"/>
    /// of <see cref="KeyValuePair{TKey, TValue}"/> of <see cref="string"/> and <see cref="object"/>
    /// (e.g. message template arguments).
    /// </remarks>
    /// <param name="writer">The field writer.</param>
    /// <param name="category">The log category name.</param>
    /// <param name="name">The log property name.</param>
    /// <param name="value">The log property value.</param>
    protected virtual void WriteProperty(IElasticFieldWriter writer, string category, string name, object? value)
    {
    }

    /// <summary>
    /// Saves buffered log data to the filesystem.
    /// </summary>
    /// <remarks>
    /// Handles <see cref="IOException"/> by writing to <see cref="Console.Error"/>.
    /// Override to implement different behavior.
    /// </remarks>
    /// <param name="path">The path to the output log file.</param>
    /// <param name="data">The log data.</param>
    /// <returns>A task for the save operation.</returns>
    protected virtual async Task SaveAsync(string path, ReadOnlyMemory<byte> data)
    {
        ArgumentNullException.ThrowIfNull(path, nameof(path));
        using var file = File.Open(path, StreamOptions);

        try
        {
            await file.WriteAsync(data).ConfigureAwait(false);
        }
        catch (IOException exception)
        {
            // File might be locked by another process.
            await Console.Error.WriteLineAsync($"Error flushing logs: {exception}").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Disposes the instance asynchronously.
    /// </summary>
    /// <returns>A task for the dispose operation.</returns>
    protected virtual ValueTask DisposeAsyncCore()
    {
        lock (buffer)
        {
            buffer.Complete();
        }

        return flushing != null ? new(flushing) : default;
    }

    /// <summary>
    /// Disposes and/or finalizes the instance.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> to dispose and finalize, <see langword="false"/> to finalize only.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (buffer)
            {
                buffer.Complete();
            }

            flushing?.Wait();
        }
    }

    private protected virtual IElasticEntry CreateEntry()
    {
        return new ElasticEntry();
    }

    void WriteExceptions(IElasticFieldWriter writer, Exception? exception)
    {
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                WriteExceptions(writer, inner);
            }
        }
        else if (exception != null)
        {
            WriteException(writer, exception);
            WriteExceptions(writer, exception.InnerException);
        }
    }

    async Task SerializeLoopAsync()
    {
        var formatArgs = environment != null
            ? new object[] { environment.ApplicationName, environment.EnvironmentName }
            : new object[] { FallbackAppName, "Production" };

        var output = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(output);

        while (await buffer.OutputAvailableAsync(CancellationToken.None).ConfigureAwait(false))
        {
            var opts = fileOptions.CurrentValue;

            using (var cancellation = new CancellationTokenSource(opts.BufferInterval))
            {
                Serialize(buffer, writer);

                while (output.WrittenCount < opts.BufferSize && !cancellation.IsCancellationRequested)
                {
                    try
                    {
                        await buffer.OutputAvailableAsync(cancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    Serialize(buffer, writer);
                }
            }

            var path = Path.Combine(
                AppContext.BaseDirectory,
                string.Format(CultureInfo.InvariantCulture, opts.Path, formatArgs));

            await SaveAsync(path, output.WrittenMemory).ConfigureAwait(false);
        }

        static void Serialize(IReceivableSourceBlock<IElasticEntry> entries, Utf8JsonWriter writer)
        {
            while (entries.TryReceive(out var entry))
            {
                writer.WriteStartObject();

                for (var i = 0; i < entry.FieldCount; i++)
                {
                    var fields = entry.GetFields(0, out var name);

                    writer.WritePropertyName(name);
                    writer.WriteStartArray();

                    for (var j = 0; j < fields.Count; j++)
                    {
                        fields[j].WriteTo(writer);
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
                writer.WriteRawValue(Delimiter, skipInputValidation: true);
            }
        }
    }

    Exception GetException()
    {
        if (buffer.Completion.Exception != null)
        {
            return buffer.Completion.Exception.GetBaseException();
        }

        return new ObjectDisposedException(GetType().FullName);
    }

    sealed class FilebeatLogger : ILogger
    {
        readonly FilebeatLoggerProvider provider;
        readonly string category;
        readonly IElasticEntry fields;

        public FilebeatLogger(FilebeatLoggerProvider provider, string category, IElasticEntry fields)
        {
            this.provider = provider;
            this.category = category;
            this.fields = fields;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            var entry = provider.CreateEntry();
            provider.WriteScope(entry, category, state);
            return entry.FieldCount > 0 ? provider.scopes.Add(entry) : null;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var entry = provider.CreateEntry();

            entry.Merge(fields);

            if (provider.scopes != null)
            {
                for (var i = 0; i < provider.scopes.Count; i++)
                {
                    entry.Merge(provider.scopes[i]);
                }
            }

            provider.WriteEntry<TState>(entry, new(logLevel, category, eventId, state, exception, formatter));

            if (entry.FieldCount > 0 && !provider.buffer.Post(entry))
            {
                throw provider.GetException();
            }
        }
    }
}
