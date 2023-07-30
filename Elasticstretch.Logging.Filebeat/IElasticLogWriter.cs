namespace Elasticstretch.Logging.Filebeat;

using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Writes log information as Elastic document fields.
/// </summary>
public interface IElasticLogWriter
{
    /// <summary>
    /// Writes static document fields for all log entries.
    /// </summary>
    /// <param name="factory">The Elastic field factory.</param>
    void WriteStatic(ElasticFieldFactory factory);

    /// <summary>
    /// Writes document fields for a log scope.
    /// </summary>
    /// <typeparam name="TState">The scope state type.</typeparam>
    /// <param name="factory">The Elastic field factory.</param>
    /// <param name="category">The log category name.</param>
    /// <param name="state">The log scope state.</param>
    void WriteScope<TState>(ElasticFieldFactory factory, string category, TState state)
        where TState : notnull;

    /// <summary>
    /// Writes document fields for a log entry.
    /// </summary>
    /// <typeparam name="TState">The entry state type.</typeparam>
    /// <param name="factory">The Elastic field factory.</param>
    /// <param name="entry">The log entry.</param>
    void WriteEntry<TState>(ElasticFieldFactory factory, in LogEntry<TState> entry);
}
