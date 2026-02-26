using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Storage.HealthChecks.Configuration;
using System.Diagnostics;
using System.Text;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;
using Storage.HealthChecks.Extensions;

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
    private readonly ILocalizedTextService _localizedTextService;
    private readonly HashSet<string> _disallowedExtensions;
    private readonly int _maxFilesToScan;
    private readonly TimeSpan _timeBudget;

    public DisallowedMediaExtensionsHealthCheck(
        MediaFileManager mediaFileManager,
        ILogger<DisallowedMediaExtensionsHealthCheck> logger,
        IOptions<ContentSettings> contentSettings,
        IOptions<StorageHealthCheckConfiguration> storageSettings,
        ILocalizedTextService localizedTextService)
    {
        _mediaFileManager = mediaFileManager;
        _logger = logger;
        _localizedTextService = localizedTextService;

        var settings = storageSettings.Value;
        _maxFilesToScan = settings.DisallowedExtensionsScanMaxFiles > 0
            ? settings.DisallowedExtensionsScanMaxFiles
            : 50_000;
        _timeBudget = TimeSpan.FromSeconds(
            settings.DisallowedExtensionsScanTimeBudgetSeconds > 0
                ? settings.DisallowedExtensionsScanTimeBudgetSeconds
                : 5);

        _disallowedExtensions = (contentSettings.Value.DisallowedUploadedFileExtensions ?? Enumerable.Empty<string>())
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
        return new HealthCheckStatus(_localizedTextService.LocalizeWithFallback("storageHealthChecks", "disallowedExtensions.noActions"))
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
                return new HealthCheckStatus(_localizedTextService.LocalizeWithFallback("storageHealthChecks", "disallowedExtensions.noConfig"))
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
                return new HealthCheckStatus(_localizedTextService.LocalizeWithFallback("storageHealthChecks", "disallowedExtensions.noIssues"))
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
            return new HealthCheckStatus(_localizedTextService.LocalizeWithFallback("storageHealthChecks", "disallowedExtensions.error", new[] { ex.Message }))
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
                    result.AbortReasonKey = "disallowedExtensions.aborted.maxFiles";
                    result.AbortReasonTokens = new[] { _maxFilesToScan.ToString("N0") };
                    return;
                }

                if (stopwatch.Elapsed >= _timeBudget)
                {
                    result.Aborted = true;
                    result.AbortReasonKey = "disallowedExtensions.aborted.timeBudget";
                    result.AbortReasonTokens = new[] { _timeBudget.TotalSeconds.ToString("F1") };
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

        if (result.Aborted)
        {
            var abortReason = _localizedTextService.LocalizeWithFallback("storageHealthChecks", result.AbortReasonKey, result.AbortReasonTokens);
            sb.Append(_localizedTextService.LocalizeWithFallback("storageHealthChecks", "disallowedExtensions.summaryAborted",
                new[] { result.TotalViolations.ToString(), abortReason }));
        }
        else
        {
            sb.Append(_localizedTextService.LocalizeWithFallback("storageHealthChecks", "disallowedExtensions.summary",
                new[] { result.TotalViolations.ToString() }));
        }

        sb.Append("<br/><br/>");

        sb.Append("<div style=\"background-color: #f5f5f5; padding: 12px 16px; border-radius: 6px; margin-bottom: 16px;\">");
        sb.Append($"<strong>{_localizedTextService.LocalizeWithFallback("storageHealthChecks", "disallowedExtensions.whyHeader")}</strong><br/>");
        sb.Append("<ul style=\"margin: 8px 0 0 0;\">");
        sb.Append($"<li>{_localizedTextService.LocalizeWithFallback("storageHealthChecks", "disallowedExtensions.whyOldConfig")}</li>");
        sb.Append($"<li>{_localizedTextService.LocalizeWithFallback("storageHealthChecks", "disallowedExtensions.whyFtp")}</li>");
        sb.Append($"<li>{_localizedTextService.LocalizeWithFallback("storageHealthChecks", "disallowedExtensions.whyCustom")}</li>");
        sb.Append($"<li>{_localizedTextService.LocalizeWithFallback("storageHealthChecks", "disallowedExtensions.whyMigration")}</li>");
        sb.Append("</ul>");
        sb.Append("</div>");

        sb.Append($"<strong>{_localizedTextService.LocalizeWithFallback("storageHealthChecks", "disallowedExtensions.filesHeader")}</strong><br/><ul>");

        foreach (var filePath in result.ViolatingPaths)
        {
            var ext = System.Net.WebUtility.HtmlEncode(
                Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant());
            var encodedPath = System.Net.WebUtility.HtmlEncode(filePath);
            sb.Append($"<li><code>/media/{encodedPath}</code> <em>(.{ext})</em></li>");
        }

        sb.Append("</ul>");

        if (result.TotalViolations > MaxExamplePaths)
            sb.Append($"<em>{_localizedTextService.LocalizeWithFallback("storageHealthChecks", "disallowedExtensions.moreItems", new[] { (result.TotalViolations - MaxExamplePaths).ToString() })}</em><br/>");

        sb.Append($"<br/><em>{_localizedTextService.LocalizeWithFallback("storageHealthChecks", "disallowedExtensions.recommendation")}</em>");

        return sb.ToString();
    }

    private class ScanResult
    {
        public int TotalScanned { get; set; }
        public int TotalViolations { get; set; }
        public List<string> ViolatingPaths { get; } = new();
        public bool Aborted { get; set; }
        public string AbortReasonKey { get; set; } = string.Empty;
        public string[] AbortReasonTokens { get; set; } = Array.Empty<string>();
    }
}
