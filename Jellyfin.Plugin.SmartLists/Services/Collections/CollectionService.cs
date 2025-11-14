using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SmartLists;
using Jellyfin.Plugin.SmartLists.Core;
using Jellyfin.Plugin.SmartLists.Core.Constants;
using Jellyfin.Plugin.SmartLists.Core.Models;
using Jellyfin.Plugin.SmartLists.Services.Abstractions;
using Jellyfin.Plugin.SmartLists.Utilities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartLists.Services.Collections
{
    /// <summary>
    /// Service for handling individual smart collection operations.
    /// Implements ISmartListService for collections.
    /// </summary>
    public class CollectionService : ISmartListService<SmartCollectionDto>
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ICollectionManager _collectionManager;
        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;
        private readonly ILogger<CollectionService> _logger;
        private readonly IProviderManager _providerManager;

        // Global semaphore to prevent concurrent refresh operations while preserving internal parallelism
        private static readonly SemaphoreSlim _refreshOperationLock = new(1, 1);

        public CollectionService(
            ILibraryManager libraryManager,
            ICollectionManager collectionManager,
            IUserManager userManager,
            IUserDataManager userDataManager,
            ILogger<CollectionService> logger,
            IProviderManager providerManager)
        {
            _libraryManager = libraryManager;
            _collectionManager = collectionManager;
            _userManager = userManager;
            _userDataManager = userDataManager;
            _logger = logger;
            _providerManager = providerManager;
        }

        public async Task<(bool Success, string Message, string Id)> RefreshAsync(SmartCollectionDto dto, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(dto);

            var stopwatch = Stopwatch.StartNew();
            try
            {
                _logger.LogDebug("Refreshing single smart collection: {CollectionName}", dto.Name);
                _logger.LogDebug("CollectionService.RefreshAsync called with: Name={Name}, Enabled={Enabled}, ExpressionSets={ExpressionSetCount}, MediaTypes={MediaTypes}",
                    dto.Name, dto.Enabled, dto.ExpressionSets?.Count ?? 0,
                    dto.MediaTypes != null ? string.Join(",", dto.MediaTypes) : "None");

                // Validate media types before processing
                _logger.LogDebug("Validating media types for collection '{CollectionName}': {MediaTypes}", dto.Name, dto.MediaTypes != null ? string.Join(",", dto.MediaTypes) : "null");

                // Note: Series media type is supported in Collections (unlike Playlists where it causes playback issues)
                
                if (dto.MediaTypes == null || dto.MediaTypes.Count == 0)
                {
                    _logger.LogError("Smart collection '{CollectionName}' has no media types specified. At least one media type must be selected. Skipping collection refresh.", dto.Name);
                    return (false, "No media types specified. At least one media type must be selected.", string.Empty);
                }

                // Check if collection is enabled
                if (!dto.Enabled)
                {
                    _logger.LogDebug("Smart collection '{CollectionName}' is disabled. Skipping refresh.", dto.Name);
                    return (true, "Collection is disabled", string.Empty);
                }

                // Get all media items (collections are server-wide, no user filtering)
                var allMedia = GetAllMedia(dto.MediaTypes, dto).ToArray();
                _logger.LogDebug("Found {MediaCount} total media items for collection", allMedia.Length);

                // Collections use an owner user for rule context (IsPlayed, IsFavorite, etc.)
                // The collection is server-wide (visible to all), but rules evaluate in the owner's context
                if (!Guid.TryParse(dto.User, out var ownerUserId) || ownerUserId == Guid.Empty)
                {
                    _logger.LogError("Collection owner user ID is invalid or empty: {User}", dto.User);
                    return (false, $"Collection owner user is required. Please set a valid owner.", string.Empty);
                }
                
                var ownerUser = _userManager.GetUserById(ownerUserId);
                
                if (ownerUser == null)
                {
                    _logger.LogError("Collection owner user {User} not found - cannot filter collection items", dto.User);
                    return (false, $"Collection owner user not found. Please set a valid owner.", string.Empty);
                }

                var smartCollection = new Core.SmartList(dto);

                // Log the collection rules
                _logger.LogDebug("Processing collection {CollectionName} with {RuleSetCount} rule sets (Owner: {OwnerUser})", 
                    dto.Name, dto.ExpressionSets?.Count ?? 0, ownerUser.Username);
                
                // Use owner's user data manager for user-specific filtering (IsPlayed, IsFavorite, etc.)
                var newItems = smartCollection.FilterPlaylistItems(allMedia, _libraryManager, ownerUser, _userDataManager, _logger).ToArray();
                _logger.LogDebug("Collection {CollectionName} filtered to {FilteredCount} items from {TotalCount} total items",
                    dto.Name, newItems.Length, allMedia.Length);

                // Create a lookup dictionary for O(1) access while preserving order from newItems
                var mediaLookup = allMedia.ToDictionary(m => m.Id, m => m);
                var newLinkedChildren = newItems
                    .Where(itemId => mediaLookup.ContainsKey(itemId))
                    .Select(itemId => new LinkedChild { ItemId = itemId, Path = mediaLookup[itemId].Path })
                    .ToArray();

                // Calculate collection statistics from the same filtered list used for the actual collection
                dto.ItemCount = newLinkedChildren.Length;
                dto.TotalRuntimeMinutes = CalculateTotalRuntimeMinutes(
                    newLinkedChildren.Where(lc => lc.ItemId.HasValue).Select(lc => lc.ItemId!.Value).ToArray(),
                    mediaLookup,
                    _logger);
                _logger.LogDebug("Calculated collection stats: {ItemCount} items, {TotalRuntime} minutes total runtime",
                    dto.ItemCount, dto.TotalRuntimeMinutes);

                // Try to find existing collection by Jellyfin collection ID
                BaseItem? existingCollectionItem = null;

                _logger.LogDebug("Looking for collection: JellyfinCollectionId={JellyfinCollectionId}",
                    dto.JellyfinCollectionId);

                // First try to find by Jellyfin collection ID (most reliable)
                if (!string.IsNullOrEmpty(dto.JellyfinCollectionId) && Guid.TryParse(dto.JellyfinCollectionId, out var jellyfinCollectionId))
                {
                    var itemById = _libraryManager.GetItemById(jellyfinCollectionId);
                    if (itemById != null && itemById.GetBaseItemKind() == BaseItemKind.BoxSet)
                    {
                        existingCollectionItem = itemById;
                        _logger.LogDebug("Found existing collection by Jellyfin collection ID: {JellyfinCollectionId} - {CollectionName}",
                            dto.JellyfinCollectionId, itemById.Name);
                    }
                    else
                    {
                        _logger.LogDebug("No collection found by Jellyfin collection ID: {JellyfinCollectionId}", dto.JellyfinCollectionId);
                    }
                }

                var collectionName = dto.Name;

                if (existingCollectionItem != null && existingCollectionItem.GetBaseItemKind() == BaseItemKind.BoxSet)
                {
                    var existingCollection = existingCollectionItem;
                    _logger.LogDebug("Processing existing collection: {CollectionName} (ID: {CollectionId})", existingCollection.Name, existingCollection.Id);

                    // Check if the collection name needs to be updated
                    // Apply prefix/suffix formatting to ensure consistency
                    var currentName = existingCollection.Name;
                    var expectedName = NameFormatter.FormatPlaylistName(collectionName);
                    var nameChanged = currentName != expectedName;

                    if (nameChanged)
                    {
                        _logger.LogDebug("Collection name changing from '{OldName}' to '{NewName}'", currentName, expectedName);
                        existingCollection.Name = expectedName;
                    }

                    // Update the collection if any changes are needed
                    if (nameChanged)
                    {
                        await existingCollection.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken);
                        _logger.LogDebug("Updated existing collection: {CollectionName}", existingCollection.Name);
                    }

                    // Update the collection items
                    await UpdateCollectionItemsAsync(existingCollection, newLinkedChildren, dto, cancellationToken);

                    _logger.LogDebug("Successfully updated existing collection: {CollectionName} with {ItemCount} items",
                        existingCollection.Name, newLinkedChildren.Length);

                    // Trigger library scan to update UI
                    try
                    {
                        _logger.LogDebug("Triggering library scan after collection update");
                        var queueScanMethod = _libraryManager.GetType().GetMethod("QueueLibraryScan");
                        if (queueScanMethod != null)
                        {
                            queueScanMethod.Invoke(_libraryManager, null);
                            _logger.LogDebug("Queued library scan after collection update");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to trigger library scan after collection update");
                    }

                    // Update LastRefreshed timestamp for successful refresh
                    dto.LastRefreshed = DateTime.UtcNow;
                    _logger.LogDebug("Updated LastRefreshed timestamp for collection: {CollectionName}", dto.Name);

                    return (true, $"Updated collection '{existingCollection.Name}' with {newLinkedChildren.Length} items", existingCollection.Id.ToString());
                }
                else
                {
                    // Create new collection
                    _logger.LogDebug("Creating new collection: {CollectionName}", collectionName);

                    var newCollectionId = await CreateNewCollectionAsync(collectionName, newLinkedChildren, dto, cancellationToken);

                    // Update the DTO with the new Jellyfin collection ID
                    dto.JellyfinCollectionId = newCollectionId;

                    // Update LastRefreshed timestamp for successful refresh
                    dto.LastRefreshed = DateTime.UtcNow;
                    _logger.LogDebug("Updated LastRefreshed timestamp for collection: {CollectionName}", dto.Name);

                    _logger.LogDebug("Successfully created new collection: {CollectionName} with {ItemCount} items",
                        collectionName, newLinkedChildren.Length);

                    return (true, $"Created collection '{collectionName}' with {newLinkedChildren.Length} items", newCollectionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing collection refresh for '{CollectionName}': {ErrorMessage}", dto.Name, ex.Message);
                return (false, $"Error processing collection '{dto.Name}': {ex.Message}", string.Empty);
            }
            finally
            {
                stopwatch.Stop();
                _logger.LogDebug("Collection refresh completed in {ElapsedMs}ms: {CollectionName}", stopwatch.ElapsedMilliseconds, dto.Name);
            }
        }

        public async Task<(bool Success, string Message, string Id)> RefreshWithTimeoutAsync(SmartCollectionDto dto, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(dto);

            // Wait up to 5 seconds for create/edit operations
            _logger.LogDebug("Attempting to acquire refresh lock for single collection: {CollectionName} (5-second timeout)", dto.Name);

            try
            {
                if (await _refreshOperationLock.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken))
                {
                    try
                    {
                        _logger.LogDebug("Acquired refresh lock for single collection: {CollectionName}", dto.Name);
                        var (success, message, collectionId) = await RefreshAsync(dto, cancellationToken);
                        return (success, message, collectionId);
                    }
                    finally
                    {
                        _refreshOperationLock.Release();
                        _logger.LogDebug("Released refresh lock for single collection: {CollectionName}", dto.Name);
                    }
                }
                else
                {
                    _logger.LogDebug("Timeout waiting for refresh lock for single collection: {CollectionName}", dto.Name);
                    return (false, "Collection refresh is already in progress, please try again in a moment.", string.Empty);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Refresh operation cancelled for collection: {CollectionName}", dto.Name);
                return (false, "Refresh operation was cancelled.", string.Empty);
            }
        }

        public Task<(bool Success, string Message)> TryRefreshAllAsync(CancellationToken cancellationToken = default)
        {
            // Immediate return for manual refresh all
            _logger.LogDebug("Attempting to acquire refresh lock for all collections (immediate return)");

            try
            {
                if (_refreshOperationLock.Wait(0, cancellationToken))
                {
                    try
                    {
                        _logger.LogDebug("Acquired refresh lock for all collections - delegating to scheduled task");
                        // Don't actually do the refresh here - just trigger the scheduled task
                        // The scheduled task will handle the actual refresh with proper batching and optimization
                        return Task.FromResult((true, "Collection refresh started successfully"));
                    }
                    finally
                    {
                        _refreshOperationLock.Release();
                        _logger.LogDebug("Released refresh lock for all collections");
                    }
                }
                else
                {
                    _logger.LogDebug("Refresh lock already held - refresh already in progress");
                    return Task.FromResult((false, "Collection refresh is already in progress. Please try again in a moment."));
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Refresh operation cancelled for all collections");
                return Task.FromResult((false, "Refresh operation was cancelled."));
            }
        }

        public Task DeleteAsync(SmartCollectionDto dto, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(dto);

            try
            {
                BaseItem? existingCollection = null;

                // Try to find by Jellyfin collection ID only
                if (!string.IsNullOrEmpty(dto.JellyfinCollectionId) && Guid.TryParse(dto.JellyfinCollectionId, out var jellyfinCollectionId))
                {
                    var itemById = _libraryManager.GetItemById(jellyfinCollectionId);
                    if (itemById != null && itemById.GetBaseItemKind() == BaseItemKind.BoxSet)
                    {
                        existingCollection = itemById;
                        _logger.LogDebug("Found collection by Jellyfin collection ID for deletion: {JellyfinCollectionId} - {CollectionName}",
                            dto.JellyfinCollectionId, existingCollection.Name);
                    }
                    else
                    {
                        _logger.LogWarning("No Jellyfin collection found by ID '{JellyfinCollectionId}' for deletion. Collection may have been manually deleted.", dto.JellyfinCollectionId);
                    }
                }
                else
                {
                    _logger.LogWarning("No Jellyfin collection ID available for collection '{CollectionName}'. Cannot delete Jellyfin collection.", dto.Name);
                }

                if (existingCollection != null)
                {
                    _logger.LogInformation("Deleting Jellyfin collection '{CollectionName}' (ID: {CollectionId})",
                        existingCollection.Name, existingCollection.Id);
                    _libraryManager.DeleteItem(existingCollection, new DeleteOptions { DeleteFileLocation = true }, true);
                    
                    // Trigger library scan to update UI
                    try
                    {
                        _logger.LogDebug("Triggering library scan after collection deletion");
                        var queueScanMethod = _libraryManager.GetType().GetMethod("QueueLibraryScan");
                        if (queueScanMethod != null)
                        {
                            queueScanMethod.Invoke(_libraryManager, null);
                            _logger.LogDebug("Queued library scan after collection deletion");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to trigger library scan after collection deletion");
                    }
                }

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting smart collection {CollectionName}", dto.Name);
                throw;
            }
        }

        public async Task DisableAsync(SmartCollectionDto dto, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(dto);

            try
            {
                _logger.LogDebug("Disabling smart collection: {CollectionName}", dto.Name);

                // Use timeout approach for disable operations since they involve deleting collections
                if (await _refreshOperationLock.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken))
                {
                    try
                    {
                        _logger.LogDebug("Acquired refresh lock for disabling collection: {CollectionName}", dto.Name);
                        await DeleteAsync(dto, cancellationToken);
                        _logger.LogInformation("Successfully disabled smart collection: {CollectionName}", dto.Name);
                    }
                    finally
                    {
                        _refreshOperationLock.Release();
                        _logger.LogDebug("Released refresh lock for disabling collection: {CollectionName}", dto.Name);
                    }
                }
                else
                {
                    _logger.LogWarning("Timeout waiting for refresh lock to disable collection: {CollectionName}", dto.Name);
                    throw new InvalidOperationException("Collection refresh is already in progress. Please try again in a moment.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disabling smart collection {CollectionName}", dto.Name);
                throw;
            }
        }

        private async Task UpdateCollectionItemsAsync(BaseItem collection, LinkedChild[] linkedChildren, SmartCollectionDto dto, CancellationToken cancellationToken)
        {
            // Verify this is a BoxSet using BaseItemKind
            if (collection.GetBaseItemKind() != BaseItemKind.BoxSet)
            {
                _logger.LogError("Expected BoxSet but got {Type} (BaseItemKind: {Kind})", collection.GetType().Name, collection.GetBaseItemKind());
                return;
            }

            _logger.LogDebug("Updating collection {CollectionName} items to {ItemCount}",
                collection.Name, linkedChildren.Length);

            // Update the collection items using reflection to access LinkedChildren property
            var linkedChildrenProperty = collection.GetType().GetProperty("LinkedChildren");
            if (linkedChildrenProperty != null && linkedChildrenProperty.CanWrite)
            {
                linkedChildrenProperty.SetValue(collection, linkedChildren);
            }
            else
            {
                _logger.LogError("Cannot set LinkedChildren property on collection {CollectionName}", collection.Name);
                return;
            }

            // Save the changes
            await collection.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);

            // Refresh metadata to generate cover images
            await RefreshCollectionMetadataAsync(collection, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> CreateNewCollectionAsync(string collectionName, LinkedChild[] linkedChildren, SmartCollectionDto dto, CancellationToken cancellationToken)
        {
            // Apply prefix/suffix to collection name using the same configuration as playlists
            var formattedName = NameFormatter.FormatPlaylistName(collectionName);
            
            _logger.LogDebug("Creating new smart collection {CollectionName} (formatted as {FormattedName}) with {ItemCount} items",
                collectionName, formattedName, linkedChildren.Length);

            // Create collection using ICollectionManager
            // Note: ICollectionManager.CreateCollectionAsync signature may vary - using reflection if needed
            var itemIds = linkedChildren.Where(lc => lc.ItemId.HasValue).Select(lc => lc.ItemId!.Value).ToList();
            
            // Try to use ICollectionManager.CreateCollectionAsync
            // If the API signature is different, we'll use reflection or create BoxSet directly
            Guid collectionId;
            
            try
            {
                // Try to find CreateCollectionAsync with various signatures
                var collectionManagerType = _collectionManager.GetType();
                _logger.LogDebug("Searching for CreateCollectionAsync methods on {Type}", collectionManagerType.Name);
                
                var allMethods = collectionManagerType.GetMethods()
                    .Where(m => m.Name.Contains("Create", StringComparison.OrdinalIgnoreCase))
                    .Select(m => $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})")
                    .ToArray();
                _logger.LogDebug("Available Create methods: {Methods}", string.Join("; ", allMethods));
                
                // Try different method signatures
                System.Reflection.MethodInfo? createMethod = null;
                
                // Find CollectionCreationOptions type
                Type? optionsType = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    optionsType = assembly.GetTypes()
                        .FirstOrDefault(t => t.Name == "CollectionCreationOptions" && t.Namespace?.Contains("MediaBrowser") == true);
                    if (optionsType != null)
                    {
                        _logger.LogDebug("Found CollectionCreationOptions type in assembly {Assembly}", assembly.GetName().Name);
                        break;
                    }
                }
                
                // Signature 1: CreateCollectionAsync(CollectionCreationOptions options)
                if (optionsType != null)
                {
                    createMethod = collectionManagerType.GetMethod("CreateCollectionAsync", new[] { optionsType });
                    if (createMethod != null)
                    {
                        _logger.LogDebug("Found CreateCollectionAsync(CollectionCreationOptions) method");
                    }
                }
                
                if (createMethod == null)
                {
                    // Signature 2: CreateCollectionAsync(string name, Guid[] itemIds)
                    createMethod = collectionManagerType.GetMethod("CreateCollectionAsync", new[] { typeof(string), typeof(Guid[]) });
                }
                
                if (createMethod == null)
                {
                    // Signature 3: CreateCollectionAsync(string name, IEnumerable<Guid> itemIds)
                    createMethod = collectionManagerType.GetMethod("CreateCollectionAsync", new[] { typeof(string), typeof(IEnumerable<Guid>) });
                }
                
                if (createMethod != null)
                {
                    _logger.LogDebug("Found CreateCollectionAsync method: {Method}", createMethod);
                    
                    var parameters = createMethod.GetParameters();
                    object? taskResult = null;
                    
                    if (parameters.Length == 1 && parameters[0].ParameterType.Name == "CollectionCreationOptions")
                    {
                        // Use CollectionCreationOptions
                        var optType = parameters[0].ParameterType;
                        var options = Activator.CreateInstance(optType);
                        if (options == null)
                        {
                            throw new InvalidOperationException("Failed to create CollectionCreationOptions instance");
                        }
                        
                        _logger.LogDebug("Created CollectionCreationOptions instance, setting properties");
                        
                        // Set Name property
                        var nameProperty = optType.GetProperty("Name");
                        if (nameProperty != null)
                        {
                            nameProperty.SetValue(options, formattedName);
                            _logger.LogDebug("Set Name to: {Name}", formattedName);
                        }
                        
                        // Note: We'll add items using AddToCollectionAsync after creation instead of setting ItemIdList
                        _logger.LogDebug("Collection will be created empty, items will be added via AddToCollectionAsync");
                        
                        _logger.LogDebug("Invoking CreateCollectionAsync with CollectionCreationOptions");
                        taskResult = createMethod.Invoke(_collectionManager, new object[] { options });
                    }
                    else
                    {
                        // Use direct parameters
                        _logger.LogDebug("Invoking CreateCollectionAsync with direct parameters");
                        taskResult = createMethod.Invoke(_collectionManager, new object[] { formattedName, itemIds.ToArray() });
                    }
                    
                    if (taskResult != null)
                    {
                        // Await the task and extract the result
                        _logger.LogDebug("Awaiting task result");
                        await ((Task)taskResult).ConfigureAwait(false);
                        var resultProperty = taskResult.GetType().GetProperty("Result");
                        var boxSetResult = resultProperty?.GetValue(taskResult);
                        
                        _logger.LogDebug("Task completed, result type: {Type}", boxSetResult?.GetType().Name ?? "null");
                        
                        if (boxSetResult is BaseItem baseItem)
                        {
                            collectionId = baseItem.Id;
                            _logger.LogDebug("Collection created via ICollectionManager with ID: {CollectionId}", collectionId);
                            
                            // Add items to the collection using AddToCollectionAsync
                            if (itemIds.Count > 0)
                            {
                                _logger.LogDebug("Adding {Count} items to collection {CollectionId} using AddToCollectionAsync", itemIds.Count, collectionId);
                                var addMethod = _collectionManager.GetType().GetMethod("AddToCollectionAsync");
                                if (addMethod != null)
                                {
                                    var addTask = addMethod.Invoke(_collectionManager, new object[] { collectionId, itemIds.ToArray() });
                                    if (addTask != null)
                                    {
                                        await ((Task)addTask).ConfigureAwait(false);
                                        _logger.LogDebug("Successfully added items to collection via AddToCollectionAsync");
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning("AddToCollectionAsync method not found on ICollectionManager");
                                }
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException($"CreateCollectionAsync did not return a BaseItem, got: {boxSetResult?.GetType().Name ?? "null"}");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("CreateCollectionAsync returned null");
                    }
                }
                else
                {
                    // Fallback: Create BoxSet using reflection
                    _logger.LogDebug("ICollectionManager.CreateCollectionAsync not found, creating BoxSet via reflection");
                    var boxSetType = typeof(BaseItem).Assembly.GetType("MediaBrowser.Controller.Entities.BoxSet") 
                        ?? AppDomain.CurrentDomain.GetAssemblies()
                            .SelectMany(a => a.GetTypes())
                            .FirstOrDefault(t => t.Name == "BoxSet" && t.IsSubclassOf(typeof(BaseItem)));
                    
                    if (boxSetType != null)
                    {
                        var boxSet = Activator.CreateInstance(boxSetType);
                        if (boxSet != null)
                        {
                            var baseItem = (BaseItem)boxSet;
                            
                            // Set ID first - must be set before persisting
                            var newCollectionId = Guid.NewGuid();
                            boxSetType.GetProperty("Id")?.SetValue(boxSet, newCollectionId);
                            _logger.LogDebug("Generated new collection ID: {CollectionId}", newCollectionId);
                            
                            boxSetType.GetProperty("Name")?.SetValue(boxSet, formattedName);
                            boxSetType.GetProperty("LinkedChildren")?.SetValue(boxSet, linkedChildren);
                            
                            // Add to library manager - use CreateItemAsync if available, otherwise try UpdateToRepositoryAsync
                            var createItemMethod = _libraryManager.GetType().GetMethod("CreateItemAsync", new[] { typeof(BaseItem), typeof(CancellationToken) });
                            if (createItemMethod != null)
                            {
                                await ((Task)createItemMethod.Invoke(_libraryManager, new object[] { baseItem, cancellationToken })!).ConfigureAwait(false);
                            }
                            else
                            {
                                // Fallback: try UpdateToRepositoryAsync
                                await baseItem.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                            }
                            collectionId = baseItem.Id;
                            _logger.LogDebug("BoxSet created with ID: {CollectionId}", collectionId);
                        }
                        else
                        {
                            throw new InvalidOperationException("Failed to create BoxSet instance via reflection");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("BoxSet type not found - cannot create collection");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create collection via ICollectionManager, trying reflection-based BoxSet creation");
                // Fallback: Create BoxSet using reflection
                var boxSetType = typeof(BaseItem).Assembly.GetType("MediaBrowser.Controller.Entities.BoxSet") 
                    ?? AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => a.GetTypes())
                        .FirstOrDefault(t => t.Name == "BoxSet" && t.IsSubclassOf(typeof(BaseItem)));
                
                if (boxSetType != null)
                {
                    var boxSet = Activator.CreateInstance(boxSetType);
                    if (boxSet != null)
                    {
                        var baseItem = (BaseItem)boxSet;
                        
                        // Set ID first - must be set before persisting
                        var newCollectionId = Guid.NewGuid();
                        boxSetType.GetProperty("Id")?.SetValue(boxSet, newCollectionId);
                        _logger.LogDebug("Generated new collection ID: {CollectionId}", newCollectionId);
                        
                        boxSetType.GetProperty("Name")?.SetValue(boxSet, formattedName);
                        boxSetType.GetProperty("LinkedChildren")?.SetValue(boxSet, linkedChildren);
                        
                        // Add to library manager - use CreateItemAsync if available, otherwise try UpdateToRepositoryAsync
                        var createItemMethod = _libraryManager.GetType().GetMethod("CreateItemAsync", new[] { typeof(BaseItem), typeof(CancellationToken) });
                        if (createItemMethod != null)
                        {
                            await ((Task)createItemMethod.Invoke(_libraryManager, new object[] { baseItem, cancellationToken })!).ConfigureAwait(false);
                        }
                        else
                        {
                            // Fallback: try UpdateToRepositoryAsync
                            await baseItem.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                        }
                        collectionId = baseItem.Id;
                        _logger.LogDebug("BoxSet created with ID: {CollectionId}", collectionId);
                    }
                    else
                    {
                        throw new InvalidOperationException("Failed to create BoxSet instance via reflection");
                    }
                }
                else
                {
                    throw new InvalidOperationException("BoxSet type not found - cannot create collection");
                }
            }

            _logger.LogDebug("Collection creation result: ID = {CollectionId}", collectionId);

            var retrievedItem = _libraryManager.GetItemById(collectionId);
            if (retrievedItem != null && retrievedItem.GetBaseItemKind() == BaseItemKind.BoxSet)
            {
                _logger.LogDebug("Retrieved new collection: Name = {Name}", retrievedItem.Name);

                // Refresh metadata to generate cover images
                await RefreshCollectionMetadataAsync(retrievedItem, cancellationToken).ConfigureAwait(false);
                
                // Report the new collection to the library manager to trigger UI updates
                try
                {
                    _logger.LogDebug("Reporting new collection {CollectionId} to library manager for UI visibility", collectionId);
                    
                    // Try to use QueueLibraryScan if available to trigger a refresh
                    var queueScanMethod = _libraryManager.GetType().GetMethod("QueueLibraryScan");
                    if (queueScanMethod != null)
                    {
                        queueScanMethod.Invoke(_libraryManager, null);
                        _logger.LogDebug("Queued library scan after collection creation");
                    }
                    else
                    {
                        // Alternative: Force an update to trigger change detection
                        await retrievedItem.UpdateToRepositoryAsync(ItemUpdateType.MetadataImport, cancellationToken).ConfigureAwait(false);
                        _logger.LogDebug("Updated collection metadata to trigger UI refresh");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to trigger UI refresh for collection {CollectionId}", collectionId);
                }

                return collectionId.ToString();
            }
            else
            {
                _logger.LogWarning("Failed to retrieve newly created collection with ID {CollectionId}", collectionId);
                return string.Empty;
            }
        }

        private IEnumerable<BaseItem> GetAllMedia(List<string> mediaTypes, SmartCollectionDto? dto = null)
        {
            // Collections are server-wide, so we query all media
            // Use the first admin user (or first user) to query all items
            // Since collections are server-wide, we want all items regardless of user permissions
            
            var baseItemKinds = GetBaseItemKindsFromMediaTypes(mediaTypes, dto);
            
            // Get the first user to query all items
            // Since collections are server-wide, we want all items regardless of user permissions
            var queryUser = _userManager.Users.FirstOrDefault();
            
            if (queryUser == null)
            {
                _logger.LogWarning("No users found - cannot query media for collections");
                return [];
            }
            
            // Query all items using the admin/first user
            // This will return all items the user has access to, which for admin should be everything
            var query = new InternalItemsQuery(queryUser)
            {
                IncludeItemTypes = baseItemKinds,
                Recursive = true,
                IsVirtualItem = false
            };
            
            return _libraryManager.GetItemsResult(query).Items;
        }

        /// <summary>
        /// Maps string media types to BaseItemKind enums for API-level filtering
        /// </summary>
        private BaseItemKind[] GetBaseItemKindsFromMediaTypes(List<string>? mediaTypes, SmartCollectionDto? dto = null)
        {
            if (mediaTypes == null || mediaTypes.Count == 0)
            {
                _logger?.LogError("GetBaseItemKindsFromMediaTypes called with empty media types - this should have been caught by validation");
                throw new InvalidOperationException("No media types specified - this should have been caught by validation");
            }

            var baseItemKinds = new List<BaseItemKind>();

            foreach (var mediaType in mediaTypes)
            {
                if (Core.Constants.MediaTypes.MediaTypeToBaseItemKind.TryGetValue(mediaType, out var baseItemKind))
                {
                    baseItemKinds.Add(baseItemKind);
                }
                else
                {
                    _logger?.LogWarning("Unknown media type '{MediaType}' - skipping", mediaType);
                }
            }

            // Smart Query Expansion: If Episodes media type is selected AND Collections episode expansion is enabled,
            // also include Series in the query so we can find series in collections and expand them to episodes
            if (dto != null && baseItemKinds.Contains(BaseItemKind.Episode) && !baseItemKinds.Contains(BaseItemKind.Series))
            {
                var hasCollectionsEpisodeExpansion = dto.ExpressionSets?.Any(set =>
                    set.Expressions?.Any(expr =>
                        expr.MemberName == "Collections" && expr.IncludeEpisodesWithinSeries == true) == true) == true;

                if (hasCollectionsEpisodeExpansion)
                {
                    baseItemKinds.Add(BaseItemKind.Series);
                    _logger?.LogDebug("Auto-including Series in query for Episodes media type due to Collections episode expansion");
                }
            }

            if (baseItemKinds.Count == 0)
            {
                _logger?.LogError("No valid media types found after processing - this should have been caught by validation");
                throw new InvalidOperationException("No valid media types found - this should have been caught by validation");
            }

            return [.. baseItemKinds];
        }

        private async Task RefreshCollectionMetadataAsync(BaseItem collection, CancellationToken cancellationToken)
        {
            // Verify this is a BoxSet using BaseItemKind
            if (collection.GetBaseItemKind() != BaseItemKind.BoxSet)
            {
                _logger.LogWarning("Expected BoxSet but got {Type} (BaseItemKind: {Kind}) for collection {Name}", 
                    collection.GetType().Name, collection.GetBaseItemKind(), collection.Name);
                return;
            }
            
            // BoxSet properties are available on BaseItem
            var boxSet = collection;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var directoryService = new BasicDirectoryService();

                // Check if collection is empty using reflection to access LinkedChildren property
                var linkedChildrenProperty = collection.GetType().GetProperty("LinkedChildren");
                var linkedChildren = linkedChildrenProperty?.GetValue(collection) as LinkedChild[];
                
                if (linkedChildren == null || linkedChildren.Length == 0)
                {
                    _logger.LogDebug("Collection {CollectionName} is empty - clearing any existing cover images", collection.Name);

                    var clearOptions = new MetadataRefreshOptions(directoryService)
                    {
                        MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                        ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                        ReplaceAllImages = true,
                        ReplaceAllMetadata = true
                    };

                    await _providerManager.RefreshSingleItem(collection, clearOptions, cancellationToken).ConfigureAwait(false);

                    stopwatch.Stop();
                    _logger.LogDebug("Cover image clearing completed for empty collection {CollectionName} in {ElapsedTime}ms", collection.Name, stopwatch.ElapsedMilliseconds);
                    return;
                }

                _logger.LogDebug("Triggering metadata refresh for collection {CollectionName} to generate cover image", collection.Name);

                var refreshOptions = new MetadataRefreshOptions(directoryService)
                {
                    MetadataRefreshMode = MetadataRefreshMode.Default,
                    ImageRefreshMode = MetadataRefreshMode.Default,
                    ReplaceAllMetadata = true,
                    ReplaceAllImages = true
                };

                await _providerManager.RefreshSingleItem(collection, refreshOptions, cancellationToken).ConfigureAwait(false);

                stopwatch.Stop();
                _logger.LogDebug("Cover image generation completed for collection {CollectionName} in {ElapsedTime}ms", collection.Name, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogWarning(ex, "Failed to refresh metadata for collection {CollectionName} after {ElapsedTime}ms. Cover image may not be generated.", collection.Name, stopwatch.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Calculates the total runtime in minutes for all items in a collection.
        /// </summary>
        private static double? CalculateTotalRuntimeMinutes(Guid[] itemIds, Dictionary<Guid, BaseItem> mediaLookup, ILogger logger)
        {
            double totalMinutes = 0.0;
            int itemsWithRuntime = 0;

            foreach (var itemId in itemIds)
            {
                if (mediaLookup.TryGetValue(itemId, out var item))
                {
                    if (item.RunTimeTicks.HasValue)
                    {
                        var itemMinutes = TimeSpan.FromTicks(item.RunTimeTicks.Value).TotalMinutes;
                        totalMinutes += itemMinutes;
                        itemsWithRuntime++;
                    }
                }
            }

            if (itemsWithRuntime > 0)
            {
                return totalMinutes;
            }

            return null;
        }
    }

    /// <summary>
    /// Basic DirectoryService implementation for collection metadata refresh.
    /// </summary>
    public class BasicDirectoryService : IDirectoryService
    {
        public List<FileSystemMetadata> GetDirectories(string path) => [];
        public List<FileSystemMetadata> GetFiles(string path) => [];
        public FileSystemMetadata[] GetFileSystemEntries(string path) => [];
        public FileSystemMetadata? GetFile(string path) => null;
        public FileSystemMetadata? GetDirectory(string path) => null;
        public FileSystemMetadata? GetFileSystemEntry(string path) => null;
        public IReadOnlyList<string> GetFilePaths(string path) => [];
        public IReadOnlyList<string> GetFilePaths(string path, bool clearCache, bool sort) => [];
        public bool IsAccessible(string path) => false;
    }
}

