using System;
using System.IO;
using System.Linq;
using MediaBrowser.Controller;

namespace Jellyfin.Plugin.SmartLists.Services.Shared
{
    /// <summary>
    /// File system interface for smart list storage
    /// Supports both playlists and collections in a unified directory
    /// </summary>
    public interface ISmartListFileSystem
    {
        string BasePath { get; }
        string? GetSmartListFilePath(string smartListId);
        string[] GetAllSmartListFilePaths();
        string GetSmartListPath(string fileName);
        string GetLegacyPath(string fileName);
    }

    /// <summary>
    /// File system implementation for smart lists
    /// Uses "smartlists" directory (migrated from "smartplaylists" for backward compatibility)
    /// </summary>
    public class SmartListFileSystem : ISmartListFileSystem
    {
        private readonly string _legacyBasePath;

        public SmartListFileSystem(IServerApplicationPaths serverApplicationPaths)
        {
            ArgumentNullException.ThrowIfNull(serverApplicationPaths);

            // New unified directory name
            BasePath = Path.Combine(serverApplicationPaths.DataPath, "smartlists");
            if (!Directory.Exists(BasePath))
            {
                Directory.CreateDirectory(BasePath);
            }

            // Legacy directory for backward compatibility
            _legacyBasePath = Path.Combine(serverApplicationPaths.DataPath, "smartplaylists");
        }

        public string BasePath { get; }

        public string? GetSmartListFilePath(string smartListId)
        {
            // Validate ID format to prevent path injection
            if (string.IsNullOrWhiteSpace(smartListId) || !Guid.TryParse(smartListId, out _))
            {
                return null;
            }

            // Check new directory first
            var filePath = Directory.GetFiles(BasePath, $"{smartListId}.json", SearchOption.AllDirectories).FirstOrDefault();
            if (!string.IsNullOrEmpty(filePath))
            {
                return filePath;
            }

            // Fallback to legacy directory for backward compatibility
            if (Directory.Exists(_legacyBasePath))
            {
                return Directory.GetFiles(_legacyBasePath, $"{smartListId}.json", SearchOption.AllDirectories).FirstOrDefault();
            }

            return null;
        }

        public string[] GetAllSmartListFilePaths()
        {
            var files = new System.Collections.Generic.List<string>();

            // Get files from new directory
            if (Directory.Exists(BasePath))
            {
                files.AddRange(Directory.GetFiles(BasePath, "*.json", SearchOption.AllDirectories));
            }

            // Also check legacy directory for backward compatibility
            if (Directory.Exists(_legacyBasePath))
            {
                files.AddRange(Directory.GetFiles(_legacyBasePath, "*.json", SearchOption.AllDirectories));
            }

            return files.ToArray();
        }

        public string GetSmartListPath(string fileName)
        {
            // Validate fileName is a valid GUID to prevent path injection
            if (string.IsNullOrWhiteSpace(fileName) || !Guid.TryParse(fileName, out _))
            {
                throw new ArgumentException("File name must be a valid GUID", nameof(fileName));
            }

            return Path.Combine(BasePath, $"{fileName}.json");
        }

        public string GetLegacyPath(string fileName)
        {
            // Validate fileName is a valid GUID to prevent path injection
            if (string.IsNullOrWhiteSpace(fileName) || !Guid.TryParse(fileName, out _))
            {
                throw new ArgumentException("File name must be a valid GUID", nameof(fileName));
            }

            return Path.Combine(_legacyBasePath, $"{fileName}.json");
        }
    }
}

