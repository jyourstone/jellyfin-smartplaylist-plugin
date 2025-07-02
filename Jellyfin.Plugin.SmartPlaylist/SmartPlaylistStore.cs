using System;
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
        void Delete(Guid userId, string smartPlaylistId);
    }

    public class SmartPlaylistStore(ISmartPlaylistFileSystem fileSystem, IUserManager userManager) : ISmartPlaylistStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };


        public async Task<SmartPlaylistDto> GetSmartPlaylistAsync(Guid smartPlaylistId)
        {
            // First try to find by ID
            var allPlaylists = await GetAllSmartPlaylistsAsync().ConfigureAwait(false);
            var playlist = allPlaylists.FirstOrDefault(p => p.Id == smartPlaylistId.ToString());
            
            if (playlist != null)
            {
                return playlist;
            }
            
            // Fallback to file path lookup
            var fileName = fileSystem.GetSmartPlaylistFilePath(smartPlaylistId.ToString());
            if (fileName == null)
            {
                return null;
            }
            return await LoadPlaylistAsync(fileName).ConfigureAwait(false);
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
            var deserializeTasks = fileSystem.GetAllSmartPlaylistFilePaths().Select(LoadPlaylistAsync).ToArray();

            await Task.WhenAll(deserializeTasks).ConfigureAwait(false);

            return [.. deserializeTasks.Select(x => x.Result)];
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

        public void Delete(Guid userId, string smartPlaylistId)
        {
            // First find the playlist by ID to get the filename
            var allPlaylists = GetAllSmartPlaylistsAsync().Result;
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