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

            // Check if any rules use AudioLanguages field to avoid expensive extraction when not needed
            var needsAudioLanguages = ExpressionSets
                .SelectMany(set => set.Expressions)
                .Any(expr => expr.MemberName == "AudioLanguages");

            var compiledRules = CompileRuleSets(logger);
            
            // OPTIMIZATION: Check for ItemType rules - apply them first for massive dataset reduction
            var itemTypeRules = new List<(int setIndex, int exprIndex, Func<Operand, bool> rule)>();
            
            for (int setIndex = 0; setIndex < ExpressionSets.Count; setIndex++)
            {
                var set = ExpressionSets[setIndex];
                for (int exprIndex = 0; exprIndex < set.Expressions.Count; exprIndex++)
                {
                    var expr = set.Expressions[exprIndex];
                    if (expr.MemberName == "ItemType")
                    {
                        itemTypeRules.Add((setIndex, exprIndex, compiledRules[setIndex][exprIndex]));
                    }
                }
            }
            
            // Apply ItemType filtering first if any ItemType rules exist
            if (itemTypeRules.Count > 0)
            {
                logger?.LogDebug("Applying ItemType pre-filtering to reduce dataset");
                var preFilteredItems = new List<BaseItem>();
                
                foreach (var item in items)
                {
                    // Create a lightweight operand for ItemType checking
                    var operand = OperandFactory.GetMediaType(libraryManager, item, user, userDataManager, logger, false);
                    
                    // Check if this item matches any ItemType rule using the compiled rule
                    bool matchesItemType = false;
                    foreach (var (setIndex, exprIndex, rule) in itemTypeRules)
                    {
                        if (rule(operand))
                        {
                            matchesItemType = true;
                            break;
                        }
                    }
                    
                    if (matchesItemType)
                    {
                        preFilteredItems.Add(item);
                    }
                }
                
                logger?.LogDebug("ItemType pre-filtering reduced dataset from {OriginalCount} to {FilteredCount} items", 
                    items.Count(), preFilteredItems.Count);
                
                // Continue with the pre-filtered items
                items = preFilteredItems;
            }
            
            if (needsAudioLanguages)
            {
                // Optimization: Separate rules into cheap and expensive categories
                var cheapCompiledRules = new List<List<Func<Operand, bool>>>();
                var audioLanguagesCompiledRules = new List<List<Func<Operand, bool>>>();
                
                logger?.LogDebug("Separating rules into cheap and expensive categories");
                
                for (int setIndex = 0; setIndex < ExpressionSets.Count; setIndex++)
                {
                    var set = ExpressionSets[setIndex];
                    var cheapRules = new List<Func<Operand, bool>>();
                    var audioRules = new List<Func<Operand, bool>>();
                    
                    for (int exprIndex = 0; exprIndex < set.Expressions.Count; exprIndex++)
                    {
                        var expr = set.Expressions[exprIndex];
                        var compiledRule = compiledRules[setIndex][exprIndex];
                        
                        if (expr.MemberName == "AudioLanguages")
                        {
                            audioRules.Add(compiledRule);
                            logger?.LogDebug("Rule set {SetIndex}: Added AudioLanguages rule: {Field} {Operator} {Value}", 
                                setIndex, expr.MemberName, expr.Operator, expr.TargetValue);
                        }
                        else
                        {
                            cheapRules.Add(compiledRule);
                            logger?.LogDebug("Rule set {SetIndex}: Added cheap rule: {Field} {Operator} {Value}", 
                                setIndex, expr.MemberName, expr.Operator, expr.TargetValue);
                        }
                    }
                    
                    cheapCompiledRules.Add(cheapRules);
                    audioLanguagesCompiledRules.Add(audioRules);
                    
                    logger?.LogDebug("Rule set {SetIndex}: {CheapCount} cheap rules, {AudioCount} audio rules", 
                        setIndex, cheapRules.Count, audioRules.Count);
                }
                
                // Check if there are ANY non-audio rules across all rule sets
                bool hasNonAudioRules = cheapCompiledRules.Any(rules => rules.Count > 0);
                
                if (!hasNonAudioRules)
                {
                    // No cheap rules to filter with - just extract audio languages for all items
                    logger?.LogDebug("No non-audio rules found, extracting audio languages for all items");
                    foreach (var i in items)
                    {
                        var operand = OperandFactory.GetMediaType(libraryManager, i, user, userDataManager, logger, true);
                        
                        // Debug: Log audio languages found for first few items
                        if (results.Count < 5)
                        {
                            logger?.LogInformation("Item '{Name}': Found {Count} audio languages: [{Languages}]", 
                                i.Name, operand.AudioLanguages.Count, string.Join(", ", operand.AudioLanguages));
                        }
                        
                        if (compiledRules.Any(set => set.All(rule => rule(operand))))
                        {
                            results.Add(i);
                        }
                    }
                }
                else
                {
                    // Two-phase filtering: cheap rules first, then expensive audio extraction
                    logger?.LogDebug("Using two-phase filtering for AudioLanguages optimization");
                
                foreach (var i in items)
                {
                    // Phase 1: Extract cheap properties and check non-audio rules
                    var cheapOperand = OperandFactory.GetMediaType(libraryManager, i, user, userDataManager, logger, false);
                    
                    // Check if item passes all non-audio rules for any rule set
                    bool passesNonAudioRules = false;
                    for (int setIndex = 0; setIndex < cheapCompiledRules.Count; setIndex++)
                    {
                        if (cheapCompiledRules[setIndex].All(rule => rule(cheapOperand)))
                        {
                            passesNonAudioRules = true;
                            break;
                        }
                    }
                    
                    if (!passesNonAudioRules) continue; // Skip expensive audio extraction
                    
                    // Phase 2: Extract audio languages and check complete rules
                    var fullOperand = OperandFactory.GetMediaType(libraryManager, i, user, userDataManager, logger, true);
                    
                    // Debug: Log audio languages found for first few items
                    if (results.Count < 5)
                    {
                        logger?.LogInformation("Item '{Name}': Found {Count} audio languages: [{Languages}]", 
                            i.Name, fullOperand.AudioLanguages.Count, string.Join(", ", fullOperand.AudioLanguages));
                    }
                    
                    if (compiledRules.Any(set => set.All(rule => rule(fullOperand))))
                    {
                        results.Add(i);
                    }
                }
                }
            }
            else
            {
                // No audio language rules - use simple single-pass filtering
                foreach (var i in items)
                {
                    var operand = OperandFactory.GetMediaType(libraryManager, i, user, userDataManager, logger, false);

                    if (compiledRules.Any(set => set.All(rule => rule(operand)))) results.Add(i);
                }
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