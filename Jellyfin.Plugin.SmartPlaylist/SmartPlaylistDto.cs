using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SmartPlaylist.QueryEngine;

namespace Jellyfin.Plugin.SmartPlaylist
{
    [Serializable]
    public class SmartPlaylistDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string FileName { get; set; }
        public Guid UserId { get; set; }
        public List<ExpressionSet> ExpressionSets { get; set; }
        public OrderDto Order { get; set; }
        public bool Public { get; set; } = false; // Default to private
        
        // Legacy support - for migration from old User field
        [Obsolete("Use UserId instead. This property is for backward compatibility only.")]
        public string User { get; set; }
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