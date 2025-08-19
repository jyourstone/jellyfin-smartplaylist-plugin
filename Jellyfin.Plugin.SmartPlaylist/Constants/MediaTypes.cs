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
        
        /// <summary>
        /// Gets all supported media types as an array
        /// </summary>
        public static readonly string[] All = [Episode, Series, Movie, Audio, MusicVideo];
        
        /// <summary>
        /// Gets video media types (Movie, Series, Episode, MusicVideo)
        /// </summary>
        public static readonly string[] Video = [Movie, Series, Episode, MusicVideo];
        
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
