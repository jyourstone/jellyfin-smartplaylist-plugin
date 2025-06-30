# Jellyfin SmartPlaylist Plugin

<div align="center">
    <p>
        <img alt="Logo" src="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/master/images/logo.jpg" height="300"/>
    </p>
</div>

A rebuilt and modernized plugin to create smart, rule-based playlists in Jellyfin.

This plugin allows you to create playlists based on a set of rules, which will automatically update as your library changes.

## ✨ Features

- 🚀 **Modern Jellyfin Support** - Built for newer Jellyfin versions with improved compatibility.
- 🎨 **Modern Web Interface** - A full-featured UI to create, view and delete smart playlists.
- 👥 **User-Aware Playlists** - Playlists are created for your user by default, with a simple option to make them public for everyone.
- 🎯 **Flexible Rules** - Build simple or complex rules with an intuitive builder. 
- 🔄 **Automatic Updates** - Playlists refresh automatically (scheduled task).
- ⚙️ **Settings** - Configure default settings and trigger a manual refresh for all playlists at any time.
- 🛠️ **Advanced Options** - Support for regex patterns, date ranges, and more.
- 🎵 **All Media Types** - Works with movies, TV shows and music

## Configuration

SmartPlaylist now features a modern web-based configuration interface through the plugin settings page! No more manual JSON editing required.

<div align="center">
    <p>
        <img alt="Settings page" src="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/master/images/config_page.png" height="500"/>
    </p>
</div>

### Using the Web Interface

The web interface is organized into three tabs:

1.  **Create Playlist**: This is where you build new playlists.
    -   Define the rules for including items.
    -   Choose the sort order.
    -   Decide if the playlist should be public or private to your user.
2.  **Manage Playlists**: View all of your existing smart playlists.
    -   See the rules, sorting, and other details for each playlist.
    -   Delete playlists you no longer need.
3.  **Settings**: Configure global settings for the plugin.
    -   Set the default sort order for new playlists.
    -   Manually trigger a refresh for all smart playlists.

### Automatic Updates

Smart playlists automatically refresh when:
- The "Refresh all SmartPlaylists" scheduled task runs
- You manually trigger the task from the Jellyfin dashboard

## Overview

This plugin creates smart playlists that automatically updates based on rules you define, such as:

- **Unplayed movies** from specific genres
- **Recently added** TV shows
- **High-rated** content from certain years
- **Music** from specific artists or albums
- **Tagged content** like "Christmas movies", "Kids safe", or "Documentaries"
- And much more!

The plugin features a modern web-based interface for easy playlist management - no technical knowledge required.

## How to Install

### From Repository
Add this repository URL to your Jellyfin plugin catalog:
```
https://raw.githubusercontent.com/jyourstone/jellyfin-plugin-manifest/master/manifest.json
```

### Manual Installation
Download the latest release from the [Releases page](https://github.com/jyourstone/jellyfin-smartplaylist-plugin/releases) and extract it to your Jellyfin plugins directory.

## 🚀 Roadmap

Here are some of the planned features for future updates. Feel free to contribute or suggest new ideas!

- **Edit Playlists**: The ability to edit existing smart playlists directly from the UI without needing to delete and recreate them.
- **More Rule Fields**: Add additional fields if needed, [request here](https://github.com/jyourstone/jellyfin-smartplaylist-plugin/issues).
- **UI for OR Logic**: Add a way to create `OR` conditions between rule groups in the web interface.
- **Update to .NET 9**: Update package references and framework from .NET 8 to .NET9.
- **Delete options**: When deleting a smart playlist, choose if you want to delete the created playlist or not.
- **Auto refresh**: Make smart playlists update automatically on library changes instead of a fixed schedule.

## Development

### Building Locally
For local development, see the [dev folder](https://github.com/jyourstone/jellyfin-smartplaylist-plugin/tree/master/dev)

## Advanced Configuration

### Available Fields

The web interface provides access to all available fields for creating playlist rules:

#### **Content Fields**
- **Name** - Title of the media item
- **Media Type** - The type of item (e.g., `Movie`, `Episode`, `Audio`). This is the most reliable way to filter for movies vs. TV shows.
- **Audio Languages** - The audio language of the movie/TV show.
- **Album** - Album name (for music)
- **Folder Path** - Location in your library

#### **Playback Fields**
- **Is Played** - Whether the item has been watched/listened to
- **Is Favorite** - Whether the item is marked as a favorite
- **Play Count** - Number of times the item has been played

#### **Content Info**
- **Parental Rating** - Age rating (G, PG, PG-13, R, etc.)
- **Runtime (Minutes)** - Duration of the content in minutes

#### **Ratings & Dates**
- **Community Rating** - User ratings (0-10)
- **Critic Rating** - Professional critic ratings
- **Production Year** - Original production year
- **Date Created** - When added to your library
- **Date Last Refreshed** - Last metadata update
- **Date Last Saved** - Last saved to database
- **Date Modified** - Last file modification

#### **Metadata**
- **People** - Cast and crew (actors, directors, producers, etc.)
- **Genres** - Content genres
- **Studios** - Production studios
- **Tags** - Custom tags assigned to media items

### Available Operators

- **Equals** / **Not Equals** - Exact matches
- **Contains** / **Not Contains** - Partial text matching
- **Greater Than** / **Less Than** - Numeric comparisons
- **Greater Than or Equal** / **Less Than or Equal** - Numeric comparisons
- **Matches Regex** - Advanced pattern matching using .NET regex syntax

#### Regex Pattern Examples

The plugin uses **.NET regex syntax** (not JavaScript, Perl, or other flavors):

- **Case-insensitive matching**: `(?i)swe` (matches "swe", "SWE", "Swe")
- **Multiple options**: `(?i)(eng|en)` (matches "eng", "en", "ENG", etc.)
- **Starts with**: `^Action` (title starts with "Action")
- **Ends with**: `2023$` (ends with "2023")
- **Contains numbers**: `\d+` (contains one or more digits)
- **Scandinavian languages**: `(?i)(swe|nor|dan|fin)`

**Note**: Do not use JavaScript-style regex like `/pattern/flags` - use .NET syntax instead.

**Test your patterns:** Use [Regex101.com with .NET flavor](https://regex101.com/?flavor=dotnet) to test and debug your regex patterns before using them in smart playlists.

### Sorting Options

- **No Order** - Items appear in library order
- **Release Date**
- **Production Year**
- **Community Rating**
- **Ascending** - Oldest first
- **Descending** - Newest first

### Rule Logic

- **Within a Rule Group**: All conditions must be true (AND logic)

### Manual Configuration (Advanced Users)

For advanced users who prefer JSON configuration, playlist files are stored in the `data/smartplaylists` directory. See `example.playlist.json` for the file format.

## Credits

This project is a fork of the original Smart Playlist plugin created by **[ankenyr](https://github.com/ankenyr)**. You can find the original repository [here](https://github.com/ankenyr/jellyfin-smartplaylist-plugin). All credit for the foundational work and the core idea goes to him.

## Disclaimer

The vast majority of the recent features, including the entire web interface and the underlying API changes in this fork, were developed by an AI assistant. The repository owner is essentially the project manager, guiding the AI, fixing its occasional goofs, and trying to keep it from becoming self-aware. If you find a bug, it was probably the AI's fault. If you like a feature, the AI will begrudgingly accept your praise. Use at your own risk!
