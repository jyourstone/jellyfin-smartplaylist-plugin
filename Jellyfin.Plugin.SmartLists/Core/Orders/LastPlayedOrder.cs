namespace Jellyfin.Plugin.SmartLists.Core.Orders
{
    public class LastPlayedOrder : LastPlayedOrderBase
    {
        public override string Name => "LastPlayed (owner) Ascending";
        protected override bool IsDescending => false;
    }

    public class LastPlayedOrderDesc : LastPlayedOrderBase
    {
        public override string Name => "LastPlayed (owner) Descending";
        protected override bool IsDescending => true;
    }
}

