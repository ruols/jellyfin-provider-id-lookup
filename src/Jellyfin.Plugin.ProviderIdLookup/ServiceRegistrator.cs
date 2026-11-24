using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ProviderIdLookup;

public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddSingleton<IStartupFilter, RequestTraceStartupFilter>();
        services.AddScoped<ProviderIdLookupFilter>();
        services.Configure<MvcOptions>(options => options.Filters.AddService<ProviderIdLookupFilter>());
    }
}
