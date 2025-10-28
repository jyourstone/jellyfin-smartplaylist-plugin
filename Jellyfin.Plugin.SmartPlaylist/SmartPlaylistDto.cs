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
        Interval = 4, // Every X hours/minutes
        Yearly = 5    // Once per year on specified month, day, and time
    }

    /// <summary>
    /// Represents a single schedule configuration for a playlist.
    /// Supports multiple schedules per playlist for flexible scheduling.
    /// </summary>
    [Serializable]
    public class Schedule
    {
        /// <summary>
        /// The type of schedule trigger (Daily, Weekly, Monthly, Yearly, Interval)
        /// </summary>
        public ScheduleTrigger Trigger { get; set; }
        
        /// <summary>
        /// Time of day for Daily/Weekly/Monthly/Yearly schedules (e.g., 15:00)
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TimeSpan? Time { get; set; }
        
        /// <summary>
        /// Day of week for Weekly schedules (0 = Sunday, 6 = Saturday)
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DayOfWeek? DayOfWeek { get; set; }
        
        /// <summary>
        /// Day of month for Monthly/Yearly schedules (1-31)
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? DayOfMonth { get; set; }
        
        /// <summary>
        /// Month for Yearly schedules (1 = January, 12 = December)
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Month { get; set; }
        
        /// <summary>
        /// Interval for Interval-based schedules (e.g., 2 hours)
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TimeSpan? Interval { get; set; }
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
        /// Migrates legacy single schedule fields to the new Schedules array format.
        /// This is called automatically during save operations.
        /// </summary>
        public void MigrateToNewScheduleFormat()
        {
            // If already using new format (Schedules array exists), nothing to do
            if (Schedules != null)
            {
                return;
            }
            
            // If no legacy schedule exists or it's set to None, don't migrate
            // This preserves truly legacy playlists (v10.10) that have no custom schedules
            if (!ScheduleTrigger.HasValue || ScheduleTrigger.Value == Jellyfin.Plugin.SmartPlaylist.ScheduleTrigger.None)
            {
                return;
            }
            
            // Migrate legacy single schedule to new array format
            var legacySchedule = new Schedule
            {
                Trigger = ScheduleTrigger.Value
            };
            
            if (ScheduleTime != null)
            {
                legacySchedule.Time = ScheduleTime;
            }
            
            if (ScheduleDayOfWeek != null)
            {
                legacySchedule.DayOfWeek = ScheduleDayOfWeek;
            }
            
            if (ScheduleDayOfMonth != null)
            {
                legacySchedule.DayOfMonth = ScheduleDayOfMonth;
            }
            
            if (ScheduleMonth != null)
            {
                legacySchedule.Month = ScheduleMonth;
            }
            
            if (ScheduleInterval != null)
            {
                legacySchedule.Interval = ScheduleInterval;
            }
            
            // Set the new format
            Schedules = [legacySchedule];
            
            // Clear legacy fields so they don't get serialized
            ScheduleTrigger = null;
            ScheduleTime = null;
            ScheduleDayOfWeek = null;
            ScheduleDayOfMonth = null;
            ScheduleMonth = null;
            ScheduleInterval = null;
        }
        
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
        
        // Multiple schedules support (new approach)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<Schedule> Schedules { get; set; }
        
        // Legacy single schedule properties - kept for backward compatibility
        // These are still read/written for rollback safety and legacy playlist support
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ScheduleTrigger? ScheduleTrigger { get; set; } = null;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TimeSpan? ScheduleTime { get; set; } // Time of day for Daily/Weekly/Monthly/Yearly (e.g., 15:00)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DayOfWeek? ScheduleDayOfWeek { get; set; } // Day of week for Weekly
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ScheduleDayOfMonth { get; set; } // Day of month for Monthly/Yearly (1-31)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ScheduleMonth { get; set; } // Month for Yearly (1-12, 1=January)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TimeSpan? ScheduleInterval { get; set; } // Interval for Interval mode (e.g., 2 hours)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTime? LastRefreshed { get; set; } // When was this playlist last refreshed (any trigger)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTime? DateCreated { get; set; } // When was this playlist created (playlist creation, UTC)
        
        // Playlist statistics (calculated during refresh)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ItemCount { get; set; } // Number of items currently in the playlist
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? TotalRuntimeMinutes { get; set; } // Total runtime of all items in minutes
        
        // Similar To comparison fields - which metadata properties to use for similarity matching
        // Defaults to ["Genre", "Tags"] for backwards compatibility
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string> SimilarityComparisonFields { get; set; }
        
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