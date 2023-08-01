namespace Elasticstretch.Logging.Filebeat;

using System.Text.Json;

/// <summary>
/// Options for writing log information using the Elastic Common Schema.
/// </summary>
public class ElasticLoggingOptions : ElasticLogPropertyOptions
{
    /// <summary>
    /// Gets the options for JSON serialization.
    /// </summary>
    public JsonSerializerOptions Json { get; } = new();

    /// <summary>
    /// Gets the category-specific property options by category name.
    /// </summary>
    /// <remarks>
    ///  Keys may be a prefix or contain a wildcard ("*").
    /// </remarks>
    public IDictionary<string, ElasticLogPropertyOptions> Categories { get; }
        = new Dictionary<string, ElasticLogPropertyOptions>();
}
