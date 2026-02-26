using Microsoft.Extensions.Logging;
using System.Text;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;
using Storage.HealthChecks.Extensions;

namespace Storage.HealthChecks.HealthChecks;

[HealthCheck(
    "E5F6A7B8-9C0D-1E2F-3A4B-5C6D7E8F9A0B",
    "Duplicate media items",
    Description = "Checks for duplicate media items based on filename and file size.",
    Group = "Media Storage")]
public class DuplicateMediaHealthCheck : HealthCheck
{
    private const int PageSize = 500;

    private readonly IMediaService _mediaService;
    private readonly ILogger<DuplicateMediaHealthCheck> _logger;
    private readonly ILocalizedTextService _localizedTextService;

    public DuplicateMediaHealthCheck(
        IMediaService mediaService,
        ILogger<DuplicateMediaHealthCheck> logger,
        ILocalizedTextService localizedTextService)
    {
        _mediaService = mediaService;
        _logger = logger;
        _localizedTextService = localizedTextService;
    }

    public override Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
    {
        var status = CheckDuplicates();
        return Task.FromResult<IEnumerable<HealthCheckStatus>>(new[] { status });
    }

    public override HealthCheckStatus ExecuteAction(HealthCheckAction action)
    {
        return new HealthCheckStatus(_localizedTextService.LocalizeWithFallback("storageHealthChecks", "duplicateMedia.noActions"))
        {
            ResultType = StatusResultType.Info
        };
    }

    private HealthCheckStatus CheckDuplicates()
    {
        try
        {
            var allMedia = CollectAllMediaInfo();
            var duplicateGroups = FindDuplicates(allMedia);

            if (duplicateGroups.Count == 0)
            {
                return new HealthCheckStatus(_localizedTextService.LocalizeWithFallback("storageHealthChecks", "duplicateMedia.noIssues"))
                {
                    ResultType = StatusResultType.Success
                };
            }

            var totalDuplicates = duplicateGroups.Sum(g => g.Items.Count - 1);
            var wastedBytes = duplicateGroups.Sum(g => g.Items.Skip(1).Sum(i => i.SizeBytes));

            return new HealthCheckStatus(BuildResultMessage(duplicateGroups, totalDuplicates, wastedBytes))
            {
                ResultType = StatusResultType.Info,
                ReadMoreLink = "https://github.com/Adolfi/Storage.HealthChecks#duplicate-media-items"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during duplicate media health check");
            return new HealthCheckStatus(_localizedTextService.LocalizeWithFallback("storageHealthChecks", "duplicateMedia.error", new[] { ex.Message }))
            {
                ResultType = StatusResultType.Error
            };
        }
    }

    private List<MediaInfo> CollectAllMediaInfo()
    {
        var allMedia = new List<MediaInfo>();
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
                    continue;

                var fileName = GetFileName(media);
                var fileSize = GetMediaFileSize(media);

                if (!string.IsNullOrEmpty(fileName) && fileSize > 0)
                {
                    allMedia.Add(new MediaInfo
                    {
                        Key = media.Key,
                        Name = media.Name ?? "(unnamed)",
                        FileName = fileName,
                        SizeBytes = fileSize,
                        CreateDate = media.CreateDate
                    });
                }
            }

            if ((pageIndex + 1) * PageSize >= totalRecords) break;
            pageIndex++;
        }

        return allMedia;
    }

    private List<DuplicateGroup> FindDuplicates(List<MediaInfo> allMedia)
    {
        return allMedia
            .GroupBy(m => new { FileName = m.FileName.ToLowerInvariant(), m.SizeBytes })
            .Where(g => g.Count() > 1)
            .Select(g => new DuplicateGroup
            {
                FileName = g.First().FileName,
                SizeBytes = g.Key.SizeBytes,
                Items = g.OrderBy(i => i.CreateDate).ToList()
            })
            .OrderByDescending(g => g.SizeBytes * (g.Items.Count - 1))
            .ToList();
    }

    private static string GetFileName(IMedia media) => Path.GetFileName(GetFilePath(media));

    private static string GetFilePath(IMedia media)
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

    private static long GetMediaFileSize(IMedia media)
    {
        var bytesProperty = media.GetValue<string>(Constants.Conventions.Media.Bytes);
        return !string.IsNullOrEmpty(bytesProperty) && long.TryParse(bytesProperty, out var bytes) ? bytes : 0;
    }

    private string BuildResultMessage(List<DuplicateGroup> groups, int totalDuplicates, long wastedBytes)
    {
        var sb = new StringBuilder();
        var wastedMB = Math.Round(wastedBytes / 1024.0 / 1024.0, 2);

        sb.Append(_localizedTextService.LocalizeWithFallback("storageHealthChecks", "duplicateMedia.summary",
            new[] { totalDuplicates.ToString(), groups.Count.ToString(), wastedMB.ToString() }));
        sb.Append("<br/><br/>");

        foreach (var group in groups.Take(5))
        {
            var groupWastedMB = Math.Round((group.SizeBytes * (group.Items.Count - 1)) / 1024.0 / 1024.0, 2);
            var groupHeader = _localizedTextService.LocalizeWithFallback("storageHealthChecks", "duplicateMedia.groupHeader",
                new[] { group.Items.Count.ToString(), groupWastedMB.ToString() });
            sb.Append($"<strong>{group.FileName}</strong> ({groupHeader})<br/><ul>");

            foreach (var item in group.Items.Take(5))
            {
                var link = $"/umbraco/section/media/workspace/media/edit/{item.Key}";
                var label = item == group.Items.First()
                    ? $" {_localizedTextService.LocalizeWithFallback("storageHealthChecks", "duplicateMedia.original")}"
                    : "";
                sb.Append($"<li><a href=\"{link}\" target=\"_blank\">{item.Name}</a>{label}</li>");
            }

            if (group.Items.Count > 5)
                sb.Append($"<li><em>{_localizedTextService.LocalizeWithFallback("storageHealthChecks", "duplicateMedia.moreItems", new[] { (group.Items.Count - 5).ToString() })}</em></li>");

            sb.Append("</ul>");
        }

        if (groups.Count > 5)
            sb.Append($"<em>{_localizedTextService.LocalizeWithFallback("storageHealthChecks", "duplicateMedia.moreGroups", new[] { (groups.Count - 5).ToString() })}</em><br/>");

        sb.Append($"<br/><em>{_localizedTextService.LocalizeWithFallback("storageHealthChecks", "duplicateMedia.recommendation")}</em>");
        return sb.ToString();
    }

    private class MediaInfo
    {
        public Guid Key { get; set; }
        public string Name { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime CreateDate { get; set; }
    }

    private class DuplicateGroup
    {
        public string FileName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public List<MediaInfo> Items { get; set; } = new();
    }
}
