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
        
        // Book Types
        public const string Book = nameof(Book);
        public const string AudioBook = nameof(AudioBook);
        
        /// <summary>
        /// Gets all supported media types as an array
        /// </summary>
        public static readonly string[] All = [Episode, Series, Movie, Audio, MusicVideo, Video, Photo, Book, AudioBook];
        
        /// <summary>
        /// Gets non-audio media types (everything except Audio and AudioBook)
        /// </summary>
        public static readonly string[] NonAudioTypes = [Movie, Series, Episode, MusicVideo, Video, Photo, Book];
        
        /// <summary>
        /// Gets audio-only media types (Audio, AudioBook)
        /// </summary>
        public static readonly string[] AudioOnly = [Audio, AudioBook];
        
        /// <summary>
        /// Gets book media types (Book, AudioBook)
        /// </summary>
        public static readonly string[] BookTypes = [Book, AudioBook];
        
        /// <summary>
        /// Gets TV media types (Series, Episode)
        /// </summary>
        public static readonly string[] TV = [Series, Episode];
        

    }
}
