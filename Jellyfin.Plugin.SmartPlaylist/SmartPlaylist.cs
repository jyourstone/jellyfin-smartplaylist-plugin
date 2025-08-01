using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
        public string Id { get; set; }
        public string Name { get; set; }
        public string FileName { get; set; }
        public Guid UserId { get; set; }
        public Order Order { get; set; }
        public List<string> MediaTypes { get; set; }
        public List<ExpressionSet> ExpressionSets { get; set; }
        public int MaxItems { get; set; }
        public int MaxPlayTimeMinutes { get; set; }

        // OPTIMIZATION: Static cache for compiled rules to avoid recompilation
        private static readonly ConcurrentDictionary<string, List<List<Func<Operand, bool>>>> _ruleCache = new();
        
        // Cache management constants and fields
        private const int MAX_CACHE_SIZE = 1000; // Maximum number of cached rule sets
        private const int CLEANUP_THRESHOLD = 800; // Clean up when cache exceeds this size
        private static readonly object _cacheCleanupLock = new();
        private static DateTime _lastCleanupTime = DateTime.MinValue;
        private static readonly TimeSpan MIN_CLEANUP_INTERVAL = TimeSpan.FromMinutes(5); // Minimum time between cleanups

        public SmartPlaylist(SmartPlaylistDto dto)
        {
            Id = dto.Id;
            Name = dto.Name;
            FileName = dto.FileName;
            UserId = dto.UserId;
            Order = OrderFactory.CreateOrder(dto.Order.Name);
            MediaTypes = dto.MediaTypes ?? [];
            MaxItems = dto.MaxItems ?? 0; // Default to 0 (unlimited) for backwards compatibility
            MaxPlayTimeMinutes = dto.MaxPlayTimeMinutes ?? 0; // Default to 0 (unlimited) for backwards compatibility

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
            try
            {
                // Check if cache cleanup is needed (with rate limiting)
                CheckAndCleanupCache(logger);
                
                // Input validation
                if (ExpressionSets == null || ExpressionSets.Count == 0)
                {
                    logger?.LogDebug("No expression sets to compile for playlist '{PlaylistName}'", Name);
                    return [];
                }
                
                // OPTIMIZATION: Generate a cache key based on the rule set content
                var ruleSetHash = GenerateRuleSetHash();
                
                return _ruleCache.GetOrAdd(ruleSetHash, _ =>
                {
                    try
                    {
                        logger?.LogDebug("Compiling rules for playlist {PlaylistName} (cache miss)", Name);
                        
                        var compiledRuleSets = new List<List<Func<Operand, bool>>>();
                        
                        for (int setIndex = 0; setIndex < ExpressionSets.Count; setIndex++)
                        {
                            var set = ExpressionSets[setIndex];
                            if (set?.Expressions == null)
                            {
                                logger?.LogDebug("Skipping null expression set at index {SetIndex} for playlist '{PlaylistName}'", setIndex, Name);
                                compiledRuleSets.Add([]);
                                continue;
                            }
                            
                            var compiledRules = new List<Func<Operand, bool>>();
                            
                            for (int exprIndex = 0; exprIndex < set.Expressions.Count; exprIndex++)
                            {
                                var expr = set.Expressions[exprIndex];
                                if (expr == null)
                                {
                                    logger?.LogDebug("Skipping null expression at set {SetIndex}, index {ExprIndex} for playlist '{PlaylistName}'", setIndex, exprIndex, Name);
                                    continue;
                                }
                                
                                try
                                {
                                    // Validate UserId before converting to string to prevent runtime errors
                                    var userIdString = UserId != Guid.Empty ? UserId.ToString() : null;
                                    if (string.IsNullOrEmpty(userIdString))
                                    {
                                        logger?.LogError("SmartPlaylist '{PlaylistName}' has no valid owner user ID. Cannot compile rules.", Name);
                                        continue; // Skip this rule set
                                    }
                                    
                                    var compiledRule = Engine.CompileRule<Operand>(expr, userIdString, logger);
                                    if (compiledRule != null)
                                    {
                                        compiledRules.Add(compiledRule);
                                    }
                                    else
                                    {
                                        logger?.LogWarning("Failed to compile rule at set {SetIndex}, index {ExprIndex} for playlist '{PlaylistName}': {Field} {Operator} {Value}", 
                                            setIndex, exprIndex, Name, expr.MemberName, expr.Operator, expr.TargetValue);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger?.LogError(ex, "Error compiling rule at set {SetIndex}, index {ExprIndex} for playlist '{PlaylistName}': {Field} {Operator} {Value}", 
                                        setIndex, exprIndex, Name, expr.MemberName, expr.Operator, expr.TargetValue);
                                    // Skip this rule and continue with others
                                }
                            }
                            
                            compiledRuleSets.Add(compiledRules);
                            logger?.LogDebug("Compiled {RuleCount} rules for expression set {SetIndex} in playlist '{PlaylistName}'", 
                                compiledRules.Count, setIndex, Name);
                        }
                        
                        logger?.LogDebug("Successfully compiled {SetCount} rule sets for playlist '{PlaylistName}'", 
                            compiledRuleSets.Count, Name);
                        
                        return compiledRuleSets;
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError(ex, "Critical error during rule compilation for playlist '{PlaylistName}'. Returning empty rule set.", Name);
                        return [];
                    }
                });
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Critical error in CompileRuleSets for playlist '{PlaylistName}'. Returning empty rule set.", Name);
                return [];
            }
        }

        /// <summary>
        /// Checks cache size and performs cleanup if needed, with rate limiting to prevent excessive cleanup operations.
        /// </summary>
        /// <param name="logger">Optional logger for diagnostics.</param>
        private static void CheckAndCleanupCache(ILogger logger = null)
        {
            var currentCacheSize = _ruleCache.Count;
            
            // Only check for cleanup if we're approaching the threshold
            if (currentCacheSize <= CLEANUP_THRESHOLD)
                return;
                
            var now = DateTime.UtcNow;
            
            // Rate limit cleanup operations to prevent excessive cleanup
            if (now - _lastCleanupTime < MIN_CLEANUP_INTERVAL)
                return;
                
            // Use lock to ensure only one thread performs cleanup at a time
            lock (_cacheCleanupLock)
            {
                // Double-check conditions after acquiring lock
                if (_ruleCache.Count <= CLEANUP_THRESHOLD || now - _lastCleanupTime < MIN_CLEANUP_INTERVAL)
                    return;
                    
                logger?.LogDebug("Rule cache size ({CurrentSize}) exceeded threshold ({Threshold}). Performing cleanup.", 
                    _ruleCache.Count, CLEANUP_THRESHOLD);
                
                // Simple cleanup strategy: remove half the cache when it gets too large
                // This is more efficient than LRU for this use case since rule compilation is expensive
                var keysToRemove = _ruleCache.Keys.Take(_ruleCache.Count / 2).ToList();
                
                int removedCount = 0;
                foreach (var key in keysToRemove)
                {
                    if (_ruleCache.TryRemove(key, out _))
                    {
                        removedCount++;
                    }
                }
                
                _lastCleanupTime = now;
                
                logger?.LogDebug("Rule cache cleanup completed. Removed {RemovedCount} entries. Cache size: {CurrentSize}/{MaxSize}", 
                    removedCount, _ruleCache.Count, MAX_CACHE_SIZE);
            }
        }
        
        /// <summary>
        /// Manually clears the entire rule cache. Useful for troubleshooting or memory management.
        /// </summary>
        /// <param name="logger">Optional logger for diagnostics.</param>
        public static void ClearRuleCache(ILogger logger = null)
        {
            lock (_cacheCleanupLock)
            {
                var previousCount = _ruleCache.Count;
                _ruleCache.Clear();
                _lastCleanupTime = DateTime.UtcNow;
                
                logger?.LogDebug("Rule cache manually cleared. Removed {RemovedCount} entries.", previousCount);
            }
        }
        
        /// <summary>
        /// Gets current cache statistics for monitoring and debugging.
        /// </summary>
        /// <returns>A tuple containing current cache size, maximum size, and cleanup threshold.</returns>
        public static (int CurrentSize, int MaxSize, int CleanupThreshold, DateTime LastCleanup) GetCacheStats()
        {
            return (_ruleCache.Count, MAX_CACHE_SIZE, CLEANUP_THRESHOLD, _lastCleanupTime);
        }

        private string GenerateRuleSetHash()
        {
            try
            {
                // Input validation
                if (ExpressionSets == null)
                {
                    return $"id:{Id ?? ""}|sets:0";
                }
                
                // Use StringBuilder for efficient string concatenation
                var hashBuilder = new System.Text.StringBuilder();
                hashBuilder.Append(Id ?? "");
                hashBuilder.Append('|');
                hashBuilder.Append(ExpressionSets.Count);
                
                for (int i = 0; i < ExpressionSets.Count; i++)
                {
                    var set = ExpressionSets[i];
                    
                    hashBuilder.Append("|set");
                    hashBuilder.Append(i);
                    hashBuilder.Append(':');
                    
                    // Handle null expression sets
                    if (set?.Expressions == null)
                    {
                        hashBuilder.Append("null");
                        continue;
                    }
                    
                    hashBuilder.Append(set.Expressions.Count);
                    
                    for (int j = 0; j < set.Expressions.Count; j++)
                    {
                        var expr = set.Expressions[j];
                        
                        hashBuilder.Append("|expr");
                        hashBuilder.Append(i);
                        hashBuilder.Append('_');
                        hashBuilder.Append(j);
                        hashBuilder.Append(':');
                        
                        // Handle null expressions
                        if (expr == null)
                        {
                            hashBuilder.Append("null");
                            continue;
                        }
                        
                        // Handle null expression properties and append efficiently
                        hashBuilder.Append(expr.MemberName ?? "");
                        hashBuilder.Append(':');
                        hashBuilder.Append(expr.Operator ?? "");
                        hashBuilder.Append(':');
                        hashBuilder.Append(expr.TargetValue ?? "");
                    }
                }
                
                return hashBuilder.ToString();
            }
            catch (Exception)
            {
                // If hash generation fails, return a fallback hash based on basic properties
                return $"fallback:{Id ?? ""}:{ExpressionSets?.Count ?? 0}";
            }
        }

        private bool EvaluateLogicGroups(List<List<Func<Operand, bool>>> compiledRules, Operand operand)
        {
            try
            {
                if (compiledRules == null || operand == null)
                {
                    return false;
                }
                
                // Each ExpressionSet is a logic group
                // Groups are combined with OR logic (any group can match)
                // Rules within each group always use AND logic
                for (int groupIndex = 0; groupIndex < ExpressionSets.Count && groupIndex < compiledRules.Count; groupIndex++)
                {
                    var group = ExpressionSets[groupIndex];
                    var groupRules = compiledRules[groupIndex];
                    
                    if (group == null || groupRules == null || groupRules.Count == 0) 
                        continue; // Skip empty or null groups
                    
                    try
                    {
                        bool groupMatches = groupRules.All(rule => 
                        {
                            try
                            {
                                return rule?.Invoke(operand) ?? false;
                            }
                            catch (Exception)
                            {
                                // Log at debug level to avoid spam, but continue evaluation
                                // Conservative approach: assume rule doesn't match if it fails
                                return false;
                            }
                        });
                        
                        if (groupMatches)
                        {
                            return true; // This group matches, so the item matches overall
                        }
                    }
                    catch (Exception)
                    {
                        // If we can't evaluate this group, skip it and continue with others
                        continue;
                    }
                }
                
                return false; // No groups matched
            }
            catch (Exception)
            {
                // If we can't evaluate any groups, conservative approach is to exclude item
                return false;
            }
        }

        // Returns the ID's of the items, if order is provided the IDs are sorted.
        public IEnumerable<Guid> FilterPlaylistItems(IEnumerable<BaseItem> items, ILibraryManager libraryManager,
            User user, IUserDataManager userDataManager = null, ILogger logger = null)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                // Input validation
                if (items == null)
                {
                    logger?.LogWarning("FilterPlaylistItems called with null items collection for playlist '{PlaylistName}'", Name);
                    return [];
                }
                
                if (libraryManager == null)
                {
                    logger?.LogError("FilterPlaylistItems called with null libraryManager for playlist '{PlaylistName}'", Name);
                    return [];
                }
                
                if (user == null)
                {
                    logger?.LogError("FilterPlaylistItems called with null user for playlist '{PlaylistName}'", Name);
                    return [];
                }
                
                var itemCount = items.Count();
                logger?.LogDebug("FilterPlaylistItems called with {ItemCount} items, ExpressionSets={ExpressionSetCount}, MediaTypes={MediaTypes}", 
                    itemCount, ExpressionSets?.Count ?? 0, MediaTypes != null ? string.Join(",", MediaTypes) : "None");
                
                // Early return for empty item collections
                if (itemCount == 0)
                {
                    logger?.LogDebug("No items to filter for playlist '{PlaylistName}'", Name);
                    return [];
                }
                
                // Media type filtering is now handled at the API level in PlaylistService.GetAllUserMedia()
                // This provides significant performance improvements by filtering at the database level
                logger?.LogDebug("Processing {ItemCount} items (already filtered by media type at API level)", itemCount);
                
                var results = new List<BaseItem>();

                // Check if any rules use expensive fields to avoid unnecessary extraction
                var needsAudioLanguages = false;
                var needsPeople = false;
                var needsNextUnwatched = false;
                var includeUnwatchedSeries = true; // Default to true for backwards compatibility
                var additionalUserIds = new List<string>();
                
                try
                {
                    if (ExpressionSets != null)
                    {
                        needsAudioLanguages = ExpressionSets
                            .SelectMany(set => set?.Expressions ?? [])
                            .Any(expr => expr?.MemberName == "AudioLanguages");
                        
                        needsPeople = ExpressionSets
                            .SelectMany(set => set?.Expressions ?? [])
                            .Any(expr => expr?.MemberName == "People");
                        
                        needsNextUnwatched = ExpressionSets
                            .SelectMany(set => set?.Expressions ?? [])
                            .Any(expr => expr?.MemberName == "NextUnwatched");
                        
                        // Extract IncludeUnwatchedSeries parameter from NextUnwatched rules
                        // If any rule explicitly sets it to false, use false; otherwise default to true
                        var nextUnwatchedRules = ExpressionSets
                            .SelectMany(set => set?.Expressions ?? [])
                            .Where(expr => expr?.MemberName == "NextUnwatched")
                            .ToList();
                        
                        includeUnwatchedSeries = !nextUnwatchedRules.Any(rule => rule.IncludeUnwatchedSeries == false);
                        

                        
                        // Collect unique user IDs from user-specific expressions
                        additionalUserIds = [..ExpressionSets
                            .SelectMany(set => set?.Expressions ?? [])
                            .Where(expr => expr?.IsUserSpecific == true && !string.IsNullOrEmpty(expr.UserId))
                            .Select(expr => expr.UserId)
                            .Distinct()];
                        
                        if (additionalUserIds.Count > 0)
                        {
                            logger?.LogDebug("Found user-specific expressions for {Count} users: [{UserIds}]", 
                                additionalUserIds.Count, string.Join(", ", additionalUserIds));
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Error analyzing expression sets for expensive fields in playlist '{PlaylistName}'. Assuming no expensive fields needed.", Name);
                }

                // Early validation of additional users to prevent exceptions during item processing
                if (additionalUserIds.Count > 0 && userDataManager != null)
                {
                    foreach (var userId in additionalUserIds)
                    {
                        if (Guid.TryParse(userId, out var userGuid))
                        {
                            var targetUser = OperandFactory.GetUserById(userDataManager, userGuid);
                            if (targetUser == null)
                            {
                                logger?.LogWarning("User with ID '{UserId}' not found for playlist '{PlaylistName}'. This playlist rule references a user that no longer exists. Skipping playlist processing.", userId, Name);
                                return []; // Return empty results to avoid exception spam
                            }
                        }
                        else
                        {
                            logger?.LogWarning("Invalid user ID format '{UserId}' for playlist '{PlaylistName}'. Skipping playlist processing.", userId, Name);
                            return []; // Return empty results
                        }
                    }
                }

                // Compile rules with error handling
                List<List<Func<Operand, bool>>> compiledRules = null;
                try
                {
                    compiledRules = CompileRuleSets(logger);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Failed to compile rules for playlist '{PlaylistName}'. Playlist will return no results.", Name);
                    return [];
                }
                
                if (compiledRules == null)
                {
                    logger?.LogError("Compiled rules is null for playlist '{PlaylistName}'. Playlist will return no results.", Name);
                    return [];
                }
                
                bool hasAnyRules = compiledRules.Any(set => set?.Count > 0);
                
                // Check if there are any non-expensive rules for two-phase filtering optimization
                bool hasNonExpensiveRules = false;
                try
                {
                    if (ExpressionSets != null)
                    {
                        hasNonExpensiveRules = ExpressionSets
                            .SelectMany(set => set?.Expressions ?? [])
                            .Any(expr => expr?.MemberName != "AudioLanguages" && expr?.MemberName != "People");
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Error analyzing non-expensive rules in playlist '{PlaylistName}'. Assuming non-expensive rules exist.", Name);
                    hasNonExpensiveRules = true; // Conservative assumption
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
                    try
                    {
                        var chunkEnd = Math.Min(chunkStart + chunkSize, totalItems);
                        var chunk = itemsArray.Skip(chunkStart).Take(chunkEnd - chunkStart);
                        
                        if (totalItems > chunkSize)
                        {
                            logger?.LogDebug("Processing chunk {ChunkNumber}/{TotalChunks} (items {Start}-{End})", 
                                (chunkStart / chunkSize) + 1, (totalItems + chunkSize - 1) / chunkSize, chunkStart + 1, chunkEnd);
                        }
                        
                        var chunkResults = ProcessItemChunk(chunk, libraryManager, user, userDataManager, logger, 
                            needsAudioLanguages, needsPeople, needsNextUnwatched, includeUnwatchedSeries, additionalUserIds, compiledRules, hasAnyRules, hasNonExpensiveRules);
                        results.AddRange(chunkResults);
                        
                        // OPTIMIZATION: Allow other operations to run between chunks for large libraries
                        if (totalItems > chunkSize * 2)
                        {
                            // Yield control briefly to prevent blocking
                            System.Threading.Thread.Sleep(1);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError(ex, "Error processing chunk {ChunkStart}-{ChunkEnd} for playlist '{PlaylistName}'. Skipping this chunk.", 
                            chunkStart, Math.Min(chunkStart + chunkSize, totalItems), Name);
                        // Continue with next chunk
                    }
                }

                stopwatch.Stop();
                logger?.LogDebug("Playlist filtering for '{PlaylistName}' completed in {ElapsedTime}ms: {InputCount} items → {OutputCount} items", 
                    Name,stopwatch.ElapsedMilliseconds, totalItems, results.Count);
                
                // Apply ordering and limits with error handling
                try
                {
                    var orderedResults = Order?.OrderBy(results) ?? results;
                    
                    // Apply limits (items and/or time)
                    if (MaxItems > 0 || MaxPlayTimeMinutes > 0)
                    {
                        var limitedResults = ApplyLimits(orderedResults, libraryManager, user, userDataManager, logger);
                        
                        if (Order is RandomOrder)
                        {
                            logger?.LogDebug("Applied random order and limited playlist '{PlaylistName}' to {LimitedCount} items from {TotalItems} total items", 
                                Name, limitedResults.Count, orderedResults.Count());
                        }
                        else
                        {
                            logger?.LogDebug("Limited playlist '{PlaylistName}' to {LimitedCount} items from {TotalItems} total items (deterministic order)", 
                                Name, limitedResults.Count, orderedResults.Count());
                        }
                        
                        return limitedResults.Select(x => x.Id);
                    }
                    else
                    {
                        // No limits - return all ordered results
                        if (Order is RandomOrder)
                        {
                            logger?.LogDebug("Applied random order to playlist '{PlaylistName}' with {TotalItems} items (no limit)", 
                                Name, orderedResults.Count());
                        }
                        
                        return orderedResults.Select(x => x.Id);
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Error applying ordering and limits to playlist '{PlaylistName}'. Returning unordered results.", Name);
                    return results.Select(x => x.Id);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                logger?.LogError(ex, "Critical error in FilterPlaylistItems for playlist '{PlaylistName}' after {ElapsedTime}ms. Returning empty results.", 
                    Name, stopwatch.ElapsedMilliseconds);
                return [];
            }
        }

        /// <summary>
        /// Applies item count and time-based limits to a collection of items.
        /// </summary>
        /// <param name="items">The ordered items to limit</param>
        /// <param name="libraryManager">Library manager for operand creation</param>
        /// <param name="user">User for operand creation</param>
        /// <param name="userDataManager">User data manager for operand creation</param>
        /// <param name="logger">Optional logger for debugging</param>
        /// <returns>The limited collection of items</returns>
        private List<BaseItem> ApplyLimits(IEnumerable<BaseItem> items, ILibraryManager libraryManager, User user, IUserDataManager userDataManager, ILogger logger = null)
        {
            var itemsList = items.ToList();
            if (itemsList.Count == 0) return itemsList;

            var limitedItems = new List<BaseItem>();
            var totalMinutes = 0.0;
            var itemCount = 0;

            foreach (var item in itemsList)
            {
                // Check item count limit
                if (MaxItems > 0 && itemCount >= MaxItems)
                {
                    logger?.LogDebug("Reached item count limit ({MaxItems}) for playlist '{PlaylistName}'", MaxItems, Name);
                    break;
                }

                // Get runtime for this item
                var itemMinutes = 0.0;
                if (MaxPlayTimeMinutes > 0)
                {
                    try
                    {
                        // Use the same runtime extraction logic as in Factory.cs
                        if (item.RunTimeTicks.HasValue)
                        {
                            // Use exact TotalMinutes as double for precise calculation
                            itemMinutes = TimeSpan.FromTicks(item.RunTimeTicks.Value).TotalMinutes;
                        }
                        else
                        {
                            // Fallback: try to get runtime from Operand extraction
                            var operand = OperandFactory.GetMediaType(libraryManager, item, user, userDataManager, logger, new MediaTypeExtractionOptions
                {
                    ExtractAudioLanguages = false,
                    ExtractPeople = false,
                    ExtractNextUnwatched = false,
                    IncludeUnwatchedSeries = true,
                    AdditionalUserIds = []
                }, new OperandFactory.RefreshCache());
                            itemMinutes = operand.RuntimeMinutes;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Error getting runtime for item '{ItemName}' in playlist '{PlaylistName}'. Assuming 0 minutes.", item.Name, Name);
                        itemMinutes = 0.0;
                    }
                }

                // Check time limit
                if (MaxPlayTimeMinutes > 0 && totalMinutes + itemMinutes > MaxPlayTimeMinutes)
                {
                    logger?.LogDebug("Reached time limit ({MaxTime} minutes) for playlist '{PlaylistName}' at {CurrentTime:F1} minutes. Next item '{ItemName}' ({ItemMinutes:F1} minutes) would exceed limit.", MaxPlayTimeMinutes, Name, totalMinutes, item.Name, itemMinutes);
                    break;
                }

                // Add item to results
                limitedItems.Add(item);
                totalMinutes += itemMinutes;
                itemCount++;
            }

            logger?.LogInformation("Applied limits to playlist '{PlaylistName}': {ItemCount} items, {TotalMinutes:F1} minutes (MaxItems: {MaxItems}, MaxTime: {MaxTime} minutes)", 
                Name, itemCount, totalMinutes, MaxItems, MaxPlayTimeMinutes);

            return limitedItems;
        }

        private List<BaseItem> ProcessItemChunk(IEnumerable<BaseItem> items, ILibraryManager libraryManager,
            User user, IUserDataManager userDataManager, ILogger logger, bool needsAudioLanguages, bool needsPeople, bool needsNextUnwatched, bool includeUnwatchedSeries,
            List<string> additionalUserIds, List<List<Func<Operand, bool>>> compiledRules, bool hasAnyRules, bool hasNonExpensiveRules)
        {
            var results = new List<BaseItem>();
            
            try
            {
                if (items == null || compiledRules == null)
                {
                    logger?.LogDebug("ProcessItemChunk called with null items or compiledRules");
                    return results;
                }
                
                if (needsAudioLanguages || needsPeople || needsNextUnwatched)
                {
                    // Create per-refresh cache for performance optimization within this chunk
                    var refreshCache = new OperandFactory.RefreshCache();
                    
                    // Optimization: Separate rules into cheap and expensive categories
                    var cheapCompiledRules = new List<List<Func<Operand, bool>>>();
                    var expensiveCompiledRules = new List<List<Func<Operand, bool>>>();

                    logger?.LogDebug("Separating rules into cheap and expensive categories (AudioLanguages: {AudioNeeded}, People: {PeopleNeeded}, NextUnwatched: {NextUnwatchedNeeded})",
                        needsAudioLanguages, needsPeople, needsNextUnwatched);
                    
                    
                    try
                    {
                        for (int setIndex = 0; setIndex < ExpressionSets.Count && setIndex < compiledRules.Count; setIndex++)
                        {
                            var set = ExpressionSets[setIndex];
                            if (set?.Expressions == null) continue;
                            
                            var cheapRules = new List<Func<Operand, bool>>();
                            var expensiveRules = new List<Func<Operand, bool>>();
                            
                            for (int exprIndex = 0; exprIndex < set.Expressions.Count && exprIndex < compiledRules[setIndex].Count; exprIndex++)
                            {
                                var expr = set.Expressions[exprIndex];
                                if (expr == null) continue;
                                
                                try
                                {
                                    var compiledRule = compiledRules[setIndex][exprIndex];
                                    
                                    if (expr.MemberName == "AudioLanguages" || expr.MemberName == "People" || expr.MemberName == "NextUnwatched")
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
                                catch (Exception ex)
                                {
                                    logger?.LogDebug(ex, "Error processing rule at set {SetIndex}, expression {ExprIndex}", setIndex, exprIndex);
                                }
                            }
                            
                            cheapCompiledRules.Add(cheapRules);
                            expensiveCompiledRules.Add(expensiveRules);
                            
                            logger?.LogDebug("Rule set {SetIndex}: {NonExpensiveCount} non-expensive rules, {ExpensiveCount} expensive rules", 
                                setIndex, cheapRules.Count, expensiveRules.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Error separating rules into cheap and expensive categories. Falling back to simple processing.");
                        return ProcessItemsSimple(items, libraryManager, user, userDataManager, logger, needsAudioLanguages, needsPeople, needsNextUnwatched, includeUnwatchedSeries, additionalUserIds, compiledRules, hasAnyRules);
                    }
                    
                    if (!hasNonExpensiveRules)
                    {
                        // No non-expensive rules - extract expensive data for all items that have expensive rules
                        logger?.LogDebug("No non-expensive rules found, extracting expensive data for all items");
                        
                        foreach (var item in items)
                        {
                            if (item == null) continue;
                            
                            try
                            {
                                var operand = OperandFactory.GetMediaType(libraryManager, item, user, userDataManager, logger, new MediaTypeExtractionOptions
                                {
                                    ExtractAudioLanguages = needsAudioLanguages,
                                    ExtractPeople = needsPeople,
                                    ExtractNextUnwatched = needsNextUnwatched,
                                    IncludeUnwatchedSeries = includeUnwatchedSeries,
                                    AdditionalUserIds = additionalUserIds
                                }, refreshCache);
                                
                                // Debug: Log expensive data found for first few items
                                if (results.Count < 5)
                                {
                                    if (needsAudioLanguages)
                                    {
                                        logger?.LogDebug("Item '{Name}': Found {Count} audio languages: [{Languages}]", 
                                            item.Name, operand.AudioLanguages?.Count ?? 0, operand.AudioLanguages != null ? string.Join(", ", operand.AudioLanguages) : "none");
                                    }
                                    if (needsPeople)
                                    {
                                        logger?.LogDebug("Item '{Name}': Found {Count} people: [{People}]", 
                                            item.Name, operand.People?.Count ?? 0, operand.People != null ? string.Join(", ", operand.People.Take(5)) : "none");
                                    }
                                    if (needsNextUnwatched)
                                    {
                                        logger?.LogDebug("Item '{Name}': NextUnwatched status: {NextUnwatchedUsers}", 
                                            item.Name, operand.NextUnwatchedByUser?.Count > 0 ? string.Join(", ", operand.NextUnwatchedByUser.Select(x => $"{x.Key}={x.Value}")) : "none");
                                    }
                                }
                                
                                bool matches = false;
                                if (!hasAnyRules) {
                                    matches = true;
                                } else {
                                    matches = EvaluateLogicGroups(compiledRules, operand);
                                }
                            
                                if (matches)
                                {
                                    results.Add(item);
                                }
                            }
                            catch (InvalidOperationException ex) when (ex.Message.Contains("User with ID") && ex.Message.Contains("not found"))
                            {
                                // User-specific rule references a user that no longer exists
                                logger?.LogWarning(ex, "Playlist '{PlaylistName}' references a user that no longer exists. Playlist processing will be skipped.", Name);
                                throw; // Re-throw to stop playlist processing entirely
                            }
                            catch (Exception ex)
                            {
                                logger?.LogDebug(ex, "Error processing item '{ItemName}' in expensive-only path. Skipping item.", item.Name);
                                // Skip this item and continue with others
                            }
                        }
                    }
                    else
                    {
                        // Two-phase filtering: non-expensive rules first, then expensive data extraction
                        logger?.LogDebug("Using two-phase filtering for expensive field optimization");
                    
                        foreach (var item in items)
                        {
                            if (item == null) continue;
                            
                            try
                            {
                                // Phase 1: Extract non-expensive properties and check non-expensive rules
                                var cheapOperand = OperandFactory.GetMediaType(libraryManager, item, user, userDataManager, logger, new MediaTypeExtractionOptions
                                {
                                    ExtractAudioLanguages = false,
                                    ExtractPeople = false,
                                    ExtractNextUnwatched = false,
                                    IncludeUnwatchedSeries = true,
                                    AdditionalUserIds = []
                                }, refreshCache);
                                
                                // Check if item passes all non-expensive rules for any rule set that has non-expensive rules
                                bool passesNonExpensiveRules = false;
                                bool hasExpensiveOnlyRuleSets = false;
                                
                                for (int setIndex = 0; setIndex < cheapCompiledRules.Count; setIndex++)
                                {
                                    try
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
                                    catch (Exception ex)
                                    {
                                        logger?.LogDebug(ex, "Error evaluating non-expensive rules for item '{ItemName}' in set {SetIndex}. Assuming rules don't match.", item.Name, setIndex);
                                        // Continue to next rule set
                                    }
                                }
                                
                                // Skip expensive data extraction only if:
                                // 1. No rule set passed the non-expensive evaluation AND
                                // 2. There are no expensive-only rule sets that still need to be checked
                                if (!passesNonExpensiveRules && !hasExpensiveOnlyRuleSets)
                                    continue;
                                
                                // Phase 2: Extract expensive data and check complete rules
                                var fullOperand = OperandFactory.GetMediaType(libraryManager, item, user, userDataManager, logger, new MediaTypeExtractionOptions
                                {
                                    ExtractAudioLanguages = needsAudioLanguages,
                                    ExtractPeople = needsPeople,
                                    ExtractNextUnwatched = needsNextUnwatched,
                                    IncludeUnwatchedSeries = includeUnwatchedSeries,
                                    AdditionalUserIds = additionalUserIds
                                }, refreshCache);
                                
                                // Debug: Log expensive data found for first few items
                                if (results.Count < 5)
                                {
                                    if (needsAudioLanguages)
                                    {
                                        logger?.LogDebug("Item '{Name}': Found {Count} audio languages: [{Languages}]", 
                                            item.Name, fullOperand.AudioLanguages?.Count ?? 0, fullOperand.AudioLanguages != null ? string.Join(", ", fullOperand.AudioLanguages) : "none");
                                    }
                                    if (needsPeople)
                                    {
                                        logger?.LogDebug("Item '{Name}': Found {Count} people: [{People}]", 
                                            item.Name, fullOperand.People?.Count ?? 0, fullOperand.People != null ? string.Join(", ", fullOperand.People.Take(5)) : "none");
                                    }
                                    if (needsNextUnwatched)
                                    {
                                        logger?.LogDebug("Item '{Name}': NextUnwatched status: {NextUnwatchedUsers}", 
                                            item.Name, fullOperand.NextUnwatchedByUser?.Count > 0 ? string.Join(", ", fullOperand.NextUnwatchedByUser.Select(x => $"{x.Key}={x.Value}")) : "none");
                                    }
                                }
                                
                                bool matches = false;
                                if (!hasAnyRules) {
                                    matches = true;
                                } else {
                                    matches = EvaluateLogicGroups(compiledRules, fullOperand);
                                }
                                
                                if (matches)
                                {
                                    results.Add(item);
                                }
                            }
                            catch (InvalidOperationException ex) when (ex.Message.Contains("User with ID") && ex.Message.Contains("not found"))
                            {
                                // User-specific rule references a user that no longer exists
                                logger?.LogWarning(ex, "Playlist '{PlaylistName}' references a user that no longer exists. Playlist processing will be skipped.", Name);
                                throw; // Re-throw to stop playlist processing entirely
                            }
                            catch (Exception ex)
                            {
                                logger?.LogDebug(ex, "Error processing item '{ItemName}' in two-phase path. Skipping item.", item.Name);
                                // Skip this item and continue with others
                            }
                        }
                    }
                }
                else
                {
                    // No expensive fields needed - use simple filtering
                    return ProcessItemsSimple(items, libraryManager, user, userDataManager, logger, needsAudioLanguages, needsPeople, needsNextUnwatched, includeUnwatchedSeries, additionalUserIds, compiledRules, hasAnyRules);
                }
                
                return results;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("User with ID") && ex.Message.Contains("not found"))
            {
                // User-specific rule references a user that no longer exists
                logger?.LogWarning(ex, "Playlist '{PlaylistName}' references a user that no longer exists. Playlist processing will be skipped.", Name);
                throw; // Re-throw to stop playlist processing entirely
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Critical error in ProcessItemChunk. Returning partial results.");
                return results; // Return whatever we managed to process
            }
        }
        
        /// <summary>
        /// Simple item processing fallback method with error handling.
        /// </summary>
        private List<BaseItem> ProcessItemsSimple(IEnumerable<BaseItem> items, ILibraryManager libraryManager,
            User user, IUserDataManager userDataManager, ILogger logger, bool needsAudioLanguages, bool needsPeople, bool needsNextUnwatched, bool includeUnwatchedSeries,
            List<string> additionalUserIds, List<List<Func<Operand, bool>>> compiledRules, bool hasAnyRules)
        {
            var results = new List<BaseItem>();
            
            // Create per-refresh cache for performance optimization within this simple processing
            var refreshCache = new OperandFactory.RefreshCache();
            
            try
            {
                foreach (var item in items)
                {
                    if (item == null) continue;
                    
                    try
                    {
                        var operand = OperandFactory.GetMediaType(libraryManager, item, user, userDataManager, logger, new MediaTypeExtractionOptions
                        {
                            ExtractAudioLanguages = needsAudioLanguages,
                            ExtractPeople = needsPeople,
                            ExtractNextUnwatched = needsNextUnwatched,
                            IncludeUnwatchedSeries = includeUnwatchedSeries,
                            AdditionalUserIds = additionalUserIds
                        }, refreshCache);
                        
                        bool matches = false;
                        if (!hasAnyRules) {
                            matches = true;
                        } else {
                            matches = EvaluateLogicGroups(compiledRules, operand);
                        }
                        
                        if (matches)
                        {
                            results.Add(item);
                        }
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("User with ID") && ex.Message.Contains("not found"))
                    {
                        // User-specific rule references a user that no longer exists
                        logger?.LogWarning(ex, "Playlist '{PlaylistName}' references a user that no longer exists. Playlist processing will be skipped.", Name);
                        throw; // Re-throw to stop playlist processing entirely
                    }
                    catch (Exception ex)
                    {
                        logger?.LogDebug(ex, "Error processing item '{ItemName}' in simple path. Skipping item.", item.Name);
                        // Skip this item and continue with others
                    }
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("User with ID") && ex.Message.Contains("not found"))
            {
                // User-specific rule references a user that no longer exists
                logger?.LogWarning(ex, "Playlist '{PlaylistName}' references a user that no longer exists. Playlist processing will be skipped.", Name);
                throw; // Re-throw to stop playlist processing entirely
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Critical error in ProcessItemsSimple. Returning partial results.");
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
            { "ReleaseDate Ascending", () => new ReleaseDateOrder() },
            { "ReleaseDate Descending", () => new ReleaseDateOrderDesc() },
            { "CommunityRating Ascending", () => new CommunityRatingOrder() },
            { "CommunityRating Descending", () => new CommunityRatingOrderDesc() },
            { "Random", () => new RandomOrder() },
            { "NoOrder", () => new NoOrder() }
        };

        public static Order CreateOrder(string orderName)
        {
            return OrderMap.TryGetValue(orderName ?? "", out var factory) 
                ? factory() 
                : new NoOrder();
        }
    }

    /// <summary>
    /// Utility class for shared ordering operations
    /// </summary>
    public static class OrderUtilities
    {
        /// <summary>
        /// Gets the release date for a BaseItem by checking the PremiereDate property
        /// </summary>
        /// <param name="item">The BaseItem to get the release date for</param>
        /// <returns>The release date or DateTime.MinValue if not available</returns>
        public static DateTime GetReleaseDate(BaseItem item)
        {
            var unixTimestamp = DateUtils.GetReleaseDateUnixTimestamp(item);
            if (unixTimestamp > 0)
            {
                return DateTimeOffset.FromUnixTimeSeconds((long)unixTimestamp).DateTime;
            }
            return DateTime.MinValue;
        }
    }

    public abstract class Order
    {
        public abstract string Name { get; }

        public virtual IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items ?? [];
        }
    }

    public class NoOrder : Order
    {
        public override string Name => "NoOrder";
    }

    public class RandomOrder : Order
    {
        public override string Name => "Random";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            if (items == null) return [];
            
            // Convert to list to ensure stable enumeration
            var itemsList = items.ToList();
            if (itemsList.Count == 0) return [];
            
            // Use current ticks as seed for different results each refresh
            var random = new Random((int)(DateTime.Now.Ticks & 0x7FFFFFFF));
            
            // Create a list of items with their random keys to ensure consistent random values
            var itemsWithKeys = itemsList.Select(item => new { Item = item, Key = random.Next() }).ToList();
            
            // Sort by the pre-generated random keys
            return itemsWithKeys.OrderBy(x => x.Key).Select(x => x.Item);
        }
    }

    public class ProductionYearOrder : Order
    {
        public override string Name => "ProductionYear Ascending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items == null ? [] : items.OrderBy(x => x.ProductionYear ?? 0);
        }
    }

    public class ProductionYearOrderDesc : Order
    {
        public override string Name => "ProductionYear Descending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items == null ? [] : items.OrderByDescending(x => x.ProductionYear ?? 0);
        }
    }

    public class NameOrder : Order
    {
        public override string Name => "Name Ascending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items == null ? [] : items.OrderBy(x => x.Name ?? "");
        }
    }

    public class NameOrderDesc : Order
    {
        public override string Name => "Name Descending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items == null ? [] : items.OrderByDescending(x => x.Name ?? "");
        }
    }

    public class DateCreatedOrder : Order
    {
        public override string Name => "DateCreated Ascending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items == null ? [] : items.OrderBy(x => x.DateCreated);
        }
    }

    public class DateCreatedOrderDesc : Order
    {
        public override string Name => "DateCreated Descending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items == null ? [] : items.OrderByDescending(x => x.DateCreated);
        }
    }

    public class ReleaseDateOrder : Order
    {
        public override string Name => "ReleaseDate Ascending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items == null ? [] : items.OrderBy(OrderUtilities.GetReleaseDate);
        }
    }

    public class ReleaseDateOrderDesc : Order
    {
        public override string Name => "ReleaseDate Descending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items == null ? [] : items.OrderByDescending(OrderUtilities.GetReleaseDate);
        }
    }

    public class CommunityRatingOrder : Order
    {
        public override string Name => "CommunityRating Ascending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items == null ? [] : items.OrderBy(x => x.CommunityRating ?? 0);
        }
    }

    public class CommunityRatingOrderDesc : Order
    {
        public override string Name => "CommunityRating Descending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items == null ? [] : items.OrderByDescending(x => x.CommunityRating ?? 0);
        }
    }
}