namespace Elasticstretch.Logging.Filebeat;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;

/// <summary>
/// A provider of loggers for use with Filebeat.
/// </summary>
/// <remarks>
/// Log entries are documents satisfying the Elastic Common Schema, written to the filesystem as UTF-8 newline-delimited JSON.
/// </remarks>
[ProviderAlias("Filebeat")]
public class FilebeatLoggerProvider : ILoggerProvider
{
    readonly IOptions<ElasticLoggingOptions> elasticOptions;
    readonly IOptions<FileLoggingOptions> fileOptions;

    readonly ConcurrentDictionary<string, JsonEncodedText> textCache = new();
    readonly ConcurrentDictionary<string, ElasticLogPropertyOptions> categoryOptions = new();

    /// <summary>
    /// Initializes the provider.
    /// </summary>
    /// <param name="elasticOptions">The Elastic logging options.</param>
    /// <param name="fileOptions">The file logging options.</param>
    public FilebeatLoggerProvider(
        IOptions<ElasticLoggingOptions> elasticOptions,
        IOptions<FileLoggingOptions> fileOptions)
    {
        this.elasticOptions = elasticOptions;
        this.fileOptions = fileOptions;
    }

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
        ArgumentNullException.ThrowIfNull(factory, nameof(factory));
        factory("ecs.version").WriteStringValue(CacheText("8.9.0"));
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
        ArgumentNullException.ThrowIfNull(factory, nameof(factory));
        ArgumentNullException.ThrowIfNull(category, nameof(category));
        ArgumentNullException.ThrowIfNull(state, nameof(state));
        WriteState(factory, category, state);
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
        logField.WriteString(CacheText("logger"), CacheText(entry.Category));

        logField.WritePropertyName(CacheText("level"));
        JsonSerializer.Serialize(logField, entry.LogLevel, elasticOptions.Value.Json);

        logField.WriteEndObject();

        if (entry.EventId != default)
        {
            var eventField = factory("event");

            eventField.WriteStartObject();
            eventField.WriteNumber(CacheText("code"), entry.EventId.Id);

            if (entry.EventId.Name != null)
            {
                eventField.WriteString(CacheText("action"), entry.EventId.Name);
            }

            eventField.WriteEndObject();
        }

        WriteExceptions(factory, entry.Exception);
        WriteState(factory, entry.Category, entry.State);
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
        errorField.WriteString(CacheText("message"), exception.Message);

        errorField.WritePropertyName(CacheText("type"));
        JsonSerializer.Serialize(errorField, exception, elasticOptions.Value.Json);

        if (exception.StackTrace != null)
        {
            errorField.WriteString(CacheText("stack_trace"), exception.StackTrace);
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
        ArgumentNullException.ThrowIfNull(factory, nameof(factory));
        ArgumentNullException.ThrowIfNull(category, nameof(category));
        ArgumentNullException.ThrowIfNull(name, nameof(name));

        if (!categoryOptions.TryGetValue(category, out var opts))
        {
            opts = elasticOptions.Value;

            foreach (var kvp in elasticOptions.Value.Categories)
            {
                if (CategoryMatch(kvp.Key, category))
                {
                    opts = kvp.Value;
                    break;
                }
            }

            categoryOptions[category] = opts;
        }

        if (opts.Mappings.TryGetValue(name, out var field))
        {
            JsonSerializer.Serialize(factory(field), value, elasticOptions.Value.Json);
        }
        else if (opts.IncludeOthers && name != "{OriginalFormat}")
        {
            JsonSerializer.Serialize(factory(name), value, elasticOptions.Value.Json);
        }
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

    // Use same prefix/wildcard matching as logger factory.
    static bool CategoryMatch(string pattern, string category)
    {
        const char WildcardChar = '*';
        int wildcardIndex = pattern.IndexOf(WildcardChar, StringComparison.Ordinal);

        if (wildcardIndex != -1 && pattern.IndexOf(WildcardChar, wildcardIndex + 1) != -1)
        {
            throw new InvalidOperationException("Only one wildcard character is allowed in category name.");
        }

        ReadOnlySpan<char> prefix, suffix;
        if (wildcardIndex == -1)
        {
            prefix = pattern.AsSpan();
            suffix = default;
        }
        else
        {
            prefix = pattern.AsSpan(0, wildcardIndex);
            suffix = pattern.AsSpan(wildcardIndex + 1);
        }

        return category.AsSpan().StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            category.AsSpan().EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
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

    void WriteState<T>(ElasticFieldFactory factory, string category, T state)
    {
        if (state is IReadOnlyList<KeyValuePair<string, object?>> props)
        {
            for (var i = 0; i < props.Count; i++)
            {
                WriteProperty(factory, category, props[i].Key, props[i].Value);
            }
        }
    }

    JsonEncodedText CacheText(string value)
    {
        return textCache.TryGetValue(value, out var bytes) ? bytes : textCache[value] = JsonEncodedText.Encode(value);
    }
}
