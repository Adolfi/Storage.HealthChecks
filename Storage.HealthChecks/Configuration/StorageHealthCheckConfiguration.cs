namespace Storage.HealthChecks.Configuration;

/// <summary>
/// Configuration for Storage Health Checks.
/// Add to appsettings.json under "StorageHealthChecks" section.
/// 
/// Example:
/// {
///   "StorageHealthChecks": {
///     "IgnoredMediaIds": ["a1b2c3d4-e5f6-7890-abcd-ef1234567890"],
///     "IgnoredMediaPaths": ["/media/legacy/", "/media/archive/"],
///     "IgnoredFileNames": ["placeholder.png", "default.jpg"]
///   }
/// }
/// </summary>
public class StorageHealthCheckConfiguration
{
    /// <summary>
    /// Section name in appsettings.json
    /// </summary>
    public const string SectionName = "StorageHealthChecks";

    /// <summary>
    /// List of media item GUIDs to ignore in all health checks.
    /// </summary>
    public List<Guid> IgnoredMediaIds { get; set; } = new();

    /// <summary>
    /// List of media paths to ignore (partial match).
    /// Example: "/media/legacy/" will ignore all files under that path.
    /// </summary>
    public List<string> IgnoredMediaPaths { get; set; } = new();

    /// <summary>
    /// List of file names to ignore (exact match, case-insensitive).
    /// Example: "placeholder.png"
    /// </summary>
    public List<string> IgnoredFileNames { get; set; } = new();

    /// <summary>
    /// Checks if a media item should be ignored based on its GUID.
    /// </summary>
    public bool IsIgnored(Guid mediaKey)
    {
        return IgnoredMediaIds.Contains(mediaKey);
    }

    /// <summary>
    /// Checks if a file path should be ignored based on path patterns.
    /// </summary>
    public bool IsPathIgnored(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return false;
        }

        var normalizedPath = filePath.Replace('\\', '/').ToLowerInvariant();

        foreach (var ignoredPath in IgnoredMediaPaths)
        {
            if (string.IsNullOrEmpty(ignoredPath))
            {
                continue;
            }

            var normalizedIgnored = ignoredPath.Replace('\\', '/').ToLowerInvariant();
            
            if (normalizedPath.Contains(normalizedIgnored))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a file name should be ignored.
    /// </summary>
    public bool IsFileNameIgnored(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        return IgnoredFileNames.Any(ignored => 
            string.Equals(ignored, fileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks if a media item should be ignored based on any criteria.
    /// </summary>
    public bool ShouldIgnore(Guid mediaKey, string? filePath, string? fileName)
    {
        return IsIgnored(mediaKey) || IsPathIgnored(filePath) || IsFileNameIgnored(fileName);
    }
}
