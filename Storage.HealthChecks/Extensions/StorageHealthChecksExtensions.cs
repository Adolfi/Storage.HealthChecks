using Microsoft.Extensions.DependencyInjection;
using Storage.HealthChecks.Configuration;
using Umbraco.Cms.Core.DependencyInjection;

namespace Storage.HealthChecks.Extensions;

/// <summary>
/// Extension methods for registering Storage Health Checks configuration.
/// </summary>
public static class StorageHealthChecksExtensions
{
    /// <summary>
    /// Adds Storage Health Checks configuration from appsettings.json.
    /// Call this in Program.cs on the UmbracoBuilder.
    /// </summary>
    public static IUmbracoBuilder AddStorageHealthChecks(this IUmbracoBuilder builder)
    {
        builder.Services.Configure<StorageHealthCheckConfiguration>(
            builder.Config.GetSection(StorageHealthCheckConfiguration.SectionName));

        return builder;
    }
}
