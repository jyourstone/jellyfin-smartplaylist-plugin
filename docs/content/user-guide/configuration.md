# Configuration

SmartPlaylist features a modern web-based configuration interface through the plugin settings page! No more manual JSON editing required.

<div align="center">
    <p>
        <img alt="Create playlist page" src="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/main/images/config_page_create.png" width="270" style="margin-right: 10px;"/>
        <img alt="Manage playlists page" src="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/main/images/config_page_manage.png" width="270" style="margin-right: 10px;"/>
        <img alt="Settings page" src="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/main/images/config_page_settings.png" width="270"/>
    </p>
</div>

## Web Interface Overview

The web interface is organized into three tabs:

### 1. Create Playlist

This is where you build new playlists:

- Define the rules for including items
- Choose the sort order
- Select which user should own the playlist
- Set the maximum number of items (defaults to 500)
- Set the maximum play time for the playlist (defaults to unlimited)
- Decide if the playlist should be public or private
- Choose whether or not to enable the playlist
- Configure auto-refresh behavior (Never, On Library Changes, On All Changes)
- Set custom refresh schedule (Daily, Weekly, Monthly, Interval or No schedule)

### 2. Manage Playlists

View and edit all of your existing smart playlists:

- **Organized Interface**: Clean, modern layout with grouped actions and filters
- **Advanced Filtering**: Filter by media type, visibility and user
- **Real-time Search**: Search all properties in real-time
- **Flexible Sorting**: Sort by name, playlist creation date, last refreshed, or enabled status
- **Bulk Operations**: Select multiple playlists to enable, disable, or delete them simultaneously
- **Detailed View**: Expand playlists to see rules, settings, creation date, and other properties
- **Quick Actions**: Edit, clone, refresh, or delete individual playlists with confirmation dialogs
- **Smart Selection**: Select all, expand all, or clear selections with intuitive controls

### 3. Settings

Configure global settings for the plugin:

- Set the default sort order for new playlists
- Set the default max items and max play time for new playlists
- Configure custom prefix and suffix for playlist names
- Set the default auto-refresh mode for new playlists
- Set the default custom schedule settings for new playlists
- Export all playlists to a ZIP file for backup or transfer
- Import playlists from a ZIP file with duplicate detection
- Manually trigger a refresh for all smart playlists

## Flexible Deletion Options

When deleting a smart playlist, you can choose whether to also delete the corresponding Jellyfin playlist:

- **Delete both (default)**: Removes both the smart playlist configuration and the Jellyfin playlist
- **Delete configuration only**: Keeps the Jellyfin playlist and removes the custom prefix/suffix (if any), making it a regular manually managed playlist

This is useful when you want to populate a playlist automatically once, then manage it manually.

## Custom Playlist Naming

You can customize how smart playlist names appear in Jellyfin by configuring a prefix and/or suffix in the Settings tab:

- **Prefix**: Text added before the playlist name (e.g., "My " → "My Action Movies")
- **Suffix**: Text added after the playlist name (e.g., " - Smart" → "Action Movies - Smart")
- **Both**: Use both prefix and suffix (e.g., "My " + " - Smart" → "My Action Movies - Smart")
- **None**: Leave both empty for no prefix/suffix

The naming configuration applies to all new smart playlists. When you delete a smart playlist but keep the Jellyfin playlist, the custom prefix/suffix will be automatically removed.

## Export & Import

The Export/Import feature allows you to backup your smart playlist configurations or transfer them between different Jellyfin instances:

### Export

- Click the "Export All Playlists" button in the Settings tab
- Downloads a timestamped ZIP file containing all your smart playlist JSON configurations
- Use this as a backup or to transfer your playlists to another Jellyfin server

### Import

- Select a ZIP file exported from the SmartPlaylist plugin
- Click "Import Selected File" to upload and process the archive
- **Duplicate Detection**: Playlists with the same GUID as existing playlists will be automatically skipped to prevent conflicts
- **User Reassignment**: When importing playlists from another Jellyfin instance, if the original playlist owner doesn't exist in the destination system, the playlist will be automatically reassigned to the admin user performing the import

!!! note "User-Specific Rules"
    Rules like "Is Played by [User]" or "Is Favorite for [User]" that reference non-existent users will need to be updated manually.

## Manual Configuration (Advanced Users)

For advanced users who prefer JSON configuration, see the [Advanced Configuration](advanced-configuration.md) guide for details about manual file editing.

