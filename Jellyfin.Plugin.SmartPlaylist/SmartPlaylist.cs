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
    public class SmartPlaylist(SmartPlaylistDto dto)
    {
        public string Id { get; set; } = dto.Id;
        public string Name { get; set; } = dto.Name;
        public string FileName { get; set; } = dto.FileName;
        public Guid UserId { get; set; } = dto.UserId;
        public List<ExpressionSet> ExpressionSets { get; set; } = Engine.FixRuleSets(dto.ExpressionSets);
        public Order Order { get; set; } = OrderFactory.CreateOrder(dto.Order.Name);

        private List<List<Func<Operand, bool>>> CompileRuleSets(ILogger logger = null)
        {
            return [.. ExpressionSets.Select(set => 
                set.Expressions.Select(r => Engine.CompileRule<Operand>(r, logger)).ToList())];
        }

        // Returns the ID's of the items, if order is provided the IDs are sorted.
        public IEnumerable<Guid> FilterPlaylistItems(IEnumerable<BaseItem> items, ILibraryManager libraryManager,
            User user, IUserDataManager userDataManager = null, ILogger logger = null)
        {
            var results = new List<BaseItem>();

            // Check if any rules use expensive fields to avoid unnecessary extraction
            var needsAudioLanguages = ExpressionSets
                .SelectMany(set => set.Expressions)
                .Any(expr => expr.MemberName == "AudioLanguages");
            
            var needsPeople = ExpressionSets
                .SelectMany(set => set.Expressions)
                .Any(expr => expr.MemberName == "People");

            var compiledRules = CompileRuleSets(logger);
            
            // OPTIMIZATION: Check for ItemType rules - apply them first for massive dataset reduction
            // Only apply pre-filtering if ALL rule sets have ItemType constraints to avoid excluding
            // items that could match rule sets without ItemType constraints
            var itemTypeRules = new List<(int setIndex, int exprIndex, Func<Operand, bool> rule)>();
            var ruleSetsWithItemType = new HashSet<int>();
            
            for (int setIndex = 0; setIndex < ExpressionSets.Count; setIndex++)
            {
                var set = ExpressionSets[setIndex];
                for (int exprIndex = 0; exprIndex < set.Expressions.Count; exprIndex++)
                {
                    var expr = set.Expressions[exprIndex];
                    if (expr.MemberName == "ItemType")
                    {
                        itemTypeRules.Add((setIndex, exprIndex, compiledRules[setIndex][exprIndex]));
                        ruleSetsWithItemType.Add(setIndex);
                    }
                }
            }
            
            // Only apply ItemType filtering if ALL rule sets have ItemType constraints
            bool canApplyItemTypeOptimization = ruleSetsWithItemType.Count == ExpressionSets.Count;
            
            if (canApplyItemTypeOptimization && itemTypeRules.Count > 0)
            {
                logger?.LogDebug("Applying ItemType pre-filtering to reduce dataset (all rule sets have ItemType constraints)");
                var preFilteredItems = new List<BaseItem>();
                
                foreach (var item in items)
                {
                    // Create a lightweight operand for ItemType checking
                    var operand = OperandFactory.GetMediaType(libraryManager, item, user, userDataManager, logger, false, false);
                    
                    // Check if this item matches ALL ItemType rules within AT LEAST ONE rule set
                    // Group ItemType rules by rule set and check each rule set independently
                    bool matchesAnyRuleSet = false;
                    
                    foreach (var ruleSetIndex in ruleSetsWithItemType)
                    {
                        // Get all ItemType rules for this specific rule set
                        var ruleSetItemTypeRules = itemTypeRules.Where(r => r.setIndex == ruleSetIndex);
                        
                        // Check if item matches ALL ItemType rules in this rule set
                        if (ruleSetItemTypeRules.All(r => r.rule(operand)))
                        {
                            matchesAnyRuleSet = true;
                            break;
                        }
                    }
                    
                    if (matchesAnyRuleSet)
                    {
                        preFilteredItems.Add(item);
                    }
                }
                
                logger?.LogDebug("ItemType pre-filtering reduced dataset from {OriginalCount} to {FilteredCount} items", 
                    items.Count(), preFilteredItems.Count);
                
                // Continue with the pre-filtered items
                items = preFilteredItems;
            }
            else if (itemTypeRules.Count > 0)
            {
                logger?.LogDebug("Skipping ItemType pre-filtering optimization: {RuleSetsWithItemType} of {TotalRuleSets} rule sets have ItemType constraints", 
                    ruleSetsWithItemType.Count, ExpressionSets.Count);
            }
            
            if (needsAudioLanguages || needsPeople)
            {
                // Optimization: Separate rules into cheap and expensive categories
                var cheapCompiledRules = new List<List<Func<Operand, bool>>>();
                var expensiveCompiledRules = new List<List<Func<Operand, bool>>>();
                
                logger?.LogDebug("Separating rules into cheap and expensive categories (AudioLanguages: {AudioNeeded}, People: {PeopleNeeded})", 
                    needsAudioLanguages, needsPeople);
                
                for (int setIndex = 0; setIndex < ExpressionSets.Count; setIndex++)
                {
                    var set = ExpressionSets[setIndex];
                    var cheapRules = new List<Func<Operand, bool>>();
                    var expensiveRules = new List<Func<Operand, bool>>();
                    
                    for (int exprIndex = 0; exprIndex < set.Expressions.Count; exprIndex++)
                    {
                        var expr = set.Expressions[exprIndex];
                        var compiledRule = compiledRules[setIndex][exprIndex];
                        
                        if (expr.MemberName == "AudioLanguages" || expr.MemberName == "People")
                        {
                            expensiveRules.Add(compiledRule);
                            logger?.LogDebug("Rule set {SetIndex}: Added expensive rule: {Field} {Operator} {Value}", 
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
                    expensiveCompiledRules.Add(expensiveRules);
                    
                    logger?.LogDebug("Rule set {SetIndex}: {CheapCount} cheap rules, {ExpensiveCount} expensive rules", 
                        setIndex, cheapRules.Count, expensiveRules.Count);
                }
                
                // Check if there are ANY non-expensive rules across all rule sets
                bool hasNonExpensiveRules = cheapCompiledRules.Any(rules => rules.Count > 0);
                
                if (!hasNonExpensiveRules)
                {
                    // No cheap rules to filter with - extract expensive data for all items
                    logger?.LogDebug("No non-expensive rules found, extracting expensive data for all items");
                    foreach (var i in items)
                    {
                        var operand = OperandFactory.GetMediaType(libraryManager, i, user, userDataManager, logger, needsAudioLanguages, needsPeople);
                        
                        // Debug: Log expensive data found for first few items
                        if (results.Count < 5)
                        {
                            if (needsAudioLanguages)
                            {
                                logger?.LogDebug("Item '{Name}': Found {Count} audio languages: [{Languages}]", 
                                    i.Name, operand.AudioLanguages.Count, string.Join(", ", operand.AudioLanguages));
                            }
                            if (needsPeople)
                            {
                                logger?.LogDebug("Item '{Name}': Found {Count} people: [{People}]", 
                                    i.Name, operand.People.Count, string.Join(", ", operand.People.Take(5)));
                            }
                        }
                        
                        if (compiledRules.Any(set => set.All(rule => rule(operand))))
                        {
                            results.Add(i);
                        }
                    }
                }
                else
                {
                    // Two-phase filtering: cheap rules first, then expensive data extraction
                    logger?.LogDebug("Using two-phase filtering for expensive field optimization");
                
                foreach (var i in items)
                {
                    // Phase 1: Extract cheap properties and check non-expensive rules
                    var cheapOperand = OperandFactory.GetMediaType(libraryManager, i, user, userDataManager, logger, false, false);
                    
                    // Check if item passes all non-expensive rules for any rule set that has non-expensive rules
                    bool passesNonExpensiveRules = false;
                    bool hasExpensiveOnlyRuleSets = false;
                    
                    for (int setIndex = 0; setIndex < cheapCompiledRules.Count; setIndex++)
                    {
                        // Check if this rule set has only expensive rules (no cheap rules)
                        if (cheapCompiledRules[setIndex].Count == 0)
                        {
                            hasExpensiveOnlyRuleSets = true;
                            continue; // Can't evaluate expensive-only rule sets in cheap phase
                        }
                        
                        // Evaluate cheap rules for this rule set
                        if (cheapCompiledRules[setIndex].All(rule => rule(cheapOperand)))
                        {
                            passesNonExpensiveRules = true;
                            break;
                        }
                    }
                    
                    // Skip expensive data extraction only if:
                    // 1. No rule set passed the cheap evaluation AND
                    // 2. There are no expensive-only rule sets that still need to be checked
                    if (!passesNonExpensiveRules && !hasExpensiveOnlyRuleSets)
                        continue;
                    
                    // Phase 2: Extract expensive data and check complete rules
                    var fullOperand = OperandFactory.GetMediaType(libraryManager, i, user, userDataManager, logger, needsAudioLanguages, needsPeople);
                    
                    // Debug: Log expensive data found for first few items
                    if (results.Count < 5)
                    {
                        if (needsAudioLanguages)
                        {
                            logger?.LogDebug("Item '{Name}': Found {Count} audio languages: [{Languages}]", 
                                i.Name, fullOperand.AudioLanguages.Count, string.Join(", ", fullOperand.AudioLanguages));
                        }
                        if (needsPeople)
                        {
                            logger?.LogDebug("Item '{Name}': Found {Count} people: [{People}]", 
                                i.Name, fullOperand.People.Count, string.Join(", ", fullOperand.People.Take(5)));
                        }
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
                // No expensive field rules - use simple single-pass filtering
                foreach (var i in items)
                {
                    var operand = OperandFactory.GetMediaType(libraryManager, i, user, userDataManager, logger, false, false);

                    if (compiledRules.Any(set => set.All(rule => rule(operand)))) results.Add(i);
                }
            }

            return Order.OrderBy(results).Select(x => x.Id);
        }

        // private static void Validate()
        // {
        //     // Future enhancement: Add validation for constructor input
        // }
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