# Jellyfin SmartPlaylist Plugin
<div align="center">
    <p>
        <img alt="Logo" src="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/master/images/logo.jpg" height="350"/><br />
        <a href="https://github.com/jyourstone/jellyfin-smartplaylist-plugin/releases"><img alt="Total GitHub Downloads" src="https://img.shields.io/github/downloads/jyourstone/jellyfin-smartplaylist-plugin/total"/></a> <a href="https://github.com/jyourstone/jellyfin-smartplaylist-plugin/issues"><img alt="GitHub Issues or Pull Requests" src="https://img.shields.io/github/issues/jyourstone/jellyfin-smartplaylist-plugin"/></a> <a href="https://github.com/jyourstone/jellyfin-smartplaylist-plugin/releases"><img alt="Build and Release" src="https://github.com/jyourstone/jellyfin-smartplaylist-plugin/actions/workflows/release.yml/badge.svg"/></a> <a href="https://jellyfin.org/"><img alt="Jellyfin Version" src="https://img.shields.io/badge/Jellyfin-10.10-blue.svg"/></a>
    </p>        
</div>

A rebuilt and modernized plugin to create smart, rule-based playlists in Jellyfin.

This plugin allows you to create dynamic playlists based on a set of rules, which will automatically update as your library changes. 

Requires Jellyfin version `10.10.0` and newer.

## 📋 Table of Contents

