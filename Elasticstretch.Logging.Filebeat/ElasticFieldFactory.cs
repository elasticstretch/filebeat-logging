namespace Elasticstretch.Logging.Filebeat;

using System.Text.Json;

/// <summary>
/// Creates or retrieves a JSON writer for an Elastic field.
/// </summary>
/// <param name="fieldName">The Elastic field name.</param>
/// <returns>The JSON field writer.</returns>
public delegate Utf8JsonWriter ElasticFieldFactory(string fieldName);