using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Jellyfin.Plugin.SmartLists.Core.Enums;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartLists.Services.Shared
{
    /// <summary>
    /// Represents an ongoing refresh operation
    /// </summary>
    public class RefreshOperation
    {
        public string ListId { get; set; } = string.Empty;
        public string ListName { get; set; } = string.Empty;
        public SmartListType ListType { get; set; }
        public RefreshTriggerType TriggerType { get; set; }
        public DateTime StartTime { get; set; }
        public int TotalItems { get; set; }
        public int ProcessedItems { get; set; }
        public TimeSpan? EstimatedTimeRemaining { get; set; }
        public string? ErrorMessage { get; set; }
        public int? BatchCurrentIndex { get; set; }
        public int? BatchTotalCount { get; set; }
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public TimeSpan ElapsedTime => _stopwatch.Elapsed;

        /// <summary>
        /// Updates the processed items count and recalculates estimated time remaining
        /// </summary>
        public void UpdateProgress(int processedItems, int totalItems)
        {
            ProcessedItems = processedItems;
            TotalItems = totalItems;

            if (processedItems > 0 && TotalItems > 0)
            {
                var elapsedMs = _stopwatch.ElapsedMilliseconds;
                
                // Guard against very small elapsed times to avoid infinity/NaN
                if (elapsedMs <= 0)
                {
                    EstimatedTimeRemaining = null;
                    return;
                }
                
                var itemsPerMs = (double)processedItems / elapsedMs;
                var remainingItems = TotalItems - processedItems;

                // Reset ETA if no items remaining or invalid rate
                if (remainingItems <= 0 || itemsPerMs <= 0)
                {
                    EstimatedTimeRemaining = null;
                }
                else if (itemsPerMs > 0)
                {
                    var estimatedMs = remainingItems / itemsPerMs;
                    EstimatedTimeRemaining = TimeSpan.FromMilliseconds(estimatedMs);
                }
            }
        }
    }

    /// <summary>
    /// Represents a completed refresh operation in history
    /// </summary>
    public class RefreshHistoryEntry
    {
        public string ListId { get; set; } = string.Empty;
        public string ListName { get; set; } = string.Empty;
        public SmartListType ListType { get; set; }
        public RefreshTriggerType TriggerType { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Service for tracking refresh operation status and history
    /// </summary>
    public class RefreshStatusService
    {
        private readonly ILogger<RefreshStatusService> _logger;
        private readonly ConcurrentDictionary<string, RefreshOperation> _ongoingOperations = new();
        private readonly ConcurrentDictionary<string, RefreshHistoryEntry> _refreshHistory = new();

        public RefreshStatusService(ILogger<RefreshStatusService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Starts tracking a new refresh operation
        /// </summary>
        public void StartOperation(
            string listId,
            string listName,
            SmartListType listType,
            RefreshTriggerType triggerType,
            int totalItems = 0,
            int? batchCurrentIndex = null,
            int? batchTotalCount = null)
        {
            var operation = new RefreshOperation
            {
                ListId = listId,
                ListName = listName,
                ListType = listType,
                TriggerType = triggerType,
                StartTime = DateTime.UtcNow,
                TotalItems = totalItems,
                ProcessedItems = 0,
                BatchCurrentIndex = batchCurrentIndex,
                BatchTotalCount = batchTotalCount
            };

            _ongoingOperations.AddOrUpdate(listId, operation, (key, existing) =>
            {
                _logger.LogWarning("Starting new operation for list {ListId} ({ListName}) that already has an ongoing operation. Existing: Started at {ExistingStart}, {ExistingProcessed}/{ExistingTotal} items. Replacing with new operation.", 
                    listId, listName, existing.StartTime, existing.ProcessedItems, existing.TotalItems);
                return operation;
            });

            _logger.LogDebug("Started tracking refresh operation for list {ListId} ({ListName})" + 
                (batchCurrentIndex.HasValue && batchTotalCount.HasValue 
                    ? $" - Batch {batchCurrentIndex.Value} of {batchTotalCount.Value}" 
                    : ""), 
                listId, listName);
        }

        /// <summary>
        /// Updates the batch index for an ongoing operation
        /// </summary>
        public void UpdateBatchIndex(string listId, int batchCurrentIndex)
        {
            if (_ongoingOperations.TryGetValue(listId, out var operation))
            {
                operation.BatchCurrentIndex = batchCurrentIndex;
            }
        }

        /// <summary>
        /// Updates the batch index for all ongoing operations in a batch (same total count)
        /// </summary>
        public void UpdateBatchIndexForAll(int batchCurrentIndex, int batchTotalCount)
        {
            foreach (var operation in _ongoingOperations.Values)
            {
                if (operation.BatchTotalCount == batchTotalCount)
                {
                    operation.BatchCurrentIndex = batchCurrentIndex;
                }
            }
        }

        /// <summary>
        /// Updates progress for an ongoing operation
        /// </summary>
        public void UpdateProgress(string listId, int processedItems, int totalItems = 0)
        {
            if (_ongoingOperations.TryGetValue(listId, out var operation))
            {
                if (totalItems > 0)
                {
                    operation.TotalItems = totalItems;
                }

                operation.UpdateProgress(processedItems, operation.TotalItems);
            }
        }

        /// <summary>
        /// Completes an operation and moves it to history
        /// </summary>
        public void CompleteOperation(
            string listId,
            bool success,
            string? errorMessage = null)
        {
            if (_ongoingOperations.TryRemove(listId, out var operation))
            {
                var historyEntry = new RefreshHistoryEntry
                {
                    ListId = operation.ListId,
                    ListName = operation.ListName,
                    ListType = operation.ListType,
                    TriggerType = operation.TriggerType,
                    StartTime = operation.StartTime,
                    EndTime = DateTime.UtcNow,
                    Duration = operation.ElapsedTime,
                    Success = success,
                    ErrorMessage = errorMessage ?? operation.ErrorMessage
                };

                _refreshHistory.AddOrUpdate(listId, historyEntry, (key, existing) => historyEntry);

                _logger.LogDebug("Completed refresh operation for list {ListId} ({ListName}): Success={Success}, Duration={Duration}ms",
                    listId, operation.ListName, success, operation.ElapsedTime.TotalMilliseconds);
            }
            else
            {
                _logger.LogWarning("Attempted to complete operation for list {ListId} that was not being tracked", listId);
            }
        }

        /// <summary>
        /// Marks an operation as failed
        /// </summary>
        public void FailOperation(string listId, string errorMessage)
        {
            if (_ongoingOperations.TryGetValue(listId, out var operation))
            {
                operation.ErrorMessage = errorMessage;
            }
        }

        /// <summary>
        /// Gets all ongoing operations (returns copies to prevent external mutation)
        /// Note: ElapsedTime is calculated from the original Stopwatch, so copies will show the same elapsed time
        /// </summary>
        public List<RefreshOperation> GetOngoingOperations()
        {
            // Create copies to prevent external mutation of tracked state
            // Note: RefreshOperation has a private Stopwatch, so we create new instances with copied properties
            // The ElapsedTime property will be calculated from a new Stopwatch, but that's acceptable for read-only views
            return _ongoingOperations.Values.Select(op =>
            {
                var copy = new RefreshOperation
                {
                    ListId = op.ListId,
                    ListName = op.ListName,
                    ListType = op.ListType,
                    TriggerType = op.TriggerType,
                    StartTime = op.StartTime,
                    TotalItems = op.TotalItems,
                    ProcessedItems = op.ProcessedItems,
                    EstimatedTimeRemaining = op.EstimatedTimeRemaining,
                    ErrorMessage = op.ErrorMessage,
                    BatchCurrentIndex = op.BatchCurrentIndex,
                    BatchTotalCount = op.BatchTotalCount
                };
                // Update progress to sync the internal state (this will recalculate EstimatedTimeRemaining)
                copy.UpdateProgress(op.ProcessedItems, op.TotalItems);
                return copy;
            }).ToList();
        }

        /// <summary>
        /// Gets refresh history (last refresh per list) (returns copies to prevent external mutation)
        /// </summary>
        public List<RefreshHistoryEntry> GetRefreshHistory()
        {
            return _refreshHistory.Values.Select(entry => new RefreshHistoryEntry
            {
                ListId = entry.ListId,
                ListName = entry.ListName,
                ListType = entry.ListType,
                TriggerType = entry.TriggerType,
                StartTime = entry.StartTime,
                EndTime = entry.EndTime,
                Duration = entry.Duration,
                Success = entry.Success,
                ErrorMessage = entry.ErrorMessage
            }).ToList();
        }

        /// <summary>
        /// Gets refresh history for a specific list
        /// </summary>
        public RefreshHistoryEntry? GetListHistory(string listId)
        {
            return _refreshHistory.TryGetValue(listId, out var entry) ? entry : null;
        }

        /// <summary>
        /// Gets statistics about refresh operations
        /// </summary>
        public RefreshStatistics GetStatistics()
        {
            var history = _refreshHistory.Values.ToList();
            var ongoing = _ongoingOperations.Values.ToList();

            return new RefreshStatistics
            {
                TotalLists = history.Count,
                OngoingOperationsCount = ongoing.Count,
                LastRefreshTime = history.OrderByDescending(h => h.EndTime ?? h.StartTime).FirstOrDefault()?.EndTime,
                AverageRefreshDuration = history.Any() 
                    ? TimeSpan.FromMilliseconds(history.Average(h => h.Duration.TotalMilliseconds))
                    : null,
                SuccessfulRefreshes = history.Count(h => h.Success),
                FailedRefreshes = history.Count(h => !h.Success)
            };
        }

        /// <summary>
        /// Checks if a list has an ongoing operation
        /// </summary>
        public bool HasOngoingOperation(string listId)
        {
            return _ongoingOperations.ContainsKey(listId);
        }
    }

    /// <summary>
    /// Statistics about refresh operations
    /// </summary>
    public class RefreshStatistics
    {
        public int TotalLists { get; set; }
        public int OngoingOperationsCount { get; set; }
        public DateTime? LastRefreshTime { get; set; }
        public TimeSpan? AverageRefreshDuration { get; set; }
        public int SuccessfulRefreshes { get; set; }
        public int FailedRefreshes { get; set; }
    }
}

