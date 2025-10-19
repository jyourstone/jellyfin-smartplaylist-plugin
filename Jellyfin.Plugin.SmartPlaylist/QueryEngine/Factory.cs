using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.Audio;
using Video = MediaBrowser.Controller.Entities.Video;
using Photo = MediaBrowser.Controller.Entities.Photo;
using Book = MediaBrowser.Controller.Entities.Book;

using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.SmartPlaylist.Constants;

namespace Jellyfin.Plugin.SmartPlaylist.QueryEngine
{
    /// <summary>
    /// Parameters object for GetMediaType operations to improve readability and maintainability.
    /// </summary>
    public class MediaTypeExtractionOptions
    {
        public bool ExtractAudioLanguages { get; set; } = false;
        public bool ExtractPeople { get; set; } = false;
        public bool ExtractCollections { get; set; } = false;
        public bool ExtractNextUnwatched { get; set; } = false;
        public bool ExtractSeriesName { get; set; } = false;
        public bool ExtractParentSeriesTags { get; set; } = false;
        public bool IncludeUnwatchedSeries { get; set; } = true;
        public List<string> AdditionalUserIds { get; set; } = [];
    }

    internal class OperandFactory
    {
        // Cache reflection method lookups for better performance - using ConcurrentDictionary for thread safety
        private static readonly ConcurrentDictionary<Type, System.Reflection.MethodInfo> _getMediaStreamsMethodCache = new();
        private static readonly ConcurrentDictionary<Type, System.Reflection.PropertyInfo> _mediaSourcesPropertyCache = new();
        private static System.Reflection.MethodInfo _getPeopleMethodCache = null;
        private static readonly object _getPeopleMethodLock = new();
        
        // Known unsupported types to avoid logging noise
        private static readonly HashSet<string> _knownUnsupportedTypes = new() 
        { 
            "CollectionFolder", "UserRootFolder", "AggregateFolder", "Folder" 
        };
        
        // Cache episode property lookups for better performance - using ConcurrentDictionary for thread safety
        private static readonly ConcurrentDictionary<Type, System.Reflection.PropertyInfo> _parentIndexPropertyCache = new();
        private static readonly ConcurrentDictionary<Type, System.Reflection.PropertyInfo> _indexPropertyCache = new();
        private static readonly ConcurrentDictionary<Type, System.Reflection.PropertyInfo> _seriesIdPropertyCache = new();
        
        // Per-refresh cache classes for better performance within single playlist processing
        public class RefreshCache
        {
            public Dictionary<(Guid SeriesId, Guid UserId), BaseItem[]> SeriesEpisodes { get; } = [];
            public Dictionary<(Guid SeriesId, Guid UserId, bool IncludeUnwatchedSeries), (Guid? NextEpisodeId, int Season, int Episode)> NextUnwatched { get; } = [];
            public Dictionary<Guid, List<string>> ItemCollections { get; } = [];
            public BaseItem[] AllCollections { get; set; } = null;
            public Dictionary<Guid, HashSet<Guid>> CollectionMembershipCache { get; } = [];
            public Dictionary<Guid, string> SeriesNameById { get; } = [];
            public Dictionary<Guid, List<string>> SeriesTagsById { get; } = [];
            public Dictionary<Guid, List<string>> ItemPeople { get; } = [];
            public bool PeopleCacheInitialized { get; set; } = false;
        }

        /// <summary>
        /// Sets fallback values for user-specific data when userData is unavailable or invalid.
        /// </summary>
        /// <param name="operand">The operand to populate</param>
        /// <param name="userId">The user ID (as string)</param>
        /// <param name="isPlayed">The IsPlayed value to use</param>
        private static void SetUserDataFallbacks(Operand operand, string userId, bool isPlayed)
        {
            operand.IsPlayedByUser[userId] = isPlayed;
            operand.PlayCountByUser[userId] = isPlayed ? 1 : 0;
            operand.IsFavoriteByUser[userId] = false;
            operand.LastPlayedDateByUser[userId] = -1; // Never played
        }

        /// <summary>
        /// Populates user-specific data from userData into the operand.
        /// </summary>
        /// <param name="operand">The operand to populate</param>
        /// <param name="userId">The user ID (as string)</param>
        /// <param name="isPlayed">The IsPlayed value</param>
        /// <param name="userData">The userData object to extract from</param>
        private static void PopulateUserData(Operand operand, string userId, bool isPlayed, object userData)
        {
            operand.IsPlayedByUser[userId] = isPlayed;
            
            // Use reflection to safely extract properties from userData
            var userDataType = userData.GetType();
            
            // Extract PlayCount
            var playCountProp = userDataType.GetProperty("PlayCount");
            if (playCountProp != null)
            {
                var playCountValue = playCountProp.GetValue(userData);
                var playCount = ExtractIntValue(playCountValue);
                operand.PlayCountByUser[userId] = playCount.GetValueOrDefault(0);
            }
            else
            {
                operand.PlayCountByUser[userId] = 0;
            }
            
            // Extract IsFavorite
            var isFavoriteProp = userDataType.GetProperty("IsFavorite");
            if (isFavoriteProp != null)
            {
                var isFavoriteValue = isFavoriteProp.GetValue(userData);
                operand.IsFavoriteByUser[userId] = isFavoriteValue != null && (bool)isFavoriteValue;
            }
            else
            {
                operand.IsFavoriteByUser[userId] = false;
            }
            
            // Extract LastPlayedDate - handle both nullable and non-nullable DateTime
            var lastPlayedDateProp = userDataType.GetProperty("LastPlayedDate");
            if (lastPlayedDateProp != null)
            {
                var lastPlayedDateValue = lastPlayedDateProp.GetValue(userData);
                if (lastPlayedDateValue != null)
                {
                    // Handle nullable DateTime
                    if (lastPlayedDateValue is DateTime dateTime && dateTime != DateTime.MinValue)
                    {
                        operand.LastPlayedDateByUser[userId] = SafeToUnixTimeSeconds(dateTime);
                    }
                    // Handle nullable DateTime (DateTime?)
                    else if (lastPlayedDateValue.GetType().IsGenericType && 
                             lastPlayedDateValue.GetType().GetGenericTypeDefinition() == typeof(Nullable<>) &&
                             lastPlayedDateValue.GetType().GetGenericArguments()[0] == typeof(DateTime))
                    {
                        var nullableDateTimeProp = lastPlayedDateValue.GetType().GetProperty("HasValue");
                        var valueProp = lastPlayedDateValue.GetType().GetProperty("Value");
                        
                        if (nullableDateTimeProp != null && valueProp != null)
                        {
                            bool hasValue = (bool)nullableDateTimeProp.GetValue(lastPlayedDateValue);
                            if (hasValue)
                            {
                                var dateValue = (DateTime)valueProp.GetValue(lastPlayedDateValue);
                                operand.LastPlayedDateByUser[userId] = SafeToUnixTimeSeconds(dateValue);
                            }
                            else
                            {
                                operand.LastPlayedDateByUser[userId] = -1; // Never played
                            }
                        }
                        else
                        {
                            operand.LastPlayedDateByUser[userId] = -1; // Never played
                        }
                    }
                    else
                    {
                        operand.LastPlayedDateByUser[userId] = -1; // Never played - unhandled type
                    }
                }
                else
                {
                    operand.LastPlayedDateByUser[userId] = -1; // Never played - null value
                }
            }
            else
            {
                operand.LastPlayedDateByUser[userId] = -1; // Never played - property not found
            }
        }

