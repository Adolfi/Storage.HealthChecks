using Microsoft.Extensions.Logging;
using System.Text;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace Storage.HealthChecks.HealthChecks;

/// <summary>
/// Health check that identifies orphaned media files on disk.
/// Orphaned files are physical files in the /media folder that no longer have
/// a corresponding media item in the Umbraco database.
/// </summary>
[HealthCheck(
    "A1B2C3D4-E5F6-7890-ABCD-EF1234567890",
    "Orphaned media files",
    Description = "Checks for physical media files that have no corresponding database entry.",
    Group = "Media Storage")]
public class OrphanedMediaFilesHealthCheck : HealthCheck
{
    private const int PageSize = 500;

    private readonly IMediaService _mediaService;
    private readonly MediaFileManager _mediaFileManager;
    private readonly ILogger<OrphanedMediaFilesHealthCheck> _logger;
    private readonly ILocalizedTextService _localizedTextService;

    public OrphanedMediaFilesHealthCheck(
        IMediaService mediaService,
        MediaFileManager mediaFileManager,
        ILogger<OrphanedMediaFilesHealthCheck> logger,
        ILocalizedTextService localizedTextService)
    {
        _mediaService = mediaService;
        _mediaFileManager = mediaFileManager;
        _logger = logger;
        _localizedTextService = localizedTextService;
    }

    public override Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
    {
        var status = CheckOrphanedFiles();
        return Task.FromResult<IEnumerable<HealthCheckStatus>>(new[] { status });
    }

    public override HealthCheckStatus ExecuteAction(HealthCheckAction action)
    {
        return new HealthCheckStatus(_localizedTextService.Localize("storageHealthChecks", "orphanedMedia.noActions"))
        {
            ResultType = StatusResultType.Info
        };
    }

