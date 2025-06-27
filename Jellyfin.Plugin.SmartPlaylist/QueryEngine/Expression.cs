namespace Jellyfin.Plugin.SmartPlaylist.QueryEngine
{
    public class Expression
    {
        public Expression(string memberName, string @operator, string targetValue)
        {
            MemberName = memberName;
            Operator = @operator;
            TargetValue = targetValue;
        }

        public string MemberName { get; set; }
        public string Operator { get; set; }
        public string TargetValue { get; set; }
    }
}