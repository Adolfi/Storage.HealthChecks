using Microsoft.Extensions.DependencyInjection;
using Storage.HealthChecks.Configuration;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Storage.HealthChecks.Composers;

public class StorageHealthChecksComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.Configure<StorageHealthCheckConfiguration>(
            builder.Config.GetSection(StorageHealthCheckConfiguration.SectionName));
    }
}
