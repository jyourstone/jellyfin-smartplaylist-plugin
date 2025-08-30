namespace Jellyfin.Plugin.SmartPlaylist.Constants
{
    /// <summary>
    /// Centralized media type constants to prevent typos and improve maintainability
    /// </summary>
    public static class MediaTypes
    {
        // TV Types
        public const string Episode = nameof(Episode);
        public const string Series = nameof(Series);
        
        // Movie Types  
        public const string Movie = nameof(Movie);
        
        // Audio Types
        public const string Audio = nameof(Audio);
        
        // Music Video Types
        public const string MusicVideo = nameof(MusicVideo);
        
        // Home Video and Photo Types (matching Jellyfin's backend types)
        public const string Video = nameof(Video);
        public const string Photo = nameof(Photo);
        
        /// <summary>
        /// Gets all supported media types as an array
        /// </summary>
        public static readonly string[] All = [Episode, Series, Movie, Audio, MusicVideo, Video, Photo];
        
        /// <summary>
        /// Gets video media types (Movie, Series, Episode, MusicVideo, Video, Photo)
        /// Note: Photo is included here because it's part of the same library type as Video
        /// </summary>
        public static readonly string[] VideoTypes = [Movie, Series, Episode, MusicVideo, Video, Photo];
        
        /// <summary>
        /// Gets TV media types (Series, Episode)
        /// </summary>
        public static readonly string[] TV = [Series, Episode];
        
        /// <summary>
        /// Gets audio-only media types (Audio)
        /// </summary>
        public static readonly string[] AudioOnly = [Audio];
    }
}
