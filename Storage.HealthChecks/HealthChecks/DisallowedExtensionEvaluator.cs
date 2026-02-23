namespace Storage.HealthChecks.HealthChecks;

/// <summary>
/// Helper for evaluating whether a file extension is disallowed.
/// Kept as a standalone static class to allow unit testing without infrastructure dependencies.
/// </summary>
public static class DisallowedExtensionEvaluator
{
    /// <summary>
    /// Determines whether the file at the given path has a disallowed extension.
    /// </summary>
    /// <param name="filePath">The file path to check.</param>
    /// <param name="disallowedExtensions">
    /// Extensions to check against. Each entry should be lowercase and without a leading dot (e.g. "exe", "php").
    /// </param>
    /// <returns>True if the file's extension is in the disallowed list; otherwise false.</returns>
    public static bool IsDisallowed(string filePath, IEnumerable<string> disallowedExtensions)
    {
        if (string.IsNullOrEmpty(filePath)) return false;

        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) return false;

        return disallowedExtensions.Contains(ext);
    }
}
