using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SmartLists.Core.Enums
{
    /// <summary>
    /// Type discriminator for smart lists (Playlist vs Collection)
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SmartListType
    {
        Playlist,
        Collection,
    }
}

