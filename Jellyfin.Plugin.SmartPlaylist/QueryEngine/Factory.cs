using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Jellyfin.Data.Entities;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.SmartPlaylist.QueryEngine
{
    /// <summary>
    /// Parameters object for GetMediaType operations to improve readability and maintainability.
    /// </summary>
    public class MediaTypeExtractionOptions
    {
        public bool ExtractAudioLanguages { get; set; } = false;
        public bool ExtractPeople { get; set; } = false;
        public bool ExtractNextUnwatched { get; set; } = false;
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
        
        // Cache episode property lookups for better performance - using ConcurrentDictionary for thread safety
        private static readonly ConcurrentDictionary<Type, System.Reflection.PropertyInfo> _parentIndexPropertyCache = new();
        private static readonly ConcurrentDictionary<Type, System.Reflection.PropertyInfo> _indexPropertyCache = new();
        private static readonly ConcurrentDictionary<Type, System.Reflection.PropertyInfo> _seriesIdPropertyCache = new();
        
        // Per-refresh cache classes for better performance within single playlist processing
        public class RefreshCache
        {
            public Dictionary<Guid, BaseItem[]> SeriesEpisodes { get; } = [];
            public Dictionary<string, (Guid? NextEpisodeId, int Season, int Episode)> NextUnwatched { get; } = [];
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

        // Clean API using options object - no more boolean flag proliferation!
        public static Operand GetMediaType(ILibraryManager libraryManager, BaseItem baseItem, User user, 
            IUserDataManager userDataManager, ILogger logger, MediaTypeExtractionOptions options,
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
            var includeUnwatchedSeries = options.IncludeUnwatchedSeries;
            var additionalUserIds = options.AdditionalUserIds;
            
            // Cache the IsPlayed result to avoid multiple expensive calls
            var isPlayed = baseItem.IsPlayed(user);

            var operand = new Operand(baseItem.Name)
            {
                Genres = [.. baseItem.Genres],
                Studios = [.. baseItem.Studios],
                CommunityRating = baseItem.CommunityRating.GetValueOrDefault(),
                CriticRating = baseItem.CriticRating.GetValueOrDefault(),
                MediaType = baseItem.MediaType.ToString(),
                ItemType = baseItem.GetType().Name,
                Album = baseItem.Album,
                ProductionYear = baseItem.ProductionYear.GetValueOrDefault(),
                Tags = baseItem.Tags is not null ? [.. baseItem.Tags] : [],
                RuntimeMinutes = baseItem.RunTimeTicks.HasValue ? TimeSpan.FromTicks(baseItem.RunTimeTicks.Value).TotalMinutes : 0.0
            };

            // Try to access user data properly
            try
            {
                if (userDataManager != null)
                {
                    var userData = userDataManager.GetUserData(user, baseItem);
                    if (userData != null)
                    {
                        // Populate user-specific data for playlist owner
                        operand.IsPlayedByUser[user.Id.ToString()] = isPlayed;
                        operand.PlayCountByUser[user.Id.ToString()] = userData.PlayCount;
                        operand.IsFavoriteByUser[user.Id.ToString()] = userData.IsFavorite;
                        
                        // Extract LastPlayedDate if available, otherwise use -1 (represents "never played")
                        if (userData.LastPlayedDate.HasValue)
                        {
                            operand.LastPlayedDateByUser[user.Id.ToString()] = SafeToUnixTimeSeconds(userData.LastPlayedDate.Value);
                        }
                        else
                        {
                            operand.LastPlayedDateByUser[user.Id.ToString()] = -1; // Never played - use -1 as sentinel value
                        }
                    }
                    else
                    {
                        // Fallback when userData is null - treat as never played for playlist owner
                        operand.IsPlayedByUser[user.Id.ToString()] = isPlayed;
                        operand.PlayCountByUser[user.Id.ToString()] = 0;
                        operand.IsFavoriteByUser[user.Id.ToString()] = false;
                        operand.LastPlayedDateByUser[user.Id.ToString()] = -1;
                    }
                }
                else
                {
                    // Fallback approach - try reflection and populate dictionaries for playlist owner
                    var userDataProperty = baseItem.GetType().GetProperty("UserData");
                    if (userDataProperty != null)
                    {
                        var userData = userDataProperty.GetValue(baseItem);
                        if (userData != null)
                        {
                            // Use the pre-calculated IsPlayed value for playlist owner
                            operand.IsPlayedByUser[user.Id.ToString()] = isPlayed;
                            
                            var playCountProp = userData.GetType().GetProperty("PlayCount");
                            var isFavoriteProp = userData.GetType().GetProperty("IsFavorite");
                            var lastPlayedDateProp = userData.GetType().GetProperty("LastPlayedDate");
                            
                            if (playCountProp != null)
                            {
                                var playCountValue = playCountProp.GetValue(userData);
                                if (playCountValue != null)
                                {
                                    operand.PlayCountByUser[user.Id.ToString()] = (int)playCountValue;
                                }
                            }
                            
                            if (isFavoriteProp != null)
                            {
                                var isFavoriteValue = isFavoriteProp.GetValue(userData);
                                if (isFavoriteValue != null)
                                {
                                    operand.IsFavoriteByUser[user.Id.ToString()] = (bool)isFavoriteValue;
                                }
                            }
                            
                            if (lastPlayedDateProp != null)
                            {
                                var lastPlayedDateValue = lastPlayedDateProp.GetValue(userData);
                                if (lastPlayedDateValue is DateTime lastPlayedDate && lastPlayedDate != DateTime.MinValue)
                                {
                                    operand.LastPlayedDateByUser[user.Id.ToString()] = SafeToUnixTimeSeconds(lastPlayedDate);
                                }
                                else
                                {
                                    operand.LastPlayedDateByUser[user.Id.ToString()] = -1; // Never played
                                }
                            }
                            else
                            {
                                operand.LastPlayedDateByUser[user.Id.ToString()] = -1; // Never played - property not found
                            }
                        }
                        else
                        {
                            // UserData is null - set fallback values for playlist owner
                            operand.IsPlayedByUser[user.Id.ToString()] = isPlayed;
                            operand.PlayCountByUser[user.Id.ToString()] = 0;
                            operand.IsFavoriteByUser[user.Id.ToString()] = false;
                            operand.LastPlayedDateByUser[user.Id.ToString()] = -1;
                        }
                    }
                    else
                    {
                        // UserData property not found - set fallback values for playlist owner
                        operand.IsPlayedByUser[user.Id.ToString()] = isPlayed;
                        operand.PlayCountByUser[user.Id.ToString()] = 0;
                        operand.IsFavoriteByUser[user.Id.ToString()] = false;
                        operand.LastPlayedDateByUser[user.Id.ToString()] = -1;
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
                                var targetUser = GetUserById(userDataManager, userGuid);
                                if (targetUser != null)
                                {
                                    var userIsPlayed = baseItem.IsPlayed(targetUser);
                                    operand.IsPlayedByUser[userId] = userIsPlayed;
                                    
                                    var targetUserData = userDataManager.GetUserData(targetUser, baseItem);
                                    if (targetUserData != null)
                                    {
                                        operand.PlayCountByUser[userId] = targetUserData.PlayCount;
                                        operand.IsFavoriteByUser[userId] = targetUserData.IsFavorite;
                                        
                                        // Extract LastPlayedDate if available, otherwise use -1 (represents "never played")
                                        if (targetUserData.LastPlayedDate.HasValue)
                                        {
                                            operand.LastPlayedDateByUser[userId] = SafeToUnixTimeSeconds(targetUserData.LastPlayedDate.Value);
                                        }
                                                                else
                        {
                            operand.LastPlayedDateByUser[userId] = -1; // Never played - use -1 as sentinel value
                        }
                    }
                    else
                    {
                        // Fallback values when targetUserData is null
                        operand.PlayCountByUser[userId] = userIsPlayed ? 1 : 0;
                        operand.IsFavoriteByUser[userId] = false;
                        operand.LastPlayedDateByUser[userId] = -1; // Never played - use -1 as sentinel value
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
                            operand.AudioLanguages = [];
            if (extractAudioLanguages)
            {
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
                            var titleProperty = stream.GetType().GetProperty("Title");
                            var displayTitleProperty = stream.GetType().GetProperty("DisplayTitle");
                            
                            if (typeProperty != null)
                            {
                                var streamType = typeProperty.GetValue(stream);
                                var language = languageProperty?.GetValue(stream) as string;
                                var title = titleProperty?.GetValue(stream) as string;
                                var displayTitle = displayTitleProperty?.GetValue(stream) as string;
                                
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
            
            // Extract all people (actors, directors, producers, etc.) - only when needed for performance
            operand.People = [];
            if (extractPeople)
            {
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
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to extract people for item {Name}", baseItem.Name);
                }
            }
            
            // Extract artists and album artists for music items (cheap operations, always extract)
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
            
            // Extract NextUnwatched status for each user - only when needed for performance
            operand.NextUnwatchedByUser = [];
            if (extractNextUnwatched)
            {
                try
                {
                    // Only process episodes - other item types cannot be "next unwatched"
                    // Check ItemType which is already populated above, avoiding repeated reflection calls
                    if (operand.ItemType == "Episode")
                    {
                        var episodeType = baseItem.GetType();
                        
                        // Use cached property lookups for better performance with thread-safe access
                        var seriesIdProperty = _seriesIdPropertyCache.GetOrAdd(episodeType, type => type.GetProperty("SeriesId"));
                        var parentIndexProperty = _parentIndexPropertyCache.GetOrAdd(episodeType, type => type.GetProperty("ParentIndexNumber"));
                        var indexProperty = _indexPropertyCache.GetOrAdd(episodeType, type => type.GetProperty("IndexNumber"));
                        
                        if (seriesIdProperty != null && parentIndexProperty != null && indexProperty != null)
                        {
                            var seriesId = seriesIdProperty.GetValue(baseItem);
                            // Safe extraction of season and episode numbers - handle both nullable and non-nullable int properties
                            var seasonNumber = ExtractIntValue(parentIndexProperty.GetValue(baseItem));
                            var episodeNumber = ExtractIntValue(indexProperty.GetValue(baseItem));
                            
                            // Safely convert SeriesId to Guid and validate all required properties
                            if (seriesId is Guid seriesGuid && seasonNumber.HasValue && episodeNumber.HasValue)
                            {
                                // Get all episodes in this series - use cache to avoid redundant database queries
                                var allEpisodes = GetCachedSeriesEpisodes(seriesGuid, user, libraryManager, cache, logger);
                                
                                // First, calculate NextUnwatched for the main user (playlist owner)
                                var mainUserNextUnwatched = IsNextUnwatchedEpisodeCached(allEpisodes, baseItem, user, seasonNumber.Value, episodeNumber.Value, includeUnwatchedSeries, seriesGuid, cache, logger);
                                operand.NextUnwatchedByUser[user.Id.ToString()] = mainUserNextUnwatched;
                                
                                // Then check for additional users
                                if (additionalUserIds != null)
                                {
                                    foreach (var userId in additionalUserIds)
                                    {
                                        if (Guid.TryParse(userId, out var userGuid))
                                        {
                                            var targetUser = GetUserById(userDataManager, userGuid);
                                            if (targetUser != null)
                                            {
                                                var isNextUnwatched = IsNextUnwatchedEpisodeCached(allEpisodes, baseItem, targetUser, seasonNumber.Value, episodeNumber.Value, includeUnwatchedSeries, seriesGuid, cache, logger);
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
            if (cache.SeriesEpisodes.TryGetValue(seriesId, out var cachedEpisodes))
            {
                logger?.LogDebug("Using cached episodes for series {SeriesId} ({EpisodeCount} episodes)", seriesId, cachedEpisodes.Length);
                return cachedEpisodes;
            }

            logger?.LogDebug("Fetching episodes for series {SeriesId} from database (cache miss)", seriesId);
            
            // Note: Using SeriesId as ParentId - this works for standard episodes but may need
            // adjustment for special cases like virtual or merged series
            var episodeQuery = new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.Episode],
                ParentId = seriesId,
                Recursive = true
            };
            
            var episodes = libraryManager.GetItemsResult(episodeQuery).Items.ToArray();
            logger?.LogDebug("Cached {EpisodeCount} episodes for series {SeriesId}", episodes.Length, seriesId);
            
            cache.SeriesEpisodes[seriesId] = episodes;
            return episodes;
        }

        /// <summary>
        /// Determines if the current episode is the next unwatched episode for a user, with caching.
        /// </summary>
        /// <param name="allEpisodes">All episodes in the series</param>
        /// <param name="currentEpisode">The episode to check</param>
        /// <param name="user">The user to check watch status for</param>
        /// <param name="currentSeason">Current episode's season number</param>
        /// <param name="currentEpisodeNumber">Current episode's episode number</param>
        /// <param name="includeUnwatchedSeries">Whether to include completely unwatched series</param>
        /// <param name="seriesId">The series ID for cache key generation</param>
        /// <param name="cache">Per-refresh cache to store calculation results</param>
        /// <param name="logger">Logger for debugging</param>
        /// <returns>True if this episode is the next unwatched episode for the user</returns>
        private static bool IsNextUnwatchedEpisodeCached(BaseItem[] allEpisodes, BaseItem currentEpisode, User user, 
            int currentSeason, int currentEpisodeNumber, bool includeUnwatchedSeries, Guid seriesId, RefreshCache cache, ILogger logger)
        {
            // Create cache key combining series, user, and settings
            var cacheKey = $"{seriesId}|{user.Id}|{includeUnwatchedSeries}";
            
            if (cache.NextUnwatched.TryGetValue(cacheKey, out var cachedResult))
            {
                logger?.LogDebug("Using cached next unwatched calculation for series {SeriesId}, user {UserId}", seriesId, user.Id);
                
                // Check if the current episode matches the cached next unwatched episode
                return cachedResult.NextEpisodeId.HasValue && 
                       cachedResult.NextEpisodeId.Value == currentEpisode.Id &&
                       cachedResult.Season == currentSeason && 
                       cachedResult.Episode == currentEpisodeNumber;
            }
            
            logger?.LogDebug("Calculating next unwatched episode for series {SeriesId}, user {UserId} (cache miss)", seriesId, user.Id);
            
            // Calculate the next unwatched episode
            var result = CalculateNextUnwatchedEpisodeInfo(allEpisodes, user, includeUnwatchedSeries, logger);
            
            // Cache the result for future use within this refresh
            cache.NextUnwatched[cacheKey] = result;
            
            // Check if the current episode matches the calculated next unwatched episode
            return result.NextEpisodeId.HasValue && 
                   result.NextEpisodeId.Value == currentEpisode.Id &&
                   result.Season == currentSeason && 
                   result.Episode == currentEpisodeNumber;
        }

        /// <summary>
        /// Calculates the next unwatched episode for a series and user (simple boolean check).
        /// </summary>
        private static bool CalculateNextUnwatchedEpisode(BaseItem[] allEpisodes, BaseItem currentEpisode, User user, 
            int currentSeason, int currentEpisodeNumber, bool includeUnwatchedSeries, ILogger logger)
        {
            var (NextEpisodeId, Season, Episode) = CalculateNextUnwatchedEpisodeInfo(allEpisodes, user, includeUnwatchedSeries, logger);
            
            return NextEpisodeId.HasValue && 
                   NextEpisodeId.Value == currentEpisode.Id &&
                   Season == currentSeason && 
                   Episode == currentEpisodeNumber;
        }

        /// <summary>
        /// Calculates the next unwatched episode info for a series and user (returns episode details).
        /// </summary>
        private static (Guid? NextEpisodeId, int Season, int Episode) CalculateNextUnwatchedEpisodeInfo(BaseItem[] allEpisodes, User user, 
            bool includeUnwatchedSeries, ILogger logger)
        {
            try
            {
                // Use the original logic to find the next unwatched episode
                var episodeList = allEpisodes.ToList();
                
                // Cache IsPlayed results for all episodes to avoid repeated expensive calls
                var isPlayedCache = new Dictionary<BaseItem, bool>();
                foreach (var episode in episodeList)
                {
                    isPlayedCache[episode] = episode.IsPlayed(user);
                }
                
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
                            var isWatched = isPlayedCache[episode];
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
        /// Gets a user by ID using reflection to access the user manager from the user data manager.
        /// This is a workaround since IUserDataManager doesn't directly expose user lookup.
        /// </summary>
        /// <param name="userDataManager">The user data manager instance.</param>
        /// <param name="userId">The user ID to look up.</param>
        /// <returns>The user if found, otherwise null.</returns>
        /// <exception cref="InvalidOperationException">Thrown when reflection fails to access the user manager.</exception>
        public static User GetUserById(IUserDataManager userDataManager, Guid userId)
        {
            if (userDataManager == null)
            {
                throw new InvalidOperationException("UserDataManager is null - cannot retrieve user information.");
            }
            
            try
            {
                // We need to use reflection to access the user manager from the user data manager
                // This is a workaround since IUserDataManager doesn't directly expose user lookup
                var userManagerField = userDataManager.GetType().GetField("_userManager", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (userManagerField == null)
                {
                    // Try alternative field names in case the internal implementation changed
                    var alternativeFields = new[] { "_userManager", "userManager", "_users", "users" };
                    foreach (var fieldName in alternativeFields)
                    {
                        userManagerField = userDataManager.GetType().GetField(fieldName, 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (userManagerField != null)
                        {
                            break;
                        }
                    }
                }
                
                if (userManagerField != null)
                {
                    if (userManagerField.GetValue(userDataManager) is IUserManager userManager)
                    {
                        return userManager.GetUserById(userId);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Failed to cast user manager field '{userManagerField.Name}' to IUserManager. The internal structure may have changed.");
                    }
                }
                else
                {
                    throw new InvalidOperationException("Failed to find user manager field in UserDataManager via reflection. The internal structure may have changed - this plugin may need to be updated for this version of Jellyfin.");
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException($"Reflection failed while trying to access user manager: {ex.Message}", ex);
            }
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
    }
}