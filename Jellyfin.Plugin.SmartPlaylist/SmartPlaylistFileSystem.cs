using System.IO;
using System.Linq;
using MediaBrowser.Controller;

namespace Jellyfin.Plugin.SmartPlaylist
{
    public interface ISmartPlaylistFileSystem
    {
        string BasePath { get; }
        string GetSmartPlaylistFilePath(string smartPlaylistId);
        string[] GetAllSmartPlaylistFilePaths();
        string GetSmartPlaylistPath(string fileName);
    }

    public class SmartPlaylistFileSystem : ISmartPlaylistFileSystem
    {
        // Class Constructor for SmartPlaylistFileSystem
        // Creates directory if it doesn't exist
        public SmartPlaylistFileSystem(IServerApplicationPaths serverApplicationPaths)
        {
            BasePath = Path.Combine(serverApplicationPaths.DataPath, "smartplaylists");
            if (!Directory.Exists(BasePath)) Directory.CreateDirectory(BasePath);
        }

        public string BasePath { get; }

        public string GetSmartPlaylistFilePath(string smartPlaylistId)
        {
            return Directory.GetFiles(BasePath, $"{smartPlaylistId}.json", SearchOption.AllDirectories).FirstOrDefault();
        }

        public string[] GetAllSmartPlaylistFilePaths()
        {
            return Directory.GetFiles(BasePath, "*.json", SearchOption.AllDirectories);
        }

        public string GetSmartPlaylistPath(string fileName)
        {
            return Path.Combine(BasePath, $"{fileName}.json");
        }
    }
}