using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Storage.HealthChecks.Configuration;
using System.Diagnostics;
using System.Text;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Core.IO;

namespace Storage.HealthChecks.HealthChecks;

[HealthCheck(
    "C4D5E6F7-A8B9-0C1D-2E3F-4A5B6C7D8E9F",
    "Disallowed media file extensions",
    Description = "Checks for media files whose extensions are listed in Umbraco's DisallowedUploadedFileExtensions setting.",
    Group = "Media Storage")]
public class DisallowedMediaExtensionsHealthCheck : HealthCheck
{
    private const int MaxExamplePaths = 50;

    private readonly MediaFileManager _mediaFileManager;
    private readonly ILogger<DisallowedMediaExtensionsHealthCheck> _logger;
    private readonly HashSet<string> _disallowedExtensions;
    private readonly int _maxFilesToScan;
    private readonly TimeSpan _timeBudget;

    public DisallowedMediaExtensionsHealthCheck(
        MediaFileManager mediaFileManager,
        ILogger<DisallowedMediaExtensionsHealthCheck> logger,
        IOptions<ContentSettings> contentSettings,
        IOptions<StorageHealthCheckConfiguration> storageSettings)
    {
        _mediaFileManager = mediaFileManager;
        _logger = logger;

        var settings = storageSettings.Value;
        _maxFilesToScan = settings.DisallowedExtensionsScanMaxFiles > 0
            ? settings.DisallowedExtensionsScanMaxFiles
            : 50_000;
        _timeBudget = TimeSpan.FromSeconds(
            settings.DisallowedExtensionsScanTimeBudgetSeconds > 0
                ? settings.DisallowedExtensionsScanTimeBudgetSeconds
                : 5);

        _disallowedExtensions = contentSettings.Value.DisallowedUploadedFileExtensions
            .Select(e => e.TrimStart('.').ToLowerInvariant())
            .Where(e => !string.IsNullOrEmpty(e))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public override Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
    {
        var status = CheckDisallowedExtensions();
        return Task.FromResult<IEnumerable<HealthCheckStatus>>(new[] { status });
    }

    public override HealthCheckStatus ExecuteAction(HealthCheckAction action)
    {
        return new HealthCheckStatus("No actions available. Please review and remove or replace the disallowed files manually.")
        {
            ResultType = StatusResultType.Info
        };
    }

    private HealthCheckStatus CheckDisallowedExtensions()
    {
        try
        {
            _logger.LogDebug("Starting disallowed media extensions check");

            if (_disallowedExtensions.Count == 0)
            {
                return new HealthCheckStatus("No disallowed file extensions are configured in ContentSettings.")
                {
                    ResultType = StatusResultType.Success
                };
            }

            var result = ScanForDisallowedFiles();

            _logger.LogInformation(
                "Disallowed media extensions check complete. Found {Violations} disallowed files out of {Scanned} scanned",
                result.TotalViolations, result.TotalScanned);

            if (result.TotalViolations == 0)
            {
                return new HealthCheckStatus("No media files with disallowed extensions found.")
                {
                    ResultType = StatusResultType.Success
                };
            }

            return new HealthCheckStatus(BuildResultMessage(result))
            {
                ResultType = StatusResultType.Warning,
                ReadMoreLink = "https://github.com/Adolfi/Storage.HealthChecks#disallowed-media-file-extensions"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disallowed media extensions health check");
            return new HealthCheckStatus($"Error checking disallowed media extensions: {ex.Message}")
            {
                ResultType = StatusResultType.Error
            };
        }
    }

    private ScanResult ScanForDisallowedFiles()
    {
        var result = new ScanResult();
        var stopwatch = Stopwatch.StartNew();
        var fileSystem = _mediaFileManager.FileSystem;

        ScanDirectoryRecursively(fileSystem, string.Empty, result, stopwatch);

        return result;
    }

    private void ScanDirectoryRecursively(IFileSystem fileSystem, string path, ScanResult result, Stopwatch stopwatch)
    {
        if (result.Aborted) return;

        try
        {
            foreach (var filePath in fileSystem.GetFiles(path))
            {
                if (result.TotalScanned >= _maxFilesToScan)
                {
                    result.Aborted = true;
                    result.AbortReason = $"scan limit of {_maxFilesToScan:N0} files reached";
                    return;
                }

                if (stopwatch.Elapsed >= _timeBudget)
                {
                    result.Aborted = true;
                    result.AbortReason = $"time budget of {_timeBudget.TotalSeconds}s exceeded";
                    return;
                }

                result.TotalScanned++;

                var fileName = Path.GetFileName(filePath);
                if (fileName.StartsWith(".") ||
                    fileName == "Thumbs.db" ||
                    fileName == "desktop.ini")
                    continue;

                if (DisallowedExtensionEvaluator.IsDisallowed(filePath, _disallowedExtensions))
                {
                    if (result.ViolatingPaths.Count < MaxExamplePaths)
                        result.ViolatingPaths.Add(filePath);
                    result.TotalViolations++;
                }
            }

            if (result.Aborted) return;

            foreach (var directory in fileSystem.GetDirectories(path))
            {
                ScanDirectoryRecursively(fileSystem, directory, result, stopwatch);
                if (result.Aborted) return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error scanning directory: {Path}", path);
        }
    }

    private string BuildResultMessage(ScanResult result)
    {
        var sb = new StringBuilder();

        sb.Append($"Found <strong>{result.TotalViolations}</strong> file{(result.TotalViolations == 1 ? "" : "s")} with disallowed extensions");
        if (result.Aborted)
            sb.Append($" (scan stopped early: {result.AbortReason})");
        sb.Append(".<br/><br/>");

        sb.Append("<div style=\"background-color: #f5f5f5; padding: 12px 16px; border-radius: 6px; margin-bottom: 16px;\">");
        sb.Append("<strong>Why might disallowed files exist?</strong><br/>");
        sb.Append("<ul style=\"margin: 8px 0 0 0;\">");
        sb.Append("<li>An older configuration allowed these extensions before they were restricted</li>");
        sb.Append("<li>Files were added directly via FTP, file share, or Azure Blob storage</li>");
        sb.Append("<li>Custom upload endpoints bypassed Umbraco validation</li>");
        sb.Append("<li>Migrations or deployments copied files into the media folder</li>");
        sb.Append("</ul>");
        sb.Append("</div>");

        sb.Append("<strong>Files with disallowed extensions:</strong><br/><ul>");

        foreach (var filePath in result.ViolatingPaths)
        {
            var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
            sb.Append($"<li><code>/media/{filePath}</code> <em>(.{ext})</em></li>");
        }

        sb.Append("</ul>");

        if (result.TotalViolations > MaxExamplePaths)
            sb.Append($"<em>...and {result.TotalViolations - MaxExamplePaths} more</em><br/>");

        sb.Append("<br/><em>Review and remove or replace these files to improve security.</em>");

        return sb.ToString();
    }

    private class ScanResult
    {
        public int TotalScanned { get; set; }
        public int TotalViolations { get; set; }
        public List<string> ViolatingPaths { get; } = new();
        public bool Aborted { get; set; }
        public string AbortReason { get; set; } = string.Empty;
    }
}
