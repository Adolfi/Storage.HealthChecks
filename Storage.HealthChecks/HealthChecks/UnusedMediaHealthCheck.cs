using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Storage.HealthChecks.Configuration;
using System.Text;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;
using Storage.HealthChecks.Extensions;

namespace Storage.HealthChecks.HealthChecks;

[HealthCheck(
    "E4F8A1B2-3C4D-5E6F-7A8B-9C0D1E2F3A4B",
    "Unused media items",
    Description = "Finds media items that have no tracked references from Umbraco content.",
    Group = "Media Storage")]
public class UnusedMediaHealthCheck : HealthCheck
{
    private const int PageSize = 500;
    
    private readonly IMediaService _mediaService;
    private readonly ITrackedReferencesService? _trackedReferencesService;
    private readonly ILogger<UnusedMediaHealthCheck> _logger;
    private readonly StorageHealthCheckConfiguration _settings;
    private readonly ILocalizedTextService _localizedTextService;

    public UnusedMediaHealthCheck(
        IMediaService mediaService,
        ILogger<UnusedMediaHealthCheck> logger,
        IOptions<StorageHealthCheckConfiguration> settings,
        ILocalizedTextService localizedTextService,
        ITrackedReferencesService? trackedReferencesService = null)
    {
        _mediaService = mediaService;
        _logger = logger;
        _settings = settings.Value;
        _localizedTextService = localizedTextService;
        _trackedReferencesService = trackedReferencesService;
    }

    public override async Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
    {
        return new[] { await CheckUnusedMediaAsync(CancellationToken.None) };
    }

    public override HealthCheckStatus ExecuteAction(HealthCheckAction action)
    {
        return new HealthCheckStatus(_localizedTextService.LocalizeWithFallback("storageHealthChecks", "unusedMedia.noActions"))
        {
            ResultType = StatusResultType.Info
        };
    }

    private async Task<HealthCheckStatus> CheckUnusedMediaAsync(CancellationToken cancellationToken)
    {
        if (_trackedReferencesService is null)
        {
            return new HealthCheckStatus(
                _localizedTextService.LocalizeWithFallback("storageHealthChecks", "unusedMedia.serviceUnavailable"))
            {
                ResultType = StatusResultType.Error
            };
        }

        try
        {
            var allMediaInfo = CollectAllMediaInfo(cancellationToken);
            var unusedMediaItems = new List<UnusedMediaInfo>();
            
            foreach (var mediaInfo in allMediaInfo)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
#pragma warning disable CS0618
                    var references = await _trackedReferencesService.GetPagedRelationsForItemAsync(
                        mediaInfo.Key, skip: 0, take: 1, filterMustBeIsDependency: true);
#pragma warning restore CS0618

                    if (references.Total == 0)
                        unusedMediaItems.Add(mediaInfo);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking references for media {MediaId}", mediaInfo.Id);
                }
            }

            if (unusedMediaItems.Count == 0)
            {
                return new HealthCheckStatus(_localizedTextService.LocalizeWithFallback("storageHealthChecks", "unusedMedia.noIssues"))
                {
                    ResultType = StatusResultType.Success
                };
            }

