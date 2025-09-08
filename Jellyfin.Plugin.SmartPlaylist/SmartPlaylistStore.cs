using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.SmartPlaylist
{
    public interface ISmartPlaylistStore
    {
        Task<SmartPlaylistDto> GetSmartPlaylistAsync(Guid smartPlaylistId);
        Task<SmartPlaylistDto[]> LoadPlaylistsAsync(Guid userId);
        Task<SmartPlaylistDto[]> GetAllSmartPlaylistsAsync();
        Task<SmartPlaylistDto> SaveAsync(SmartPlaylistDto smartPlaylist);
        Task DeleteAsync(Guid userId, string smartPlaylistId);
    }

    public class SmartPlaylistStore(ISmartPlaylistFileSystem fileSystem, IUserManager userManager) : ISmartPlaylistStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };


        public async Task<SmartPlaylistDto> GetSmartPlaylistAsync(Guid smartPlaylistId)
        {
            // Try direct file lookup first (O(1) operation)
            var filePath = fileSystem.GetSmartPlaylistFilePath(smartPlaylistId.ToString());
            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    var playlist = await LoadPlaylistAsync(filePath).ConfigureAwait(false);
                    if (playlist != null)
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
            var allPlaylists = await GetAllSmartPlaylistsAsync().ConfigureAwait(false);
            var fallbackPlaylist = allPlaylists.FirstOrDefault(p => p.Id == smartPlaylistId.ToString());
            
            if (fallbackPlaylist != null)
            {
                return fallbackPlaylist;
            }
            
            return null;
        }

        public async Task<SmartPlaylistDto[]> LoadPlaylistsAsync(Guid userId)
        {
            var user = userManager.GetUserById(userId);
            if (user == null)
            {
                return [];
            }

            var allPlaylists = await GetAllSmartPlaylistsAsync().ConfigureAwait(false);

            return [.. allPlaylists.Where(p => p.UserId == userId)];
        }

        public async Task<SmartPlaylistDto[]> GetAllSmartPlaylistsAsync()
        {
            var filePaths = fileSystem.GetAllSmartPlaylistFilePaths();
            var validPlaylists = new List<SmartPlaylistDto>();

            foreach (var filePath in filePaths)
            {
                try
                {
                    var playlist = await LoadPlaylistAsync(filePath).ConfigureAwait(false);
                    if (playlist != null)
                    {
                        validPlaylists.Add(playlist);
                    }
                }
                catch (Exception)
                {
                    // Skip invalid playlist files and continue loading others
                    // Note: Could add proper logging here if ILogger was injected
                }
            }

            return [.. validPlaylists];
        }

        public async Task<SmartPlaylistDto> SaveAsync(SmartPlaylistDto smartPlaylist)
        {
            var fileName = smartPlaylist.Id;
            smartPlaylist.FileName = $"{fileName}.json";

            var filePath = fileSystem.GetSmartPlaylistPath(fileName);
            
            await using var writer = File.Create(filePath);
            await JsonSerializer.SerializeAsync(writer, smartPlaylist, JsonOptions).ConfigureAwait(false);
            return smartPlaylist;
        }

        public async Task DeleteAsync(Guid userId, string smartPlaylistId)
        {
            // First find the playlist by ID to get the filename
            var allPlaylists = await GetAllSmartPlaylistsAsync().ConfigureAwait(false);
            var playlist = allPlaylists.FirstOrDefault(p => p.Id == smartPlaylistId);
            
            if (playlist != null && !string.IsNullOrEmpty(playlist.FileName))
            {
                // Use the actual filename to construct the path
                var fileName = Path.GetFileNameWithoutExtension(playlist.FileName);
                var filePath = fileSystem.GetSmartPlaylistPath(fileName);
                if (File.Exists(filePath)) 
                {
                    File.Delete(filePath);
                }
            }
        }

        private async Task<SmartPlaylistDto> LoadPlaylistAsync(string filePath)
        {
            await using var reader = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<SmartPlaylistDto>(reader).ConfigureAwait(false);
        }
    }
}