using System;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.SmartPlaylist.QueryEngine
{
    public static class DateUtils
    {
        /// <summary>
        /// Extracts the PremiereDate property from a BaseItem and returns its Unix timestamp, or 0 on error.
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
                        return new DateTimeOffset(premiereDateTime).ToUnixTimeSeconds();
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