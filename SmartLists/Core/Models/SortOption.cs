namespace Jellyfin.Plugin.SmartLists.Core.Models
{
    /// <summary>
    /// Represents a single sorting option with field and direction
    /// </summary>
    public class SortOption
    {
        public string SortBy { get; set; } = null!;      // e.g., "Name", "ProductionYear", "SeasonNumber"
        public string SortOrder { get; set; } = null!;   // "Ascending" or "Descending",
    }
}

