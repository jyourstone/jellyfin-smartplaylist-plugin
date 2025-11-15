# Configuration

SmartLists features a modern web-based configuration interface through the plugin settings page!

<div align="center" style="display: flex; justify-content: center; gap: 10px; flex-wrap: wrap;">
    <a href="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/main/images/config_page_create.png" target="_blank" style="cursor: pointer;">
        <img alt="Create list page" src="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/main/images/config_page_create.png" width="240"/>
    </a>
    <a href="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/main/images/config_page_manage.png" target="_blank" style="cursor: pointer;">
        <img alt="Manage lists page" src="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/main/images/config_page_manage.png" width="240"/>
    </a>
    <a href="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/main/images/config_page_status.png" target="_blank" style="cursor: pointer;">
        <img alt="Status page" src="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/main/images/config_page_status.png" width="240"/>
    </a>
    <a href="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/main/images/config_page_settings.png" target="_blank" style="cursor: pointer;">
        <img alt="Settings page" src="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/main/images/config_page_settings.png" width="240"/>
    </a>
</div>

## Playlists vs Collections

Before creating your first list, it's important to understand the differences between **Playlists** and **Collections**:

### Playlists
- **User-specific**: Each playlist belongs to a specific user (the "owner")
- **Sorting**: Items can be sorted using multiple sorting levels (see [Sorting and Limits](sorting-and-limits.md))
- **Max Playtime**: Can set a maximum playtime limit
- **Visibility**: Can be set as public (visible to all users) or private (visible only to the owner)
- **Use cases**: Personal music playlists, "Continue Watching" lists, workout mixes, etc.

### Collections
- **Server-wide**: Collections are visible to all users on the server (no individual ownership)
- **No Sorting**: Collections do not support custom sorting
- **No Max Playtime**: Collections cannot have a playtime limit
- **User Reference**: While collections don't have an "owner" in the traditional sense, you must select a user whose context will be used when evaluating rules and filtering items. This user's library access permissions and user-specific data (like "Is Played", "Is Favorite", etc.) are used to determine which items are included in the collection
- **Automatic Image Generation**: Collections automatically generate cover images based on the media items they contain (see details below)
- **Use cases**: Organizing related content for browsing (e.g., "Action Movies", "Holiday Collection", "Director's Collection")

#### Automatic Image Generation for Collections

SmartLists automatically generates cover images for collections based on the media items they contain. This feature works as follows:

- **Single Item**: If a collection contains only one item with an image, that item's primary image is used directly as the collection cover
- **Multiple Items**: If a collection contains two or more items with images, a 4-image collage is automatically created using the first items from the collection
- **Image Selection**: The plugin prioritizes Movies and Series with images, falling back to any items with images if needed
- **Automatic Updates**: Collection images are automatically regenerated when the collection is refreshed to reflect the current items

!!! important "Custom Images Are Preserved"
    Automatic image generation **only occurs** when a collection doesn't already have a custom image in place. Custom images can be set through:
    - **Metadata cover downloads**: Images downloaded by Jellyfin's metadata providers
    - **User image uploads**: Images manually uploaded through Jellyfin's interface
    
    If a custom image exists, the plugin will preserve it and skip automatic generation. This ensures that any images you specifically set or download are never overwritten.

!!! note "User Selection for Collections"
    When creating a collection, the user you select is used as a **reference** for rule evaluation, not as an owner. The collection itself is server-wide and visible to everyone. This user's context is important for:
    - Evaluating user-specific rules (Is Played, Is Favorite, Play Count, etc.)
    - Respecting library access permissions
    - Filtering items based on what that user can see and access

## Web Interface Overview

The web interface is organized into four tabs:

### 1. Create List

This is where you build new playlists and collections:

