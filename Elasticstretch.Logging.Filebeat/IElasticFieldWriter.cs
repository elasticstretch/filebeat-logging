namespace Elasticstretch.Logging.Filebeat;

using System.Text.Json;

/// <summary>
/// Writes JSON data for Elasticsearch document fields.
/// </summary>
public interface IElasticFieldWriter
{
    /// <summary>
    /// Begins writing a JSON field.
    /// </summary>
    /// <param name="name">The JSON field name.</param>
    /// <returns>A writer for the JSON field value.</returns>
    Utf8JsonWriter Begin(JsonEncodedText name);
}
