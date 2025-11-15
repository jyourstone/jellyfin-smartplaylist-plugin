using System;
using System.Reflection;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartLists.Utilities
{
    /// <summary>
    /// Utility class for common ILibraryManager operations using reflection.
    /// </summary>
    public static class LibraryManagerHelper
    {
        /// <summary>
        /// Triggers a library scan using reflection to call QueueLibraryScan if available.
        /// </summary>
        /// <param name="libraryManager">The library manager instance</param>
        /// <param name="logger">Optional logger for diagnostics</param>
        public static void QueueLibraryScan(ILibraryManager libraryManager, ILogger? logger = null)
        {
            ArgumentNullException.ThrowIfNull(libraryManager);
            
            try
            {
                logger?.LogDebug("Triggering library scan");
                var queueScanMethod = libraryManager.GetType().GetMethod("QueueLibraryScan");
                if (queueScanMethod != null)
                {
                    queueScanMethod.Invoke(libraryManager, null);
                    logger?.LogDebug("Queued library scan");
                }
                else
                {
                    logger?.LogWarning("QueueLibraryScan method not found on ILibraryManager");
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to trigger library scan");
            }
        }
    }
}

