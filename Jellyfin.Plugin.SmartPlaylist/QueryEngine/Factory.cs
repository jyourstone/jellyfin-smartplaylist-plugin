using System;
using System.Linq;
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
            IUserDataManager userDataManager = null, ILogger logger = null)
        {
            var operand = new Operand(baseItem.Name);

            operand.Genres = baseItem.Genres.ToList();
            operand.IsPlayed = baseItem.IsPlayed(user);
            operand.Studios = baseItem.Studios.ToList();
            operand.CommunityRating = baseItem.CommunityRating.GetValueOrDefault();
            operand.CriticRating = baseItem.CriticRating.GetValueOrDefault();
            operand.MediaType = baseItem.MediaType.ToString();
            operand.ItemType = baseItem.GetType().Name;
            operand.Album = baseItem.Album;
            operand.ProductionYear = baseItem.ProductionYear.GetValueOrDefault();
            operand.Tags = baseItem.Tags?.ToList() ?? new List<string>();

            // New fields
            operand.RuntimeMinutes = baseItem.RunTimeTicks.HasValue ? 
                (int)TimeSpan.FromTicks(baseItem.RunTimeTicks.Value).TotalMinutes : 0;
            
            // Debug runtime calculation
            logger?.LogDebug("Item {Name}: RunTimeTicks={RunTimeTicks}, RuntimeMinutes={RuntimeMinutes}", 
                baseItem.Name, baseItem.RunTimeTicks, operand.RuntimeMinutes);
            
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
                        
                        logger?.LogDebug("Item {Name}: PlayCount={PlayCount}, IsFavorite={IsFavorite} (from UserDataManager)", 
                            baseItem.Name, operand.PlayCount, operand.IsFavorite);
                    }
                    else
                    {
                        logger?.LogWarning("Item {Name}: UserData is null", baseItem.Name);
                        operand.PlayCount = baseItem.IsPlayed(user) ? 1 : 0;
                        operand.IsFavorite = false;
                    }
                }
                else
                {
                    logger?.LogWarning("UserDataManager is null, using fallback approach");
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
                            
                            logger?.LogDebug("Item {Name}: PlayCount={PlayCount}, IsFavorite={IsFavorite} (from reflection)", 
                                baseItem.Name, operand.PlayCount, operand.IsFavorite);
                        }
                    }
                    
                    if (operand.PlayCount == 0 && operand.IsFavorite == false)
                    {
                        // Simplified fallback
                        operand.PlayCount = baseItem.IsPlayed(user) ? 1 : 0;
                        operand.IsFavorite = false;
                        
                        logger?.LogDebug("Item {Name}: PlayCount={PlayCount}, IsFavorite={IsFavorite} (fallback)", 
                            baseItem.Name, operand.PlayCount, operand.IsFavorite);
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
            
            // Extract audio languages from media streams 
            operand.AudioLanguages = new List<string>();
            try
            {
                logger?.LogDebug("Extracting audio languages for item {Name} (Type: {ItemType})", baseItem.Name, baseItem.GetType().Name);
                
                // List all available properties to see what we can access
                var allProperties = baseItem.GetType().GetProperties();
                var propertyNames = string.Join(", ", allProperties.Select(p => p.Name).OrderBy(n => n).Take(20));
                logger?.LogDebug("Available properties (first 20): {Properties}", propertyNames);
                
                // Try multiple approaches to access media stream information
                var mediaStreams = new List<object>();
                
                // Approach 1: Look for MediaStreams property (known to fail, but let's confirm)
                var mediaStreamsProperty = baseItem.GetType().GetProperty("MediaStreams");
                logger?.LogDebug("MediaStreams property found: {Found}", mediaStreamsProperty != null);
                
                // Approach 2: Try GetMediaStreams method if it exists
                var getMediaStreamsMethod = baseItem.GetType().GetMethod("GetMediaStreams");
                logger?.LogDebug("GetMediaStreams method found: {Found}", getMediaStreamsMethod != null);
                if (getMediaStreamsMethod != null)
                {
                    try
                    {
                        var result = getMediaStreamsMethod.Invoke(baseItem, null);
                        if (result is IEnumerable<object> streamEnum)
                        {
                            mediaStreams.AddRange(streamEnum);
                            logger?.LogDebug("Found {Count} streams via GetMediaStreams method", mediaStreams.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogDebug(ex, "Error calling GetMediaStreams method");
                    }
                }
                
                // Approach 3: Check if it implements IHasMediaSources interface
                var hasMediaSourcesInterface = baseItem.GetType().GetInterface("IHasMediaSources");
                logger?.LogDebug("IHasMediaSources interface found: {Found}", hasMediaSourcesInterface != null);
                
                // Approach 4: Look for MediaSources property (similar to how RunTimeTicks works)
                var mediaSourcesProperty = baseItem.GetType().GetProperty("MediaSources");
                logger?.LogDebug("MediaSources property found: {Found}", mediaSourcesProperty != null);
                
                if (mediaSourcesProperty != null)
                {
                    var mediaSources = mediaSourcesProperty.GetValue(baseItem);
                    logger?.LogDebug("MediaSources value: {IsNull}, Type: {Type}", 
                        mediaSources == null, mediaSources?.GetType().Name ?? "null");
                    
                    if (mediaSources != null)
                    {
                        // Try to access MediaSources as enumerable
                        if (mediaSources is IEnumerable<object> sourceEnum)
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
                                            logger?.LogDebug("Found {Count} streams via MediaSources property", streamList.Count());
                                        }
                                    }
                                }
                                catch (Exception sourceEx)
                                {
                                    logger?.LogWarning(sourceEx, "Error processing MediaSource for item {Name}", baseItem.Name);
                                }
                            }
                        }
                    }
                }
                
                // Process found streams
                logger?.LogDebug("Total media streams found: {Count}", mediaStreams.Count);
                foreach (var stream in mediaStreams)
                {
                    try
                    {
                        var streamProperties = stream.GetType().GetProperties();
                        var streamPropNames = string.Join(", ", streamProperties.Select(p => p.Name).OrderBy(n => n));
                        logger?.LogDebug("Stream properties: {Properties}", streamPropNames);
                        
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
                            
                            logger?.LogDebug("Stream - Type: {Type}, Language: {Language}, Title: {Title}, DisplayTitle: {DisplayTitle}", 
                                streamType, language ?? "null", title ?? "null", displayTitle ?? "null");
                            
                            // Check if it's an audio stream
                            if (streamType != null && streamType.ToString() == "Audio")
                            {
                                // Try multiple sources for language info
                                var languageToAdd = language;
                                
                                // If no language code, try to extract from title or displayTitle
                                if (string.IsNullOrEmpty(languageToAdd))
                                {
                                    if (!string.IsNullOrEmpty(title) && title.ToLowerInvariant().Contains("swedish"))
                                    {
                                        languageToAdd = "swe";
                                    }
                                    else if (!string.IsNullOrEmpty(displayTitle) && displayTitle.ToLowerInvariant().Contains("swedish"))
                                    {
                                        languageToAdd = "swe";
                                    }
                                }
                                
                                if (!string.IsNullOrEmpty(languageToAdd))
                                {
                                    // Normalize language codes
                                    var normalizedLang = languageToAdd.ToLowerInvariant();
                                    if (normalizedLang == "sv") normalizedLang = "swe";
                                    if (normalizedLang == "swedish") normalizedLang = "swe";
                                    if (normalizedLang == "en") normalizedLang = "eng";
                                    if (normalizedLang == "english") normalizedLang = "eng";
                                    
                                    if (!operand.AudioLanguages.Contains(normalizedLang))
                                    {
                                        operand.AudioLanguages.Add(normalizedLang);
                                        logger?.LogDebug("Added audio language: {Language} (original: {Original})", normalizedLang, languageToAdd);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception streamEx)
                    {
                        logger?.LogWarning(streamEx, "Error processing individual stream for item {Name}", baseItem.Name);
                    }
                }
                
                logger?.LogInformation("Item {Name}: Found {AudioLanguageCount} audio languages: {AudioLanguages}", 
                    baseItem.Name, operand.AudioLanguages.Count, string.Join(", ", operand.AudioLanguages));
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error extracting audio languages for item {Name}", baseItem.Name);
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