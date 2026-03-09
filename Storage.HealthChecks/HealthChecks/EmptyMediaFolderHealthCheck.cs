using Microsoft.Extensions.Logging;
using System.Text;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Core.Services;

namespace Storage.HealthChecks.HealthChecks;

/// <summary>
/// Health check that identifies empty media folders in the Umbraco Media section.
/// Empty folders do not serve any purpose and can clutter the Media section.
/// </summary>
[HealthCheck(
    "C7D8E9F0-A1B2-3C4D-5E6F-7A8B9C0D1E2F",
    "Empty media folders",
    Description = "Checks for media folders that contain no children.",
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
            var emptyFolders = FindEmptyFolders();

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
            _logger.LogError(ex, "Error during empty media folders health check");
            return new HealthCheckStatus($"Error: {ex.Message}")
            {
                ResultType = StatusResultType.Error
            };
        }
    }

    private List<FolderInfo> FindEmptyFolders()
    {
        var allFolders = new Dictionary<int, FolderInfo>();
        var parentIds = new HashSet<int>();
        var pageIndex = 0L;

        while (true)
        {
            var mediaPage = _mediaService.GetPagedDescendants(
                Constants.System.Root, pageIndex, PageSize, out var totalRecords);

            var mediaList = mediaPage.ToList();
            if (mediaList.Count == 0) break;

            foreach (var media in mediaList)
            {
                parentIds.Add(media.ParentId);

                if (media.ContentType.Alias.Equals("Folder", StringComparison.OrdinalIgnoreCase))
                {
                    allFolders[media.Id] = new FolderInfo
                    {
                        Key = media.Key,
                        Name = media.Name ?? "(unnamed)",
                        Path = media.Path
                    };
                }
            }

            if ((pageIndex + 1) * PageSize >= totalRecords) break;
            pageIndex++;
        }

        return allFolders
            .Where(kvp => !parentIds.Contains(kvp.Key))
            .Select(kvp => kvp.Value)
            .ToList();
    }

    private string BuildResultMessage(List<FolderInfo> emptyFolders)
    {
        var sb = new StringBuilder();

        sb.Append($"Found <strong>{emptyFolders.Count}</strong> empty media folder{(emptyFolders.Count == 1 ? "" : "s")}.<br/><br/>");

        sb.Append("<strong>Empty folders:</strong><br/><ul>");

        foreach (var folder in emptyFolders.Take(15))
        {
            var link = $"/umbraco/section/media/workspace/media/edit/{folder.Key}";
            sb.Append($"<li><a href=\"{link}\" target=\"_blank\"><strong>{folder.Name}</strong></a></li>");
        }

        sb.Append("</ul>");

        if (emptyFolders.Count > 15)
            sb.Append($"<em>...and {emptyFolders.Count - 15} more</em><br/>");

        sb.Append("<br/><em>Review and remove any empty folders that are no longer needed.</em>");
        return sb.ToString();
    }

    private class FolderInfo
    {
        public Guid Key { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }
}
