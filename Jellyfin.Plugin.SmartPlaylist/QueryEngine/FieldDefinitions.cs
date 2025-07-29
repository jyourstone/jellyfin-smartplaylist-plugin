using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartPlaylist.QueryEngine
{
    /// <summary>
    /// Centralized field definitions to avoid duplication across the codebase.
    /// </summary>
    public static class FieldDefinitions
    {
        /// <summary>
        /// Date fields that require special handling for date operations.
        /// </summary>
        public static readonly HashSet<string> DateFields =
        [
            "DateCreated",
            "DateLastRefreshed", 
            "DateLastSaved",
            "DateModified",
            "ReleaseDate"
        ];

        /// <summary>
        /// List fields that contain collections of strings.
        /// </summary>
        public static readonly HashSet<string> ListFields =
        [
            "People",
            "Genres", 
            "Studios",
            "Tags",
            "Artists",
            "AlbumArtists"
        ];

        /// <summary>
        /// Numeric fields that support numeric comparisons.
        /// </summary>
        public static readonly HashSet<string> NumericFields =
        [
            "ProductionYear",
            "CommunityRating",
            "CriticRating", 
            "RuntimeMinutes",
            "PlayCount"
        ];

        /// <summary>
        /// Boolean fields that only support equality comparisons.
        /// </summary>
        public static readonly HashSet<string> BooleanFields =
        [
            "IsPlayed",
            "IsFavorite"
        ];

        /// <summary>
        /// Simple fields that have predefined values.
        /// </summary>
        public static readonly HashSet<string> SimpleFields =
        [
            "ItemType"
        ];

        /// <summary>
        /// User-specific fields that can be filtered by user.
        /// </summary>
        public static readonly HashSet<string> UserDataFields =
        [
            "IsPlayed",
            "IsFavorite", 
            "PlayCount"
        ];

        /// <summary>
        /// Checks if a field is a date field that requires special date handling.
        /// </summary>
        /// <param name="fieldName">The field name to check</param>
        /// <returns>True if it's a date field, false otherwise</returns>
        public static bool IsDateField(string fieldName)
        {
            return DateFields.Contains(fieldName);
        }

        /// <summary>
        /// Checks if a field is a list field that contains collections.
        /// </summary>
        /// <param name="fieldName">The field name to check</param>
        /// <returns>True if it's a list field, false otherwise</returns>
        public static bool IsListField(string fieldName)
        {
            return ListFields.Contains(fieldName);
        }

        /// <summary>
        /// Checks if a field is a numeric field that supports numeric operations.
        /// </summary>
        /// <param name="fieldName">The field name to check</param>
        /// <returns>True if it's a numeric field, false otherwise</returns>
        public static bool IsNumericField(string fieldName)
        {
            return NumericFields.Contains(fieldName);
        }

        /// <summary>
        /// Checks if a field is a boolean field that only supports equality.
        /// </summary>
        /// <param name="fieldName">The field name to check</param>
        /// <returns>True if it's a boolean field, false otherwise</returns>
        public static bool IsBooleanField(string fieldName)
        {
            return BooleanFields.Contains(fieldName);
        }

        /// <summary>
        /// Checks if a field is a simple field with predefined values.
        /// </summary>
        /// <param name="fieldName">The field name to check</param>
        /// <returns>True if it's a simple field, false otherwise</returns>
        public static bool IsSimpleField(string fieldName)
        {
            return SimpleFields.Contains(fieldName);
        }

        /// <summary>
        /// Checks if a field supports user-specific filtering.
        /// </summary>
        /// <param name="fieldName">The field name to check</param>
        /// <returns>True if it's a user data field, false otherwise</returns>
        public static bool IsUserDataField(string fieldName)
        {
            return UserDataFields.Contains(fieldName);
        }

        /// <summary>
        /// Gets all available field names for API responses.
        /// </summary>
        /// <returns>Array of all field names</returns>
        public static string[] GetAllFieldNames()
        {
            var allFields = new HashSet<string>();
            allFields.UnionWith(DateFields);
            allFields.UnionWith(ListFields);
            allFields.UnionWith(NumericFields);
            allFields.UnionWith(BooleanFields);
            allFields.UnionWith(SimpleFields);
            
            // Add other fields that aren't in the main categories
            allFields.Add("Name");
            allFields.Add("Album");
            allFields.Add("AudioLanguages");
            allFields.Add("OfficialRating");
            allFields.Add("Overview");
            allFields.Add("FileName");
            allFields.Add("FolderPath");
            allFields.Add("MediaType");
            
            return [.. allFields];
        }
    }
} 