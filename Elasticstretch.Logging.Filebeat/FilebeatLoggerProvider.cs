namespace Elasticstretch.Logging.Filebeat;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// A provider of loggers for use with Filebeat.
/// </summary>
/// <remarks>
/// Log entries are documents satisfying the Elastic Common Schema, written to the filesystem as UTF-8 newline-delimited JSON.
/// </remarks>
[ProviderAlias("Filebeat")]
public class FilebeatLoggerProvider : ILoggerProvider
{
    /// <inheritdoc/>
    public virtual ILogger CreateLogger(string categoryName)
    {
        throw new NotImplementedException();
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
}
