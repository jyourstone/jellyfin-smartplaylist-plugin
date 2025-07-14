using System;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Jellyfin.Data.Entities;

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
            IUserDataManager userDataManager = null, ILogger logger = null, bool extractAudioLanguages = false, bool extractPeople = false)
        {
            return GetMediaType(libraryManager, baseItem, user, userDataManager, logger, extractAudioLanguages, extractPeople, []);
        }
        
        // Overload that supports extracting user data for multiple users
        public static Operand GetMediaType(ILibraryManager libraryManager, BaseItem baseItem, User user, 
            IUserDataManager userDataManager = null, ILogger logger = null, bool extractAudioLanguages = false, bool extractPeople = false, 
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
                RuntimeMinutes = baseItem.RunTimeTicks.HasValue ?
                    (int)TimeSpan.FromTicks(baseItem.RunTimeTicks.Value).TotalMinutes : 0,
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
                                logger?.LogWarning("User with ID {UserId} not found for user-specific data extraction. This playlist rule references a user that no longer exists.", userId);
                                throw new InvalidOperationException($"User with ID {userId} not found. This playlist rule references a user that no longer exists.");
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
                        // This stops playlist processing when a referenced user no longer exists
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Error extracting user data for user {UserId} on item {Name}", userId, baseItem.Name);
                    }
                }
            }
            
            operand.OfficialRating = baseItem.OfficialRating ?? "";
            operand.DateCreated = SafeToUnixTimeSeconds(baseItem.DateCreated);
            operand.DateLastRefreshed = SafeToUnixTimeSeconds(baseItem.DateLastRefreshed);
            operand.DateLastSaved = SafeToUnixTimeSeconds(baseItem.DateLastSaved);
            operand.DateModified = SafeToUnixTimeSeconds(baseItem.DateModified);
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
            
            return operand;
        }

        /// <summary>
        /// Gets a user by ID using reflection to access the user manager from the user data manager.
        /// This is a workaround since IUserDataManager doesn't directly expose user lookup.
        /// </summary>
        /// <param name="userDataManager">The user data manager instance.</param>
        /// <param name="userId">The user ID to look up.</param>
        /// <returns>The user if found, otherwise null.</returns>
        public static User GetUserById(IUserDataManager userDataManager, Guid userId)
        {
            if (userDataManager == null)
            {
                return null;
            }
            
            // We need to use reflection to access the user manager from the user data manager
            // This is a workaround since IUserDataManager doesn't directly expose user lookup
            var userManagerField = userDataManager.GetType().GetField("_userManager", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (userManagerField != null)
            {
                var userManager = userManagerField.GetValue(userDataManager) as IUserManager;
                return userManager?.GetUserById(userId);
            }
            
            return null;
        }
        
        /// <summary>
        /// Safely converts a DateTime to Unix timestamp, handling invalid dates.
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

                return new DateTimeOffset(dateTime).ToUnixTimeSeconds();
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