            return new HealthCheckStatus(BuildResultMessage(unusedMediaItems))
            {
                ResultType = StatusResultType.Warning,
                ReadMoreLink = "https://github.com/Adolfi/Storage.HealthChecks#unused-media-items"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during unused media health check");
            return new HealthCheckStatus(_localizedTextService.LocalizeWithFallback("storageHealthChecks", "unusedMedia.error", new[] { ex.Message }))
            {
                ResultType = StatusResultType.Error
            };
        }
    }

    private List<UnusedMediaInfo> CollectAllMediaInfo(CancellationToken cancellationToken)
    {
        var allMediaInfo = new List<UnusedMediaInfo>();
        var pageIndex = 0L;

        while (!cancellationToken.IsCancellationRequested)
        {
            var mediaPage = _mediaService.GetPagedDescendants(
                Constants.System.Root, pageIndex, PageSize, out var totalRecords);

            var mediaList = mediaPage.ToList();
            if (mediaList.Count == 0) break;

            foreach (var media in mediaList)
            {
                if (media.ContentType.Alias.Equals("Folder", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (_settings.ShouldIgnore(media.Key))
                    continue;

                allMediaInfo.Add(new UnusedMediaInfo
                {
                    Id = media.Id,
                    Key = media.Key,
                    Name = media.Name ?? "(unnamed)",
                    SizeBytes = GetMediaFileSize(media)
                });
            }

            if ((pageIndex + 1) * PageSize >= totalRecords) break;
            pageIndex++;
        }

        return allMediaInfo;
    }

    private static long GetMediaFileSize(IMedia media)
    {
        var bytesProperty = media.GetValue<string>(Constants.Conventions.Media.Bytes);
        return !string.IsNullOrEmpty(bytesProperty) && long.TryParse(bytesProperty, out var bytes) ? bytes : 0;
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

    private string BuildResultMessage(List<UnusedMediaInfo> unusedItems)
    {
        var sb = new StringBuilder();
        var totalBytes = unusedItems.Sum(x => x.SizeBytes);
        var totalMB = Math.Round(totalBytes / 1024.0 / 1024.0, 2);

        sb.Append(_localizedTextService.LocalizeWithFallback("storageHealthChecks", "unusedMedia.summary",
            new[] { unusedItems.Count.ToString(), totalMB.ToString() }));
        sb.Append("<br/><br/>");

        sb.Append("<div style=\"background-color: #f5f5f5; padding: 12px 16px; border-radius: 6px; margin-bottom: 16px;\">");
        sb.Append($"<strong>{_localizedTextService.LocalizeWithFallback("storageHealthChecks", "unusedMedia.whyHeader")}</strong><br/>");
        sb.Append("<ul style=\"margin: 8px 0 0 0;\">");
        sb.Append($"<li>{_localizedTextService.LocalizeWithFallback("storageHealthChecks", "unusedMedia.whyNoRef")}</li>");
        sb.Append($"<li>{_localizedTextService.LocalizeWithFallback("storageHealthChecks", "unusedMedia.whyNoDocProp")}</li>");
        sb.Append($"<li>{_localizedTextService.LocalizeWithFallback("storageHealthChecks", "unusedMedia.whyHardcoded")}</li>");
        sb.Append($"<li>{_localizedTextService.LocalizeWithFallback("storageHealthChecks", "unusedMedia.whyExternal")}</li>");
        sb.Append("</ul>");
        sb.Append("</div>");

        sb.Append($"<strong>{_localizedTextService.LocalizeWithFallback("storageHealthChecks", "unusedMedia.itemsHeader")}</strong><br/>");
        sb.Append("<ul>");

        var itemsToShow = unusedItems.Take(15).ToList();

        foreach (var item in itemsToShow)
        {
            var link = $"/umbraco/section/media/workspace/media/edit/{item.Key}";
            sb.Append($"<li><a href=\"{link}\" target=\"_blank\">{item.Name}</a> ({item.SizeMB} MB)</li>");
        }

        sb.Append("</ul>");

        if (unusedItems.Count > 15)
        {
            sb.Append($"<em>{_localizedTextService.LocalizeWithFallback("storageHealthChecks", "unusedMedia.moreItems", new[] { (unusedItems.Count - 15).ToString() })}</em><br/><br/>");
        }

        sb.Append($"<br/><em>{_localizedTextService.LocalizeWithFallback("storageHealthChecks", "unusedMedia.recommendation")}</em>");

        return sb.ToString();
    }

    private class UnusedMediaInfo
    {
        public int Id { get; set; }
        public Guid Key { get; set; }
        public string Name { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public double SizeMB => Math.Round(SizeBytes / 1024.0 / 1024.0, 2);
    }
}
