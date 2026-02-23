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
    /// Extensions to check against. The file extension is normalized to lowercase and stripped of any leading dot
    /// before comparison. Callers should provide entries without a leading dot (e.g. "exe", "php") and either
    /// normalize them to lowercase before adding them to the collection or use a case-insensitive collection.
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
