using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Entities;
using Jellyfin.Plugin.SmartPlaylist.QueryEngine;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartPlaylist
{
    public class SmartPlaylist
    {
        public SmartPlaylist(SmartPlaylistDto dto)
        {
            Id = dto.Id;
            Name = dto.Name;
            FileName = dto.FileName;
            User = dto.User;
            ExpressionSets = Engine.FixRuleSets(dto.ExpressionSets);
            Order = OrderFactory.CreateOrder(dto.Order.Name);
        }

        public string Id { get; set; }
        public string Name { get; set; }
        public string FileName { get; set; }
        public string User { get; set; }
        public List<ExpressionSet> ExpressionSets { get; set; }
        public Order Order { get; set; }

        private List<List<Func<Operand, bool>>> CompileRuleSets(ILogger logger = null)
        {
            var compiledRuleSets = new List<List<Func<Operand, bool>>>();
            foreach (var set in ExpressionSets)
                compiledRuleSets.Add(set.Expressions.Select(r => Engine.CompileRule<Operand>(r, logger)).ToList());
            return compiledRuleSets;
        }

        // Returns the ID's of the items, if order is provided the IDs are sorted.
        public IEnumerable<Guid> FilterPlaylistItems(IEnumerable<BaseItem> items, ILibraryManager libraryManager,
            User user, IUserDataManager userDataManager = null, ILogger logger = null)
        {
            var results = new List<BaseItem>();

            var compiledRules = CompileRuleSets(logger);
            foreach (var i in items)
            {
                var operand = OperandFactory.GetMediaType(libraryManager, i, user, userDataManager, logger);

                if (compiledRules.Any(set => set.All(rule => rule(operand)))) results.Add(i);
            }

            return Order.OrderBy(results).Select(x => x.Id);
        }

        private static void Validate()
        {
            //Todo create validation for constructor
        }
    }

    public static class OrderFactory
    {
        private static readonly Dictionary<string, Func<Order>> OrderMap = new()
        {
            { "Name Ascending", () => new NameOrder() },
            { "Name Descending", () => new NameOrderDesc() },
            { "ProductionYear Ascending", () => new ProductionYearOrder() },
            { "ProductionYear Descending", () => new ProductionYearOrderDesc() },
            { "DateCreated Ascending", () => new DateCreatedOrder() },
            { "DateCreated Descending", () => new DateCreatedOrderDesc() },
            { "CommunityRating Ascending", () => new CommunityRatingOrder() },
            { "CommunityRating Descending", () => new CommunityRatingOrderDesc() },
            { "NoOrder", () => new NoOrder() }
        };

        public static Order CreateOrder(string orderName)
        {
            return OrderMap.TryGetValue(orderName ?? "", out var factory) 
                ? factory() 
                : new NoOrder();
        }
    }

    public abstract class Order
    {
        public abstract string Name { get; }

        public virtual IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items;
        }
    }

    public class NoOrder : Order
    {
        public override string Name => "NoOrder";
    }

    public class ProductionYearOrder : Order
    {
        public override string Name => "ProductionYear Ascending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items.OrderBy(x => x.ProductionYear ?? 0);
        }
    }

    public class ProductionYearOrderDesc : Order
    {
        public override string Name => "ProductionYear Descending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items.OrderByDescending(x => x.ProductionYear ?? 0);
        }
    }

    public class NameOrder : Order
    {
        public override string Name => "Name Ascending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items.OrderBy(x => x.Name);
        }
    }

    public class NameOrderDesc : Order
    {
        public override string Name => "Name Descending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items.OrderByDescending(x => x.Name);
        }
    }

    public class DateCreatedOrder : Order
    {
        public override string Name => "DateCreated Ascending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items.OrderBy(x => x.DateCreated);
        }
    }

    public class DateCreatedOrderDesc : Order
    {
        public override string Name => "DateCreated Descending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items.OrderByDescending(x => x.DateCreated);
        }
    }

    public class CommunityRatingOrder : Order
    {
        public override string Name => "CommunityRating Ascending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items.OrderBy(x => x.CommunityRating ?? 0);
        }
    }

    public class CommunityRatingOrderDesc : Order
    {
        public override string Name => "CommunityRating Descending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items.OrderByDescending(x => x.CommunityRating ?? 0);
        }
    }
}