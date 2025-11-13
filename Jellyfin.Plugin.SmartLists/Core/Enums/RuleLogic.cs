using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SmartLists.Core.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RuleLogic
    {
        And,
        Or,
    }
}

