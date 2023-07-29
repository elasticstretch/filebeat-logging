namespace Elasticstretch.Logging.Filebeat;

using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

/// <summary>
/// Writes log information as Elastic document fields.
/// </summary>
public interface IElasticLogger
{
    /// <summary>
    /// Determines if any fields exist for a log category.
    /// </summary>
    /// <param name="category">The log category name.</param>
    /// <returns><see langword="true"/> if one or more fields exist, otherwise <see langword="false"/>.</returns>
    bool IsEnabled(string category);

    /// <summary>
    /// Formats a log scope into document fields.
    /// </summary>
    /// <typeparam name="TState">The scope state type.</typeparam>
    /// <param name="fieldFactory">A delegate to retrieve a JSON writer for an Elastic field.</param>
    /// <param name="category">The log category name.</param>
    /// <param name="state">The log scope state.</param>
    void WriteScope<TState>(Func<string, Utf8JsonWriter> fieldFactory, string category, TState state)
        where TState : notnull;

    /// <summary>
    /// Formats a log entry into document fields.
    /// </summary>
    /// <typeparam name="TState">The entry state type.</typeparam>
    /// <param name="fieldFactory">A delegate to retrieve a JSON writer for an Elastic field.</param>
    /// <param name="entry">The log entry.</param>
    void WriteEntry<TState>(Func<string, Utf8JsonWriter> fieldFactory, in LogEntry<TState> entry);
}
