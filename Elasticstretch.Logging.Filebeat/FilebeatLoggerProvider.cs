namespace Elasticstretch.Logging.Filebeat;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using System.Buffers;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

/// <summary>
/// A provider of loggers for use with Filebeat.
/// </summary>
/// <remarks>
/// Log entries are documents satisfying the Elastic Common Schema, written to the filesystem as UTF-8 newline-delimited JSON.
/// </remarks>
[ProviderAlias("Filebeat")]
public class FilebeatLoggerProvider : ILoggerProvider, IAsyncDisposable
{
    static readonly JsonEncodedText EcsVersion = JsonEncodedText.Encode(ElasticSchema.Version);

    // Use same fallback as Host
    // https://github.com/dotnet/runtime/blob/7a0b4f99a3e90d24e152e5e077839971e7678cfd/src/libraries/Microsoft.Extensions.Hosting/src/HostBuilder.cs#L240
    static readonly string FallbackAppName = Assembly.GetEntryAssembly()?.GetName()?.Name ?? "dotnet";

    static readonly FileStreamOptions StreamOptions =
        new()
        {
            Mode = FileMode.Append,
            Access = FileAccess.Write,
            Share = FileShare.ReadWrite,
            Options = FileOptions.Asynchronous,
            BufferSize = 0,
        };

    readonly IOptionsMonitor<FileLoggingOptions> fileOptions;
    readonly IHostEnvironment? environment;

    readonly LogSerializer serializer = new();
    readonly LogLocal<ElasticEntry> scopes = new();

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
        if (serializer.TryStart())
        {
            Volatile.Write(ref flushing, SerializeLoopAsync());
        }

        var fields = new ElasticEntry();
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

        using (var field = writer.Begin(ElasticSchema.Fields.LogLogger))
        {
            field.WriteStringValue(category);
        }

        using (var field = writer.Begin(ElasticSchema.Fields.EcsVersion))
        {
            field.WriteStringValue(EcsVersion);
        }
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

        using (var field = writer.Begin(ElasticSchema.Fields.Timestamp))
        {
            field.WriteStringValue(DateTimeOffset.UtcNow);
        }

        using (var field = writer.Begin(ElasticSchema.Fields.LogLevel))
        {
            field.WriteNumberValue((int)entry.LogLevel);
        }

        using (var field = writer.Begin(ElasticSchema.Fields.Message))
        {
            field.WriteStringValue(entry.Formatter(entry.State, entry.Exception));
        }

        if (entry.EventId != default)
        {
            using var field = writer.Begin(ElasticSchema.Fields.Event);

            field.WriteStartObject();
            field.WriteNumber(ElasticSchema.Fields.Code, entry.EventId.Id);

            if (entry.EventId.Name != null)
            {
                field.WriteString(ElasticSchema.Fields.Action, entry.EventId.Name);
            }

            field.WriteEndObject();
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

        using var field = writer.Begin(ElasticSchema.Fields.Error);

        field.WriteStartObject();
        field.WriteString(ElasticSchema.Fields.Message, exception.Message);
        field.WriteString(ElasticSchema.Fields.Type, exception.GetType().ToString());

        if (exception.StackTrace != null)
        {
            field.WriteString(ElasticSchema.Fields.StackTrace, exception.StackTrace);
        }

        field.WriteEndObject();
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
    /// Initializes a stream to save log data to the filesystem.
    /// </summary>
    /// <param name="path">The path to the output log file.</param>
    /// <param name="options">The file stream options.</param>
    /// <returns>The file stream.</returns>
    protected virtual Stream OpenFile(string path, FileStreamOptions options)
    {
        return File.Open(path, options);
    }

    /// <summary>
    /// Disposes the instance asynchronously.
    /// </summary>
    /// <returns>A task for the dispose operation.</returns>
    protected virtual ValueTask DisposeAsyncCore()
    {
        var task = CompleteAsync();
        return task != null ? new(task) : default;
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
            CompleteAsync()?.Wait();
        }
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

        while (await serializer.TryFlushAsync(writer, CancellationToken.None).ConfigureAwait(false))
        {
            var opts = fileOptions.CurrentValue;

            if (output.WrittenCount < opts.BufferSize)
            {
                bool active;
                using var cancellation = new CancellationTokenSource(opts.BufferInterval);

                do
                {
                    try
                    {
                        active = await serializer.TryFlushAsync(writer, cancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        active = false;
                    }
                }
                while (active && output.WrittenCount < opts.BufferSize);
            }

            writer.Flush();

            var path = Path.Combine(
                AppContext.BaseDirectory,
                string.Format(CultureInfo.InvariantCulture, opts.Path, formatArgs));

            using var file = OpenFile(path, StreamOptions);
            await file.WriteAsync(output.WrittenMemory).ConfigureAwait(false);
        }
    }

    Task? CompleteAsync()
    {
        serializer.Complete();
        return Volatile.Read(ref flushing);
    }

    sealed class FilebeatLogger : ILogger
    {
        readonly FilebeatLoggerProvider provider;
        readonly string category;
        readonly ElasticEntry fields;

        public FilebeatLogger(FilebeatLoggerProvider provider, string category, ElasticEntry fields)
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
            var entry = new ElasticEntry();
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
            var entry = new ElasticEntry();
            entry.Merge(fields);

            for (var i = 0; i < provider.scopes.Count; i++)
            {
                entry.Merge(provider.scopes[i]);
            }

            provider.WriteEntry<TState>(entry, new(logLevel, category, eventId, state, exception, formatter));

            if (entry.FieldCount > 0 && !provider.serializer.TryAppend(entry))
            {
                throw new ObjectDisposedException(provider.GetType().FullName);
            }
        }
    }
}
