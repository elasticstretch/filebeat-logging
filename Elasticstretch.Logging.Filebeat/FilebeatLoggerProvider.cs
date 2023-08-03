namespace Elasticstretch.Logging.Filebeat;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

/// <summary>
/// A provider of loggers for use with Filebeat.
/// </summary>
/// <remarks>
/// Log entries are documents satisfying the Elastic Common Schema, written to the filesystem as UTF-8 newline-delimited JSON.
/// </remarks>
[ProviderAlias("Filebeat")]
public class FilebeatLoggerProvider : ILoggerProvider
{
    readonly IOptions<FileLoggingOptions> fileOptions;

    /// <summary>
    /// Initializes the provider.
    /// </summary>
    /// <param name="fileOptions">The file logging options.</param>
    public FilebeatLoggerProvider(IOptions<FileLoggingOptions> fileOptions)
    {
        this.fileOptions = fileOptions;
    }

    /// <inheritdoc/>
    public virtual ILogger CreateLogger(string categoryName)
    {
        return new FilebeatLogger(this, categoryName);
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
    /// <param name="factory">The Elastic field factory.</param>
    protected virtual void WriteStatic(ElasticFieldFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory, nameof(factory));
        factory("ecs.version").WriteStringValue("8.9.0");
    }

    /// <summary>
    /// Writes document fields for a log scope.
    /// </summary>
    /// <typeparam name="TState">The scope state type.</typeparam>
    /// <param name="factory">The Elastic field factory.</param>
    /// <param name="category">The log category name.</param>
    /// <param name="state">The log scope state.</param>
    protected virtual void WriteScope<TState>(ElasticFieldFactory factory, string category, TState state)
        where TState : notnull
    {
    }

    /// <summary>
    /// Writes document fields for a log entry.
    /// </summary>
    /// <typeparam name="TState">The entry state type.</typeparam>
    /// <param name="factory">The Elastic field factory.</param>
    /// <param name="entry">The log entry.</param>
    protected virtual void WriteEntry<TState>(ElasticFieldFactory factory, in LogEntry<TState> entry)
    {
        ArgumentNullException.ThrowIfNull(factory, nameof(factory));

        factory("@timestamp").WriteStringValue(DateTimeOffset.Now);
        factory("message").WriteStringValue(entry.Formatter(entry.State, entry.Exception));

        var logField = factory("log");

        logField.WriteStartObject();
        logField.WriteString("logger", entry.Category);
        logField.WriteNumber("level", (int)entry.LogLevel);
        logField.WriteEndObject();

        if (entry.EventId != default)
        {
            var eventField = factory("event");

            eventField.WriteStartObject();
            eventField.WriteNumber("code", entry.EventId.Id);

            if (entry.EventId.Name != null)
            {
                eventField.WriteString("action", entry.EventId.Name);
            }

            eventField.WriteEndObject();
        }

        WriteExceptions(factory, entry.Exception);
    }

    /// <summary>
    /// Writes document fields for a logged exception.
    /// </summary>
    /// <remarks>
    /// Applies to each exception included in a log entry, including inner exceptions.
    /// </remarks>
    /// <param name="factory">The Elastic field factory.</param>
    /// <param name="exception">The logged exception.</param>
    protected virtual void WriteException(ElasticFieldFactory factory, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(factory, nameof(factory));
        ArgumentNullException.ThrowIfNull(exception, nameof(exception));

        var errorField = factory("error");

        errorField.WriteStartObject();
        errorField.WriteString("message", exception.Message);
        errorField.WriteString("type", exception.GetType().ToString());

        if (exception.StackTrace != null)
        {
            errorField.WriteString("stack_trace", exception.StackTrace);
        }

        errorField.WriteEndObject();
    }

    /// <summary>
    /// Writes document fields for a log property.
    /// </summary>
    /// <remarks>
    /// Applies to log entry and scope state implementing <see cref="IReadOnlyList{T}"/> of <see cref="KeyValuePair{TKey, TValue}"/> of <see cref="string"/> and <see cref="object"/> (e.g. message template arguments).
    /// </remarks>
    /// <param name="factory">The Elastic field factory.</param>
    /// <param name="category">The log category name.</param>
    /// <param name="name">The log property name.</param>
    /// <param name="value">The log property value.</param>
    protected virtual void WriteProperty(ElasticFieldFactory factory, string category, string name, object? value)
    {
    }

    /// <summary>
    /// Disposes and/or finalizes the instance.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> to dispose and finalize, <see langword="false"/> to finalize only.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
    }

    void WriteExceptions(ElasticFieldFactory factory, Exception? exception)
    {
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                WriteExceptions(factory, inner);
            }
        }
        else if (exception != null)
        {
            WriteException(factory, exception);
            WriteExceptions(factory, exception.InnerException);
        }
    }

    sealed class FilebeatLogger : ILogger
    {
        private readonly FilebeatLoggerProvider provider;
        private readonly string category;

        public FilebeatLogger(FilebeatLoggerProvider provider, string category)
        {
            this.provider = provider;
            this.category = category;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            throw new NotImplementedException();
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            throw new NotImplementedException();
        }
    }
}
