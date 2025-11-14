using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.SmartLists.Core.Enums;
using Jellyfin.Plugin.SmartLists.Core.QueryEngine;

namespace Jellyfin.Plugin.SmartLists.Core.Models
{
    /// <summary>
    /// Base class for all smart lists (Playlists and Collections)
    /// Contains all shared properties and logic
    /// </summary>
    [Serializable]
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
    [JsonDerivedType(typeof(SmartPlaylistDto), typeDiscriminator: "Playlist")]
    [JsonDerivedType(typeof(SmartCollectionDto), typeDiscriminator: "Collection")]
    public abstract class SmartListDto
    {
        /// <summary>
        /// Type discriminator - determines if this is a Playlist or Collection
        /// </summary>
        public SmartListType Type { get; set; }

        // Core identification
        // Id is optional for creation (generated if not provided)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Id { get; set; }
        public string Name { get; set; } = null!;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? FileName { get; set; }
        
        /// <summary>
        /// Owner user ID - the user this list belongs to or whose context is used for rule evaluation
        /// </summary>
        public string User { get; set; } = string.Empty;

        // Query and filtering
        public List<ExpressionSet> ExpressionSets { get; set; } = [];
        // Order is optional for creation (initialized if not provided)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public OrderDto? Order { get; set; }

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
                    .Where(mt => Core.Constants.MediaTypes.MediaTypeToBaseItemKind.ContainsKey(mt))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }
        }

        // State and limits
        public bool Enabled { get; set; } = true; // Default to enabled
        public int? MaxItems { get; set; } // Nullable to support backwards compatibility
        public int? MaxPlayTimeMinutes { get; set; } // Nullable to support backwards compatibility

        // Auto-refresh
        public AutoRefreshMode AutoRefresh { get; set; } = AutoRefreshMode.Never; // Default to never for backward compatibility

        // Scheduling
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<Schedule> Schedules { get; set; } = [];

        // Legacy single schedule properties - kept for backward compatibility
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ScheduleTrigger? ScheduleTrigger { get; set; } = null;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TimeSpan? ScheduleTime { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonConverter(typeof(DayOfWeekAsIntegerConverter))]
        public DayOfWeek? ScheduleDayOfWeek { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ScheduleDayOfMonth { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ScheduleMonth { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public TimeSpan? ScheduleInterval { get; set; }

        // Timestamps
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTime? LastRefreshed { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTime? DateCreated { get; set; }

        // Statistics (calculated during refresh)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ItemCount { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? TotalRuntimeMinutes { get; set; }

        // Similarity comparison fields
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string> SimilarityComparisonFields { get; set; } = [];

        // Legacy support - for migration from old RuleLogic field
        [Obsolete("Use ExpressionSet.Logic instead. This property is for backward compatibility only.")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public RuleLogic? RuleLogic { get; set; }

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
            // This preserves truly legacy lists (v10.10) that have no custom schedules
            if (!ScheduleTrigger.HasValue || ScheduleTrigger.Value == Core.Enums.ScheduleTrigger.None)
            {
                return;
            }

            // Migrate legacy single schedule to new array format
            var legacySchedule = new Schedule
            {
                Trigger = ScheduleTrigger.Value,
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
    }
}

