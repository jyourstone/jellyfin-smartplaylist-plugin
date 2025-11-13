using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartLists.Core.Models
{
    /// <summary>
    /// DTO for server-wide smart collections
    /// Collections are not user-bound and appear in specified libraries
    /// </summary>
    [Serializable]
    public class SmartCollectionDto : SmartListDto
    {
        public SmartCollectionDto()
        {
            Type = Core.Enums.SmartListType.Collection;
        }

        // Collection-specific properties
        public string JellyfinCollectionId { get; set; } = null!;  // Jellyfin collection (BoxSet) ID for reliable lookup
        public List<Guid> LibraryIds { get; set; } = [];  // Which libraries this collection appears in,
    }
}

