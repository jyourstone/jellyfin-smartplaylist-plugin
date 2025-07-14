namespace Jellyfin.Plugin.SmartPlaylist.QueryEngine
{
    public class Expression(string memberName, string @operator, string targetValue)
    {
        public string MemberName { get; set; } = memberName;
        public string Operator { get; set; } = @operator;
        public string TargetValue { get; set; } = targetValue;
        
        // User-specific query support
        public string UserId { get; set; } = null;
        
        // Helper property to check if this is a user-specific expression
        public bool IsUserSpecific => !string.IsNullOrEmpty(UserId);
        
        // Helper property to get the user-specific field name for reflection
        public string UserSpecificField => IsUserSpecific ? GetUserSpecificFieldName() : MemberName;
        
        private string GetUserSpecificFieldName()
        {
            return MemberName switch
            {
                "IsPlayed" => "GetIsPlayedByUser",
                "PlayCount" => "GetPlayCountByUser", 
                "IsFavorite" => "GetIsFavoriteByUser",
                _ => MemberName
            };
        }
    }
}