- Choose whether to create a Playlist or Collection
- Define the rules for including items
- Choose the sort order (playlists only - collections don't support sorting)
- Select which user should own the list (for playlists) or serve as reference user (for collections)
- Set the maximum number of items
- Set the maximum playtime for the list (playlists only)
- Decide if the list should be public or private (playlists only - collections are always server-wide)
- Choose whether or not to enable the list
- Configure auto-refresh behavior (Never, On Library Changes, On All Changes)
- Set custom refresh schedule (Daily, Weekly, Monthly, Yearly, Interval or No schedule)

### 2. Manage Lists

View and edit all of your existing smart playlists and collections:

- **Organized Interface**: Clean, modern layout with grouped actions and filters
- **Advanced Filtering**: Filter by list type, media type, visibility and user
- **Real-time Search**: Search all properties in real-time
- **Flexible Sorting**: Sort by name, list creation date, last refreshed, or enabled status
- **Bulk Operations**: Select multiple lists to enable, disable, or delete them simultaneously
- **Detailed View**: Expand lists to see rules, settings, creation date, and other properties
- **Quick Actions**: Edit, clone, refresh, or delete individual lists with confirmation dialogs
- **Smart Selection**: Select all, expand all, or clear selections with intuitive controls

### 3. Status

Monitor refresh operations and view statistics:

- **Ongoing Operations**: View all currently running refresh operations with real-time progress
  - See which lists are being refreshed
  - Monitor progress with progress bars showing items processed vs. total items
  - View estimated time remaining for each operation
  - Track elapsed time and trigger type (Manual, Auto, or Scheduled)
- **Statistics**: View refresh statistics since the last server restart
  - Total number of lists tracked
  - Number of ongoing operations
  - Last refresh time across all lists
  - Average refresh duration
  - Count of successful and failed refreshes
- **Refresh History**: View the last refresh for each list
  - See when each list was last refreshed
  - View refresh duration and item counts
  - Check success/failure status
  - See which trigger type initiated each refresh

!!! note "Statistics Scope"
    Statistics and refresh history are tracked in-memory and reset when the Jellyfin server is restarted. Historical data is not persisted across server restarts.

!!! tip "Auto-Refresh"
    The status page automatically refreshes every 2 seconds when operations are active, and every 30 seconds when idle. You can also manually refresh using the "Refresh" button at the top of the page.

!!! tip "Quick Access"
    When you click "Refresh All Lists" in the Settings tab, you'll be automatically redirected to the Status page to monitor the progress of all refresh operations in real-time.

### 4. Settings

Configure global settings for the plugin:

- Set the default sort order for new lists
- Set the default max items and max playtime for new lists
- Configure custom prefix and suffix for list names
- Set the default auto-refresh mode for new lists
- Set the default custom schedule settings for new lists
- Configure performance settings (parallel concurrency limit)
- Export all lists to a ZIP file for backup or transfer
- Import lists from a ZIP file with duplicate detection
- Manually trigger a refresh for all smart lists

## Flexible Deletion Options

When deleting a smart list, you can choose whether to also delete the corresponding Jellyfin playlist or collection:

- **Delete both (default)**: Removes both the smart list configuration and the Jellyfin playlist/collection
- **Delete configuration only**: Keeps the Jellyfin playlist/collection and removes the custom prefix/suffix (if any), making it a regular manually managed list

This is useful when you want to populate a list automatically once, then manage it manually.

## Custom List Naming

You can customize how smart list names appear in Jellyfin by configuring a prefix and/or suffix in the Settings tab:

- **Prefix**: Text added before the list name (e.g., "My " → "My Action Movies")
- **Suffix**: Text added after the list name (e.g., " - Smart" → "Action Movies - Smart")
- **Both**: Use both prefix and suffix (e.g., "My " + " - Smart" → "My Action Movies - Smart")
- **None**: Leave both empty for no prefix/suffix

The naming configuration applies to all new smart lists. When you delete a smart list but keep the Jellyfin playlist/collection, the custom prefix/suffix will be automatically removed.

## Export & Import

The Export/Import feature allows you to backup your smart list configurations or transfer them between different Jellyfin instances:

### Export

- Click the "Export All Lists" button in the Settings tab
- Downloads a timestamped ZIP file containing all your smart list JSON configurations
- Use this as a backup or to transfer your lists to another Jellyfin server

### Import

- Select a ZIP file exported from the SmartLists plugin
- Click "Import Selected File" to upload and process the archive
- **Duplicate Detection**: Lists with the same GUID as existing lists will be automatically skipped to prevent conflicts
- **User Reassignment**: When importing lists from another Jellyfin instance, if the original list owner doesn't exist in the destination system, the list will be automatically reassigned to the admin user performing the import

!!! note "User-Specific Rules"
    Rules like "Is Played by [User]" or "Is Favorite for [User]" that reference non-existent users will need to be updated manually.

## Performance Settings

### Parallel Concurrency Limit

Control how many threads are used during individual list refreshes:

- **Auto-detect (default)**: Leave empty or set to `0` to automatically detect the optimal number of threads based on your CPU cores
- **Custom limit**: Set a specific number to limit parallel processing within a list refresh (e.g., `4` for 4 threads processing items simultaneously)
- **Disable parallelism**: Set to `1` to process list items sequentially (one at a time, useful for troubleshooting or low-resource systems)

!!! tip "When to Adjust"
    - **Increase** if you have large lists and a powerful multi-core CPU
    - **Decrease** if you experience high CPU usage or system slowdowns during list refreshes
    - **Set to 1** if you need to debug list refresh issues or have very limited system resources

### Processing Batch Size

Control how many items are processed in each batch during list refreshes:

- **Default**: `300` items per batch
- **Recommended**: `200-500` for libraries with 1,000-20,000 items
- **Smaller batches** (100-200): Provide more frequent progress updates on the Status page, useful for monitoring refresh progress in real-time
- **Larger batches** (400-500): Improve processing efficiency and reduce overhead, better for very large libraries

**How it works:**
- Items are processed sequentially in batches (one batch at a time)
- Within each batch, items are processed in parallel using multiple threads (controlled by Parallel Concurrency Limit)
- Progress is reported after each batch completes, so smaller batches = more frequent updates
- The batch size affects both processing efficiency and the granularity of progress reporting on the Status page

!!! tip "When to Adjust"
    - **Decrease** (100-200) if you want more frequent progress updates on the Status page
    - **Increase** (400-500) if you have very large libraries (10,000+ items) and want maximum processing efficiency
    - **Default (300)** is optimal for most use cases, providing a good balance between efficiency and progress reporting

## Manual Configuration (Advanced Users)

For advanced users who prefer JSON configuration, see the [Advanced Configuration](advanced-configuration.md) guide for details about manual file editing.

!!! tip "Dashboard Theme Recommendation"
    This plugin is best used with the **Dark** dashboard theme in Jellyfin. The plugin's custom styling mimics the dark theme, providing the best visual experience and consistency with the Jellyfin interface.