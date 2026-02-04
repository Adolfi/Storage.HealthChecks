using Microsoft.Extensions.Logging;
using System.Text;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Storage.HealthChecks.HealthChecks;

[HealthCheck(
    "B2C3D4E5-F6A7-8901-BCDE-F23456789012",
    "Missing media files",
    Description = "Checks for media items in the database that are missing their physical files on disk.",
    Group = "Media Storage")]
public class MissingMediaFilesHealthCheck : HealthCheck
{
    private const int PageSize = 500;

    private readonly IMediaService _mediaService;
    private readonly MediaFileManager _mediaFileManager;
    private readonly ILogger<MissingMediaFilesHealthCheck> _logger;

    public MissingMediaFilesHealthCheck(
        IMediaService mediaService,
        MediaFileManager mediaFileManager,
        ILogger<MissingMediaFilesHealthCheck> logger)
    {
        _mediaService = mediaService;
        _mediaFileManager = mediaFileManager;
        _logger = logger;
    }

    public override Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
    {
        var status = CheckMissingFiles();
        return Task.FromResult<IEnumerable<HealthCheckStatus>>(new[] { status });
    }

    public override HealthCheckStatus ExecuteAction(HealthCheckAction action)
    {
        return new HealthCheckStatus("No actions available. Please re-upload the missing files or remove the media items.")
        {
            ResultType = StatusResultType.Info
        };
    }

    private HealthCheckStatus CheckMissingFiles()
    {
        try
        {
            var missingFiles = FindMissingFiles();

            if (missingFiles.Count == 0)
            {
                return new HealthCheckStatus("All media items have their physical files present.")
                {
                    ResultType = StatusResultType.Success
                };
            }

            return new HealthCheckStatus(BuildResultMessage(missingFiles))
            {
                ResultType = StatusResultType.Error,
                ReadMoreLink = "https://google.com"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during missing media files health check");
            return new HealthCheckStatus($"Error: {ex.Message}")
            {
                ResultType = StatusResultType.Error
            };
        }
    }

    private List<MissingFileInfo> FindMissingFiles()
    {
        var missingFiles = new List<MissingFileInfo>();
        var pageIndex = 0L;
        var fileSystem = _mediaFileManager.FileSystem;

        while (true)
        {
            var mediaPage = _mediaService.GetPagedDescendants(
                Constants.System.Root, pageIndex, PageSize, out var totalRecords);

            var mediaList = mediaPage.ToList();
            if (mediaList.Count == 0) break;

            foreach (var media in mediaList)
            {
                if (media.ContentType.Alias.Equals("Folder", StringComparison.OrdinalIgnoreCase))
                    continue;

                var filePath = GetMediaFilePath(media);
                if (string.IsNullOrEmpty(filePath)) continue;

                var normalizedPath = NormalizePath(filePath);
                
                if (!fileSystem.FileExists(normalizedPath))
                {
                    missingFiles.Add(new MissingFileInfo
                    {
                        Key = media.Key,
                        Name = media.Name ?? "(unnamed)",
                        ExpectedPath = filePath
                    });
                }
            }

            if ((pageIndex + 1) * PageSize >= totalRecords) break;
            pageIndex++;
        }

        return missingFiles;
    }

    private static string GetMediaFilePath(IMedia media)
    {
        var umbracoFile = media.GetValue<string>(Constants.Conventions.Media.File);
        if (string.IsNullOrEmpty(umbracoFile)) return string.Empty;

        if (umbracoFile.StartsWith("{"))
        {
            var srcIndex = umbracoFile.IndexOf("\"src\":", StringComparison.OrdinalIgnoreCase);
            if (srcIndex > 0)
            {
                var start = umbracoFile.IndexOf('"', srcIndex + 6) + 1;
                var end = umbracoFile.IndexOf('"', start);
                return umbracoFile.Substring(start, end - start);
            }
        }
        return umbracoFile;
    }

    private static string NormalizePath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("media/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(6);
        return normalized;
    }

    private string BuildResultMessage(List<MissingFileInfo> missingFiles)
    {
        var sb = new StringBuilder();

        sb.Append($"<strong style=\"color: #d32f2f;\">⚠️ Found {missingFiles.Count} media item{(missingFiles.Count == 1 ? "" : "s")} with missing files!</strong><br/><br/>");

        sb.Append("<div style=\"background-color: #f5f5f5; padding: 12px 16px; border-radius: 6px; margin-bottom: 16px;\">");
        sb.Append("<strong>Why does this happen?</strong><br/>");
        sb.Append("<ul style=\"margin: 8px 0 0 0;\">");
        sb.Append("<li>Server migration where media files were not copied</li>");
        sb.Append("<li>Disk failure or file system corruption</li>");
        sb.Append("<li>Manual deletion of files via FTP/file manager</li>");
        sb.Append("<li>Failed deployment missing media files</li>");
        sb.Append("<li>Cloud storage sync issues (Azure Blob, AWS S3)</li>");
        sb.Append("</ul>");
        sb.Append("</div>");

        sb.Append("<strong>Missing files:</strong><br/><ul>");

        foreach (var file in missingFiles.Take(15))
        {
            var link = $"/umbraco/section/media/workspace/media/edit/{file.Key}";
            sb.Append($"<li><a href=\"{link}\" target=\"_blank\"><strong>{file.Name}</strong></a> ");
            sb.Append($"<em>(expected: <code>{file.ExpectedPath}</code>)</em></li>");
        }

        sb.Append("</ul>");

        if (missingFiles.Count > 15)
            sb.Append($"<em>...and {missingFiles.Count - 15} more</em><br/>");

        sb.Append("<br/><strong>Action required:</strong> Re-upload the missing files or delete the media items.");
        return sb.ToString();
    }

    private class MissingFileInfo
    {
        public Guid Key { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ExpectedPath { get; set; } = string.Empty;
    }
}
