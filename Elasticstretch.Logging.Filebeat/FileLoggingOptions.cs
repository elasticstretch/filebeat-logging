namespace Elasticstretch.Logging.Filebeat;

/// <summary>
/// Options for writing log information to the filesystem.
/// </summary>
public class FileLoggingOptions
{
    /// <summary>
    /// The path to the output log file.
    /// </summary>
    /// <remarks>
    /// Supports application and environment as format items. Default is <c>{0}.{1}.log</c>.
    /// </remarks>
    public string Path { get; set; } = "{0}.{1}.log";

    /// <summary>
    /// Gets or sets the maximum number of log bytes to store in memory.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>4096</c>. The actual size may be less based on <see cref="BufferInterval"/>.
    /// </remarks>
    public int BufferSize { get; set; } = 4096;

    /// <summary>
    /// Gets or sets the maximum log time interval to store in memory.
    /// </summary>
    /// <remarks>
    /// Defaults to one second. The actual interval may be less based on <see cref="BufferSize"/>.
    /// </remarks>
    public TimeSpan BufferInterval { get; set; } = TimeSpan.FromSeconds(1);
}
