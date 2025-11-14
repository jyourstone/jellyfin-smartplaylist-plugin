# Configuration

SmartLists features a modern web-based configuration interface through the plugin settings page!

<div align="center" style="display: flex; justify-content: center; gap: 10px; flex-wrap: wrap;">
    <a href="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/main/images/config_page_create.png" target="_blank" style="cursor: pointer;">
        <img alt="Create list page" src="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/main/images/config_page_create.png" width="240"/>
    </a>
    <a href="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/main/images/config_page_manage.png" target="_blank" style="cursor: pointer;">
        <img alt="Manage lists page" src="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/main/images/config_page_manage.png" width="240"/>
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
- **Use cases**: Organizing related content for browsing (e.g., "Action Movies", "Holiday Collection", "Director's Collection")

!!! note "User Selection for Collections"
    When creating a collection, the user you select is used as a **reference** for rule evaluation, not as an owner. The collection itself is server-wide and visible to everyone. This user's context is important for:
    - Evaluating user-specific rules (Is Played, Is Favorite, Play Count, etc.)
    - Respecting library access permissions
    - Filtering items based on what that user can see and access

## Web Interface Overview

The web interface is organized into three tabs:

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

### 3. Settings

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

## Manual Configuration (Advanced Users)

For advanced users who prefer JSON configuration, see the [Advanced Configuration](advanced-configuration.md) guide for details about manual file editing.