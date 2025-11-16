using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SmartLists.Core.Enums
{
    /// <summary>
    /// Defines the sort order direction for sorting operations.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SortOrder
    {
        /// <summary>
        /// Sort in ascending order (A-Z, 0-9, oldest to newest).
        /// </summary>
        Ascending,

        /// <summary>
        /// Sort in descending order (Z-A, 9-0, newest to oldest).
        /// </summary>
        Descending
    }
}

