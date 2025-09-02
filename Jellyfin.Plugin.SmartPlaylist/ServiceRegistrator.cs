using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using MediaBrowser.Common;

namespace Jellyfin.Plugin.SmartPlaylist
{
    /// <summary>
    /// Service registrator for SmartPlaylist plugin services.
    /// </summary>
    public sealed class ServiceRegistrator : IPluginServiceRegistrator
    {
        /// <summary>
        /// Registers services for the SmartPlaylist plugin.
        /// </summary>
        /// <param name="serviceCollection">The service collection.</param>
        /// <param name="applicationHost">The application host.</param>
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddHostedService<AutoRefreshHostedService>();
        }
    }
}
