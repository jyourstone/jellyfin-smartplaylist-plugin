using System;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.SmartPlaylist.QueryEngine
{
    public static class DateUtils
    {
        /// <summary>
        /// Extracts the PremiereDate property from a BaseItem and returns its Unix timestamp, or 0 on error.
        /// Treats the PremiereDate as UTC to ensure consistency with user-input date handling.
        /// </summary>
        public static double GetReleaseDateUnixTimestamp(BaseItem item)
        {
            try
            {
                var premiereDateProperty = item.GetType().GetProperty("PremiereDate");
                if (premiereDateProperty != null)
                {
                    var premiereDate = premiereDateProperty.GetValue(item);
                    if (premiereDate is DateTime premiereDateTime && premiereDateTime != DateTime.MinValue)
                    {
                        // Treat the PremiereDate as UTC to ensure consistency with user-input date handling
                        // This assumes Jellyfin stores dates in UTC, which is the typical behavior
                        return new DateTimeOffset(premiereDateTime, TimeSpan.Zero).ToUnixTimeSeconds();
                    }
                }
            }
            catch
            {
                // Ignore errors and fall back to 0
            }
            return 0;
        }
    }
} 