using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SmartLists.Core.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AutoRefreshMode
    {
        Never = 0,           // Manual only (current behavior)
        OnLibraryChanges = 1, // Only when items added
        OnAllChanges = 2     // Any metadata updates (including playback status),
    }
}

