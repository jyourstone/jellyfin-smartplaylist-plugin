using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartLists.Core.Models;

namespace Jellyfin.Plugin.SmartLists.Services.Abstractions
{
    /// <summary>
    /// Generic store interface for smart list persistence (Playlists and Collections)
    /// </summary>
    /// <typeparam name="TDto">The DTO type (SmartPlaylistDto or SmartCollectionDto)</typeparam>
    public interface ISmartListStore<TDto> where TDto : SmartListDto
    {
        /// <summary>
        /// Gets a smart list by ID
        /// </summary>
        Task<TDto?> GetByIdAsync(Guid id);

        /// <summary>
        /// Gets all smart lists of this type
        /// </summary>
        Task<TDto[]> GetAllAsync();

        /// <summary>
        /// Saves a smart list (creates or updates)
        /// </summary>
        Task<TDto> SaveAsync(TDto dto);

        /// <summary>
        /// Deletes a smart list by ID
        /// </summary>
        Task DeleteAsync(Guid id);
    }
}

