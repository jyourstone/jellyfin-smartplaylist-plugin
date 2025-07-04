using System;
using Jellyfin.Data.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartPlaylist.QueryEngine
{
    internal class OperandFactory
    {
        // Returns a specific operand povided a baseitem, user, and library manager object.
        public static Operand GetMediaType(ILibraryManager libraryManager, BaseItem baseItem, User user, 
            IUserDataManager userDataManager = null, ILogger logger = null, bool extractAudioLanguages = false, bool extractPeople = false)
        {
            var operand = new Operand(baseItem.Name)
            {
                Genres = [.. baseItem.Genres],
                IsPlayed = baseItem.IsPlayed(user),
                Studios = [.. baseItem.Studios],
                CommunityRating = baseItem.CommunityRating.GetValueOrDefault(),
                CriticRating = baseItem.CriticRating.GetValueOrDefault(),
                MediaType = baseItem.MediaType.ToString(),
                ItemType = baseItem.GetType().Name,
                Album = baseItem.Album,
                ProductionYear = baseItem.ProductionYear.GetValueOrDefault(),
                Tags = baseItem.Tags is not null ? [.. baseItem.Tags] : [],
                RuntimeMinutes = baseItem.RunTimeTicks.HasValue ? 
                    (int)TimeSpan.FromTicks(baseItem.RunTimeTicks.Value).TotalMinutes : 0
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
                    else
                    {
                        operand.PlayCount = baseItem.IsPlayed(user) ? 1 : 0;
                        operand.IsFavorite = false;
                    }
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
                                operand.PlayCount = (int)(playCountProp.GetValue(userData) ?? 0);
                            }
                            
                            if (isFavoriteProp != null)
                            {
                                operand.IsFavorite = (bool)(isFavoriteProp.GetValue(userData) ?? false);
                            }
                        }
                    }
                    
                    if (operand.PlayCount == 0 && operand.IsFavorite == false)
                    {
                        // Simplified fallback
                        operand.PlayCount = baseItem.IsPlayed(user) ? 1 : 0;
                        operand.IsFavorite = false;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error accessing user data for item {Name}", baseItem.Name);
                // Fallback to simplified values
                operand.PlayCount = baseItem.IsPlayed(user) ? 1 : 0;
                operand.IsFavorite = false;
            }
            
            operand.OfficialRating = baseItem.OfficialRating ?? "";
            operand.DateCreated = SafeToUnixTimeSeconds(baseItem.DateCreated);
            operand.DateLastRefreshed = SafeToUnixTimeSeconds(baseItem.DateLastRefreshed);
            operand.DateLastSaved = SafeToUnixTimeSeconds(baseItem.DateLastSaved);
            operand.DateModified = SafeToUnixTimeSeconds(baseItem.DateModified);
            operand.FolderPath = baseItem.ContainingFolderPath;
            operand.FileName = System.IO.Path.GetFileName(baseItem.Path) ?? "";
            
            // Extract audio languages from media streams - only when needed for performance
            operand.AudioLanguages = [];
            if (extractAudioLanguages)
            {
                try
                {
                    // Try multiple approaches to access media stream information
                    var mediaStreams = new List<object>();
                    
                    // Approach 1: Try GetMediaStreams method if it exists
                    var getMediaStreamsMethod = baseItem.GetType().GetMethod("GetMediaStreams");
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
                        catch (Exception)
                        {
                            // Silently ignore errors in GetMediaStreams method
                        }
                    }
                    
                    // Approach 2: Look for MediaSources property (similar to how RunTimeTicks works)
                    var mediaSourcesProperty = baseItem.GetType().GetProperty("MediaSources");
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
                                catch (Exception)
                                {
                                    // Silently ignore errors processing individual MediaSources
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
                        catch (Exception)
                        {
                            // Silently ignore errors processing individual streams
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Error extracting audio languages for item {Name}", baseItem.Name);
                }
            }
            
            // Extract all people (actors, directors, producers, etc.) - only when needed for performance
            operand.People = [];
            if (extractPeople)
            {
                try
                {
                    // Cache the GetPeople method lookup for better performance
                    var getPeopleMethod = libraryManager.GetType().GetMethod("GetPeople", [typeof(InternalPeopleQuery)]);
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
                    logger?.LogWarning(ex, "Error extracting people for item {Name}", baseItem.Name);
                }
            }
            
            return operand;
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