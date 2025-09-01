using System.Collections.Generic;
using Jellyfin.Data.Enums;

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
        /// Centralized mapping between BaseItemKind and MediaTypes.
        /// This is the single source of truth for all media type mappings.
        /// </summary>
        public static readonly Dictionary<BaseItemKind, string> BaseItemKindToMediaType = new()
        {
            { BaseItemKind.Episode, Episode },
            { BaseItemKind.Series, Series },
            { BaseItemKind.Movie, Movie },
            { BaseItemKind.Audio, Audio },
            { BaseItemKind.MusicVideo, MusicVideo },
            { BaseItemKind.Video, Video },
            { BaseItemKind.Photo, Photo },
            { BaseItemKind.Book, Book },
            { BaseItemKind.AudioBook, AudioBook }
        };
        
        /// <summary>
        /// Reverse mapping from MediaTypes to BaseItemKind.
        /// </summary>
        public static readonly Dictionary<string, BaseItemKind> MediaTypeToBaseItemKind = new()
        {
            { Episode, BaseItemKind.Episode },
            { Series, BaseItemKind.Series },
            { Movie, BaseItemKind.Movie },
            { Audio, BaseItemKind.Audio },
            { MusicVideo, BaseItemKind.MusicVideo },
            { Video, BaseItemKind.Video },
            { Photo, BaseItemKind.Photo },
            { Book, BaseItemKind.Book },
            { AudioBook, BaseItemKind.AudioBook }
        };
        
        /// <summary>
        /// Gets all supported media types as an array (derived from centralized mapping)
        /// </summary>
        public static readonly string[] All = [.. BaseItemKindToMediaType.Values];
        
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
