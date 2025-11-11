# Auto-Refresh

Smart playlists can update automatically in multiple ways.

## Real-Time Auto-Refresh

Configure playlists to refresh automatically when your library changes:

- **Per-Playlist Setting**: Each playlist can be set to `Never`, `On Library Changes`, or `On All Changes`
- **Global Default**: Set the default auto-refresh mode for new playlists in Settings
- **Unified Batching**: All changes use intelligent 3-second batching to prevent spam during bulk operations
- **Performance Optimized**: Uses advanced caching to only refresh playlists that are actually affected by changes
- **Automatic Deduplication**: Multiple events for the same item are combined into a single refresh

### Auto-Refresh Modes

- **Never**: Scheduled and manual refresh only (original behavior)
- **On Library Changes**: Refresh only when new items are added to your library
- **On All Changes**: Refresh for library additions AND all updates (metadata changes, playback status, favorites, etc.)

## Custom Playlist Scheduling

Configure individual playlists with their own refresh schedules:

- **Per-playlist scheduling**: Each playlist can have its own schedule
- **Schedule types**: Daily, Weekly, Monthly, Yearly, or Interval
- **Flexible intervals**: 15 min, 30 min, 1 h, 2 h, 3 h, 4 h, 6 h, 8 h, 12 h, or 24 h
- **Backward compatible**: Existing playlists continue using legacy Jellyfin scheduled tasks

### Schedule Options

- **Daily**: Refresh at a specific time each day (e.g., 3:00 AM)
- **Weekly**: Refresh on a specific day and time each week (e.g., Sunday at 8:00 PM)
- **Monthly**: Refresh on a specific day and time each month (e.g., 1st at 2:00 AM)
- **Yearly**: Refresh on a specific month, day and time each year (e.g., January 1st at midnight)
- **Interval**: Refresh at regular intervals (e.g., every 2 hours, every 30 minutes)
- **No schedule**: Disable all scheduled refreshes (auto-refresh and manual only)

!!! tip "Multiple Schedules"
    You can add multiple schedules to a single playlist. For example, you could set both a Daily schedule at 6:00 AM and an Interval schedule every 4 hours to refresh the playlist both at a specific time and at regular intervals throughout the day.

## Legacy Scheduled Tasks

For old playlists where custom schedules do not exist, the original Jellyfin scheduled tasks are still used:

- **🎵 Audio SmartPlaylists**: Runs by default daily at **3:30 AM** (handles music and audiobooks)
- **🎬 Media SmartPlaylists**: Runs by default **hourly** (handles movies, TV shows, readable books, music videos, home videos, and photos)

## Example Use Cases

### Custom Scheduling Examples

- **Daily Random Mix**: Random-sorted playlist with a Daily schedule at 6:00 AM → fresh random order every morning
- **Weekly Discoveries**: New-content playlist with a Weekly schedule on Sunday at 8:00 PM → weekly refresh for weekend planning
- **Monthly Archive**: Year-based movie playlist with a Monthly schedule on the 1st at 2:00 AM → monthly refresh for archival content
- **Background Refresh**: Mood-based music playlist with 4-hour intervals → regular updates without being intrusive

### Auto-Refresh Examples

- **Continue Watching**: NextUnwatched playlist with auto-refresh on all changes → updates when episodes are watched (batched)
- **New Releases**: Date-based playlist with auto-refresh on library changes → updates when new content is added
- **Favorites Collection**: Favorite-based playlist with auto-refresh on all changes → updates when items are favorited/unfavorited (batched)

### Mixed Approach

Combine both systems for optimal performance:

- Use **custom scheduling** for playlists that benefit from regular refresh (random order, time-based rules)
- Use **auto-refresh** for playlists that need immediate updates (playback status, new additions)

## Scheduled Refresh Control

Perfect for randomized playlists:

- Enable scheduled refresh for randomized playlists to get fresh random order daily/hourly
- Disable for rule-based playlists that rely on real-time auto-refresh instead
- Mix and match: some playlists on schedule, others auto-refresh only

## Manual Refresh

- Use the **"Refresh All Playlists"** button in the Settings tab to trigger a refresh of all playlists
- Use the **"Refresh"** button next to each playlist in the Manage Playlists tab to refresh individual playlists

!!! note "Refresh Time"
    A full refresh of all playlists can take some time depending on how many media items and playlists you have. Large libraries with many playlists may take several minutes or even hours to complete, depending on the hardware. Individual playlist refreshes are typically faster.

## Performance Considerations

### Auto-Refresh Settings

- **`Never`**: Best performance, no automatic refreshes
- **`On Library Changes`**: Good performance, refreshes only for library additions
- **`On All Changes`**: Refreshes for additions AND updates (metadata, playback status, etc.)

### Large Library Recommendations

- All changes are automatically batched with a 3-second delay to prevent spam during bulk operations
- Even `On All Changes` is efficient thanks to intelligent batching during large library scans
- **Removed items** are automatically handled by Jellyfin (no refresh needed)
- Consider limiting the number of playlists with auto-refresh enabled to optimize server performance

### Third-Party Plugin Compatibility

- Plugins that sync watched status may trigger many simultaneous updates
- If you experience performance issues during bulk sync operations, temporarily set playlists to `Never` or `On Library Changes`