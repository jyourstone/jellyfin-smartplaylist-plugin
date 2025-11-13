using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using MediaBrowser.Common;
using Jellyfin.Plugin.SmartLists.Services.Shared;

namespace Jellyfin.Plugin.SmartLists
{
    /// <summary>
    /// Service registrator for SmartLists plugin services.
    /// </summary>
    public sealed class ServiceRegistrator : IPluginServiceRegistrator
    {
        /// <summary>
        /// Registers services for the SmartLists plugin.
        /// </summary>
        /// <param name="serviceCollection">The service collection.</param>
        /// <param name="applicationHost">The application host.</param>
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddHostedService<AutoRefreshHostedService>();
            serviceCollection.AddScoped<IManualRefreshService, ManualRefreshService>();
        }
    }
}

