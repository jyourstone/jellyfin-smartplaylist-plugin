using System.Linq;

namespace Jellyfin.Plugin.SmartLists.Core.Constants
{
    /// <summary>
    /// Represents a resolution with its display name and numeric value for comparisons.
    /// </summary>
    public record ResolutionInfo(string Value, string DisplayName, int Height);

    /// <summary>
    /// Centralized resolution definitions for the resolution field.
    /// </summary>
    public static class ResolutionTypes
    {
        /// <summary>
        /// All available resolution options for the UI dropdown.
        /// </summary>
        public static readonly ResolutionInfo[] AllResolutions =
        [
            new ResolutionInfo("480p", "480p (854x480)", 480),
            new ResolutionInfo("720p", "720p (1280x720)", 720),
            new ResolutionInfo("1080p", "1080p (1920x1080)", 1080),
            new ResolutionInfo("1440p", "1440p (2560x1440)", 1440),
            new ResolutionInfo("4K", "4K (3840x2160)", 2160),
            new ResolutionInfo("8K", "8K (7680x4320)", 4320)
        ];

        /// <summary>
        /// Gets a resolution info by its value.
        /// </summary>
        /// <param name="value">The resolution value (e.g., "1080p")</param>
        /// <returns>The resolution info or null if not found</returns>
        public static ResolutionInfo? GetByValue(string value)
        {
            return AllResolutions.FirstOrDefault(r => r.Value == value);
        }

        /// <summary>
        /// Gets a resolution info by its height.
        /// </summary>
        /// <param name="height">The resolution height in pixels</param>
        /// <returns>The resolution info or null if not found</returns>
        public static ResolutionInfo? GetByHeight(int height)
        {
            return AllResolutions.FirstOrDefault(r => r.Height == height);
        }

        /// <summary>
        /// Gets all resolution values for API responses.
        /// </summary>
        /// <returns>Array of resolution values</returns>
        public static string[] GetAllValues()
        {
            return [.. AllResolutions.Select(r => r.Value)];
        }

        /// <summary>
        /// Gets all resolution display names for UI dropdowns.
        /// </summary>
        /// <returns>Array of resolution display names</returns>
        public static string[] GetAllDisplayNames()
        {
            return [.. AllResolutions.Select(r => r.DisplayName)];
        }

        /// <summary>
        /// Gets the numeric height value for a resolution string.
        /// </summary>
        /// <param name="resolutionValue">The resolution value (e.g., "1080p")</param>
        /// <returns>The height in pixels, or -1 if not found</returns>
        public static int GetHeightForResolution(string resolutionValue)
        {
            var resolution = GetByValue(resolutionValue);
            return resolution?.Height ?? -1;
        }
    }
}