        /// <summary>
        /// Safely extracts an integer value from a property, handling both nullable and non-nullable int properties.
        /// </summary>
        /// <param name="value">The property value to convert</param>
        /// <returns>Nullable int representing the extracted value</returns>
        private static int? ExtractIntValue(object value)
        {
            if (value is int intValue)
                return intValue;
            if (value == null)
                return null;
            
            // Try to convert to int if it's some other numeric type
            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts audio languages from media streams.
        /// </summary>
        private static void ExtractAudioLanguages(Operand operand, BaseItem baseItem, ILogger logger)
        {
            operand.AudioLanguages = [];
            try
            {
                // Try multiple approaches to access media stream information
                List<object> mediaStreams = [];
                
                // Approach 1: Try GetMediaStreams method if it exists (with caching)
                var baseItemType = baseItem.GetType();
                var getMediaStreamsMethod = _getMediaStreamsMethodCache.GetOrAdd(baseItemType, type => type.GetMethod("GetMediaStreams"));
                
                if (getMediaStreamsMethod != null)
                {
                    try
                    {
                        var result = getMediaStreamsMethod.Invoke(baseItem, null);
                        if (result is IEnumerable<object> streamEnum)
                        {
                            mediaStreams.AddRange(streamEnum);
                        }
                        else
                        {
                            logger?.LogWarning(
                                "GetMediaStreams method for item {Name} returned a non-enumerable type: {Type}",
                                baseItem.Name,
                                result?.GetType().FullName ?? "null"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogDebug(ex, "Failed to call GetMediaStreams method for item {Name}", baseItem.Name);
                    }
                }
                
                // Approach 2: Look for MediaSources property (with caching)
                var mediaSourcesProperty = _mediaSourcesPropertyCache.GetOrAdd(baseItemType, type => type.GetProperty("MediaSources"));
                
                if (mediaSourcesProperty != null)
                {
                    var mediaSources = mediaSourcesProperty.GetValue(baseItem);
                    if (mediaSources != null && mediaSources is IEnumerable<object> sourceEnum)
                    {
                        foreach (var source in sourceEnum)
                        {
                            try
                            {
                                var streamsProperty = source.GetType().GetProperty("MediaStreams");
                                if (streamsProperty != null)
                                {
                                    var streams = streamsProperty.GetValue(source);
                                    if (streams is IEnumerable<object> streamList)
                                    {
                                        mediaStreams.AddRange(streamList);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                logger?.LogDebug(ex, "Failed to process MediaSource for item {Name}", baseItem.Name);
                            }
                        }
                    }
                }
                
                // Process found streams
                foreach (var stream in mediaStreams)
                {
                    try
                    {
                        var typeProperty = stream.GetType().GetProperty("Type");
                        var languageProperty = stream.GetType().GetProperty("Language");
                        
                        if (typeProperty != null)
                        {
                            var streamType = typeProperty.GetValue(stream);
                            var language = languageProperty?.GetValue(stream) as string;
                            
                            // Check if it's an audio stream
                            if (streamType != null && streamType.ToString() == "Audio")
                            {
                                if (!string.IsNullOrEmpty(language) && !operand.AudioLanguages.Contains(language))
                                {
                                    operand.AudioLanguages.Add(language);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogDebug(ex, "Failed to process individual stream for item {Name}", baseItem.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to extract audio languages for item {Name}", baseItem.Name);
            }
        }

        /// <summary>
        /// Extracts resolution from media streams.
        /// </summary>
        private static void ExtractResolution(Operand operand, BaseItem baseItem, ILogger logger)
        {
            operand.Resolution = "";
            try
            {
                // Try multiple approaches to access media stream information
                List<object> mediaStreams = [];
                
                // Approach 1: Try GetMediaStreams method if it exists (with caching)
                var baseItemType = baseItem.GetType();
                var getMediaStreamsMethod = _getMediaStreamsMethodCache.GetOrAdd(baseItemType, type => type.GetMethod("GetMediaStreams"));
                
                if (getMediaStreamsMethod != null)
                {
                    try
                    {
                        var result = getMediaStreamsMethod.Invoke(baseItem, null);
                        if (result is IEnumerable<object> streamEnum)
                        {
                            mediaStreams.AddRange(streamEnum);
                        }
                        else
                        {
                            logger?.LogWarning(
                                "GetMediaStreams method for item {Name} returned a non-enumerable type: {Type}",
                                baseItem.Name,
                                result?.GetType().FullName ?? "null"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogDebug(ex, "Failed to call GetMediaStreams method for item {Name}", baseItem.Name);
                    }
                }
                
                // Approach 2: Look for MediaSources property (with caching)
                var mediaSourcesProperty = _mediaSourcesPropertyCache.GetOrAdd(baseItemType, type => type.GetProperty("MediaSources"));
                
                if (mediaSourcesProperty != null)
                {
                    var mediaSources = mediaSourcesProperty.GetValue(baseItem);
                    if (mediaSources != null && mediaSources is IEnumerable<object> sourceEnum)
                    {
                        foreach (var source in sourceEnum)
                        {
                            try
                            {
                                var streamsProperty = source.GetType().GetProperty("MediaStreams");
                                if (streamsProperty != null)
                                {
                                    var streams = streamsProperty.GetValue(source);
                                    if (streams is IEnumerable<object> streamList)
                                    {
                                        mediaStreams.AddRange(streamList);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                logger?.LogDebug(ex, "Failed to process MediaSource for item {Name}", baseItem.Name);
                            }
                        }
                    }
                }
                
                // Process found streams to find the highest resolution video stream
                int maxHeight = 0;
                foreach (var stream in mediaStreams)
                {
                    try
                    {
                        var typeProperty = stream.GetType().GetProperty("Type");
                        var heightProperty = stream.GetType().GetProperty("Height");
                        
                        if (typeProperty != null && heightProperty != null)
                        {
                            var streamType = typeProperty.GetValue(stream);
                            var height = heightProperty.GetValue(stream);
                            
                            // Check if it's a video stream
                            if (streamType != null && streamType.ToString() == "Video" && height != null)
                            {
                                if (int.TryParse(height.ToString(), out int heightValue) && heightValue > maxHeight)
                                {
                                    maxHeight = heightValue;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogDebug(ex, "Failed to process individual stream for item {Name}", baseItem.Name);
                    }
                }
                
                // Convert height to resolution string
                if (maxHeight > 0)
                {
                    operand.Resolution = maxHeight switch
                    {
                        <= 480 => "480p",
                        <= 720 => "720p",
                        <= 1080 => "1080p",
                        <= 1440 => "1440p",
                        <= 2160 => "4K",
                        <= 4320 => "8K",
                        _ => "8K" // For anything higher, default to 8K
                    };
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to extract resolution for item {Name}", baseItem.Name);
            }
        }

        /// <summary>
        /// Extracts framerate from media streams.
        /// </summary>
        private static void ExtractFramerate(Operand operand, BaseItem baseItem, ILogger logger)
        {
            operand.Framerate = null;
            try
            {
                // Try multiple approaches to access media stream information
                List<object> mediaStreams = [];
                
                // Approach 1: Try GetMediaStreams method if it exists (with caching)
                var baseItemType = baseItem.GetType();
                var getMediaStreamsMethod = _getMediaStreamsMethodCache.GetOrAdd(baseItemType, type => type.GetMethod("GetMediaStreams"));
                
                if (getMediaStreamsMethod != null)
                {
                    try
                    {
                        var result = getMediaStreamsMethod.Invoke(baseItem, null);
                        if (result is IEnumerable<object> streamEnum)
                        {
                            mediaStreams.AddRange(streamEnum);
                        }
                        else
                        {
                            logger?.LogWarning(
                                "GetMediaStreams method for item {Name} returned a non-enumerable type: {Type}",
                                baseItem.Name,
                                result?.GetType().FullName ?? "null"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogDebug(ex, "Failed to call GetMediaStreams method for item {Name}", baseItem.Name);
                    }
                }
                
                // Approach 2: Look for MediaSources property (with caching)
                var mediaSourcesProperty = _mediaSourcesPropertyCache.GetOrAdd(baseItemType, type => type.GetProperty("MediaSources"));
                
                if (mediaSourcesProperty != null)
                {
                    var mediaSources = mediaSourcesProperty.GetValue(baseItem);
                    if (mediaSources != null && mediaSources is IEnumerable<object> sourceEnum)
                    {
                        foreach (var source in sourceEnum)
                        {
                            try
                            {
                                var streamsProperty = source.GetType().GetProperty("MediaStreams");
                                if (streamsProperty != null)
                                {
                                    var streams = streamsProperty.GetValue(source);
                                    if (streams is IEnumerable<object> streamList)
                                    {
                                        mediaStreams.AddRange(streamList);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                logger?.LogDebug(ex, "Failed to process MediaSource for item {Name}", baseItem.Name);
                            }
                        }
                    }
                }
                
                // Process found streams to find the first video stream with framerate information
                foreach (var stream in mediaStreams)
                {
                    try
                    {
                        var typeProperty = stream.GetType().GetProperty("Type");
                        var framerateProperty = stream.GetType().GetProperty("RealFrameRate") ?? stream.GetType().GetProperty("AverageFrameRate");
                        
                        if (typeProperty != null && framerateProperty != null)
                        {
                            var streamType = typeProperty.GetValue(stream);
                            var framerate = framerateProperty.GetValue(stream);
                            
                            // Check if it's a video stream
                            if (streamType != null && streamType.ToString() == "Video" && framerate != null)
                            {
                                // Try to parse framerate as different numeric types
                                if (framerate is float floatFramerate && floatFramerate > 0)
                                {
                                    operand.Framerate = floatFramerate;
                                    break; // Use the first valid framerate found
                                }
                                else if (framerate is double doubleFramerate && doubleFramerate > 0)
                                {
                                    operand.Framerate = (float)doubleFramerate;
                                    break;
                                }
                                else if (framerate is int intFramerate && intFramerate > 0)
                                {
                                    operand.Framerate = intFramerate;
                                    break;
                                }
                                else if (double.TryParse(framerate.ToString(), out var parsedFramerate) && parsedFramerate > 0)
                                {
                                    operand.Framerate = (float)parsedFramerate;
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogDebug(ex, "Failed to process individual stream for item {Name}", baseItem.Name);
                    }
                }
                
                logger?.LogDebug("Extracted framerate for item {Name}: {Framerate}", baseItem.Name, operand.Framerate?.ToString() ?? "null");
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to extract framerate for item {Name}", baseItem.Name);
            }
        }

        /// <summary>
        /// Helper method to safely extract SeriesId as Guid from episode items.
        /// Handles Guid, Guid?, and string representations.
        /// </summary>
        private static bool TryGetEpisodeSeriesGuid(BaseItem baseItem, out Guid seriesGuid)
        {
            seriesGuid = Guid.Empty;
            if (baseItem is not Episode) return false;

            var episodeType = baseItem.GetType();
            var seriesIdProperty = _seriesIdPropertyCache.GetOrAdd(episodeType, t => t.GetProperty("SeriesId"));
            if (seriesIdProperty == null) return false;

            var seriesId = seriesIdProperty.GetValue(baseItem);
            if (seriesId is Guid g) { seriesGuid = g; return true; }
            if (seriesId != null && seriesId.GetType() == typeof(Guid?))
            {
                var nullableGuid = (Guid?)seriesId;
                if (nullableGuid.HasValue) { seriesGuid = nullableGuid.Value; return true; }
            }
            if (seriesId is string s && Guid.TryParse(s, out var parsed)) { seriesGuid = parsed; return true; }

            return false;
        }

        /// <summary>
        /// Extracts the series name for episodes with per-refresh caching.
        /// </summary>
        private static void ExtractSeriesName(Operand operand, BaseItem baseItem, ILibraryManager libraryManager, RefreshCache cache, ILogger logger)
        {
            operand.SeriesName = "";
            try
            {
                // Use helper to extract SeriesId safely
                if (TryGetEpisodeSeriesGuid(baseItem, out var seriesGuid))
                {
                    // Check cache first to avoid repeated library lookups
                    if (cache.SeriesNameById.TryGetValue(seriesGuid, out var cachedName))
                    {
                        operand.SeriesName = cachedName;
                        logger?.LogDebug("Using cached series name '{SeriesName}' for episode '{EpisodeName}'", 
                            operand.SeriesName, baseItem.Name);
                    }
                    else
                    {
                        try
                        {
                            // Get the parent series from the library manager
                            var parentSeries = libraryManager.GetItemById(seriesGuid);
                            var seriesName = parentSeries?.Name ?? "";
                            
                            // Cache the result for future episodes from the same series
                            cache.SeriesNameById[seriesGuid] = seriesName;
                            operand.SeriesName = seriesName;
                            
                            logger?.LogDebug("Extracted and cached series name '{SeriesName}' for episode '{EpisodeName}'", 
                                operand.SeriesName, baseItem.Name);
                        }
                        catch (Exception ex)
                        {
                            logger?.LogDebug(ex, "Failed to get parent series for episode '{EpisodeName}' with SeriesId {SeriesId}", 
                                baseItem.Name, seriesGuid);
                            
                            // Cache empty string to avoid repeated failures
                            cache.SeriesNameById[seriesGuid] = "";
                        }
                    }
                }
                else
                {
                    // Either not an episode, no SeriesId property, or unsupported SeriesId value
                    if (baseItem is Episode)
                    {
                        logger?.LogDebug("Could not extract valid SeriesId from episode '{EpisodeName}'", baseItem.Name);
                    }
                    else
                    {
                        logger?.LogDebug("Item '{ItemName}' is not an episode, series name remains empty", baseItem.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to extract series name for item '{ItemName}'", baseItem.Name);
            }
        }

        /// <summary>
        /// Extracts the parent series tags for episodes with per-refresh caching.
        /// This is an expensive operation as it requires a database lookup, so caching is critical for performance.
        /// </summary>
        private static void ExtractParentSeriesTags(Operand operand, BaseItem baseItem, ILibraryManager libraryManager, RefreshCache cache, ILogger logger)
        {
            operand.ParentSeriesTags = [];
            try
            {
                // Only process episodes - other item types don't have parent series
                if (baseItem is not Episode)
                {
                    logger?.LogDebug("Item '{ItemName}' is not an episode, parent series tags remain empty", baseItem.Name);
                    return;
                }
                
                // Use helper to extract SeriesId safely
                if (TryGetEpisodeSeriesGuid(baseItem, out var seriesGuid))
                {
                    // Check cache first to avoid repeated library lookups (expensive!)
                    if (cache.SeriesTagsById.TryGetValue(seriesGuid, out var cachedTags))
                    {
                        operand.ParentSeriesTags = cachedTags;
                        logger?.LogDebug("Using cached parent series tags for episode '{EpisodeName}' (series ID: {SeriesId}): [{Tags}]", 
                            baseItem.Name, seriesGuid, string.Join(", ", cachedTags));
                    }
                    else
                    {
                        try
                        {
                            // Get the parent series from the library manager (expensive operation!)
                            var parentSeries = libraryManager.GetItemById(seriesGuid);
                            var seriesTags = parentSeries?.Tags?.ToList() ?? [];
                            
                            // Cache the result for future episodes from the same series
                            cache.SeriesTagsById[seriesGuid] = seriesTags;
                            operand.ParentSeriesTags = seriesTags;
                            
                            logger?.LogDebug("Extracted and cached parent series tags for episode '{EpisodeName}' (series: '{SeriesName}'): [{Tags}]", 
                                baseItem.Name, parentSeries?.Name ?? "Unknown", string.Join(", ", seriesTags));
                        }
                        catch (Exception ex)
                        {
                            logger?.LogDebug(ex, "Failed to get parent series tags for episode '{EpisodeName}' with SeriesId {SeriesId}", 
                                baseItem.Name, seriesGuid);
                            
                            // Cache empty list to avoid repeated failures
                            cache.SeriesTagsById[seriesGuid] = [];
                        }
                    }
                }
                else
                {
                    logger?.LogDebug("Could not extract valid SeriesId from episode '{EpisodeName}'", baseItem.Name);
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to extract parent series tags for item '{ItemName}'", baseItem.Name);
            }
        }

        /// <summary>
        /// Extracts people (actors, directors, producers, etc.) associated with the item.
        /// </summary>
        private static void ExtractPeople(Operand operand, BaseItem baseItem, ILibraryManager libraryManager, RefreshCache cache, ILogger logger)
        {
            operand.People = [];
            
            // Check cache first if available
            if (cache != null && cache.ItemPeople.TryGetValue(baseItem.Id, out var cachedPeople))
            {
                operand.People = new List<string>(cachedPeople);
                return;
            }
            
            // Cache miss or no cache - perform query
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                // Cache the GetPeople method lookup for better performance
                var getPeopleMethod = _getPeopleMethodCache;
                if (getPeopleMethod == null)
                {
                    lock (_getPeopleMethodLock)
                    {
                        if (_getPeopleMethodCache == null)
                        {
                            _getPeopleMethodCache = libraryManager.GetType().GetMethod("GetPeople", [typeof(InternalPeopleQuery)]);
                        }
                        getPeopleMethod = _getPeopleMethodCache;
                    }
                }
                
                if (getPeopleMethod != null)
                {
                    // Use InternalPeopleQuery to get people associated with this item
                    var peopleQuery = new InternalPeopleQuery
                    {
                        ItemId = baseItem.Id
                    };
                    
                    var result = getPeopleMethod.Invoke(libraryManager, [peopleQuery]);
                    
                    if (result is IEnumerable<object> peopleEnum)
                    {
                        foreach (var person in peopleEnum)
                        {
                            if (person != null)
                            {
                                var nameProperty = person.GetType().GetProperty("Name");
                                if (nameProperty != null)
                                {
                                    var name = nameProperty.GetValue(person) as string;
                                    if (!string.IsNullOrEmpty(name) && !operand.People.Contains(name))
                                    {
                                        operand.People.Add(name);
                                    }
                                }
                            }
                        }
                    }
                    
                    stopwatch.Stop();
                    logger?.LogDebug("People query for item {ItemId} completed in {Ms}ms ({PeopleCount} people)", 
                        baseItem.Id, stopwatch.ElapsedMilliseconds, operand.People.Count);
                    
                    // Store in cache for future use
                    if (cache != null)
                    {
                        cache.ItemPeople[baseItem.Id] = new List<string>(operand.People);
                    }
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                logger?.LogWarning(ex, "Failed to extract people for item {Name} after {Ms}ms", baseItem.Name, stopwatch.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Preloads people data for all items in parallel to improve performance.
        /// </summary>
        public static void PreloadPeopleCache(ILibraryManager libraryManager, IEnumerable<BaseItem> items, RefreshCache cache, ILogger logger)
        {
            if (cache == null || items == null)
            {
                return;
            }
            
            // Skip if already initialized
            if (cache.PeopleCacheInitialized)
            {
                logger?.LogDebug("People cache already initialized, skipping preload");
                return;
            }
            
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var itemList = items.ToList();
            logger?.LogDebug("Starting parallel people cache initialization for {Count} items", itemList.Count);
            
            // Use plugin configuration for parallelism
            var config = Plugin.Instance?.Configuration;
            var maxConcurrency = ParallelismHelper.CalculateParallelConcurrency(config);
            
            // Thread-safe dictionary for collecting results
            var tempCache = new ConcurrentDictionary<Guid, List<string>>();
            var processedCount = 0;
            var totalMs = 0L;
            var lockObj = new object();
            
            // Process items in parallel
            System.Threading.Tasks.Parallel.ForEach(
                itemList,
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = maxConcurrency },
                item =>
                {
                    var itemStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    
                    try
                    {
                        // Cache the GetPeople method lookup
                        var getPeopleMethod = _getPeopleMethodCache;
                        if (getPeopleMethod == null)
                        {
                            lock (_getPeopleMethodLock)
                            {
                                if (_getPeopleMethodCache == null)
                                {
                                    _getPeopleMethodCache = libraryManager.GetType().GetMethod("GetPeople", [typeof(InternalPeopleQuery)]);
                                }
                                getPeopleMethod = _getPeopleMethodCache;
                            }
                        }
                        
                        if (getPeopleMethod != null)
                        {
                            var peopleQuery = new InternalPeopleQuery
                            {
                                ItemId = item.Id
                            };
                            
                            var result = getPeopleMethod.Invoke(libraryManager, [peopleQuery]);
                            var peopleList = new List<string>();
                            
                            if (result is IEnumerable<object> peopleEnum)
                            {
                                foreach (var person in peopleEnum)
                                {
                                    if (person != null)
                                    {
                                        var nameProperty = person.GetType().GetProperty("Name");
                                        if (nameProperty != null)
                                        {
                                            var name = nameProperty.GetValue(person) as string;
                                            if (!string.IsNullOrEmpty(name) && !peopleList.Contains(name))
                                            {
                                                peopleList.Add(name);
                                            }
                                        }
                                    }
                                }
                            }
                            
                            tempCache[item.Id] = peopleList;
                            
                            itemStopwatch.Stop();
                            lock (lockObj)
                            {
                                processedCount++;
                                totalMs += itemStopwatch.ElapsedMilliseconds;
                                
                                // Log progress every 100 items
                                if (processedCount % 100 == 0)
                                {
                                    var avgMs = totalMs / processedCount;
                                    logger?.LogDebug("People cache progress: {Processed}/{Total} items ({Avg}ms avg per item)", 
                                        processedCount, itemList.Count, avgMs);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        itemStopwatch.Stop();
                        logger?.LogWarning(ex, "Failed to preload people for item {ItemId} after {Ms}ms", item.Id, itemStopwatch.ElapsedMilliseconds);
                    }
                });
            
            // Transfer from concurrent dictionary to cache
            foreach (var kvp in tempCache)
            {
                cache.ItemPeople[kvp.Key] = kvp.Value;
            }
            
            cache.PeopleCacheInitialized = true;
            stopwatch.Stop();
            
            var avgTimeMs = processedCount > 0 ? totalMs / processedCount : 0;
            logger?.LogDebug("People cache initialization completed in {TotalMs}ms for {Count} items (avg {AvgMs}ms per item, {Concurrency} parallel threads)", 
                stopwatch.ElapsedMilliseconds, itemList.Count, avgTimeMs, maxConcurrency);
        }

        /// <summary>
        /// Extracts artists and album artists for music items.
        /// </summary>
        private static void ExtractArtists(Operand operand, BaseItem baseItem, ILogger logger)
        {
            operand.Artists = [];
            operand.AlbumArtists = [];
            
            try
            {
                // Try to extract Artist property
                var artistProperty = baseItem.GetType().GetProperty("Artist");
                if (artistProperty != null)
                {
                    var artistValue = artistProperty.GetValue(baseItem) as string;
                    if (!string.IsNullOrEmpty(artistValue))
                    {
                        operand.Artists.Add(artistValue);
                    }
                }
                
                // Try to extract Artists property (collection)
                var artistsProperty = baseItem.GetType().GetProperty("Artists");
                if (artistsProperty != null)
                {
                    var artistsValue = artistsProperty.GetValue(baseItem);
                    if (artistsValue is IEnumerable<string> artistsCollection)
                    {
                        foreach (var artist in artistsCollection)
                        {
                            if (!string.IsNullOrEmpty(artist) && !operand.Artists.Contains(artist))
                            {
                                operand.Artists.Add(artist);
                            }
                        }
                    }
                }
                
                // Try to extract AlbumArtist property
                var albumArtistProperty = baseItem.GetType().GetProperty("AlbumArtist");
                if (albumArtistProperty != null)
                {
                    var albumArtistValue = albumArtistProperty.GetValue(baseItem) as string;
                    if (!string.IsNullOrEmpty(albumArtistValue))
                    {
                        operand.AlbumArtists.Add(albumArtistValue);
                    }
                }
                
                // Try to extract AlbumArtists property (collection)
                var albumArtistsProperty = baseItem.GetType().GetProperty("AlbumArtists");
                if (albumArtistsProperty != null)
                {
                    var albumArtistsValue = albumArtistsProperty.GetValue(baseItem);
                    if (albumArtistsValue is IEnumerable<string> albumArtistsCollection)
                    {
                        foreach (var albumArtist in albumArtistsCollection)
                        {
                            if (!string.IsNullOrEmpty(albumArtist) && !operand.AlbumArtists.Contains(albumArtist))
                            {
                                operand.AlbumArtists.Add(albumArtist);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to extract artists for item {Name}", baseItem.Name);
            }
        }

        // Clean API using options object - no more boolean flag proliferation!
        public static Operand GetMediaType(ILibraryManager libraryManager, BaseItem baseItem, User user, 
            IUserDataManager userDataManager, IUserManager userManager, ILogger logger, MediaTypeExtractionOptions options,
            RefreshCache cache)
        {
            // Validate options parameter to avoid NullReferenceException
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options), "MediaTypeExtractionOptions cannot be null");
            }
            
            // Extract options for easier access
            var extractAudioLanguages = options.ExtractAudioLanguages;
            var extractPeople = options.ExtractPeople;  
            var extractNextUnwatched = options.ExtractNextUnwatched;
            var extractSeriesName = options.ExtractSeriesName;
            var extractParentSeriesTags = options.ExtractParentSeriesTags;
            var includeUnwatchedSeries = options.IncludeUnwatchedSeries;
            var additionalUserIds = options.AdditionalUserIds;
            
            // Get user data first for Jellyfin 10.11 compatibility
            var userData = userDataManager?.GetUserData(user, baseItem);
            
            // Cache the IsPlayed result to avoid multiple expensive calls
            var isPlayed = userData != null ? baseItem.IsPlayed(user, userData) : false;

            var operand = new Operand(baseItem.Name)
            {
                Genres = [.. baseItem.Genres],
                Studios = [.. baseItem.Studios],
                CommunityRating = baseItem.CommunityRating.GetValueOrDefault(),
                CriticRating = baseItem.CriticRating.GetValueOrDefault(),
                MediaType = baseItem.MediaType.ToString(),
                ItemType = GetItemTypeName(baseItem, logger),
                Album = baseItem.Album,
                ProductionYear = baseItem.ProductionYear.GetValueOrDefault(),
                Tags = baseItem.Tags is not null ? [.. baseItem.Tags] : [],
                RuntimeMinutes = baseItem.RunTimeTicks.HasValue ? TimeSpan.FromTicks(baseItem.RunTimeTicks.Value).TotalMinutes : 0.0
            };

            // Extract series name for episodes - only when needed for performance
            if (extractSeriesName)
            {
                ExtractSeriesName(operand, baseItem, libraryManager, cache, logger);
            }
            else
            {
                operand.SeriesName = ""; // Ensure consistent default
                logger?.LogDebug("SeriesName extraction skipped for item {Name} - not needed by rules", baseItem.Name);
            }

            // Try to access user data properly
            try
            {
                if (userDataManager != null && userData != null)
                {
                    // Populate user-specific data for playlist owner
                    PopulateUserData(operand, user.Id.ToString(), isPlayed, userData);
                }
                else if (userDataManager != null)
                {
                    // Fallback when userData is null - treat as never played for playlist owner
                    SetUserDataFallbacks(operand, user.Id.ToString(), isPlayed);
                }
                else
                {
                    // Fallback approach - try reflection and populate dictionaries for playlist owner
                    var userDataProperty = baseItem.GetType().GetProperty("UserData");
                    if (userDataProperty != null)
                    {
                        var reflectedUserData = userDataProperty.GetValue(baseItem);
                        if (reflectedUserData != null)
                        {
                            // Use our helper method to populate user data consistently
                            PopulateUserData(operand, user.Id.ToString(), isPlayed, reflectedUserData);
                        }
                        else
                        {
                            // UserData is null - set fallback values for playlist owner
                            SetUserDataFallbacks(operand, user.Id.ToString(), isPlayed);
                        }
                    }
                    else
                    {
                        // UserData property not found - set fallback values for playlist owner
                        SetUserDataFallbacks(operand, user.Id.ToString(), isPlayed);
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error accessing user data for item {Name}", baseItem.Name);
                // Keep the fallback values we set above
            }
            
            // Extract user-specific data for additional users
            if (additionalUserIds != null && additionalUserIds.Count > 0 && userDataManager != null)
            {
                foreach (var userId in additionalUserIds)
                {
                    try
                    {
                        if (Guid.TryParse(userId, out var userGuid))
                        {
                            // Try to get user by ID
                            try
                            {
                                var targetUser = GetUserById(userManager, userGuid);
                                if (targetUser != null)
                                {
                                    // Get user data first for Jellyfin 10.11 compatibility
                                    var targetUserData = userDataManager.GetUserData(targetUser, baseItem);
                                    var userIsPlayed = targetUserData != null ? baseItem.IsPlayed(targetUser, targetUserData) : false;
                                    operand.IsPlayedByUser[userId] = userIsPlayed;
                                    
                                    if (targetUserData != null)
                                    {
                                        PopulateUserData(operand, userId, userIsPlayed, targetUserData);
                                    }
                                    else
                                    {
                                        // Fallback values when targetUserData is null
                                        SetUserDataFallbacks(operand, userId, userIsPlayed);
                                    }
                                }
                                else
                                {
                                    // User exists in system but GetUserById returned null - this is a legitimate "user not found" case
                                    logger?.LogWarning("User with ID {UserId} not found for user-specific data extraction. This playlist rule references a user that no longer exists.", userId);
                                    throw new InvalidOperationException($"User with ID {userId} not found. This playlist rule references a user that no longer exists.");
                                }
                            }
                            catch (InvalidOperationException ex) when (ex.Message.Contains("reflection") || ex.Message.Contains("internal structure"))
                            {
                                // This is a reflection failure, not a missing user - provide a more helpful error
                                logger?.LogError(ex, "Failed to access user manager via reflection for user {UserId}. This may be due to a Jellyfin version compatibility issue.", userId);
                                throw new InvalidOperationException($"Unable to access user information due to internal system changes. This plugin may need to be updated for this version of Jellyfin. Original error: {ex.Message}", ex);
                            }
                        }
                        else
                        {
                            logger?.LogWarning("Invalid user ID format: {UserId}", userId);
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Re-throw InvalidOperationException to allow SmartPlaylist.cs to handle it properly
                        // This stops playlist processing when a referenced user no longer exists or when reflection fails
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Error extracting user data for user {UserId} on item {Name}", userId, baseItem.Name);
                    }
                }
            }
            
            operand.OfficialRating = baseItem.OfficialRating ?? "";
            
            // Extract Overview property using reflection
            try
            {
                var overviewProperty = baseItem.GetType().GetProperty("Overview");
                if (overviewProperty != null)
                {
                    var overviewValue = overviewProperty.GetValue(baseItem) as string;
                    operand.Overview = overviewValue ?? "";
                }
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Failed to extract Overview for item {Name}", baseItem.Name);
                operand.Overview = "";
            }
            
            operand.DateCreated = SafeToUnixTimeSeconds(baseItem.DateCreated);
            operand.DateLastRefreshed = SafeToUnixTimeSeconds(baseItem.DateLastRefreshed);
            operand.DateLastSaved = SafeToUnixTimeSeconds(baseItem.DateLastSaved);
            operand.DateModified = SafeToUnixTimeSeconds(baseItem.DateModified);
            
            // Extract ReleaseDate from PremiereDate property
            operand.ReleaseDate = DateUtils.GetReleaseDateUnixTimestamp(baseItem);
            
            operand.FolderPath = baseItem.ContainingFolderPath;
            
            // Fix null reference exception for Path
            operand.FileName = !string.IsNullOrEmpty(baseItem.Path) ? 
                System.IO.Path.GetFileName(baseItem.Path) ?? "" : "";
            
            // Extract audio languages from media streams - only when needed for performance
            if (extractAudioLanguages)
            {
                ExtractAudioLanguages(operand, baseItem, logger);
            }
            else
            {
                operand.AudioLanguages = [];
            }

            // Extract people - only when needed for performance
            if (extractPeople)
            {
                ExtractPeople(operand, baseItem, libraryManager, cache, logger);
            }
            else
            {
                operand.People = [];
                logger?.LogDebug("People extraction skipped for item {Name} - not needed by rules", baseItem.Name);
            }

            // Extract resolution/framerate only for items that can have video streams
            if (MediaTypes.VideoStreamCapableSet.Contains(operand.ItemType))
            {
                ExtractResolution(operand, baseItem, logger);
                ExtractFramerate(operand, baseItem, logger);
            }
            
            // Extract collections - only when needed for performance
            if (options.ExtractCollections)
            {
                operand.Collections = ExtractCollections(baseItem, user, libraryManager, cache, logger);
            }
            else
            {
                operand.Collections = [];
            }
            
            // Extract parent series tags for episodes - only when needed for performance
            // This is an expensive operation (database lookup), so we use caching
            if (extractParentSeriesTags)
            {
                ExtractParentSeriesTags(operand, baseItem, libraryManager, cache, logger);
            }
            else
            {
                operand.ParentSeriesTags = [];
            }
            
            // Extract artists and album artists only for music-related items (cheap operations when applicable)
            if (MediaTypes.MusicRelatedSet.Contains(operand.ItemType))
            {
                ExtractArtists(operand, baseItem, logger);
            }
            else
            {
                operand.Artists = [];
                operand.AlbumArtists = [];
            }
            
            // Extract NextUnwatched status for each user - only when needed for performance
            operand.NextUnwatchedByUser = [];
            if (extractNextUnwatched)
            {
                try
                {
                    // Only process episodes - other item types cannot be "next unwatched"
                    // Use proper type checking instead of string comparison
                    if (baseItem is Episode)
                    {
                        var episodeType = baseItem.GetType();
                        
                        // Use cached property lookups for better performance with thread-safe access
                        var parentIndexProperty = _parentIndexPropertyCache.GetOrAdd(episodeType, type => type.GetProperty("ParentIndexNumber"));
                        var indexProperty = _indexPropertyCache.GetOrAdd(episodeType, type => type.GetProperty("IndexNumber"));
                        
                        if (parentIndexProperty != null && indexProperty != null)
                        {
                            // Safe extraction of season and episode numbers - handle both nullable and non-nullable int properties
                            var seasonNumber = ExtractIntValue(parentIndexProperty.GetValue(baseItem));
                            var episodeNumber = ExtractIntValue(indexProperty.GetValue(baseItem));
                            
                            // Use helper to safely extract SeriesId and validate all required properties
                            if (TryGetEpisodeSeriesGuid(baseItem, out var seriesGuid) && seasonNumber.HasValue && episodeNumber.HasValue)
                            {
                                // Get all episodes in this series - use cache to avoid redundant database queries
                                var allEpisodes = GetCachedSeriesEpisodes(seriesGuid, user, libraryManager, cache, logger);
                                
                                // First, calculate NextUnwatched for the main user (playlist owner)
                                var mainUserNextUnwatched = IsNextUnwatchedEpisodeCached(allEpisodes, baseItem, user, seasonNumber.Value, episodeNumber.Value, includeUnwatchedSeries, seriesGuid, cache, userDataManager, logger);
                                operand.NextUnwatchedByUser[user.Id.ToString()] = mainUserNextUnwatched;
                                
                                // Then check for additional users
                                if (additionalUserIds != null)
                                {
                                    foreach (var userId in additionalUserIds)
                                    {
                                        if (Guid.TryParse(userId, out var userGuid))
                                        {
                                            var targetUser = GetUserById(userManager, userGuid);
                                            if (targetUser != null)
                                            {
                                                var episodesForUser = GetCachedSeriesEpisodes(seriesGuid, targetUser, libraryManager, cache, logger);
                                                var isNextUnwatched = IsNextUnwatchedEpisodeCached(episodesForUser, baseItem, targetUser, seasonNumber.Value, episodeNumber.Value, includeUnwatchedSeries, seriesGuid, cache, userDataManager, logger);
                                                operand.NextUnwatchedByUser[userId] = isNextUnwatched;
                                            }
                                        }
                                        else
                                        {
                                            logger?.LogWarning("Invalid user ID format: {UserId}", userId);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to extract NextUnwatched status for item {Name}", baseItem.Name);
                }
            }
            
            return operand;
        }



        /// <summary>
        /// Gets the item type name using efficient type checking instead of reflection
        /// </summary>
        /// <param name="item">The BaseItem to get the type name for</param>
        /// <returns>The item type name</returns>
        private static string GetItemTypeName(BaseItem item, ILogger logger = null)
        {
            // First try direct type matching for performance
            var directMatch = item switch
            {
                Episode => MediaTypes.Episode,
                Series => MediaTypes.Series,
                Movie => MediaTypes.Movie,
                Audio => MediaTypes.Audio,
                MusicVideo => MediaTypes.MusicVideo,
                Video => MediaTypes.Video,
                Photo => MediaTypes.Photo,
                Book => MediaTypes.Book,
                _ => null
            };
            
            if (directMatch != null)
            {
                return directMatch;
            }
            
            // Fallback to BaseItemKind mapping for types that don't have direct C# classes
            if (MediaTypes.BaseItemKindToMediaType.TryGetValue(item.GetBaseItemKind(), out var mappedType))
            {
                return mappedType;
            }
            
            // Log truly unknown types (not in our supported mapping)
            var typeName = item.GetType().Name;
            var baseItemKind = item.GetBaseItemKind().ToString();
            
            // Only log if it's not a known unsupported type to reduce noise
            if (!_knownUnsupportedTypes.Contains(typeName))
            {
                logger?.LogDebug("Unsupported item type encountered: {ItemType} (BaseItemKind: {BaseItemKind}) for item: {ItemName}", 
                    typeName, baseItemKind, item.Name);
            }
            
            return typeName;
        }

        /// <summary>
        /// Gets all episodes for a series, using cache to avoid redundant database queries.
        /// </summary>
        /// <param name="seriesId">The series ID to get episodes for</param>
        /// <param name="user">User for the query context</param>
        /// <param name="libraryManager">Library manager for database queries</param>
        /// <param name="cache">Per-refresh cache to store results</param>
        /// <param name="logger">Logger for debugging</param>
        /// <returns>Array of all episodes in the series</returns>
        private static BaseItem[] GetCachedSeriesEpisodes(Guid seriesId, User user, ILibraryManager libraryManager, RefreshCache cache, ILogger logger)
        {
            var key = (seriesId, user.Id);
            if (cache.SeriesEpisodes.TryGetValue(key, out var cachedEpisodes))
            {
                logger?.LogDebug("Using cached episodes for series {SeriesId} user {UserId} ({EpisodeCount} episodes)", seriesId, user.Id, cachedEpisodes.Length);
                return cachedEpisodes;
            }

            logger?.LogDebug("Fetching episodes for series {SeriesId} user {UserId} from database (cache miss)", seriesId, user.Id);
            
            // Note: Using SeriesId as ParentId - this works for standard episodes but may need
            // adjustment for special cases like virtual or merged series
            var episodeQuery = new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.Episode],
                ParentId = seriesId,
                Recursive = true
            };
            
            var episodes = libraryManager.GetItemsResult(episodeQuery).Items.ToArray();
            logger?.LogDebug("Cached {EpisodeCount} episodes for series {SeriesId} user {UserId}", episodes.Length, seriesId, user.Id);
            
            cache.SeriesEpisodes[key] = episodes;
            return episodes;
        }

        /// <summary>
        /// Determines if the current episode is the next unwatched episode for a user.
        /// Note: NextUnwatched is cached per refresh (per series/user/flag) to avoid recomputation,
        /// using live IsPlayed() data at calculation time.
        /// </summary>
        /// <param name="allEpisodes">All episodes in the series</param>
        /// <param name="currentEpisode">The episode to check</param>
        /// <param name="user">The user to check watch status for</param>
        /// <param name="currentSeason">Current episode's season number</param>
        /// <param name="currentEpisodeNumber">Current episode's episode number</param>
        /// <param name="includeUnwatchedSeries">Whether to include completely unwatched series</param>
        /// <param name="seriesId">The series ID for cache key generation</param>
        /// <param name="cache">Per-refresh cache to store calculation results</param>
        /// <param name="userDataManager">User data manager for retrieving user data</param>
        /// <param name="logger">Logger for debugging</param>
        /// <returns>True if this episode is the next unwatched episode for the user</returns>
        private static bool IsNextUnwatchedEpisodeCached(BaseItem[] allEpisodes, BaseItem currentEpisode, User user, 
            int currentSeason, int currentEpisodeNumber, bool includeUnwatchedSeries, Guid seriesId, RefreshCache cache, IUserDataManager userDataManager, ILogger logger)
        {
            // Use per-refresh cache to avoid O(E²) recomputation for large series
            // Cache is scoped to single refresh, so no staleness issues across refreshes
            var cacheKey = (seriesId, user.Id, includeUnwatchedSeries);
            if (!cache.NextUnwatched.TryGetValue(cacheKey, out var result))
            {
                logger?.LogDebug("Calculating next unwatched episode for series {SeriesId}, user {UserId}", seriesId, user.Id);
                result = CalculateNextUnwatchedEpisodeInfo(allEpisodes, user, includeUnwatchedSeries, userDataManager, logger);
                cache.NextUnwatched[cacheKey] = result;
            }
            
            // Check if the current episode matches the calculated next unwatched episode
            return result.NextEpisodeId.HasValue && 
                   result.NextEpisodeId.Value == currentEpisode.Id &&
                   result.Season == currentSeason && 
                   result.Episode == currentEpisodeNumber;
        }

        /// <summary>
        /// Calculates the next unwatched episode info for a series and user (returns episode details).
        /// </summary>
        private static (Guid? NextEpisodeId, int Season, int Episode) CalculateNextUnwatchedEpisodeInfo(BaseItem[] allEpisodes, User user, 
            bool includeUnwatchedSeries, IUserDataManager userDataManager, ILogger logger)
        {
            try
            {
                // Use the original logic to find the next unwatched episode
                var episodeList = allEpisodes.ToList();
                
                // Create a list of episode info with season/episode numbers (excluding season 0 specials)
                var episodeInfos = new List<(BaseItem Episode, int Season, int EpisodeNum, bool IsWatched)>();
                
                foreach (var episode in episodeList)
                {
                    var episodeType = episode.GetType();
                    
                    // Use cached property lookups for better performance with thread-safe access
                    var parentIndexProperty = _parentIndexPropertyCache.GetOrAdd(episodeType, type => type.GetProperty("ParentIndexNumber"));
                    var indexProperty = _indexPropertyCache.GetOrAdd(episodeType, type => type.GetProperty("IndexNumber"));
                    
                    if (parentIndexProperty != null && indexProperty != null)
                    {
                        // Safe extraction of season and episode numbers - handle both nullable and non-nullable int properties
                        var seasonNum = ExtractIntValue(parentIndexProperty.GetValue(episode));
                        var episodeNum = ExtractIntValue(indexProperty.GetValue(episode));
                        
                        // Skip season 0 (specials) and only include episodes with valid season/episode numbers
                        if (seasonNum.HasValue && episodeNum.HasValue && seasonNum.Value > 0)
                        {
                            // Call IsPlayed() fresh each time to ensure real-time accuracy
                            // Get user data for Jellyfin 10.11 compatibility
                            var episodeUserData = userDataManager?.GetUserData(user, episode);
                            var isWatched = episodeUserData != null ? episode.IsPlayed(user, episodeUserData) : false;
                            episodeInfos.Add((episode, seasonNum.Value, episodeNum.Value, isWatched));
                        }
                    }
                }
                
                // Sort episodes by season, then episode number
                var sortedEpisodes = episodeInfos.OrderBy(e => e.Season).ThenBy(e => e.EpisodeNum).ToList();
                
                // Find the first unwatched episode
                var (Episode, Season, EpisodeNum, IsWatched) = sortedEpisodes.FirstOrDefault(e => !e.IsWatched);
                
                if (Episode != null)
                {
                    // If includeUnwatchedSeries is false, check if this is a completely unwatched series
                    if (!includeUnwatchedSeries)
                    {
                        // If ALL episodes are unwatched, this is a completely unwatched series - exclude it
                        if (sortedEpisodes.All(e => !e.IsWatched))
                        {
                            return (null, 0, 0); // No next unwatched episode
                        }
                    }
                    
                    return (Episode.Id, Season, EpisodeNum);
                }
                
                // If all episodes are watched, no episode is "next unwatched"
                return (null, 0, 0);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to calculate next unwatched episode info");
                return (null, 0, 0);
            }
        }

        /// <summary>
        /// Extracts the collections that a media item belongs to, with caching for performance.
        /// </summary>
        /// <param name="baseItem">The media item to check</param>
        /// <param name="user">The user context for collection access</param>
        /// <param name="libraryManager">Library manager to query collections</param>
        /// <param name="cache">Per-refresh cache to avoid repeated queries</param>
        /// <param name="logger">Logger for debugging</param>
        /// <returns>List of collection names this item belongs to</returns>
        private static List<string> ExtractCollections(BaseItem baseItem, User user, ILibraryManager libraryManager, RefreshCache cache, ILogger logger)
        {
            // Check if we already have the result cached for this item
            if (cache.ItemCollections.TryGetValue(baseItem.Id, out var cachedCollections))
            {
                return cachedCollections;
            }
            
            var collections = new List<string>();
            
            try
            {
                // Load all collections once and cache them
                if (cache.AllCollections == null)
                {
                    logger?.LogDebug("Loading all collections for user {UserId} (cache miss)", user.Id);
                    var collectionQuery = new InternalItemsQuery(user)
                    {
                        IncludeItemTypes = [BaseItemKind.BoxSet],
                        Recursive = true
                    };
                    
                    cache.AllCollections = [.. libraryManager.GetItemsResult(collectionQuery).Items];
                    logger?.LogDebug("Cached {CollectionCount} collections for user {UserId}", cache.AllCollections.Length, user.Id);
                    
                    // Debug: Log collection names (only if debug level logging)
                    if (cache.AllCollections.Length <= 10) // Only log if reasonable number
                    {
                        foreach (var col in cache.AllCollections)
                        {
                            logger?.LogDebug("Found collection: '{CollectionName}' (ID: {CollectionId})", col.Name, col.Id);
                        }
                    }
                }
                
                // Build the reverse lookup cache if it's empty (one-time expensive operation per refresh)
                if (cache.CollectionMembershipCache.Count == 0 && cache.AllCollections.Length > 0)
                {
                    logger?.LogDebug("Building collection membership cache for {CollectionCount} collections", cache.AllCollections.Length);
                    
                    foreach (var collection in cache.AllCollections)
                    {
                        try
                        {
                            // Try multiple approaches to get collection items
                            BaseItem[] itemsInCollection = null;
                            
                            // Approach 1: Try GetChildren method using reflection
                            try
                            {
                                var getChildrenMethod = collection.GetType().GetMethod("GetChildren", [typeof(User), typeof(bool)]);
                                if (getChildrenMethod != null)
                                {
                                    var children = getChildrenMethod.Invoke(collection, [user, true]);
                                    if (children is IEnumerable<BaseItem> childrenEnumerable)
                                    {
                                        itemsInCollection = [.. childrenEnumerable];
                                        logger?.LogDebug("Collection '{CollectionName}' GetChildren() returned {ItemCount} items", collection.Name, itemsInCollection.Length);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                logger?.LogDebug(ex, "GetChildren method failed for collection '{CollectionName}'", collection.Name);
                            }
                            
                            // Approach 2: Try GetLinkedChildren method using reflection  
                            if (itemsInCollection == null || itemsInCollection.Length == 0)
                            {
                                try
                                {
                                    var getLinkedChildrenMethod = collection.GetType().GetMethod("GetLinkedChildren");
                                    if (getLinkedChildrenMethod != null)
                                    {
                                        var linkedChildren = getLinkedChildrenMethod.Invoke(collection, null);
                                        if (linkedChildren is IEnumerable<BaseItem> linkedEnumerable)
                                        {
                                            itemsInCollection = [.. linkedEnumerable];
                                            logger?.LogDebug("Collection '{CollectionName}' GetLinkedChildren() returned {ItemCount} items", collection.Name, itemsInCollection.Length);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger?.LogDebug(ex, "GetLinkedChildren method failed for collection '{CollectionName}'", collection.Name);
                                }
                            }
                            
                            // Approach 3: Fallback to ParentId query (original approach)
                            if (itemsInCollection == null || itemsInCollection.Length == 0)
                            {
                                var itemsInCollectionQuery = new InternalItemsQuery(user)
                                {
                                    ParentId = collection.Id,
                                    Recursive = true
                                };
                                
                                itemsInCollection = [.. libraryManager.GetItemsResult(itemsInCollectionQuery).Items];
                                logger?.LogDebug("Collection '{CollectionName}' ParentId query returned {ItemCount} items", collection.Name, itemsInCollection.Length);
                            }
                            
                            // Build the reverse lookup set for this collection (O(1) lookups)
                            var membershipSet = new HashSet<Guid>();
                            if (itemsInCollection != null)
                            {
                                foreach (var item in itemsInCollection)
                                {
                                    membershipSet.Add(item.Id);
                                }
                            }
                            
                            cache.CollectionMembershipCache[collection.Id] = membershipSet;
                            
                            // Debug: Log first few items in collection (only for small collections)
                            if (itemsInCollection != null && itemsInCollection.Length <= 5 && itemsInCollection.Length > 0)
                            {
                                foreach (var collectionItem in itemsInCollection.Take(3))
                                {
                                    logger?.LogDebug("  Collection item: '{ItemName}' (ID: {ItemId})", collectionItem.Name, collectionItem.Id);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger?.LogDebug(ex, "Error building membership cache for collection '{CollectionName}'", collection.Name);
                            // Create empty set for failed collections to avoid repeated attempts
                            cache.CollectionMembershipCache[collection.Id] = [];
                        }
                    }
                    
                    logger?.LogDebug("Collection membership cache built with {CacheCount} collections", cache.CollectionMembershipCache.Count);
                }
                
                // Use the reverse lookup cache for O(1) membership checks (fast!)
                foreach (var collection in cache.AllCollections)
                {
                    if (cache.CollectionMembershipCache.TryGetValue(collection.Id, out var membershipSet) && 
                        membershipSet.Contains(baseItem.Id))
                    {
                        collections.Add(collection.Name);

                    }
                }
                

            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to extract collections for item {Name}", baseItem.Name);
            }
            
            // Cache the result
            cache.ItemCollections[baseItem.Id] = collections;
            return collections;
        }

        /// <summary>
        /// Gets a user by ID using the user manager.
        /// </summary>
        /// <param name="userManager">The user manager instance.</param>
        /// <param name="userId">The user ID to look up.</param>
        /// <returns>The user object if found, null otherwise.</returns>
        public static User GetUserById(IUserManager userManager, Guid userId)
        {
            if (userManager == null)
            {
                throw new InvalidOperationException("UserManager is null - cannot retrieve user information.");
            }
            
            return userManager.GetUserById(userId);
        }

        /// <summary>
        /// Safely converts a DateTime to Unix timestamp, handling invalid dates.
        /// Treats the DateTime as UTC to ensure consistency with other date handling in the plugin.
        /// </summary>
        /// <param name="dateTime">The DateTime to convert.</param>
        /// <returns>Unix timestamp in seconds, or 0 if the date is invalid.</returns>
        private static double SafeToUnixTimeSeconds(DateTime dateTime)
        {
            try
            {
                // Check if the date is within valid range for DateTimeOffset
                if (dateTime < new DateTime(1, 1, 1) || dateTime > new DateTime(9999, 12, 31))
                {
                    return 0; // Return 0 for invalid dates
                }

                // Check for common invalid dates
                if (dateTime == DateTime.MinValue || dateTime == DateTime.MaxValue)
                {
                    return 0;
                }

                // Treat the DateTime as UTC to ensure consistency with other date handling in the plugin
                // This assumes Jellyfin stores dates in UTC, which is the typical behavior
                return new DateTimeOffset(dateTime, TimeSpan.Zero).ToUnixTimeSeconds();
            }
            catch (ArgumentOutOfRangeException)
            {
                // If DateTimeOffset creation fails, return 0
                return 0;
            }
            catch (Exception)
            {
                // For any other unexpected errors, return 0
                return 0;
            }
        }

        /// <summary>
        /// Reference metadata extracted from similar-to queries for comparison.
        /// </summary>
        public class ReferenceMetadata
        {
            public HashSet<string> Genres { get; set; } = [];
            public HashSet<string> Tags { get; set; } = [];
            public HashSet<string> People { get; set; } = [];
            public HashSet<string> Studios { get; set; } = [];
        }

        /// <summary>
        /// Builds reference metadata from SimilarTo expressions by finding and aggregating metadata from matching items.
        /// </summary>
        /// <param name="similarToExpressions">List of SimilarTo expressions to process</param>
        /// <param name="allItems">All items to search through for matches</param>
        /// <param name="libraryManager">Library manager for item queries</param>
        /// <param name="refreshCache">Per-refresh cache for performance</param>
        /// <param name="logger">Logger for debugging</param>
        /// <returns>Aggregated reference metadata</returns>
        public static ReferenceMetadata BuildReferenceMetadata(
            List<Expression> similarToExpressions,
            IEnumerable<BaseItem> allItems,
            ILibraryManager libraryManager,
            RefreshCache refreshCache,
            ILogger logger)
        {
            var referenceMetadata = new ReferenceMetadata();
            
            if (similarToExpressions == null || similarToExpressions.Count == 0)
            {
                logger?.LogDebug("No SimilarTo expressions to process");
                return referenceMetadata;
            }
            
            var referenceItems = new List<BaseItem>();
            
            // Find all items matching the SimilarTo expressions
            foreach (var expr in similarToExpressions)
            {
                if (string.IsNullOrWhiteSpace(expr?.TargetValue))
                {
                    logger?.LogWarning("SimilarTo expression has null or empty target value");
                    continue;
                }
                
                logger?.LogDebug("Processing SimilarTo expression: {Operator} '{Value}'", expr.Operator, expr.TargetValue);
                
                // Apply the operator to find matching items
                var matchingItems = allItems.Where(item =>
                {
                    if (item?.Name == null) return false;
                    
                    return expr.Operator switch
                    {
                        "Equal" => item.Name.Equals(expr.TargetValue, StringComparison.OrdinalIgnoreCase),
                        "Contains" => item.Name.Contains(expr.TargetValue, StringComparison.OrdinalIgnoreCase),
                        "NotContains" => !item.Name.Contains(expr.TargetValue, StringComparison.OrdinalIgnoreCase),
                        "IsIn" => IsNameInList(item.Name, expr.TargetValue),
                        "IsNotIn" => !IsNameInList(item.Name, expr.TargetValue),
                        "MatchRegex" => MatchesRegex(item.Name, expr.TargetValue, logger),
                        _ => false
                    };
                }).ToList();
                
                logger?.LogDebug("Found {Count} items matching SimilarTo query '{Value}'", matchingItems.Count, expr.TargetValue);
                
                referenceItems.AddRange(matchingItems);
            }
            
            // Deduplicate reference items by ID
            referenceItems = referenceItems.DistinctBy(item => item.Id).ToList();
            
            logger?.LogDebug("Total reference items after deduplication: {Count}", referenceItems.Count);
            
            if (referenceItems.Count == 0)
            {
                logger?.LogWarning("No reference items found for SimilarTo queries");
                return referenceMetadata;
            }
            
            // Log reference item names for debugging
            foreach (var item in referenceItems.Take(10))
            {
                logger?.LogDebug("Reference item: '{Name}'", item.Name);
            }
            
            // Extract and aggregate metadata from reference items (only Genres and Tags for accurate similarity)
            foreach (var item in referenceItems)
            {
                // Extract genres
                if (item.Genres != null)
                {
                    foreach (var genre in item.Genres)
                    {
                        if (!string.IsNullOrWhiteSpace(genre))
                        {
                            referenceMetadata.Genres.Add(genre);
                        }
                    }
                }
                
                // Extract tags
                if (item.Tags != null)
                {
                    foreach (var tag in item.Tags)
                    {
                        if (!string.IsNullOrWhiteSpace(tag))
                        {
                            referenceMetadata.Tags.Add(tag);
                        }
                    }
                }
            }
            
            logger?.LogDebug("Reference metadata - Genres: {GenreCount}, Tags: {TagCount}",
                referenceMetadata.Genres.Count, referenceMetadata.Tags.Count);
            
            return referenceMetadata;
        }

        /// <summary>
        /// Calculates similarity score for an operand against reference metadata.
        /// </summary>
        /// <param name="operand">The operand to calculate similarity for</param>
        /// <param name="referenceMetadata">Reference metadata to compare against</param>
        /// <param name="minSharedItems">Minimum number of shared metadata items required</param>
        /// <param name="logger">Logger for debugging</param>
        /// <returns>True if item passes similarity threshold, false otherwise</returns>
        public static bool CalculateSimilarityScore(
            Operand operand,
            ReferenceMetadata referenceMetadata,
            int minSharedItems,
            ILogger logger)
        {
            if (operand == null || referenceMetadata == null)
            {
                return false;
            }
            
            int sharedCount = 0;
            float score = 0;
            
            // Count shared genres (1 point each)
            if (operand.Genres != null && referenceMetadata.Genres.Count > 0)
            {
                var sharedGenres = operand.Genres.Intersect(referenceMetadata.Genres, StringComparer.OrdinalIgnoreCase).ToList();
                sharedCount += sharedGenres.Count;
                score += sharedGenres.Count;
            }
            
            // Count shared tags (1 point each)
            if (operand.Tags != null && referenceMetadata.Tags.Count > 0)
            {
                var sharedTags = operand.Tags.Intersect(referenceMetadata.Tags, StringComparer.OrdinalIgnoreCase).ToList();
                sharedCount += sharedTags.Count;
                score += sharedTags.Count;
            }
            
            // Store score in operand for potential sorting
            operand.SimilarityScore = score;
            
            // Check if meets minimum threshold
            bool passes = sharedCount >= minSharedItems;
            
            if (passes)
            {
                logger?.LogDebug("Item '{Name}' passes similarity threshold: {SharedCount} shared genres/tags, score: {Score}",
                    operand.Name, sharedCount, score);
            }
            
            return passes;
        }

        /// <summary>
        /// Helper method to check if a name is in a semicolon-separated list (partial matching).
        /// </summary>
        private static bool IsNameInList(string name, string targetList)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(targetList))
                return false;
            
            var listItems = targetList.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item));
            
            return listItems.Any(item => name.Contains(item, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Helper method to check if a name matches a regex pattern.
        /// </summary>
        private static bool MatchesRegex(string name, string pattern, ILogger logger)
        {
            try
            {
                var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.Compiled);
                return regex.IsMatch(name);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Invalid regex pattern '{Pattern}' in SimilarTo expression", pattern);
                return false;
            }
        }
    }
}