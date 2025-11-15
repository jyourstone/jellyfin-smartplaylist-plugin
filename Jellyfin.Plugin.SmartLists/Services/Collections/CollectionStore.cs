using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartLists.Core.Models;
using Jellyfin.Plugin.SmartLists.Services.Abstractions;
using Jellyfin.Plugin.SmartLists.Services.Shared;

namespace Jellyfin.Plugin.SmartLists.Services.Collections
{
    /// <summary>
    /// Store implementation for smart collections
    /// Handles JSON serialization/deserialization with type discrimination
    /// </summary>
    public class CollectionStore : ISmartListStore<SmartCollectionDto>
    {
        private readonly ISmartListFileSystem _fileSystem;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            // Support polymorphic deserialization based on Type field
            Converters = { new JsonStringEnumConverter() }
        };

        public CollectionStore(ISmartListFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
        }

        public async Task<SmartCollectionDto?> GetByIdAsync(Guid id)
        {
            // Validate GUID format to prevent path injection
            if (id == Guid.Empty)
            {
                return null;
            }

            // Try direct file lookup first (O(1) operation)
            var filePath = _fileSystem.GetSmartListFilePath(id.ToString());
            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    var collection = await LoadCollectionAsync(filePath).ConfigureAwait(false);
                    if (collection != null && collection.Type == Core.Enums.SmartListType.Collection)
                    {
                        return collection;
                    }
                }
                catch
                {
                    // File exists but couldn't be loaded, fall back to scanning all files
                }
            }

            // Fallback: scan all collections if direct lookup failed
            // Use case-insensitive comparison to handle GUID casing differences
            var allCollections = await GetAllAsync().ConfigureAwait(false);
            return allCollections.FirstOrDefault(c => string.Equals(c.Id, id.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        public async Task<SmartCollectionDto[]> GetAllAsync()
        {
            // Use shared helper to read files once
            var (_, collections) = await _fileSystem.GetAllSmartListsAsync().ConfigureAwait(false);
            return collections;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "Collection ID is validated as GUID before use in file paths, preventing path injection")]
        public async Task<SmartCollectionDto> SaveAsync(SmartCollectionDto smartCollection)
        {
            ArgumentNullException.ThrowIfNull(smartCollection);

            // Ensure type is set
            smartCollection.Type = Core.Enums.SmartListType.Collection;

            // Validate ID is a valid GUID to prevent path injection
            if (string.IsNullOrWhiteSpace(smartCollection.Id) || !Guid.TryParse(smartCollection.Id, out var parsedId) || parsedId == Guid.Empty)
            {
                throw new ArgumentException("Collection ID must be a valid non-empty GUID", nameof(smartCollection));
            }

            // Normalize ID to canonical GUID string for consistent file lookups
            smartCollection.Id = parsedId.ToString();
            var fileName = smartCollection.Id;
            smartCollection.FileName = $"{fileName}.json";

            var filePath = _fileSystem.GetSmartListPath(fileName);
            var tempPath = filePath + ".tmp";

            // Check if this collection exists in the legacy directory (for migration)
            var legacyPath = _fileSystem.GetLegacyPath(fileName);
            bool existsInLegacy = File.Exists(legacyPath);

            try
            {
                await using (var writer = File.Create(tempPath))
                {
                    await JsonSerializer.SerializeAsync(writer, smartCollection, JsonOptions).ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                }

                if (File.Exists(filePath))
                {
                    // Replace is atomic on the same volume
                    File.Replace(tempPath, filePath, null);
                }
                else
                {
                    File.Move(tempPath, filePath);
                }

                // After successfully saving to new location, delete legacy file if it exists
                // This migrates the collection from old directory to new directory
                if (existsInLegacy)
                {
                    try
                    {
                        File.Delete(legacyPath);
                    }
                    catch (Exception ex)
                    {
                        // Log but don't fail the save operation if legacy deletion fails
                        // The file will be in both locations, but the new location takes precedence
                        System.Diagnostics.Debug.WriteLine($"Warning: Failed to delete legacy collection file {legacyPath}: {ex.Message}");
                    }
                }
            }
            finally
            {
                // Clean up temp file if it still exists
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* ignore cleanup errors */ }
            }

            return smartCollection;
        }

        public async Task DeleteAsync(Guid id)
        {
            var collection = await GetByIdAsync(id).ConfigureAwait(false);
            if (collection == null)
                return;

            // Use the actual filename to construct the path
            var fileName = string.IsNullOrWhiteSpace(collection.FileName)
                ? collection.Id
                : Path.GetFileNameWithoutExtension(collection.FileName);

            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("Collection ID cannot be null or empty", nameof(id));
            }

            var filePath = _fileSystem.GetSmartListPath(fileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            // Also check legacy directory
            var legacyPath = _fileSystem.GetLegacyPath(fileName);
            if (File.Exists(legacyPath))
            {
                File.Delete(legacyPath);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "File path is validated upstream - only valid GUIDs are passed to this method")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Method is part of instance interface implementation")]
        private async Task<SmartCollectionDto?> LoadCollectionAsync(string filePath)
        {
            await using var reader = File.OpenRead(filePath);
            var dto = await JsonSerializer.DeserializeAsync<SmartCollectionDto>(reader, JsonOptions).ConfigureAwait(false);

            // Only return collections - if this is a playlist or has wrong type, return null
            if (dto == null)
            {
                return null;
            }

            // If type is explicitly set to Playlist, this is not a collection - return null
            if (dto.Type == Core.Enums.SmartListType.Playlist)
            {
                return null;
            }

            // If type is not set (default/legacy), we need to check if it's actually a collection
            // For legacy files without Type, we can't determine if it's a collection or playlist
            // So we return null to avoid duplicates (let PlaylistStore handle legacy files)
            if (dto.Type == 0) // Default enum value
            {
                return null;
            }

            // Ensure type is set correctly for collections
            if (dto.Type != Core.Enums.SmartListType.Collection)
            {
                dto.Type = Core.Enums.SmartListType.Collection;
            }

            return dto;
        }
    }
}

