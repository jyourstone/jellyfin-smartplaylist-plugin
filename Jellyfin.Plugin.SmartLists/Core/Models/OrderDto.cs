using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SmartLists.Core.Models
{
    /// <summary>
    /// Represents the sorting configuration for a smart list
    /// Supports both legacy single Order format and new multiple SortOptions format
    /// </summary>
    public class OrderDto
    {
        // Legacy single order format (for backward compatibility)
        // Name is optional when SortOptions is used
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; }

        // New multiple sort options format
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<SortOption>? SortOptions { get; set; }
    }
}

