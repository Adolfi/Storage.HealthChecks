namespace Storage.HealthChecks.Configuration;

public class StorageHealthCheckConfiguration
{
    /// <summary>
    /// Section name in appsettings.json
    /// </summary>
    public const string SectionName = "StorageHealthChecks";

    /// <summary>
    /// List of media item GUIDs to ignore in health checks.
    /// </summary>
    public List<Guid> IgnoredMediaIds { get; set; } = new();

    /// <summary>
    /// Maximum file size in MB before a file is considered "large".
    /// Default is 5 MB.
    /// </summary>
    public double LargeMediaThresholdMB { get; set; } = 5.0;

    /// <summary>
    /// Checks if a media item should be ignored based on its GUID.
    /// </summary>
    public bool ShouldIgnore(Guid mediaKey)
    {
        return IgnoredMediaIds.Contains(mediaKey);
    }
}
