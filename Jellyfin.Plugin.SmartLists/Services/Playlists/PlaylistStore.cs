using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartLists.Core.Models;
using Jellyfin.Plugin.SmartLists.Services.Abstractions;
using Jellyfin.Plugin.SmartLists.Services.Shared;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.SmartLists.Services.Playlists
{
    /// <summary>
    /// Store implementation for smart playlists
    /// Handles JSON serialization/deserialization with type discrimination
    /// </summary>
    public class PlaylistStore : ISmartListStore<SmartPlaylistDto>
    {
        private readonly ISmartListFileSystem _fileSystem;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            // Support polymorphic deserialization based on Type field
            Converters = { new JsonStringEnumConverter() }
        };

        public PlaylistStore(ISmartListFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
        }

        public async Task<SmartPlaylistDto?> GetByIdAsync(Guid id)
        {
            // Validate GUID format to prevent path injection
            if (id == Guid.Empty)
            {
                return null;
            }

            // Try direct file lookup first (O(1) operation)
            var filePath = _fileSystem.GetSmartListFilePath(id.ToString());
            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    var playlist = await LoadPlaylistAsync(filePath).ConfigureAwait(false);
                    if (playlist != null && playlist.Type == Core.Enums.SmartListType.Playlist)
                    {
                        return playlist;
                    }
                }
                catch
                {
                    // File exists but couldn't be loaded, fall back to scanning all files
                }
            }

            // Fallback: scan all playlists if direct lookup failed
            var allPlaylists = await GetAllAsync().ConfigureAwait(false);
            return allPlaylists.FirstOrDefault(p => string.Equals(p.Id, id.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        public async Task<SmartPlaylistDto[]> GetAllAsync()
        {
            // Use shared helper to read files once
            var (playlists, _) = await _fileSystem.GetAllSmartListsAsync().ConfigureAwait(false);
            return playlists;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Playlist ID is validated as GUID before use in file paths, preventing path injection")]
        public async Task<SmartPlaylistDto> SaveAsync(SmartPlaylistDto smartPlaylist)
        {
            ArgumentNullException.ThrowIfNull(smartPlaylist);

            // Ensure type is set
            smartPlaylist.Type = Core.Enums.SmartListType.Playlist;

            // Validate ID is a valid GUID to prevent path injection
            if (string.IsNullOrWhiteSpace(smartPlaylist.Id) || !Guid.TryParse(smartPlaylist.Id, out var parsedId) || parsedId == Guid.Empty)
            {
                throw new ArgumentException("Playlist ID must be a valid non-empty GUID", nameof(smartPlaylist));
            }

            var fileName = smartPlaylist.Id;
            smartPlaylist.FileName = $"{fileName}.json";

            var filePath = _fileSystem.GetSmartListPath(fileName);
            var tempPath = filePath + ".tmp";

            // Check if this playlist exists in the legacy directory (for migration)
            var legacyPath = _fileSystem.GetLegacyPath(fileName);
            bool existsInLegacy = File.Exists(legacyPath);

            try
            {
                await using (var writer = File.Create(tempPath))
                {
                    await JsonSerializer.SerializeAsync(writer, smartPlaylist, JsonOptions).ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                }

                if (File.Exists(filePath))
                {
                    // Replace is atomic on the same volume
                    File.Replace(tempPath, filePath, null);
                }
                else
                {
                    File.Move(tempPath, filePath);
                }

                // After successfully saving to new location, delete legacy file if it exists
                // This migrates the playlist from old directory to new directory
                if (existsInLegacy)
                {
                    try
                    {
                        File.Delete(legacyPath);
                    }
                    catch (Exception ex)
                    {
                        // Log but don't fail the save operation if legacy deletion fails
                        // The file will be in both locations, but the new location takes precedence
                        System.Diagnostics.Debug.WriteLine($"Warning: Failed to delete legacy playlist file {legacyPath}: {ex.Message}");
                    }
                }
            }
            finally
            {
                // Clean up temp file if it still exists
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* ignore cleanup errors */ }
            }

            return smartPlaylist;
        }

        public async Task DeleteAsync(Guid id)
        {
            var playlist = await GetByIdAsync(id).ConfigureAwait(false);
            if (playlist == null)
                return;

            // Use the playlist Id, which is validated and used as the canonical filename elsewhere
            var fileName = playlist.Id;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("Playlist ID cannot be null or empty", nameof(id));
            }

            var filePath = _fileSystem.GetSmartListPath(fileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            // Also check legacy directory
            var legacyPath = _fileSystem.GetLegacyPath(fileName);
            if (File.Exists(legacyPath))
            {
                File.Delete(legacyPath);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "File path is validated upstream - only valid GUIDs are passed to this method")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Method is part of instance interface implementation")]
        private async Task<SmartPlaylistDto?> LoadPlaylistAsync(string filePath)
        {
            await using var reader = File.OpenRead(filePath);
            var dto = await JsonSerializer.DeserializeAsync<SmartPlaylistDto>(reader, JsonOptions).ConfigureAwait(false);

            // Only return playlists - if this is a collection, return null
            if (dto == null)
            {
                return null;
            }

            // If type is explicitly set to Collection, this is not a playlist - return null
            if (dto.Type == Core.Enums.SmartListType.Collection)
            {
                return null;
            }

            // Handle backward compatibility: if Type is not set (legacy file), default to Playlist
            if (dto.Type == 0) // Default enum value
            {
                dto.Type = Core.Enums.SmartListType.Playlist;
            }

            return dto;
        }
    }
}

