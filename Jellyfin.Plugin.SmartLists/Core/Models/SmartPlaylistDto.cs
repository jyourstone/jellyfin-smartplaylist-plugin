using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SmartLists.Core.Models
{
    /// <summary>
    /// DTO for user-specific smart playlists
    /// </summary>
    [Serializable]
    public class SmartPlaylistDto : SmartListDto
    {
        public SmartPlaylistDto()
        {
            Type = Core.Enums.SmartListType.Playlist;
        }

        // Playlist-specific properties
        public string? JellyfinPlaylistId { get; set; }  // Jellyfin playlist ID for reliable lookup
        public bool Public { get; set; } = false; // Default to private
    }
}

