using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.SmartPlaylist.Constants;
using Jellyfin.Plugin.SmartPlaylist.QueryEngine;

namespace Jellyfin.Plugin.SmartPlaylist
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RuleLogic
    {
        And,
        Or
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AutoRefreshMode
    {
        Never = 0,           // Manual only (current behavior)
        OnLibraryChanges = 1, // Only when items added/removed  
        OnAllChanges = 2     // Any metadata updates (including playback status)
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ScheduleTrigger
    {
        None = 0,     // Explicitly no schedule (different from null which means legacy tasks)
        Daily = 1,    // Once per day at specified time
        Weekly = 2,   // Once per week on specified day/time  
        Monthly = 3,  // Once per month on specified day and time
        Interval = 4  // Every X hours/minutes
    }

    [Serializable]
    public class SmartPlaylistDto
    {
        public string Id { get; set; }
        public string JellyfinPlaylistId { get; set; }  // Jellyfin playlist ID for reliable lookup
        public string Name { get; set; }
        public string FileName { get; set; }
        public Guid UserId { get; set; }
        public List<ExpressionSet> ExpressionSets { get; set; }
        public OrderDto Order { get; set; }
        public bool Public { get; set; } = false; // Default to private
        private List<string> _mediaTypes = [];
        
        /// <summary>
        /// Pre-filter media types with validation to prevent corruption
        /// </summary>
        public List<string> MediaTypes 
        { 
            get => _mediaTypes;
            set
            {
                var source = value ?? [];
                // Keep only known types and remove duplicates (ordinal)
                _mediaTypes = source
                    .Where(mt => Constants.MediaTypes.MediaTypeToBaseItemKind.ContainsKey(mt))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }
        }
        public bool Enabled { get; set; } = true; // Default to enabled
        public int? MaxItems { get; set; } // Nullable to support backwards compatibility
        public int? MaxPlayTimeMinutes { get; set; } // Nullable to support backwards compatibility
        public AutoRefreshMode AutoRefresh { get; set; } = AutoRefreshMode.Never; // Default to never for backward compatibility
        
        // Custom scheduling properties (null = no custom schedule, use legacy tasks)
        public ScheduleTrigger? ScheduleTrigger { get; set; } = null;
        public TimeSpan? ScheduleTime { get; set; } // Time of day for Daily/Weekly/Monthly (e.g., 15:00)
        public DayOfWeek? ScheduleDayOfWeek { get; set; } // Day of week for Weekly
        public int? ScheduleDayOfMonth { get; set; } // Day of month for Monthly (1-31)
        public TimeSpan? ScheduleInterval { get; set; } // Interval for Interval mode (e.g., 2 hours)
        public DateTime? LastRefreshed { get; set; } // When was this playlist last refreshed (any trigger)
        
        // Legacy support - for migration from old User field
        [Obsolete("Use UserId instead. This property is for backward compatibility only.")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string User { get; set; }
        
        // Legacy support - for migration from old RuleLogic field
        [Obsolete("Use ExpressionSet.Logic instead. This property is for backward compatibility only.")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public RuleLogic? RuleLogic { get; set; }
    }

    public class ExpressionSet
    {
        public List<Expression> Expressions { get; set; }
    }

    public class OrderDto
    {
        public string Name { get; set; }
    }
}