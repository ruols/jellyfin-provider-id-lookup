using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ProviderIdLookup;

public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddScoped<ProviderIdLookupFilter>();
        services.Configure<MvcOptions>(options => options.Filters.AddService<ProviderIdLookupFilter>());
    }
}