    private HealthCheckStatus CheckOrphanedFiles()
    {
        try
        {
            _logger.LogDebug("Starting orphaned media files check");

            var databasePaths = GetAllMediaPathsFromDatabase();
            var physicalFiles = GetAllPhysicalMediaFiles();
            var orphanedFiles = FindOrphanedFiles(physicalFiles, databasePaths);

            _logger.LogInformation(
                "Orphaned media files check complete. Found {Count} orphaned files out of {Total} physical files",
                orphanedFiles.Count, physicalFiles.Count);

            if (orphanedFiles.Count == 0)
            {
                return new HealthCheckStatus(_localizedTextService.Localize("storageHealthChecks", "orphanedMedia.noIssues"))
                {
                    ResultType = StatusResultType.Success
                };
            }

            var totalOrphanedSize = orphanedFiles.Sum(f => f.SizeBytes);
            var message = BuildResultMessage(orphanedFiles, totalOrphanedSize);

            return new HealthCheckStatus(message)
            {
                ResultType = StatusResultType.Warning,
                ReadMoreLink = "https://github.com/Adolfi/Storage.HealthChecks#orphaned-media-files"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during orphaned media files health check");
            return new HealthCheckStatus(_localizedTextService.Localize("storageHealthChecks", "orphanedMedia.error", new[] { ex.Message }))
            {
                ResultType = StatusResultType.Error
            };
        }
    }

    private HashSet<string> GetAllMediaPathsFromDatabase()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pageIndex = 0L;

        while (true)
        {
            var mediaPage = _mediaService.GetPagedDescendants(
                Constants.System.Root,
                pageIndex,
                PageSize,
                out var totalRecords);

            var mediaList = mediaPage.ToList();

            if (mediaList.Count == 0)
            {
                break;
            }

            foreach (var media in mediaList)
            {
                if (media.ContentType.Alias.Equals("Folder", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var filePath = GetMediaFilePath(media);
                if (!string.IsNullOrEmpty(filePath))
                {
                    var normalizedPath = NormalizePath(filePath);
                    paths.Add(normalizedPath);
                }
            }

            if ((pageIndex + 1) * PageSize >= totalRecords)
            {
                break;
            }

            pageIndex++;
        }

        return paths;
    }

    private List<PhysicalFileInfo> GetAllPhysicalMediaFiles()
    {
        var files = new List<PhysicalFileInfo>();

        try
        {
            var fileSystem = _mediaFileManager.FileSystem;
            CollectFilesRecursively(fileSystem, string.Empty, files);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading physical media files");
        }

        return files;
    }

    private void CollectFilesRecursively(IFileSystem fileSystem, string path, List<PhysicalFileInfo> files)
    {
        try
        {
            foreach (var filePath in fileSystem.GetFiles(path))
            {
                try
                {
                    var size = fileSystem.GetSize(filePath);
                    files.Add(new PhysicalFileInfo
                    {
                        Path = filePath,
                        NormalizedPath = NormalizePath(filePath),
                        SizeBytes = size
                    });
                }
                catch
                {
                    // Skip files we can't access
                }
            }

            foreach (var directory in fileSystem.GetDirectories(path))
            {
                CollectFilesRecursively(fileSystem, directory, files);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error reading directory: {Path}", path);
        }
    }

    private static string GetMediaFilePath(Umbraco.Cms.Core.Models.IMedia media)
    {
        var umbracoFile = media.GetValue<string>(Constants.Conventions.Media.File);
        
        if (string.IsNullOrEmpty(umbracoFile))
        {
            return string.Empty;
        }

        if (umbracoFile.StartsWith("{"))
        {
            try
            {
                var srcIndex = umbracoFile.IndexOf("\"src\":", StringComparison.OrdinalIgnoreCase);
                if (srcIndex > 0)
                {
                    var start = umbracoFile.IndexOf('"', srcIndex + 6) + 1;
                    var end = umbracoFile.IndexOf('"', start);
                    return umbracoFile.Substring(start, end - start);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        return umbracoFile;
    }

    private static string NormalizePath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        
        if (normalized.StartsWith("media/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(6);
        }

        return normalized.ToLowerInvariant();
    }

    private List<PhysicalFileInfo> FindOrphanedFiles(List<PhysicalFileInfo> physicalFiles, HashSet<string> databasePaths)
    {
        var orphaned = new List<PhysicalFileInfo>();

        foreach (var file in physicalFiles)
        {
            var fileName = Path.GetFileName(file.Path);
            if (fileName.StartsWith(".") || fileName == "Thumbs.db" || fileName == "desktop.ini")
            {
                continue;
            }

            if (!databasePaths.Contains(file.NormalizedPath))
            {
                orphaned.Add(file);
            }
        }

        return orphaned.OrderByDescending(f => f.SizeBytes).ToList();
    }

    private string BuildResultMessage(List<PhysicalFileInfo> orphanedFiles, long totalSize)
    {
        var sb = new StringBuilder();

        var totalSizeMB = Math.Round(totalSize / 1024.0 / 1024.0, 2);

        sb.Append(_localizedTextService.Localize("storageHealthChecks", "orphanedMedia.summary",
            new[] { orphanedFiles.Count.ToString(), totalSizeMB.ToString() }));
        sb.Append("<br/><br/>");

        sb.Append("<div style=\"background-color: #f5f5f5; padding: 12px 16px; border-radius: 6px; margin-bottom: 16px;\">");
        sb.Append($"<strong>{_localizedTextService.Localize("storageHealthChecks", "orphanedMedia.whyHeader")}</strong><br/>");
        sb.Append("<ul style=\"margin: 8px 0 0 0;\">");
        sb.Append($"<li>{_localizedTextService.Localize("storageHealthChecks", "orphanedMedia.whyDeleted")}</li>");
        sb.Append($"<li>{_localizedTextService.Localize("storageHealthChecks", "orphanedMedia.whyRestore")}</li>");
        sb.Append($"<li>{_localizedTextService.Localize("storageHealthChecks", "orphanedMedia.whyDirect")}</li>");
        sb.Append($"<li>{_localizedTextService.Localize("storageHealthChecks", "orphanedMedia.whyFailed")}</li>");
        sb.Append("</ul>");
        sb.Append("</div>");

        sb.Append($"<strong>{_localizedTextService.Localize("storageHealthChecks", "orphanedMedia.filesHeader")}</strong><br/>");
        sb.Append("<ul>");

        var filesToShow = orphanedFiles.Take(15).ToList();

        foreach (var file in filesToShow)
        {
            var sizeMB = Math.Round(file.SizeBytes / 1024.0 / 1024.0, 2);
            sb.Append($"<li><code>/media/{file.Path}</code> ({sizeMB} MB)</li>");
        }

        sb.Append("</ul>");

        if (orphanedFiles.Count > 15)
        {
            sb.Append($"<em>{_localizedTextService.Localize("storageHealthChecks", "orphanedMedia.moreItems", new[] { (orphanedFiles.Count - 15).ToString() })}</em><br/><br/>");
        }

        sb.Append($"<br/><em>{_localizedTextService.Localize("storageHealthChecks", "orphanedMedia.recommendation")}</em>");

        return sb.ToString();
    }

    private class PhysicalFileInfo
    {
        public string Path { get; set; } = string.Empty;
        public string NormalizedPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
    }
}
