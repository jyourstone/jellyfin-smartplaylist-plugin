# Fields and Operators

## Available Fields

The web interface provides access to all available fields for creating playlist rules.

### Content Fields

- **Album** - Album name (for music)
- **Audio Languages** - The audio language of the movie/TV show
- **Audio Bitrate** - Audio bitrate in kbps (e.g., 128, 256, 320, 1411)
- **Audio Sample Rate** - Audio sample rate in Hz (e.g., 44100, 48000, 96000, 192000)
- **Audio Bit Depth** - Audio bit depth in bits (e.g., 16, 24)
- **Audio Codec** - Audio codec format (e.g., FLAC, MP3, AAC, ALAC)
- **Audio Channels** - Number of audio channels (e.g., 2 for stereo, 6 for 5.1)
- **Name** - Title of the media item
- **Series Name** - Name of the parent series (for episodes only)
- **Similar To** - Find items similar to a reference item based on metadata
- **Parental Rating** - Age rating (G, PG, PG-13, R, etc.)
- **Overview** - Description/summary of the content
- **Production Year** - Original production year
- **Release Date** - Original release date of the media
- **Resolution** - Video resolution (480p, 720p, 1080p, 1440p, 4K, 8K)
- **Framerate** - Video framerate in frames per second (e.g., 23.976, 29.97, 59.94)

### Ratings & Playback Fields

- **Community Rating** - User ratings (0-10)
- **Critic Rating** - Professional critic ratings
- **Is Favorite** - Whether the item is marked as a favorite
- **Is Played** - Whether the item has been watched/listened to
- **Last Played** - When the item was last played (user-specific)
- **Next Unwatched** - Shows only the next unwatched episode in chronological order for TV series
- **Play Count** - Number of times the item has been played
- **Runtime (Minutes)** - Duration of the content in minutes

### File Info Fields

- **Date Modified** - Last file modification date
- **File Name** - Name of the media file
- **Folder Path** - Location in your library

### Library Fields

- **Date Added to Library** - When added to your Jellyfin library
- **Last Metadata Refresh** - When Jellyfin last updated metadata from online sources
- **Last Database Save** - When the item's data was last saved to Jellyfin's database

### People Fields (Movies & TV Shows)

- **People (All)** - All cast and crew
- **Actors** - Actors in the movie or TV show
- **Directors** - Directors of the movie or TV show
- **Writers** - Writers/screenwriters
- **Producers** - Producers
- **Guest Stars** - Guest stars in TV show episodes

### Collection Fields

- **Collections** - All Jellyfin collections that contain the media item
- **Genres** - Content genres
- **Studios** - Production studios
- **Tags** - Custom tags assigned to media items
- **Artists** - Track-level artists (for music)
- **Album Artists** - Album-level primary artists (for music)

## Available Operators

- **equals** / **not equals** - Exact matches
- **contains** / **not contains** - Partial text matching
- **is in** / **is not in** - Check if value contains any item (partial matching)
  - **Tip**: Use this instead of creating multiple OR rule groups! For example, instead of creating separate rule groups for "Action", "Comedy", and "Drama", you can use a single rule: `Genre is in "Action;Comedy;Drama"`
- **greater than** / **less than** - Numeric comparisons
- **greater than or equal** / **less than or equal** - Numeric comparisons
- **after** / **before** - Date comparisons
- **newer than** / **older than** - Relative date comparisons (days, weeks, months, years)
- **matches regex** - Advanced pattern matching using .NET regex syntax

### Using "Is In" to Simplify Playlists

The **"is in"** and **"is not in"** operators are powerful tools that can help you simplify your playlists. Instead of creating multiple OR rule groups, you can combine multiple values in a single rule using semicolons.

**Example: Instead of this (multiple OR rule groups):**
```
Rule Group 1:
  - Genre contains "Action"
  - Is Played = False

Rule Group 2:
  - Genre contains "Comedy"
  - Is Played = False

Rule Group 3:
  - Genre contains "Drama"
  - Is Played = False
```

**You can use this (single rule with "is in"):**
```
Rule Group 1:
  - Genre is in "Action;Comedy;Drama"
  - Is Played = False
```

Both approaches produce the same result, but the second is much simpler and easier to maintain! The "is in" operator checks if the field value contains any of the semicolon-separated items.

**Syntax**: Separate multiple values with semicolons: `value1; value2; value3`

## Rule Logic

Understanding how rule groups work is key to creating effective playlists. The plugin uses two types of logic:

### Within a Rule Group (AND Logic)

**All rules within the same group must be true** for an item to match. This means you're looking for items that meet ALL the conditions in that group.

**Example:**
```
Rule Group 1:
  - Genre contains "Action"
  - Is Played = False
  - Production Year > 2010
```

This matches items that are:
- **Action** movies **AND**
- **Unwatched** **AND**
- **Released after 2010**

All three conditions must be true!

### Between Rule Groups (OR Logic)

**Different rule groups are separated with OR logic**. An item matches if it satisfies ANY of the rule groups.

**Example:**
```
Rule Group 1:
  - Genre contains "Action"
  - Is Played = False

Rule Group 2:
  - Genre contains "Comedy"
  - Is Played = False
```

This matches items that are:
- **(Action AND Unwatched)** **OR**
- **(Comedy AND Unwatched)**

An item matches if it's either an unwatched action movie OR an unwatched comedy.

### Complex Example

Here's a more complex example to illustrate both concepts:

```
Rule Group 1:
  - Genre contains "Action"
  - Production Year > 2010
  - Community Rating > 7

Rule Group 2:
  - Genre contains "Sci-Fi"
  - Is Favorite = True
```

This playlist will include items that are:
- **(Action AND After 2010 AND Rating > 7)** **OR**
- **(Sci-Fi AND Favorite)**

So you'll get highly-rated recent action movies, plus any sci-fi movies you've marked as favorites, regardless of when they were made or their rating.

