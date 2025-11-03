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
        public List<string> Actors { get; set; } = [];
        public List<string> Directors { get; set; } = [];
        public List<string> Composers { get; set; } = [];
        public List<string> Writers { get; set; } = [];
        public List<string> GuestStars { get; set; } = [];
        public List<string> Producers { get; set; } = [];
        public List<string> Conductors { get; set; } = [];
        public List<string> Lyricists { get; set; } = [];
        public List<string> Arrangers { get; set; } = [];
        public List<string> SoundEngineers { get; set; } = [];
        public List<string> Mixers { get; set; } = [];
        public List<string> Remixers { get; set; } = [];
        public List<string> Creators { get; set; } = [];
        public List<string> PersonArtists { get; set; } = []; // Person role "Artist" (different from music Artists field)
        public List<string> PersonAlbumArtists { get; set; } = []; // Person role "Album Artist" (different from music AlbumArtists field)
        public List<string> Authors { get; set; } = [];
        public List<string> Illustrators { get; set; } = [];
        public List<string> Pencilers { get; set; } = [];
        public List<string> Inkers { get; set; } = [];
        public List<string> Colorists { get; set; } = [];
        public List<string> Letterers { get; set; } = [];
        public List<string> CoverArtists { get; set; } = [];
        public List<string> Editors { get; set; } = [];
        public List<string> Translators { get; set; } = [];
        public string Resolution { get; set; } = "";
        public float? Framerate { get; set; } = null;
        
        // Music-specific fields
        public List<string> Artists { get; set; } = [];
        public List<string> AlbumArtists { get; set; } = [];
        
        // Audio quality fields (from media streams)
        public int AudioBitrate { get; set; } = 0;  // In kbps
        public int AudioSampleRate { get; set; } = 0;  // In Hz (e.g., 44100, 48000, 96000, 192000)
        public int AudioBitDepth { get; set; } = 0;  // In bits (e.g., 16, 24)
        public string AudioCodec { get; set; } = "";  // e.g., FLAC, MP3, AAC, ALAC
        public int AudioChannels { get; set; } = 0;  // e.g., 2 for stereo, 6 for 5.1
        
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