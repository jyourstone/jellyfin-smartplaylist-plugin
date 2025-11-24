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
            // Guard against null baseName
            if (baseName == null)
            {
                baseName = string.Empty;
            }
            
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

        /// <summary>
        /// Strips the configured prefix and suffix from a collection/playlist name.
        /// This is useful for matching collection names in rules, where users may enter
        /// the base name without prefix/suffix, but the actual collection has them applied.
        /// </summary>
        /// <param name="formattedName">The formatted name that may contain prefix/suffix</param>
        /// <returns>The base name without prefix/suffix</returns>
        public static string StripPrefixAndSuffix(string formattedName)
        {
            try
            {
                var config = Plugin.Instance?.Configuration;
                if (config == null)
                {
                    // Fallback to default suffix if configuration is not available
                    return StripPrefixAndSuffixWithSettings(formattedName, "", DefaultSuffix);
                }

                var prefix = config.PlaylistNamePrefix ?? "";
                var suffix = config.PlaylistNameSuffix ?? DefaultSuffix;

                return StripPrefixAndSuffixWithSettings(formattedName, prefix, suffix);
            }
            catch (Exception)
            {
                // Fallback to default behavior if any error occurs
                return StripPrefixAndSuffixWithSettings(formattedName, "", DefaultSuffix);
            }
        }

        /// <summary>
        /// Strips specific prefix and suffix values from a name.
        /// </summary>
        /// <param name="formattedName">The formatted name that may contain prefix/suffix</param>
        /// <param name="prefix">The prefix to remove (can be null or empty)</param>
        /// <param name="suffix">The suffix to remove (can be null or empty)</param>
        /// <returns>The base name without prefix/suffix</returns>
        public static string StripPrefixAndSuffixWithSettings(string formattedName, string prefix, string suffix)
        {
            if (string.IsNullOrEmpty(formattedName))
                return formattedName ?? string.Empty;

            var result = formattedName;

            // Remove suffix first (from the end)
            if (!string.IsNullOrEmpty(suffix))
            {
                var suffixWithSpace = " " + suffix;
                if (result.EndsWith(suffixWithSpace, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(0, result.Length - suffixWithSpace.Length);
                }
                // Also check without space (in case of edge cases)
                else if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(0, result.Length - suffix.Length);
                }
            }

            // Remove prefix (from the beginning)
            if (!string.IsNullOrEmpty(prefix))
            {
                var prefixWithSpace = prefix + " ";
                if (result.StartsWith(prefixWithSpace, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(prefixWithSpace.Length);
                }
                // Also check without space (in case of edge cases)
                else if (result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(prefix.Length);
                }
            }

            return result.Trim();
        }
    }
}