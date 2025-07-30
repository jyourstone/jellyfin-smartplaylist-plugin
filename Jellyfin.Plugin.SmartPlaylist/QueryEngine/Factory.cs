using System;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Jellyfin.Data.Entities;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.SmartPlaylist.QueryEngine
{
    internal class OperandFactory
    {
        // Cache reflection method lookups for better performance
        private static readonly Dictionary<Type, System.Reflection.MethodInfo> _getMediaStreamsMethodCache = [];
        private static readonly Dictionary<Type, System.Reflection.PropertyInfo> _mediaSourcesPropertyCache = [];
        private static System.Reflection.MethodInfo _getPeopleMethodCache = null;
        private static readonly object _getPeopleMethodLock = new();

        // Returns a specific operand povided a baseitem, user, and library manager object.
        public static Operand GetMediaType(ILibraryManager libraryManager, BaseItem baseItem, User user, 
            IUserDataManager userDataManager = null, ILogger logger = null, bool extractAudioLanguages = false, bool extractPeople = false, bool extractNextUnwatched = false, bool includeUnwatchedSeries = true)
        {
            return GetMediaType(libraryManager, baseItem, user, userDataManager, logger, extractAudioLanguages, extractPeople, extractNextUnwatched, includeUnwatchedSeries, []);
        }
        
        // Overload that supports extracting user data for multiple users
        public static Operand GetMediaType(ILibraryManager libraryManager, BaseItem baseItem, User user, 
            IUserDataManager userDataManager = null, ILogger logger = null, bool extractAudioLanguages = false, bool extractPeople = false, bool extractNextUnwatched = false, bool includeUnwatchedSeries = true,
            List<string> additionalUserIds = null)
        {
            // Cache the IsPlayed result to avoid multiple expensive calls
            var isPlayed = baseItem.IsPlayed(user);

            var operand = new Operand(baseItem.Name)
            {
                Genres = [.. baseItem.Genres],
                IsPlayed = isPlayed,
                Studios = [.. baseItem.Studios],
                CommunityRating = baseItem.CommunityRating.GetValueOrDefault(),
                CriticRating = baseItem.CriticRating.GetValueOrDefault(),
                MediaType = baseItem.MediaType.ToString(),
                ItemType = baseItem.GetType().Name,
                Album = baseItem.Album,
                ProductionYear = baseItem.ProductionYear.GetValueOrDefault(),
                Tags = baseItem.Tags is not null ? [.. baseItem.Tags] : [],
                RuntimeMinutes = baseItem.RunTimeTicks.HasValue ? TimeSpan.FromTicks(baseItem.RunTimeTicks.Value).TotalMinutes : 0.0,
                // Initialize user data properties with fallback values
                PlayCount = isPlayed ? 1 : 0,
                IsFavorite = false
            };

            // Try to access user data properly
            try
            {
                if (userDataManager != null)
                {
                    var userData = userDataManager.GetUserData(user, baseItem);
                    if (userData != null)
                    {
                        operand.PlayCount = userData.PlayCount;
                        operand.IsFavorite = userData.IsFavorite;
                    }
                    // If userData is null, keep the fallback values we set above
                }
                else
                {
                    // Fallback approach - try reflection
                    var userDataProperty = baseItem.GetType().GetProperty("UserData");
                    if (userDataProperty != null)
                    {
                        var userData = userDataProperty.GetValue(baseItem);
                        if (userData != null)
                        {
                            var playCountProp = userData.GetType().GetProperty("PlayCount");
                            var isFavoriteProp = userData.GetType().GetProperty("IsFavorite");
                            
                            if (playCountProp != null)
                            {
                                var playCountValue = playCountProp.GetValue(userData);
                                if (playCountValue != null)
                                {
                                    operand.PlayCount = (int)playCountValue;
                                }
                            }
                            
                            if (isFavoriteProp != null)
                            {
                                var isFavoriteValue = isFavoriteProp.GetValue(userData);
                                if (isFavoriteValue != null)
                                {
                                    operand.IsFavorite = (bool)isFavoriteValue;
                                }
                            }
                        }
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
                                    }
                                    else
                                    {
                                        // Fallback values
                                        operand.PlayCountByUser[userId] = userIsPlayed ? 1 : 0;
                                        operand.IsFavoriteByUser[userId] = false;
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
                    if (!_getMediaStreamsMethodCache.TryGetValue(baseItemType, out var getMediaStreamsMethod))
                    {
                        getMediaStreamsMethod = baseItemType.GetMethod("GetMediaStreams");
                        _getMediaStreamsMethodCache[baseItemType] = getMediaStreamsMethod;
                    }
                    
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
                    if (!_mediaSourcesPropertyCache.TryGetValue(baseItemType, out var mediaSourcesProperty))
                    {
                        mediaSourcesProperty = baseItemType.GetProperty("MediaSources");
                        _mediaSourcesPropertyCache[baseItemType] = mediaSourcesProperty;
                    }
                    
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
                    if (baseItem.GetType().Name == "Episode")
                    {
                        // Get series (parent) of this episode
                        var seriesIdProperty = baseItem.GetType().GetProperty("SeriesId");
                        var parentIndexProperty = baseItem.GetType().GetProperty("ParentIndexNumber");
                        var indexProperty = baseItem.GetType().GetProperty("IndexNumber");
                        
                        if (seriesIdProperty != null && parentIndexProperty != null && indexProperty != null)
                        {
                            var seriesId = seriesIdProperty.GetValue(baseItem);
                            var seasonNumber = parentIndexProperty.GetValue(baseItem) as int?;
                            var episodeNumber = indexProperty.GetValue(baseItem) as int?;
                            
                            if (seriesId != null && seasonNumber.HasValue && episodeNumber.HasValue)
                            {
                                // Get all episodes in this series
                                var query = new InternalItemsQuery(user)
                                {
                                    IncludeItemTypes = [BaseItemKind.Episode],
                                    ParentId = (Guid)seriesId,
                                    Recursive = true
                                };
                                
                                var allEpisodes = libraryManager.GetItemsResult(query).Items;
                                
                                // First, calculate NextUnwatched for the main user (playlist owner)
                                var mainUserNextUnwatched = IsNextUnwatchedEpisode(allEpisodes, baseItem, user, seasonNumber.Value, episodeNumber.Value, includeUnwatchedSeries, logger);
                                operand.NextUnwatched = mainUserNextUnwatched;
                                operand.NextUnwatchedByUser[user.Id.ToString()] = mainUserNextUnwatched;
                                
                                // Then check for additional users
                                if (additionalUserIds != null)
                                {
                                    foreach (var userId in additionalUserIds)
                                    {
                                        var targetUser = GetUserById(userDataManager, Guid.Parse(userId));
                                        if (targetUser != null)
                                        {
                                            var isNextUnwatched = IsNextUnwatchedEpisode(allEpisodes, baseItem, targetUser, seasonNumber.Value, episodeNumber.Value, includeUnwatchedSeries, logger);
                                            operand.NextUnwatchedByUser[userId] = isNextUnwatched;
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
        /// Determines if the given episode is the "next unwatched" episode for a user.
        /// This means it's the first unwatched episode in the series when episodes are sorted by season/episode number.
        /// </summary>
        /// <param name="allEpisodes">All episodes in the series</param>
        /// <param name="currentEpisode">The episode to check</param>
        /// <param name="user">The user to check watch status for</param>
        /// <param name="currentSeason">Season number of the current episode</param>
        /// <param name="currentEpisodeNumber">Episode number of the current episode</param>
        /// <param name="includeUnwatchedSeries">If false, excludes episodes from completely unwatched series</param>
        /// <param name="logger">Logger instance</param>
        /// <returns>True if this episode is the next unwatched episode for the user</returns>
        private static bool IsNextUnwatchedEpisode(IEnumerable<BaseItem> allEpisodes, BaseItem currentEpisode, User user, int currentSeason, int currentEpisodeNumber, bool includeUnwatchedSeries, ILogger logger)
        {
            try
            {
                // Create a list of episode info with season/episode numbers
                var episodeInfos = new List<(BaseItem Episode, int Season, int EpisodeNum, bool IsWatched)>();
                
                foreach (var episode in allEpisodes)
                {
                    var parentIndexProperty = episode.GetType().GetProperty("ParentIndexNumber");
                    var indexProperty = episode.GetType().GetProperty("IndexNumber");
                    
                    if (parentIndexProperty != null && indexProperty != null)
                    {
                        var seasonNum = parentIndexProperty.GetValue(episode) as int?;
                        var episodeNum = indexProperty.GetValue(episode) as int?;
                        
                        if (seasonNum.HasValue && episodeNum.HasValue)
                        {
                            var isWatched = episode.IsPlayed(user);
                            episodeInfos.Add((episode, seasonNum.Value, episodeNum.Value, isWatched));
                        }
                    }
                }
                
                // Sort episodes by season then episode number
                var sortedEpisodes = episodeInfos.OrderBy(e => e.Season).ThenBy(e => e.EpisodeNum).ToList();
                
                // Find the first unwatched episode
                var firstUnwatched = sortedEpisodes.FirstOrDefault(e => !e.IsWatched);
                
                if (firstUnwatched.Episode != null)
                {
                    // If includeUnwatchedSeries is false, check if this is a completely unwatched series
                    if (!includeUnwatchedSeries)
                    {
                        // If ALL episodes are unwatched, this is a completely unwatched series - exclude it
                        if (sortedEpisodes.All(e => !e.IsWatched))
                        {
                            return false;
                        }
                    }
                    
                    // Check if the current episode is the first unwatched episode
                    return firstUnwatched.Season == currentSeason && 
                           firstUnwatched.EpisodeNum == currentEpisodeNumber &&
                           firstUnwatched.Episode.Id == currentEpisode.Id;
                }
                
                // If all episodes are watched, no episode is "next unwatched"
                return false;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to determine next unwatched episode status");
                return false;
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