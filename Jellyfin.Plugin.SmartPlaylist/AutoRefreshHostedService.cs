using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartPlaylist
{
    /// <summary>
    /// Hosted service that initializes the AutoRefreshService when Jellyfin starts.
    /// </summary>
    public class AutoRefreshHostedService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AutoRefreshHostedService> _logger;
        private AutoRefreshService _autoRefreshService;

        public AutoRefreshHostedService(IServiceProvider serviceProvider, ILogger<AutoRefreshHostedService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting SmartPlaylist AutoRefreshService...");
                
                // Get required services from DI container
                var libraryManager = _serviceProvider.GetRequiredService<ILibraryManager>();
                var userManager = _serviceProvider.GetRequiredService<IUserManager>();
                var playlistManager = _serviceProvider.GetRequiredService<IPlaylistManager>();
                var userDataManager = _serviceProvider.GetRequiredService<IUserDataManager>();
                var providerManager = _serviceProvider.GetRequiredService<IProviderManager>();
                var serverApplicationPaths = _serviceProvider.GetRequiredService<IServerApplicationPaths>();
                var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
                
                var autoRefreshLogger = loggerFactory.CreateLogger<AutoRefreshService>();
                var playlistServiceLogger = loggerFactory.CreateLogger<PlaylistService>();
                
                var fileSystem = new SmartPlaylistFileSystem(serverApplicationPaths);
                var playlistStore = new SmartPlaylistStore(fileSystem, userManager);
                var playlistService = new PlaylistService(userManager, libraryManager, playlistManager, userDataManager, playlistServiceLogger, providerManager);
                
                _autoRefreshService = new AutoRefreshService(libraryManager, autoRefreshLogger, playlistStore, playlistService, userDataManager, userManager);
                
                _logger.LogInformation("SmartPlaylist AutoRefreshService started successfully (schedule timer initialized)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start AutoRefreshService");
            }
            
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Stopping SmartPlaylist AutoRefreshService...");
                _autoRefreshService?.Dispose();
                _autoRefreshService = null;
                _logger.LogInformation("SmartPlaylist AutoRefreshService stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping AutoRefreshService");
            }
            
            return Task.CompletedTask;
        }
    }
}
