using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Entities;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartPlaylist
{
    /// <summary>
    /// Service for handling manual refresh operations initiated by users.
    /// This includes both individual playlist refreshes and "Refresh All" operations from the UI.
    /// </summary>
    public interface IManualRefreshService
    {
        /// <summary>
        /// Refresh all smart playlists manually, bypassing the Jellyfin task system.
        /// This method processes ALL playlists regardless of their ScheduleTrigger settings.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Tuple of (success, message)</returns>
        Task<(bool Success, string Message)> RefreshAllPlaylistsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Refresh a single smart playlist manually.
        /// </summary>
        /// <param name="playlist">The playlist to refresh</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Tuple of (success, message, jellyfinPlaylistId)</returns>
        Task<(bool Success, string Message, string JellyfinPlaylistId)> RefreshSinglePlaylistAsync(SmartPlaylistDto playlist, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Implementation of manual refresh service that handles user-initiated refresh operations.
    /// This consolidates logic for both individual playlist refreshes and "refresh all" operations.
    /// </summary>
    public class ManualRefreshService(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IServerApplicationPaths applicationPaths,
        IPlaylistManager playlistManager,
        IUserDataManager userDataManager,
        IProviderManager providerManager,
        ILogger<ManualRefreshService> logger,
        Microsoft.Extensions.Logging.ILoggerFactory loggerFactory) : IManualRefreshService
    {
        private readonly IUserManager _userManager = userManager;
        private readonly ILibraryManager _libraryManager = libraryManager;
        private readonly IServerApplicationPaths _applicationPaths = applicationPaths;
        private readonly IPlaylistManager _playlistManager = playlistManager;
        private readonly IUserDataManager _userDataManager = userDataManager;
        private readonly IProviderManager _providerManager = providerManager;
        private readonly ILogger<ManualRefreshService> _logger = logger;
        private readonly Microsoft.Extensions.Logging.ILoggerFactory _loggerFactory = loggerFactory;

        /// <summary>
        /// Gets the user for a playlist, handling migration from old User field to new UserId field.
        /// </summary>
        /// <param name="playlist">The playlist.</param>
        /// <returns>The user, or null if not found.</returns>
        private async Task<User> GetPlaylistUserAsync(SmartPlaylistDto playlist)
        {
            var userId = await GetPlaylistUserIdAsync(playlist);
            if (userId == Guid.Empty)
            {
                return null;
            }
            
            return _userManager.GetUserById(userId);
        }

        /// <summary>
        /// Gets the user ID for a playlist, handling migration from old User field to new UserId field.
        /// This is a simplified version of the logic from SmartPlaylistController.
        /// </summary>
        /// <param name="playlist">The playlist.</param>
        /// <returns>The user ID, or Guid.Empty if not found.</returns>
        private Task<Guid> GetPlaylistUserIdAsync(SmartPlaylistDto playlist)
        {
            // If new UserId field is set and not empty, use it
            if (playlist.UserId != Guid.Empty)
            {
                return Task.FromResult(playlist.UserId);
            }

            // Handle migration from old User field (backward compatibility)
#pragma warning disable CS0618 // Type or member is obsolete
            if (!string.IsNullOrEmpty(playlist.User))
            {
                var user = _userManager.GetUserByName(playlist.User);
                if (user != null)
                {
                    _logger.LogInformation("Migrating playlist '{PlaylistName}' from username '{UserName}' to User ID '{UserId}'",
                        playlist.Name, playlist.User, user.Id);
                    
                    // Update the playlist with the resolved user ID
                    playlist.UserId = user.Id;
                    playlist.User = null; // Clear the old field
                    
                    return Task.FromResult(user.Id);
                }
            }
#pragma warning restore CS0618

            return Task.FromResult(Guid.Empty);
        }

        /// <summary>
        /// Creates a PlaylistService instance for playlist operations.
        /// </summary>
        private IPlaylistService GetPlaylistService()
        {
            // Create a logger specifically for PlaylistService
            var playlistServiceLogger = _loggerFactory.CreateLogger<PlaylistService>();
            
            return new PlaylistService(
                _userManager,
                _libraryManager,
                _playlistManager,
                _userDataManager,
                playlistServiceLogger,
                _providerManager);
        }

        /// <summary>
        /// Refresh all smart playlists manually without using Jellyfin scheduled tasks.
        /// This method performs the same work as the scheduled tasks but processes ALL playlists
        /// regardless of their ScheduleTrigger settings, since this is a manual operation.
        /// </summary>
        public async Task<(bool Success, string Message)> RefreshAllPlaylistsAsync(CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                _logger.LogInformation("Starting direct refresh of all smart playlists (manual trigger)");

                // Create playlist store
                var fileSystem = new SmartPlaylistFileSystem(_applicationPaths);
                var plStore = new SmartPlaylistStore(fileSystem, _userManager);
                var playlistService = GetPlaylistService();

                var allDtos = await plStore.GetAllSmartPlaylistsAsync().ConfigureAwait(false);
                
                _logger.LogInformation("Found {TotalCount} total playlists for manual refresh", allDtos.Length);
                
                if (allDtos.Length == 0)
                {
                    stopwatch.Stop();
                    var message = "No playlists found to refresh";
                    _logger.LogInformation("{Message} (completed in {ElapsedTime}ms)", message, stopwatch.ElapsedMilliseconds);
                    return (true, message);
                }
                
                // Log disabled playlists for informational purposes
                var disabledPlaylists = allDtos.Where(dto => !dto.Enabled).ToList();
                if (disabledPlaylists.Count > 0)
                {
                    var disabledNames = string.Join(", ", disabledPlaylists.Select(p => $"'{p.Name}'"));
                    _logger.LogDebug("Skipping {DisabledCount} disabled playlists: {DisabledNames}", disabledPlaylists.Count, disabledNames);
                }

                // Process all enabled playlists (not just legacy ones - this is a manual trigger)
                var enabledPlaylists = allDtos.Where(dto => dto.Enabled).ToList();
                _logger.LogInformation("Processing {EnabledCount} enabled playlists", enabledPlaylists.Count);

                // Pre-resolve users and group playlists by user
                var resolvedPlaylists = new List<(SmartPlaylistDto dto, User user)>();
                
                foreach (var dto in enabledPlaylists)
                {
                    var user = await GetPlaylistUserAsync(dto);
                    if (user != null)
                    {
                        resolvedPlaylists.Add((dto, user));
                    }
                    else
                    {
                        _logger.LogWarning("User not found for playlist '{PlaylistName}'. Skipping.", dto.Name);
                    }
                }

                // Group by user for efficient media caching
                var playlistsByUser = resolvedPlaylists
                    .GroupBy(p => p.user.Id)
                    .ToDictionary(g => g.Key, g => g.ToList());

                _logger.LogDebug("Grouped {UserCount} users with {TotalPlaylists} playlists after user resolution", 
                    playlistsByUser.Count, resolvedPlaylists.Count);

                // Cache media per user
                var userMediaCache = new Dictionary<Guid, BaseItem[]>();
                
                foreach (var (userId, userPlaylistPairs) in playlistsByUser)
                {
                    var user = userPlaylistPairs.First().user;
                    
                    // Get all media for this user (both audio and non-audio)
                    var allUserMedia = playlistService.GetAllUserMediaForPlaylist(user, new List<string>()).ToArray();
                    userMediaCache[userId] = allUserMedia;
                    
                    _logger.LogDebug("Cached {MediaCount} items for user '{Username}' ({UserId}) - will be shared across {PlaylistCount} playlists", 
                        allUserMedia.Length, user.Username, userId, userPlaylistPairs.Count);
                }

                // Process playlists using cached media
                var processedCount = 0;
                var successCount = 0;
                var failureCount = 0;

                foreach (var (userId, userPlaylistPairs) in playlistsByUser)
                {
                    var user = userPlaylistPairs.First().user;
                    var userPlaylists = userPlaylistPairs.Select(p => p.dto).ToList();
                    var relevantUserMedia = userMediaCache[userId];
                    
                    _logger.LogDebug("Processing {PlaylistCount} playlists for user '{Username}' using cached media ({MediaCount} items)", 
                        userPlaylists.Count, user.Username, relevantUserMedia.Length);

                    foreach (var dto in userPlaylists)
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            // Get playlist-specific media based on MediaTypes
                            var playlistSpecificMedia = playlistService.GetAllUserMediaForPlaylist(user, dto.MediaTypes?.ToList() ?? new List<string>(), dto).ToArray();
                            
                            var refreshResult = await playlistService.ProcessPlaylistRefreshWithCachedMediaAsync(
                                dto, 
                                user, 
                                playlistSpecificMedia,
                                async (updatedDto) => await plStore.SaveAsync(updatedDto),
                                cancellationToken);
                            
                            if (refreshResult.Success)
                            {
                                // Save the playlist to persist LastRefreshed timestamp
                                await plStore.SaveAsync(dto);
                                successCount++;
                                _logger.LogDebug("Playlist {PlaylistName} processed successfully: {Message}", dto.Name, refreshResult.Message);
                            }
                            else
                            {
                                failureCount++;
                                _logger.LogWarning("Playlist {PlaylistName} processing failed: {Message}", dto.Name, refreshResult.Message);
                            }
                            
                            processedCount++;
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogInformation("Direct refresh operation was cancelled");
                            throw;
                        }
                        catch (Exception ex)
                        {
                            failureCount++;
                            processedCount++;
                            _logger.LogError(ex, "Error processing playlist {PlaylistName}", dto.Name);
                        }
                    }
                }

                stopwatch.Stop();
                var resultMessage = $"Direct refresh completed: {successCount} successful, {failureCount} failed out of {processedCount} processed playlists (completed in {stopwatch.ElapsedMilliseconds}ms)";
                _logger.LogInformation(resultMessage);
                
                return (true, resultMessage);
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                _logger.LogInformation("Direct playlist refresh was cancelled (after {ElapsedTime}ms)", stopwatch.ElapsedMilliseconds);
                return (false, "Refresh operation was cancelled");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error during direct playlist refresh (after {ElapsedTime}ms)", stopwatch.ElapsedMilliseconds);
                return (false, $"Error during direct playlist refresh: {ex.Message}");
            }
        }

        /// <summary>
        /// Refresh a single smart playlist manually.
        /// This method provides the same functionality as the individual "Refresh" button in the UI.
        /// </summary>
        public async Task<(bool Success, string Message, string JellyfinPlaylistId)> RefreshSinglePlaylistAsync(SmartPlaylistDto playlist, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Starting manual refresh of single playlist: {PlaylistName} ({PlaylistId})", playlist.Name, playlist.Id);

                var playlistService = GetPlaylistService();
                var result = await playlistService.RefreshSinglePlaylistWithTimeoutAsync(playlist, cancellationToken);
                
                if (result.Success)
                {
                    _logger.LogInformation("Successfully refreshed single playlist: {PlaylistName} ({PlaylistId}) - Jellyfin ID: {JellyfinPlaylistId}", 
                        playlist.Name, playlist.Id, result.JellyfinPlaylistId ?? "none");
                }
                else
                {
                    _logger.LogWarning("Failed to refresh single playlist: {PlaylistName} ({PlaylistId}). Error: {ErrorMessage}", 
                        playlist.Name, playlist.Id, result.Message);
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Single playlist refresh was cancelled for playlist: {PlaylistName} ({PlaylistId})", playlist.Name, playlist.Id);
                return (false, "Refresh operation was cancelled", string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during single playlist refresh for playlist: {PlaylistName} ({PlaylistId})", playlist.Name, playlist.Id);
                return (false, $"Error during playlist refresh: {ex.Message}", string.Empty);
            }
        }
    }
}
