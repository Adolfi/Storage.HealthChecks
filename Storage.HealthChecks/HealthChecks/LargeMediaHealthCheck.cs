using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Storage.HealthChecks.Configuration;
using System.Text;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Storage.HealthChecks.HealthChecks;

[HealthCheck(
    "F6A7B8C9-0D1E-2F3A-4B5C-6D7E8F9A0B1C",
    "Large media items",
    Description = "Checks for media items exceeding large file size threshold.",
    Group = "Media Storage")]
public class LargeMediaHealthCheck : HealthCheck
{
    private const int PageSize = 500;

    private readonly IMediaService _mediaService;
    private readonly ILogger<LargeMediaHealthCheck> _logger;
    private readonly StorageHealthCheckConfiguration _settings;
    private readonly long _maxFileSizeBytes;
    private readonly double _maxFileSizeMB;

    public LargeMediaHealthCheck(
        IMediaService mediaService,
        ILogger<LargeMediaHealthCheck> logger,
        IOptions<StorageHealthCheckConfiguration> settings)
    {
        _mediaService = mediaService;
        _logger = logger;
        _settings = settings.Value;
        _maxFileSizeMB = _settings.LargeMediaThresholdMB > 0 ? _settings.LargeMediaThresholdMB : 5.0;
        _maxFileSizeBytes = (long)(_maxFileSizeMB * 1024 * 1024);
    }

    public override Task<IEnumerable<HealthCheckStatus>> GetStatusAsync()
    {
        var status = CheckLargeMedia();
        return Task.FromResult<IEnumerable<HealthCheckStatus>>(new[] { status });
    }

    public override HealthCheckStatus ExecuteAction(HealthCheckAction action)
    {
        return new HealthCheckStatus("No actions available. Please optimize large files manually.")
        {
            ResultType = StatusResultType.Info
        };
    }

    private HealthCheckStatus CheckLargeMedia()
    {
        try
        {
            var largeMedia = FindLargeMedia();

            if (largeMedia.Count == 0)
            {
                return new HealthCheckStatus($"No media files exceeding {_maxFileSizeMB} MB found.")
                {
                    ResultType = StatusResultType.Success
                };
            }

            var totalExcess = largeMedia.Sum(i => i.SizeBytes - _maxFileSizeBytes);
            return new HealthCheckStatus(BuildResultMessage(largeMedia, totalExcess))
            {
                ResultType = StatusResultType.Info,
                ReadMoreLink = "https://github.com/Adolfi/Storage.HealthChecks#large-media-items"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during large media health check");
            return new HealthCheckStatus($"Error: {ex.Message}")
            {
                ResultType = StatusResultType.Error
            };
        }
    }

    private List<LargeMediaInfo> FindLargeMedia()
    {
        var largeMedia = new List<LargeMediaInfo>();
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

                if (_settings.ShouldIgnore(media.Key))
                    continue;

                var fileName = GetFileName(media);
                var fileSize = GetMediaFileSize(media);

                if (fileSize > _maxFileSizeBytes)
                {
                    largeMedia.Add(new LargeMediaInfo
                    {
                        Key = media.Key,
                        Name = media.Name ?? "(unnamed)",
                        FileName = fileName,
                        SizeBytes = fileSize
                    });
                }
            }

            if ((pageIndex + 1) * PageSize >= totalRecords) break;
            pageIndex++;
        }

        return largeMedia.OrderByDescending(i => i.SizeBytes).ToList();
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

    private string BuildResultMessage(List<LargeMediaInfo> largeMedia, long totalExcessBytes)
    {
        var sb = new StringBuilder();
        var totalExcessMB = Math.Round(totalExcessBytes / 1024.0 / 1024.0, 2);

        sb.Append($"Found <strong>{largeMedia.Count}</strong> file{(largeMedia.Count == 1 ? "" : "s")} ");
        sb.Append($"exceeding {_maxFileSizeMB} MB ({totalExcessMB} MB total excess).<br/><br/><ul>");

        foreach (var file in largeMedia.Take(20))
        {
            var sizeMB = Math.Round(file.SizeBytes / 1024.0 / 1024.0, 2);
            var link = $"/umbraco/section/media/workspace/media/edit/{file.Key}";
            sb.Append($"<li><a href=\"{link}\" target=\"_blank\">{file.Name}</a> ({file.FileName}) - <strong>{sizeMB} MB</strong></li>");
        }

        sb.Append("</ul>");
        if (largeMedia.Count > 20)
            sb.Append($"<em>...and {largeMedia.Count - 20} more</em><br/>");

        sb.Append($"<br/><em>Files exceeding {_maxFileSizeMB} MB should be optimized or compressed.</em>");
        return sb.ToString();
    }

    private class LargeMediaInfo
    {
        public Guid Key { get; set; }
        public string Name { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
    }
}
