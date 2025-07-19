# Known Issues and Limitations

## Playlist MediaType Issue

### Description
Jellyfin has a core limitation where all playlist XML files show `<PlaylistMediaType>Audio</PlaylistMediaType>` regardless of the actual content in the playlist. This affects smart playlists created by this plugin as well as manually created playlists.

### Impact
- **Most users**: No impact on functionality - video content plays correctly despite the incorrect XML
- **Some users**: May experience playback issues where only audio plays instead of video
- **Specific affected scenarios**: 
  - Certain client applications that strictly validate playlist XML
  - Some external playlist parsers
  - VisionOS 2.0 and potentially other Apple platforms

### Root Cause
This is a Jellyfin server bug where:
1. New playlists default to "Audio" MediaType when created empty
2. Adding items to existing playlists doesn't update the MediaType property
3. The MediaType property is read-only and cannot be modified by plugins
4. Metadata refresh operations don't recalculate MediaType based on content

### Current Status
- **Plugin limitation**: This plugin cannot fix this issue as it requires changes to Jellyfin's core playlist handling
- **Jellyfin issue**: This affects all playlists in Jellyfin, not just smart playlists
- **Workaround**: None available at the plugin level. Editing the playlist XML file directly might help.

### Verification
You can verify this issue by:
1. Creating any playlist (manual or smart) with video content
2. Looking at the playlist XML file in your Jellyfin data directory
3. Observing `<PlaylistMediaType>Audio</PlaylistMediaType>` even for video playlists

### Recommendations
If you experience playback issues:
1. **Try different clients**: The issue may be client-specific
2. **Use direct library browsing**: Instead of playlists, browse content directly
3. **Report to Jellyfin**: Consider reporting this as a Jellyfin core issue
4. **Manual playlist test**: Create a manual playlist with the same content to confirm the issue isn't plugin-specific

### For Developers
This issue has been thoroughly investigated:
- MediaType property is read-only (`CanWrite: False`)
- Reflection attempts to set MediaType fail
- Metadata refresh operations don't fix the MediaType
- The issue exists in Jellyfin's core `PlaylistManager.CreatePlaylist()` method

### Related Information
- **Bug first reported**: User feedback about VisionOS 2.0 playback issues
- **Jellyfin versions affected**: All known versions
- **Plugin versions affected**: All versions (not a plugin bug) 