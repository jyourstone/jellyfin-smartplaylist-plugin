﻿using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SmartPlaylist.QueryEngine
{
    public class Expression(string memberName, string @operator, string targetValue)
    {
        public string MemberName { get; set; } = memberName;
        public string Operator { get; set; } = @operator;
        public string TargetValue { get; set; } = targetValue;
        
        // User-specific query support - only serialize when meaningful
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string UserId { get; set; } = null;
        
        // NextUnwatched-specific option - only serialize when meaningful
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? IncludeUnwatchedSeries { get; set; } = null;
        
        // Helper property to check if this is a user-specific expression
        // Only serialize when UserId is not null
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsUserSpecific => !string.IsNullOrEmpty(UserId);
        
        // Helper property to get the user-specific field name for reflection
        // Only serialize when it's actually a user-specific field (different from MemberName)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string UserSpecificField => IsUserSpecific && IsUserSpecificField(MemberName) ? GetUserSpecificFieldName() : null;
        
        private string GetUserSpecificFieldName()
        {
            return MemberName switch
            {
                "IsPlayed" => "GetIsPlayedByUser",
                "PlayCount" => "GetPlayCountByUser", 
                "IsFavorite" => "GetIsFavoriteByUser",
                "NextUnwatched" => "GetNextUnwatchedByUser",
                "LastPlayedDate" => "GetLastPlayedDateByUser",
                _ => MemberName
            };
        }
        
        private static bool IsUserSpecificField(string memberName)
        {
            return memberName switch
            {
                "IsPlayed" => true,
                "PlayCount" => true,
                "IsFavorite" => true,
                "NextUnwatched" => true,
                "LastPlayedDate" => true,
                _ => false
            };
        }
    }
}