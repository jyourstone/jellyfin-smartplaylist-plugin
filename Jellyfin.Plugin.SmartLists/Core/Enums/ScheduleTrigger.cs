using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SmartLists.Core.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ScheduleTrigger
    {
        None = 0,     // Explicitly no schedule (different from null which means legacy tasks)
        Daily = 1,    // Once per day at specified time
        Weekly = 2,   // Once per week on specified day/time
        Monthly = 3,  // Once per month on specified day and time
        Interval = 4, // Every X hours/minutes
        Yearly = 5    // Once per year on specified month, day, and time,
    }
}

