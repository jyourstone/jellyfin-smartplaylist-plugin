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