- [✨ Features](#-features)
- [⚙️ Configuration](#️-configuration)
- [📋 Overview](#-overview)
- [📦 How to Install](#-how-to-install)
- [🚀 Roadmap](#-roadmap)
- [🛠️ Development](#️-development)
- [🔧 Advanced Configuration](#-advanced-configuration)
- [🙏 Credits](#-credits)
- [⚠️ Disclaimer](#️-disclaimer)

## ✨ Features

- 🚀 **Modern Jellyfin Support** - Built for newer Jellyfin versions with improved compatibility.
- 🎨 **Modern Web Interface** - A full-featured UI to create, edit, view and delete smart playlists.
- ✏️ **Edit Playlists** - Modify existing smart playlists directly from the UI.
- 👥 **User Selection** - Choose which user should own a playlist with an intuitive dropdown, making it easy to create playlists for different family members.
- 🎯 **Flexible Rules** - Build simple or complex rules with an intuitive builder. 
- 🔄 **Automatic Updates** - Playlists refresh automatically (scheduled tasks).
- ⚙️ **Settings** - Configure default settings and trigger a manual refresh for all playlists at any time.
- 🛠️ **Advanced Options** - Support for regex patterns, date ranges, and more.
- 🎵 **All Media Types** - Works with movies, series, episodes, and music

## ⚙️ Configuration

SmartPlaylist now features a modern web-based configuration interface through the plugin settings page! No more manual JSON editing required.

<div align="center">
    <p>
        <img alt="Create playlist page" src="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/master/images/config_page_create.png" width="400" style="margin-right: 10px;"/>
        <img alt="Manage playlists page" src="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/master/images/config_page_manage.png" width="400"/>
    </p>
</div>

### Using the Web Interface

The web interface is organized into three tabs:

1.  **Create Playlist**: This is where you build new playlists.
    -   Define the rules for including items.
    -   Choose the sort order.
    -   Set the maximum number of items (defaults to 500).
    -   Select which user should own the playlist.
    -   Decide if the playlist should be public or private.
    -   Choose whether or not to enable the playlist.
2.  **Manage Playlists**: View and edit all of your existing smart playlists.
    -   See the rules, sorting, and other details for each playlist.
    -   Edit existing playlists to modify rules, ownership, or settings.
    -   Enable or disable playlists to show or hide them in Jellyfin.
    -   Delete playlists you no longer need with flexible deletion options.
3.  **Settings**: Configure global settings for the plugin.
    -   Set the default sort order for new playlists.
    -   Configure custom prefix and suffix for playlist names.
    -   Manually trigger a refresh for all smart playlists.

#### Flexible Deletion Options

When deleting a smart playlist, you can choose whether to also delete the corresponding Jellyfin playlist:

- **Delete both (default)**: Removes both the smart playlist configuration and the Jellyfin playlist
- **Delete configuration only**: Keeps the Jellyfin playlist and removes the custom prefix/suffix (if any), making it a regular manually managed playlist

This is useful when you for example want to populate a playlist automatically once, then manage it manually.

#### Custom Playlist Naming

You can customize how smart playlist names appear in Jellyfin by configuring a prefix and/or suffix in the Settings tab:

- **Prefix**: Text added before the playlist name (e.g., "My " → "My Action Movies")
- **Suffix**: Text added after the playlist name (e.g., " - Smart" → "Action Movies - Smart")
- **Both**: Use both prefix and suffix (e.g., "My " + " - Smart" → "My Action Movies - Smart")
- **None**: Leave both empty for no prefix/suffix

The naming configuration applies to all new smart playlists. When you delete a smart playlist but keep the Jellyfin playlist, the custom prefix/suffix will be automatically removed.

### Automatic Updates

Smart playlists automatically refresh using scheduled tasks:

#### **Scheduled Tasks**
- **🎵 Audio SmartPlaylists**: Runs by default daily at **3:30 AM**
- **🎬 Video SmartPlaylists**: Runs by default **hourly**

These tasks can be configured in the Jellyfin admin dashboard.

#### **Manual Refresh**
- Use the **"Refresh All Playlists"** button in the Settings tab to trigger both tasks immediately
- Use the **"Refresh"** button next to each playlist in the Manage Playlists tab to refresh individual playlists
- Individual tasks can also be triggered separately from the Jellyfin admin dashboard

## 📋 Overview

This plugin creates smart playlists that automatically updates based on rules you define, such as:

- **Unplayed movies** from specific genres
- **Recently added** series or episodes
- **Next unwatched episodes** for "Continue Watching" playlists
- **High-rated** content from certain years
- **Music** from specific artists or albums
- **Tagged content** like "Christmas", "Kids", or "Documentaries"
- And much more!

The plugin features a modern web-based interface for easy playlist management - no technical knowledge required.

### Common Use Cases

Here are some popular playlist types you can create:

#### **TV Shows & Movies**
- **Continue Watching** - Next Unwatched = True (shows next episodes to watch for each series)
- **Family Movie Night** - Next Unwatched = True AND Parental Rating = "PG" or "G"
- **Unwatched Action Movies** - Is Played = False AND Genre contains "Action"
- **Recent Additions** - Date Created newer than "2 weeks"
- **Holiday Classics** - Tags contain "Christmas" AND Production Year before "2000"

#### **Music**
- **Workout Mix** - Genre contains "Electronic" OR "Rock" AND Max Play Time 45 minutes
- **Discover New Music** - Play Count = 0 AND Date Created newer than "1 month"
- **Top Rated Favorites** - Is Favorite = True AND Community Rating greater than 8

#### **Advanced Examples**
- **Weekend Binge Queue** - Next Unwatched = True (excluding unwatched series) for started shows only
- **Kids' Shows Progress** - Next Unwatched = True AND Tags contain "Kids"
- **Foreign Language Practice** - Audio Languages match "(?i)(ger|fra|spa)" AND Is Played = False

### Enhanced Music Tagging

For even more powerful music playlist creation, combine this plugin with the **[MusicTags plugin](https://github.com/jyourstone/jellyfin-musictags-plugin)**. The MusicTags plugin extracts custom tags from your audio files (like "BPM", "Mood", "Key", etc.) and makes them available as tags in Jellyfin. You can then use these extracted tags in SmartPlaylist rules to create dynamic playlists based on the actual content of your music files.

For example, create playlists for:
- **Running playlist** (tracks with BPM 140-160 AND genre "Dance")
- **Favorite relaxing music** (tracks with mood "Chill" AND marked as favorite)

## 📦 How to Install

### From Repository
Add this repository URL to your Jellyfin plugin catalog:
```
https://raw.githubusercontent.com/jyourstone/jellyfin-plugin-manifest/master/manifest.json
```

### Manual Installation
Download the latest release from the [Releases page](https://github.com/jyourstone/jellyfin-smartplaylist-plugin/releases) and extract it to your Jellyfin plugins directory.

## 🚀 Roadmap

Here are some of the planned features for future updates. Feel free to contribute or suggest new ideas!

- **More Rule Fields**: Add additional fields if needed, [request here](https://github.com/jyourstone/jellyfin-smartplaylist-plugin/issues).
- **Auto refresh**: Make smart playlists update automatically on library changes instead of a fixed schedule.

## 🛠️ Development

### Building Locally
For local development, see the [dev folder](https://github.com/jyourstone/jellyfin-smartplaylist-plugin/tree/master/dev)

## 🔧 Advanced Configuration

### Available Fields

The web interface provides access to all available fields for creating playlist rules:

#### **Content Fields**
- **Name** - Title of the media item
- **Media Type** - The type of item (e.g., `Movie`, `Episode`, `Series`, `Music`)
- **Audio Languages** - The audio language of the movie/TV show.
- **Album** - Album name (for music)
- **Folder Path** - Location in your library

#### **Playback Fields**
- **Is Played** - Whether the item has been watched/listened to
- **Is Favorite** - Whether the item is marked as a favorite
- **Play Count** - Number of times the item has been played
- **Next Unwatched** - Shows only the next unwatched episode in chronological order for TV series
> **Note:** These playback fields can optionally be set to a specific user. This allows you to create rules like "Is Played by user X" or "Is Favorite for user X AND for user Y".
> **Next Unwatched**: This field is specifically designed for creating "Continue Watching" style playlists. For TV series, it identifies the next episode a user should watch based on their viewing history:
> - If a user has watched Season 1 completely and Season 2 episodes 1-3, it shows Season 2 Episode 4
> - For completely unwatched series, it shows Season 1 Episode 1 (configurable)
> - If a user skipped an episode, that skipped episode becomes the "next unwatched"
> - For multiple series in a playlist, it shows the next unwatched episode from ALL series
> - **Include unwatched series**: Optional setting to include/exclude Season 1 Episode 1 of completely unwatched series
> - **⚠️ Note**: Specials (Season 0 episodes) are automatically excluded from the "Next Unwatched" logic to focus on the main storyline

#### **Content Info**
- **Parental Rating** - Age rating (G, PG, PG-13, R, etc.)
- **Runtime (Minutes)** - Duration of the content in minutes

#### **Ratings & Dates**
- **Community Rating** - User ratings (0-10)
- **Critic Rating** - Professional critic ratings
- **Production Year** - Original production year
- **Release Date** - Original release date of the media
- **Date Created** - When added to your library
- **Date Last Refreshed** - Last metadata update
- **Date Last Saved** - Last saved to database
- **Date Modified** - Last file modification

> **Date Filtering**: Date fields support both exact date comparisons and relative date filtering:
> - **Exact dates**: Use "After" or "Before" with a specific date (e.g., "2024-01-01")
> - **Relative dates**: Use "Newer Than" or "Older Than" with a time period (e.g., "3 weeks", "1 month", "2 years")
> 
> **Note**: Relative date calculations use UTC time to ensure consistent behavior across different server timezones. This means "items from the last 3 days" is calculated from the current UTC time, not your local timezone.

#### **Metadata**
- **People** - Cast and crew (actors, directors, producers, etc.) *for movies and TV shows*
- **Artists** - Track-level artists *for music*
- **Album Artists** - Album-level primary artists *for music*
- **Genres** - Content genres
- **Studios** - Production studios
- **Tags** - Custom tags assigned to media items

> **Music Fields**: For music libraries, use **Artists** to find specific artists and **Album Artists** to find music by the primary artist of an album. The **People** field is designed for movies/TV and contains cast/crew information rather than music performers.

### Available Operators

- **Equals** / **Not Equals** - Exact matches
- **Contains** / **Not Contains** - Partial text matching  
- **Greater Than** / **Less Than** - Numeric comparisons
- **Greater Than or Equal** / **Less Than or Equal** - Numeric comparisons
- **After** / **Before** - Date comparisons
- **Newer Than** / **Older Than** - Relative date comparisons (days, weeks, months, years)
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
- **Name** - Sort by title
- **Release Date** - Sort by release date
- **Production Year** - Sort by production year
- **Community Rating** - Sort by user ratings
- **Date Created** - Sort by when added to library
- **Random** - Randomize the order of items
- **Ascending** - Oldest first
- **Descending** - Newest first

### Max Items

You can optionally set a maximum number of items for your smart playlist. This is for example useful for:
- Limiting large playlists to a manageable size
- Creating "Top 10" or "Best of" style playlists
- Improving performance for very large libraries

**Note**: Setting this to unlimited (0) might cause performance issues or even crashes for very large playlists.

### Max Play Time

You can optionally set a maximum play time in minutes for your smart playlist. This is for example useful for:
- Creating workout playlists that match your exercise duration
- Setting up Pomodoro-style work sessions with music
- Ensuring playlists fit within specific time constraints

**How it works**: The plugin calculates the total runtime of items in the playlist and stops adding items when the time limit is reached. The last item that would exceed the limit is not included, ensuring the playlist stays within your specified duration.

**Note**: This feature works with all media types (movies, TV shows, music) and uses the actual runtime of each item. For music, this means the exact duration of each track.

### Rule Logic

- **Within a Rule Group**: All conditions must be true (AND logic)

### Manual Configuration (Advanced Users)

For advanced users who prefer JSON configuration, playlist files are stored in the `data/smartplaylists` directory. See `example.playlist.json` for the file format.

## 🙏 Credits

This project is a fork of the original SmartPlaylist plugin created by **[ankenyr](https://github.com/ankenyr)**. You can find the original repository [here](https://github.com/ankenyr/jellyfin-smartplaylist-plugin). All credit for the foundational work and the core idea goes to him.

## ⚠️ Disclaimer

The vast majority of the recent features, including the entire web interface and the underlying API changes in this plugin, were developed by an AI assistant. While I do have some basic experience with C# from a long time ago, I'm essentially the project manager, guiding the AI, fixing its occasional goofs, and trying to keep it from becoming self-aware.