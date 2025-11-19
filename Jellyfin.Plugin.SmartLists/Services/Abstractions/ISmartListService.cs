using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SmartLists.Core.Models;
using Jellyfin.Plugin.SmartLists.Services.Shared;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.SmartLists.Services.Abstractions
{
    /// <summary>
    /// Generic service interface for smart list operations (Playlists and Collections)
    /// </summary>
    /// <typeparam name="TDto">The DTO type (SmartPlaylistDto or SmartCollectionDto)</typeparam>
    public interface ISmartListService<TDto> where TDto : SmartListDto
    {
        /// <summary>
        /// Refreshes a single smart list
        /// This method is called by the queue processor and assumes no locking is needed.
        /// </summary>
        Task<(bool Success, string Message, string Id)> RefreshAsync(
            TDto dto, Action<int, int>? progressCallback = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a smart list
        /// </summary>
        Task DeleteAsync(TDto dto, CancellationToken cancellationToken = default);

        /// <summary>
        /// Disables a smart list (deletes the underlying Jellyfin entity)
        /// </summary>
        Task DisableAsync(TDto dto, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all user media for a playlist, filtered by media types.
        /// </summary>
        /// <param name="user">The user</param>
        /// <param name="mediaTypes">List of media types to include</param>
        /// <param name="dto">The playlist DTO (optional, for validation)</param>
        /// <returns>Enumerable of BaseItem matching the media types</returns>
        IEnumerable<BaseItem> GetAllUserMediaForPlaylist(User user, List<string> mediaTypes, TDto? dto = null);

        /// <summary>
        /// Processes a playlist refresh with pre-cached media for efficient batch processing.
        /// </summary>
        /// <param name="dto">The playlist DTO to process</param>
        /// <param name="user">The user for this playlist</param>
        /// <param name="allUserMedia">All media items for the user (cached)</param>
        /// <param name="refreshCache">RefreshCache instance for caching expensive operations</param>
        /// <param name="saveCallback">Optional callback to save the DTO when JellyfinPlaylistId is updated</param>
        /// <param name="progressCallback">Optional callback to report progress</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Tuple of (success, message, jellyfinPlaylistId)</returns>
        Task<(bool Success, string Message, string JellyfinPlaylistId)> ProcessPlaylistRefreshWithCachedMediaAsync(
            TDto dto,
            User user,
            BaseItem[] allUserMedia,
            RefreshQueueService.RefreshCache refreshCache,
            Func<TDto, Task>? saveCallback = null,
            Action<int, int>? progressCallback = null,
            CancellationToken cancellationToken = default);
    }
}

