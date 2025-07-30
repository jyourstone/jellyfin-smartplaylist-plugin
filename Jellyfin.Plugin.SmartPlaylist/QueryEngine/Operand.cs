using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartPlaylist.QueryEngine
{
    public class Operand(string name)
    {

        public float CommunityRating { get; set; } = 0;
        public float CriticRating { get; set; } = 0;
        public List<string> Genres { get; set; } = [];
        public bool IsPlayed { get; set; }
        public string Name { get; set; } = name;
        public string FolderPath { get; set; } = "";
        public string FileName { get; set; } = "";
        public int ProductionYear { get; set; } = 0;
        public List<string> Studios { get; set; } = [];
        public string MediaType { get; set; } = "";
        public string ItemType { get; set; } = "";
        public string Album { get; set; } = "";
        public string Overview { get; set; } = "";
        public double DateCreated { get; set; } = 0;
        public double DateLastRefreshed { get; set; } = 0;
        public double DateLastSaved { get; set; } = 0;
        public double DateModified { get; set; } = 0;
        public double ReleaseDate { get; set; } = 0;
        public double LastPlayedDate { get; set; } = 0;
        public List<string> Tags { get; set; } = [];
        public double RuntimeMinutes { get; set; } = 0;
        public int PlayCount { get; set; } = 0;
        public string OfficialRating { get; set; } = "";
        public bool IsFavorite { get; set; } = false;
        public bool NextUnwatched { get; set; } = false;
        public List<string> AudioLanguages { get; set; } = [];
        public List<string> People { get; set; } = [];
        
        // Music-specific fields
        public List<string> Artists { get; set; } = [];
        public List<string> AlbumArtists { get; set; } = [];
        
        // User-specific data - Store user ID -> data mappings
        // These will be populated based on which users are referenced in rules
        public Dictionary<string, bool> IsPlayedByUser { get; set; } = [];
        public Dictionary<string, int> PlayCountByUser { get; set; } = [];
        public Dictionary<string, bool> IsFavoriteByUser { get; set; } = [];
        public Dictionary<string, bool> NextUnwatchedByUser { get; set; } = [];
        public Dictionary<string, double> LastPlayedDateByUser { get; set; } = [];
        
        // Helper methods to check user-specific data
        public bool GetIsPlayedByUser(string userId)
        {
            return IsPlayedByUser.TryGetValue(userId, out var value) && value;
        }
        
        public int GetPlayCountByUser(string userId)
        {
            return PlayCountByUser.TryGetValue(userId, out var value) ? value : 0;
        }
        
        public bool GetIsFavoriteByUser(string userId)
        {
            return IsFavoriteByUser.TryGetValue(userId, out var value) && value;
        }
        
        public bool GetNextUnwatchedByUser(string userId)
        {
            return NextUnwatchedByUser.TryGetValue(userId, out var value) && value;
        }
        
        public double GetLastPlayedDateByUser(string userId)
        {
            return LastPlayedDateByUser.TryGetValue(userId, out var value) ? value : 0;
        }
    }
}