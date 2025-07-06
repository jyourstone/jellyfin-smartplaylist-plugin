using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SmartPlaylist.QueryEngine;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartPlaylist
{
    public class SmartPlaylist
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string FileName { get; set; }
        public Guid UserId { get; set; }
        public Order Order { get; set; }
        public List<string> MediaTypes { get; set; }
        public List<ExpressionSet> ExpressionSets { get; set; }

        // OPTIMIZATION: Static cache for compiled rules to avoid recompilation
        private static readonly ConcurrentDictionary<string, List<List<Func<Operand, bool>>>> _ruleCache = new();

        public SmartPlaylist(SmartPlaylistDto dto)
        {
            Id = dto.Id;
            Name = dto.Name;
            FileName = dto.FileName;
            UserId = dto.UserId;
            Order = OrderFactory.CreateOrder(dto.Order.Name);
            MediaTypes = dto.MediaTypes ?? [];

            if (dto.ExpressionSets != null && dto.ExpressionSets.Count > 0)
            {
                ExpressionSets = Engine.FixRuleSets(dto.ExpressionSets);
            }
            else
            {
                ExpressionSets = [];
            }
        }

        private List<List<Func<Operand, bool>>> CompileRuleSets(ILogger logger = null)
        {
            // OPTIMIZATION: Generate a cache key based on the rule set content
            var ruleSetHash = GenerateRuleSetHash();
            
            return _ruleCache.GetOrAdd(ruleSetHash, _ =>
            {
                logger?.LogDebug("Compiling rules for playlist {PlaylistName} (cache miss)", Name);
                return [.. ExpressionSets.Select(set => 
                    set.Expressions.Select(r => Engine.CompileRule<Operand>(r, logger)).ToList())];
            });
        }

        private string GenerateRuleSetHash()
        {
            // Create a hash based on the rule set structure and content
            var hashParts = new List<string>
            {
                Id ?? "",
                ExpressionSets.Count.ToString()
            };
            
            for (int i = 0; i < ExpressionSets.Count; i++)
            {
                var set = ExpressionSets[i];
                hashParts.Add($"set{i}:{set.Expressions.Count}");
                
                for (int j = 0; j < set.Expressions.Count; j++)
                {
                    var expr = set.Expressions[j];
                    hashParts.Add($"expr{i}_{j}:{expr.MemberName}:{expr.Operator}:{expr.TargetValue}");
                }
            }
            
            return string.Join("|", hashParts);
        }

        private bool EvaluateLogicGroups(List<List<Func<Operand, bool>>> compiledRules, Operand operand, ILogger logger = null)
        {
            // Each ExpressionSet is a logic group
            // Groups are combined with OR logic (any group can match)
            // Rules within each group always use AND logic
            for (int groupIndex = 0; groupIndex < ExpressionSets.Count; groupIndex++)
            {
                var group = ExpressionSets[groupIndex];
                var groupRules = compiledRules[groupIndex];
                if (groupRules.Count == 0) continue; // Skip empty groups
                bool groupMatches = groupRules.All(rule => rule(operand)); // Always AND logic
                if (groupMatches)
                {
                    return true; // This group matches, so the item matches overall
                }
            }
            return false; // No groups matched
        }

        // Returns the ID's of the items, if order is provided the IDs are sorted.
        public IEnumerable<Guid> FilterPlaylistItems(IEnumerable<BaseItem> items, ILibraryManager libraryManager,
            User user, IUserDataManager userDataManager = null, ILogger logger = null)
        {
            var stopwatch = Stopwatch.StartNew();
            logger?.LogDebug("[DEBUG] FilterPlaylistItems called with {ItemCount} items, ExpressionSets={ExpressionSetCount}, MediaTypes={MediaTypes}", 
                items.Count(), ExpressionSets.Count, string.Join(",", MediaTypes));
            
            // Apply media type pre-filtering first for performance
            if (MediaTypes != null && MediaTypes.Count > 0)
            {
                var originalCount = items.Count();
                items = items.Where(item => MediaTypes.Contains(item.GetType().Name));
                logger?.LogDebug("[DEBUG] Media type pre-filtering reduced items from {OriginalCount} to {FilteredCount} (filtering for: {MediaTypes})", 
                    originalCount, items.Count(), string.Join(", ", MediaTypes));
            }
            else
            {
                logger?.LogDebug("[DEBUG] No media type pre-filtering applied (no MediaTypes specified)");
            }
            
            var results = new List<BaseItem>();

            // Check if any rules use expensive fields to avoid unnecessary extraction
            var needsAudioLanguages = ExpressionSets
                .SelectMany(set => set.Expressions)
                .Any(expr => expr.MemberName == "AudioLanguages");
            
            var needsPeople = ExpressionSets
                .SelectMany(set => set.Expressions)
                .Any(expr => expr.MemberName == "People");

            var compiledRules = CompileRuleSets(logger);
            bool hasAnyRules = compiledRules.Any(set => set.Count > 0);
            
            // Check if there are any non-expensive rules for two-phase filtering optimization
            bool hasNonExpensiveRules = ExpressionSets
                .SelectMany(set => set.Expressions)
                .Any(expr => expr.MemberName != "AudioLanguages" && expr.MemberName != "People");
            
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
            
            // OPTIMIZATION: Process items in chunks for large libraries to prevent memory issues
            const int chunkSize = 1000; // Process 1000 items at a time
            var itemsArray = items.ToArray();
            var totalItems = itemsArray.Length;
            
            if (totalItems > chunkSize)
            {
                logger?.LogDebug("Processing large library ({TotalItems} items) in chunks of {ChunkSize}", totalItems, chunkSize);
            }
            
            for (int chunkStart = 0; chunkStart < totalItems; chunkStart += chunkSize)
            {
                var chunkEnd = Math.Min(chunkStart + chunkSize, totalItems);
                var chunk = itemsArray.Skip(chunkStart).Take(chunkEnd - chunkStart);
                
                if (totalItems > chunkSize)
                {
                    logger?.LogDebug("Processing chunk {ChunkNumber}/{TotalChunks} (items {Start}-{End})", 
                        (chunkStart / chunkSize) + 1, (totalItems + chunkSize - 1) / chunkSize, chunkStart + 1, chunkEnd);
                }
                
                var chunkResults = ProcessItemChunk(chunk, libraryManager, user, userDataManager, logger, 
                    needsAudioLanguages, needsPeople, compiledRules, hasAnyRules, hasNonExpensiveRules);
                results.AddRange(chunkResults);
                
                // OPTIMIZATION: Allow other operations to run between chunks for large libraries
                if (totalItems > chunkSize * 2)
                {
                    // Yield control briefly to prevent blocking
                    System.Threading.Thread.Sleep(1);
                }
            }

            stopwatch.Stop();
            logger?.LogInformation("Playlist filtering completed in {ElapsedTime}ms: {InputCount} items → {OutputCount} items", 
                stopwatch.ElapsedMilliseconds, totalItems, results.Count);
            
            return Order.OrderBy(results).Select(x => x.Id);
        }

        private List<BaseItem> ProcessItemChunk(IEnumerable<BaseItem> items, ILibraryManager libraryManager,
            User user, IUserDataManager userDataManager, ILogger logger, bool needsAudioLanguages, bool needsPeople,
            List<List<Func<Operand, bool>>> compiledRules, bool hasAnyRules, bool hasNonExpensiveRules)
        {
            var results = new List<BaseItem>();
            
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
                            logger?.LogDebug("Rule set {SetIndex}: Added non-expensive rule: {Field} {Operator} {Value}", 
                                setIndex, expr.MemberName, expr.Operator, expr.TargetValue);
                        }
                    }
                    
                    cheapCompiledRules.Add(cheapRules);
                    expensiveCompiledRules.Add(expensiveRules);
                    
                    logger?.LogDebug("Rule set {SetIndex}: {NonExpensiveCount} non-expensive rules, {ExpensiveCount} expensive rules", 
                        setIndex, cheapRules.Count, expensiveRules.Count);
                }
                
                if (!hasNonExpensiveRules)
                {
                    // No non-expensive rules - extract expensive data for all items that have expensive rules
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
                        
                        bool matches = false;
                        if (!hasAnyRules) {
                            matches = true;
                        } else {
                            matches = EvaluateLogicGroups(compiledRules, operand, logger);
                        }
                    
                        if (matches)
                        {
                            results.Add(i);
                        }
                    }
                }
                else
                {
                    // Two-phase filtering: non-expensive rules first, then expensive data extraction
                    logger?.LogDebug("Using two-phase filtering for expensive field optimization");
                
                    foreach (var i in items)
                    {
                        // Phase 1: Extract non-expensive properties and check non-expensive rules
                        var cheapOperand = OperandFactory.GetMediaType(libraryManager, i, user, userDataManager, logger, false, false);
                        
                        // Check if item passes all non-expensive rules for any rule set that has non-expensive rules
                        bool passesNonExpensiveRules = false;
                        bool hasExpensiveOnlyRuleSets = false;
                        
                        for (int setIndex = 0; setIndex < cheapCompiledRules.Count; setIndex++)
                        {
                            // Check if this rule set has only expensive rules (no non-expensive rules)
                            if (cheapCompiledRules[setIndex].Count == 0)
                            {
                                hasExpensiveOnlyRuleSets = true;
                                continue; // Can't evaluate expensive-only rule sets in non-expensive phase
                            }
                            
                            // Evaluate non-expensive rules for this rule set
                            if (cheapCompiledRules[setIndex].All(rule => rule(cheapOperand)))
                            {
                                passesNonExpensiveRules = true;
                                break;
                            }
                        }
                        
                        // Skip expensive data extraction only if:
                        // 1. No rule set passed the non-expensive evaluation AND
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
                        
                        bool matches = false;
                        if (!hasAnyRules) {
                            matches = true;
                        } else {
                            matches = EvaluateLogicGroups(compiledRules, fullOperand, logger);
                        }
                        
                        if (matches)
                        {
                            results.Add(i);
                        }
                    }
                }
            }
            else
            {
                // No expensive fields needed - use simple filtering
                logger?.LogDebug("No expensive fields required, using simple filtering");
                
                foreach (var i in items)
                {
                    var operand = OperandFactory.GetMediaType(libraryManager, i, user, userDataManager, logger, false, false);
                    
                    bool matches = false;
                    if (!hasAnyRules) {
                        matches = true;
                    } else {
                        matches = EvaluateLogicGroups(compiledRules, operand, logger);
                    }
                    
                    if (matches)
                    {
                        results.Add(i);
                    }
                }
            }
            
            return results;
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