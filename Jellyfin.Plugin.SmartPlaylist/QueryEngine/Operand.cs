using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartPlaylist.QueryEngine
{
    public class Operand
    {
        public Operand(string name)
        {
            CommunityRating = 0;
            CriticRating = 0;
            Genres = new List<string>();
            Name = name;
            FolderPath = "";
            FileName = "";
            ProductionYear = 0;
            Studios = new List<string>();
            MediaType = "";
            ItemType = "";
            Album = "";
            DateCreated = 0;
            DateLastRefreshed = 0;
            DateLastSaved = 0;
            DateModified = 0;
            Tags = new List<string>();
            RuntimeMinutes = 0;
            PlayCount = 0;
            OfficialRating = "";
            IsFavorite = false;
            AudioLanguages = new List<string>();
            People = new List<string>();
        }

        public float CommunityRating { get; set; }
        public float CriticRating { get; set; }
        public List<string> Genres { get; set; }
        public bool IsPlayed { get; set; }
        public string Name { get; set; }
        public string FolderPath { get; set; }
        public string FileName { get; set; }
        public int ProductionYear { get; set; }
        public List<string> Studios { get; set; }
        public string MediaType { get; set; }
        public string ItemType { get; set; }
        public string Album { get; set; }
        public double DateCreated { get; set; }
        public double DateLastRefreshed { get; set; }
        public double DateLastSaved { get; set; }
        public double DateModified { get; set; }
        public List<string> Tags { get; set; }
        public int RuntimeMinutes { get; set; }
        public int PlayCount { get; set; }
        public string OfficialRating { get; set; }
        public bool IsFavorite { get; set; }
        public List<string> AudioLanguages { get; set; }
        public List<string> People { get; set; }
    }
}