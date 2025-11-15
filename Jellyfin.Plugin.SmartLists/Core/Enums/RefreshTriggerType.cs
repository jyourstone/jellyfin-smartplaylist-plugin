using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SmartLists.Core.Enums
{
    /// <summary>
    /// Type of trigger that initiated a refresh operation
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RefreshTriggerType
    {
        Manual,
        Auto,
        Scheduled
    }
}

