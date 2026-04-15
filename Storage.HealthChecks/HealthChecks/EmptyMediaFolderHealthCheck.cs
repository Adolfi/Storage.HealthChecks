using Microsoft.Extensions.Logging;
using System.Text;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Storage.HealthChecks.HealthChecks;

[HealthCheck(
    "F9A1B2C3-D4E5-6F7A-8B9C-0D1E2F3A4B5C",
    "Empty media folders",
    Description = "Finds media folders that contain no child items (neither files nor subfolders).",
    Group = "Media Storage")]
public class EmptyMediaFolderHealthCheck : HealthCheck
{
    private const int PageSize = 500;

    private readonly IMediaService _mediaService;
    private readonly ILogger<EmptyMediaFolderHealthCheck> _logger;

    public EmptyMediaFolderHealthCheck(
        IMediaService mediaService,
        ILogger<EmptyMediaFolderHealthCheck> logger)
    {
        _mediaService = mediaService;
        _logger = logger;
    }

    public override Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
    {
        var status = CheckEmptyFolders();
        return Task.FromResult<IEnumerable<HealthCheckStatus>>(new[] { status });
    }

    public override HealthCheckStatus ExecuteAction(HealthCheckAction action)
    {
        return new HealthCheckStatus("No actions available. Please review and remove empty folders manually.")
        {
            ResultType = StatusResultType.Info
        };
    }

    private HealthCheckStatus CheckEmptyFolders()
    {
        try
        {
            var allFolders = CollectAllFolders();
            var emptyFolders = FindEmptyFolders(allFolders);

            if (emptyFolders.Count == 0)
            {
                return new HealthCheckStatus("No empty media folders found.")
                {
                    ResultType = StatusResultType.Success
                };
            }

            return new HealthCheckStatus(BuildResultMessage(emptyFolders))
            {
                ResultType = StatusResultType.Info,
                ReadMoreLink = "https://github.com/Adolfi/Storage.HealthChecks#empty-media-folders"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during empty media folder health check");
            return new HealthCheckStatus($"Error: {ex.Message}")
            {
                ResultType = StatusResultType.Error
            };
        }
    }

    private List<FolderInfo> CollectAllFolders()
    {
        var allFolders = new List<FolderInfo>();
        var pageIndex = 0L;

        while (true)
        {
            var mediaPage = _mediaService.GetPagedDescendants(
                Constants.System.Root, pageIndex, PageSize, out var totalRecords);

            var mediaList = mediaPage.ToList();
            if (mediaList.Count == 0) break;

            foreach (var media in mediaList)
            {
                if (media.ContentType.Alias.Equals("Folder", StringComparison.OrdinalIgnoreCase))
                {
                    allFolders.Add(new FolderInfo
                    {
                        Id = media.Id,
                        Key = media.Key,
                        Name = media.Name ?? "(unnamed)",
                        Path = media.Path,
                        Level = media.Level
                    });
                }
            }

            if ((pageIndex + 1) * PageSize >= totalRecords) break;
            pageIndex++;
        }

        return allFolders;
    }

    private List<FolderInfo> FindEmptyFolders(List<FolderInfo> allFolders)
    {
        var emptyFolders = new List<FolderInfo>();

        foreach (var folder in allFolders)
        {
            try
            {
                var children = _mediaService.GetPagedChildren(folder.Id, 0, 1, out var totalChildren);

                if (totalChildren == 0)
                {
                    emptyFolders.Add(folder);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking children for folder {FolderId}", folder.Id);
            }
        }

        return emptyFolders.OrderBy(f => f.Level).ThenBy(f => f.Name).ToList();
    }

    private string BuildResultMessage(List<FolderInfo> emptyFolders)
    {
        var sb = new StringBuilder();

        sb.Append($"Found <strong>{emptyFolders.Count}</strong> empty media folder{(emptyFolders.Count == 1 ? "" : "s")}.<br/><br/>");

        sb.Append("<div style=\"background-color: #f5f5f5; padding: 12px 16px; border-radius: 6px; margin-bottom: 16px;\">");
        sb.Append("<strong>Why are these folders empty?</strong><br/>");
        sb.Append("<ul style=\"margin: 8px 0 0 0;\">");
        sb.Append("<li>Media items may have been moved or deleted</li>");
        sb.Append("<li>Folders were created but never used</li>");
        sb.Append("<li>Content was removed during cleanup operations</li>");
        sb.Append("</ul>");
        sb.Append("</div>");

        sb.Append("<strong>Empty folders:</strong><br/>");
        sb.Append("<ul>");

        var itemsToShow = emptyFolders.Take(20).ToList();

        foreach (var folder in itemsToShow)
        {
            var link = $"/umbraco/section/media/workspace/media/edit/{folder.Key}";
            sb.Append($"<li><a href=\"{link}\" target=\"_blank\">{folder.Name}</a></li>");
        }

        sb.Append("</ul>");

        if (emptyFolders.Count > 20)
        {
            sb.Append($"<em>...and {emptyFolders.Count - 20} more empty folders</em><br/><br/>");
        }

        sb.Append("<br/><em>Review these folders and delete them if they are no longer needed.</em>");

        return sb.ToString();
    }

    private class FolderInfo
    {
        public int Id { get; set; }
        public Guid Key { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public int Level { get; set; }
    }
}
