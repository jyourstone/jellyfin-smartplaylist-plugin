# Fields and Operators

## Available Fields

The web interface provides access to all available fields for creating list rules.

### Content Fields

- **Album** - Album name (for music)
- **Audio Languages** - The audio language of the movie/TV show
- **Audio Bitrate** - Audio bitrate in kbps (e.g., 128, 256, 320, 1411)
- **Audio Sample Rate** - Audio sample rate in Hz (e.g., 44100, 48000, 96000, 192000)
- **Audio Bit Depth** - Audio bit depth in bits (e.g., 16, 24)
- **Audio Codec** - Audio codec format (e.g., FLAC, MP3, AAC, ALAC)
- **Audio Profile** - Audio codec profile (e.g., Dolby TrueHD, Dolby Atmos)
- **Audio Channels** - Number of audio channels (e.g., 2 for stereo, 6 for 5.1)
- **Resolution** - Video resolution (480p, 720p, 1080p, 1440p, 4K, 8K)
- **Framerate** - Video framerate in frames per second (e.g., 23.976, 29.97, 59.94)
- **Video Codec** - Video codec format (e.g., HEVC, H264, AV1, VP9)
- **Video Profile** - Video codec profile (e.g., Main 10, High)
- **Video Range** - Video dynamic range (e.g., SDR, HDR)
- **Video Range Type** - Specific HDR format (e.g., HDR10, DOVIWithHDR10, HDR10Plus, HLG)
- **Name** - Title of the media item
- **Series Name** - Name of the parent series (for episodes only)
- **Similar To** - Find items similar to a reference item based on metadata
- **Parental Rating** - Age rating (G, PG, PG-13, R, etc.)
- **Overview** - Description/summary of the content
- **Production Year** - Original production year
- **Release Date** - Original release date of the media

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

### Using "Is In" to Simplify Lists

The **"is in"** and **"is not in"** operators are powerful tools that can help you simplify your lists. Instead of creating multiple OR rule groups, you can combine multiple values in a single rule using semicolons.

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

Understanding how rule groups work is key to creating effective lists. The plugin uses two types of logic:

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

This list will include items that are:
- **(Action AND After 2010 AND Rating > 7)** **OR**
- **(Sci-Fi AND Favorite)**

So you'll get highly-rated recent action movies, plus any sci-fi movies you've marked as favorites, regardless of when they were made or their rating.

### Using Regex for Advanced Pattern Matching

The **matches regex** operator allows you to create complex pattern matching rules using .NET regular expression syntax.

!!! important "Important: .NET Syntax Required"
    SmartLists uses **.NET regex syntax**, not JavaScript-style regex. Do not use JavaScript-style patterns like `/pattern/flags`.

**Common Examples:**

- **Case-insensitive matching**: `(?i)swe` - Matches "swe", "Swe", "SWE", etc.
- **Multiple options**: `(?i)(eng|en)` - Matches "eng", "EN", "en", etc. (case-insensitive)
- **Starts with**: `^Action` - Matches items that start with "Action" (e.g., "Action Movie", "Action Hero")
- **Ends with**: `Movie$` - Matches items that end with "Movie" (e.g., "Action Movie", "Comedy Movie")
- **Contains word**: `\bAction\b` - Matches the word "Action" as a whole word (not "ActionMovie" or "InAction")

**Testing Your Patterns:**

You can test your regex patterns using [Regex101.com](https://regex101.com/) - make sure to select the **.NET** flavor when testing.

!!! tip "Regex Tips"
    - Use `(?i)` at the start of your pattern for case-insensitive matching
    - Use `^` to match the start of a string
    - Use `$` to match the end of a string
    - Use `|` for "OR" logic (e.g., `(eng|en|english)`)
    - Use `\b` to match word boundaries