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