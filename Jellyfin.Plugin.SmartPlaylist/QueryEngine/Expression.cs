namespace Jellyfin.Plugin.SmartPlaylist.QueryEngine
{
    public class Expression(string memberName, string @operator, string targetValue)
    {
        public string MemberName { get; set; } = memberName;
        public string Operator { get; set; } = @operator;
        public string TargetValue { get; set; } = targetValue;
    }
}