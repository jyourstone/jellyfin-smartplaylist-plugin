using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartPlaylist.QueryEngine
{
    public class Operand(string name)
    {

        public float CommunityRating { get; set; } = 0;
        public float CriticRating { get; set; } = 0;
        public List<string> Genres { get; set; } = [];
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
        public List<string> Tags { get; set; } = [];
        public List<string> ParentSeriesTags { get; set; } = [];
        public double RuntimeMinutes { get; set; } = 0;
        public string OfficialRating { get; set; } = "";
        public List<string> AudioLanguages { get; set; } = [];
        public List<string> People { get; set; } = [];
        public string Resolution { get; set; } = "";
        public float? Framerate { get; set; } = null;
        
        // Music-specific fields
        public List<string> Artists { get; set; } = [];
        public List<string> AlbumArtists { get; set; } = [];
        
        // Collections field - indicates which collections this item belongs to  
        public List<string> Collections { get; set; } = [];
        
        // Series name field - for episodes, contains the name of the parent series
        public string SeriesName { get; set; } = "";
        
        // User-specific data - Store user ID -> data mappings
        // These will be populated based on which users are referenced in rules
        public Dictionary<string, bool> IsPlayedByUser { get; set; } = [];
        public Dictionary<string, int> PlayCountByUser { get; set; } = [];
        public Dictionary<string, bool> IsFavoriteByUser { get; set; } = [];
        public Dictionary<string, bool> NextUnwatchedByUser { get; set; } = [];
        public Dictionary<string, double> LastPlayedDateByUser { get; set; } = [];
        
        // Similarity score - calculated when SimilarTo rules are present
        public float? SimilarityScore { get; set; } = null;
        
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
            return LastPlayedDateByUser.TryGetValue(userId, out var value) ? value : -1;
        }
    }
}