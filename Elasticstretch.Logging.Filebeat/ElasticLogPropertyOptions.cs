namespace Elasticstretch.Logging.Filebeat;

/// <summary>
/// Options for writing log properties as Elastic fields.
/// </summary>
public class ElasticLogPropertyOptions
{
    /// <summary>
    /// Gets the mappings from property name to Elastic field name.
    /// </summary>
    public IDictionary<string, string> Mappings { get; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets or sets whether to include unmapped properties as top-level fields.
    /// </summary>
    /// <remarks>
    /// The special key "{OriginalFormat}" must be explicitly mapped.
    /// </remarks>
    public bool IncludeOthers { get; set; }
}
