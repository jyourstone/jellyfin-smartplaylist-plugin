# Jellyfin SmartPlaylist Plugin
<div align="center">
    <p>
        <img alt="Logo" src="https://raw.githubusercontent.com/jyourstone/jellyfin-smartplaylist-plugin/main/images/logo.jpg" height="350"/><br />
        <a href="https://github.com/jyourstone/jellyfin-smartplaylist-plugin/releases"><img alt="Total GitHub Downloads" src="https://img.shields.io/github/downloads/jyourstone/jellyfin-smartplaylist-plugin/total"/></a> <a href="https://github.com/jyourstone/jellyfin-smartplaylist-plugin/issues"><img alt="GitHub Issues or Pull Requests" src="https://img.shields.io/github/issues/jyourstone/jellyfin-smartplaylist-plugin"/></a> <a href="https://github.com/jyourstone/jellyfin-smartplaylist-plugin/releases"><img alt="Build and Release" src="https://github.com/jyourstone/jellyfin-smartplaylist-plugin/actions/workflows/release.yml/badge.svg"/></a> <a href="https://jellyfin.org/"><img alt="Jellyfin Version" src="https://img.shields.io/badge/Jellyfin-10.11-blue.svg"/></a>
    </p>        
</div>

A rebuilt and modernized plugin to create smart, rule-based playlists in Jellyfin.

This plugin allows you to create dynamic playlists based on a set of rules, which will automatically update as your library changes.

**Requires Jellyfin version `10.10.0` and newer.** New functionality is only developed for Jellyfin `10.11.0` and newer.

## ✨ Features

- 🚀 **Modern Jellyfin Support** - Built for newer Jellyfin versions with improved compatibility
- 🎨 **Modern Web Interface** - A full-featured UI to create, edit, view and delete smart playlists
- ✏️ **Edit Playlists** - Modify existing smart playlists directly from the UI
- 👥 **User Selection** - Choose which user should own a playlist with an intuitive dropdown
- 🎯 **Flexible Rules** - Build simple or complex rules with an intuitive builder
- 🔄 **Automatic Updates** - Playlists refresh automatically on library updates or via scheduled tasks
- 📦 **Export/Import** - Export all playlists to a ZIP file for backup or transfer between Jellyfin instances
- 🎵 **Media Types** - Works with all Jellyfin media types

## 🚀 Quick Start

1. **Install the Plugin**: [See installation instructions](#-how-to-install)
2. **Access Plugin Settings**: Go to Dashboard → My Plugins → SmartPlaylist
3. **Create Your First Playlist**: Use the "Create Playlist" tab
4. **Example**: Create a playlist for "Unwatched Action Movies" with:
   - Media type: "Movie"
   - Genre contains "Action"
   - Is Played = False

## 📚 Documentation

<div align="center">

### 📖 **[View Full Documentation →](https://jellyfin-smartplaylist.dinsten.se)**

Complete guide with detailed field descriptions, operators, examples, advanced configuration, and more!

</div>

## 📦 How to Install


### From Repository
Add this repository URL to your Jellyfin plugin catalog:
```
https://raw.githubusercontent.com/jyourstone/jellyfin-plugin-manifest/main/manifest.json
```

### Manual Installation
Download the latest release from the [Releases page](https://github.com/jyourstone/jellyfin-smartplaylist-plugin/releases) and extract it to a subfolder in your Jellyfin plugins directory (for example `/config/plugins/SmartPlaylist`).

### Try RC Releases (Unstable)
Want to test the latest features before they're officially released? You can try release candidate (RC) versions using the unstable manifest:
```
https://raw.githubusercontent.com/jyourstone/jellyfin-plugin-manifest/unstable/manifest.json
```

**⚠️ Warning**: RC releases are pre-release versions that may contain bugs or incomplete features. Use at your own risk and consider backing up your smart playlist configurations before upgrading.

## 📋 Overview

This plugin creates smart playlists that automatically update based on rules you define, such as:

- **Unplayed movies** from specific genres
- **Recently added** series or episodes
- **Next unwatched episodes** for "Continue Watching" playlists
- **High-rated** content from certain years
- **Music** from specific artists or albums
- **Tagged content** like "Christmas", "Kids", or "Documentaries"
- And much more!

The plugin features a modern web-based interface for easy playlist management - no technical knowledge required.

### Supported Media Types

SmartPlaylist works with all media types supported by Jellyfin:

- **🎬 Movie** - Individual movie files
- **📺 Episode** - Individual TV show episodes
- **🎵 Audio (Music)** - Music tracks and albums
- **🎬 Music Video** - Music video files
- **📹 Video (Home Video)** - Personal home videos and recordings
- **📸 Photo (Home Photo)** - Personal photos and images
- **📚 Book** - eBooks, comics, and other readable content
- **🎧 Audiobook** - Spoken word audio books

## 🙏 Credits

This project is a fork of the original SmartPlaylist plugin created by **[ankenyr](https://github.com/ankenyr)**. You can find the original repository [here](https://github.com/ankenyr/jellyfin-smartplaylist-plugin). All credit for the foundational work and the core idea goes to him.

## ⚠️ Disclaimer

The vast majority of the recent features, including the entire web interface and the underlying API changes in this plugin, were developed by an AI assistant. While I do have some basic experience with C# from a long time ago, I'm essentially the project manager, guiding the AI, fixing its occasional goofs, and trying to keep it from becoming self-aware.
