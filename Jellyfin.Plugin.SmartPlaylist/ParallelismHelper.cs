using System;
using Jellyfin.Plugin.SmartPlaylist.Configuration;

namespace Jellyfin.Plugin.SmartPlaylist
{
    /// <summary>
    /// Helper class for calculating parallel concurrency limits using Jellyfin's library scan logic.
    /// </summary>
    public static class ParallelismHelper
    {
        /// <summary>
        /// Calculates the parallel concurrency limit based on plugin configuration and system resources.
        /// Uses the same logic as Jellyfin's library scan fanout concurrency.
        /// </summary>
        /// <param name="config">Plugin configuration containing ParallelConcurrencyLimit setting.</param>
        /// <returns>
        /// 1 if sequential processing should be used,
        /// otherwise the number of parallel threads to use.
        /// </returns>
        public static int CalculateParallelConcurrency(PluginConfiguration config)
        {
            if (config == null)
            {
                return CalculateParallelConcurrency(0);
            }

            return CalculateParallelConcurrency(config.ParallelConcurrencyLimit);
        }

        /// <summary>
        /// Calculates the parallel concurrency limit based on the concurrency setting and system resources.
        /// Implements Jellyfin's exact library scan logic:
        /// - If setting == 1: Force sequential (1 thread)
        /// - If setting &lt;= 0 AND ProcessorCount &lt;= 3: Sequential (1 thread)  
        /// - If setting &lt;= 0 AND ProcessorCount &gt; 3: ProcessorCount - 3
        /// - Otherwise: Use the setting value
        /// </summary>
        /// <param name="concurrencySetting">
        /// The parallel concurrency limit setting:
        /// 0 or negative = Auto (uses ProcessorCount - 3 for 4+ cores, sequential for 1-3 cores)
        /// 1 = Force sequential processing (no parallelism)
        /// 2+ = Use specified number of parallel threads
        /// </param>
        /// <returns>The number of parallel threads to use.</returns>
        public static int CalculateParallelConcurrency(int concurrencySetting)
        {
            // Force sequential if explicitly set to 1
            if (concurrencySetting == 1)
            {
                return 1;
            }

            // Auto mode (setting <= 0)
            if (concurrencySetting <= 0)
            {
                // For systems with 3 or fewer cores, use sequential processing
                if (Environment.ProcessorCount <= 3)
                {
                    return 1;
                }

                // For systems with 4+ cores, use ProcessorCount - 3
                // This leaves 3 cores free for Jellyfin server and other operations
                return Math.Max(1, Environment.ProcessorCount - 3);
            }

            // Use explicit user setting (2 or more)
            return concurrencySetting;
        }

        /// <summary>
        /// Determines if sequential operation should be forced based on the concurrency setting.
        /// Implements Jellyfin's ShouldForceSequentialOperation logic.
        /// </summary>
        /// <param name="concurrencySetting">The parallel concurrency limit setting.</param>
        /// <returns>True if sequential processing should be used, false otherwise.</returns>
        public static bool ShouldForceSequential(int concurrencySetting)
        {
            // Force sequential if set to 1 OR (unset/auto AND cores <= 3)
            return concurrencySetting == 1 || (concurrencySetting <= 0 && Environment.ProcessorCount <= 3);
        }
    }
}

