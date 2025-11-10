# Advanced Configuration

For advanced users who prefer direct file editing or need to perform bulk operations, SmartPlaylist stores all playlist configurations as JSON files.

## File Location

Smart playlist files are stored in the Jellyfin data directory:

```
{DataPath}/smartplaylists/
```

Where `{DataPath}` is your Jellyfin data path (typically `/config/data` on Linux, `C:\ProgramData\Jellyfin\Server\data` on Windows, or `~/Library/Application Support/Jellyfin/Server/data` on macOS).

Each playlist is stored as a separate JSON file named `{playlistId}.json`, where `{playlistId}` is a unique GUID identifier for the playlist.

## File Format

Playlist files use JSON format with the following structure:

- **Indented JSON** - Files are formatted with indentation for readability
- **UTF-8 encoding** - All files use UTF-8 character encoding
- **GUID-based filenames** - Each file is named using the playlist's unique identifier

## Manual Editing

You can manually edit these JSON files if needed, but please be aware:

!!! warning "Edit at Your Own Risk"
    - **No validation safeguards**: The plugin may not have safeguards in place for misconfigured JSON files
    - **Backup first**: Always backup your playlist files before editing
    - **Syntax errors**: Invalid JSON syntax will prevent the playlist from loading
    - **Data corruption**: Incorrect field values or types may cause unexpected behavior or errors
    - **File locking**: Make sure Jellyfin is not actively using the file when editing (consider stopping the service temporarily)

### Best Practices

1. **Always backup** your `smartplaylists` directory before making changes
2. **Validate JSON syntax** using a JSON validator before saving
3. **Test thoroughly** after making changes to ensure playlists still work correctly
4. **Use the web interface** when possible - it's safer and includes validation

## Example Use Cases

Manual editing can be useful for:

- **Bulk operations**: Making the same change to multiple playlists
- **Advanced configurations**: Settings not available in the web interface
- **Migration**: Copying playlists between Jellyfin instances
- **Backup/restore**: Manual backup and restoration of playlist configurations

## File Structure Reference

For a reference of the JSON file structure, you can:

1. **Export a playlist** using the web interface (Settings → Export All Playlists) to see the format
2. **Examine existing files** in your `smartplaylists` directory
3. **Check the repository** for example files (if available)

The JSON structure follows the `SmartPlaylistDto` format, which includes fields for:
- Playlist metadata (name, ID, owner, etc.)
- Rules and logic groups
- Sort options
- Refresh settings
- Limits (max items, max play time)
- And more

## Troubleshooting

If a playlist file becomes corrupted or invalid:

1. **Check JSON syntax** - Use a JSON validator to find syntax errors
2. **Restore from backup** - If you have a backup, restore the file
3. **Recreate via UI** - Delete the corrupted file and recreate the playlist using the web interface
4. **Check logs** - Review Jellyfin logs for specific error messages about the playlist

!!! tip "Prefer the Web Interface"
    While manual editing is possible, the web interface is the recommended method for creating and editing playlists. It includes validation, error checking, and is much safer than manual file editing.

