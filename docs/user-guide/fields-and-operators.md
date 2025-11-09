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
- **greater than** / **less than** - Numeric comparisons
- **greater than or equal** / **less than or equal** - Numeric comparisons
- **after** / **before** - Date comparisons
- **newer than** / **older than** - Relative date comparisons (days, weeks, months, years)
- **matches regex** - Advanced pattern matching using .NET regex syntax

## Rule Logic

- **Within a Rule Group**: All conditions must be true (AND logic)

