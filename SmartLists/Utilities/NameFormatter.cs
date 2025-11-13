using System;
using Jellyfin.Plugin.SmartLists.Configuration;

namespace Jellyfin.Plugin.SmartLists.Utilities
{
    /// <summary>
    /// Utility class for formatting smart list names with prefix and suffix.
    /// </summary>
    public static class NameFormatter
    {
        /// <summary>
        /// Default suffix used when no configuration is available or configured.
        /// </summary>
        private const string DefaultSuffix = "[Smart]";

        /// <summary>
        /// Formats a playlist name based on plugin configuration settings.
        /// </summary>
        /// <param name="playlistName">The base playlist name</param>
        /// <returns>The formatted playlist name</returns>
        public static string FormatPlaylistName(string playlistName)
        {
            try
            {
                // TODO: Update to use Plugin.Instance when Plugin.cs is migrated
                // For now, use default behavior
                var config = Plugin.Instance?.Configuration;
                if (config == null)
                {
                    // Fallback to default behavior if configuration is not available
                    return FormatPlaylistNameWithSettings(playlistName, "", DefaultSuffix);
                }

                var prefix = config.PlaylistNamePrefix ?? "";
                var suffix = config.PlaylistNameSuffix ?? DefaultSuffix;

                return FormatPlaylistNameWithSettings(playlistName, prefix, suffix);
            }
            catch (Exception)
            {
                // Fallback to default behavior if any error occurs
                return FormatPlaylistNameWithSettings(playlistName, "", DefaultSuffix);
            }
        }

        /// <summary>
        /// Formats a playlist name with specific prefix and suffix values.
        /// </summary>
        /// <param name="baseName">The base playlist name</param>
        /// <param name="prefix">The prefix to add (can be null or empty)</param>
        /// <param name="suffix">The suffix to add (can be null or empty)</param>
        /// <returns>The formatted playlist name</returns>
        public static string FormatPlaylistNameWithSettings(string baseName, string prefix, string suffix)
        {
            var formatted = baseName;
            if (!string.IsNullOrEmpty(prefix))
            {
                formatted = prefix + " " + formatted;
            }
            if (!string.IsNullOrEmpty(suffix))
            {
                formatted = formatted + " " + suffix;
            }
            return formatted.Trim();
        }
    }
}