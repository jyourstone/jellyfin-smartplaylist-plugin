# Sorting and Limits

## Multiple Sorting Levels

You can add up to **3 sorting options** to create cascading sorts. Items are first sorted by the first option, then items with equal values are sorted by the second option, and so on.

### Example Use Cases

- **Best Movies by Year**: Sort by "Production Year" descending, then "Community Rating" descending - Groups movies by year, with highest-rated movies first within each year
- **Least Played Mix**: Sort by "Play Count (owner)" ascending, then "Random" - Prioritizes less-played items, while shuffling tracks with the same play count to prevent album grouping

## Available Sort Fields

- **No Order** - Items appear in library order
- **Name** - Sort by title
- **Name (Ignore 'The')** - Sort by name while ignoring leading article 'The'
- **Release Date** - Sort by release date
- **Production Year** - Sort by production year
- **Season Number** - Sort by TV season number
- **Episode Number** - Sort by TV episode number
- **Series Name** - Sort by series name (for TV episodes)
- **Community Rating** - Sort by user ratings
- **Date Created** - Sort by when added to library
- **Play Count (owner)** - Sort by how many times the playlist owner has played each item
- **Last Played (owner)** - Sort by when the playlist owner last played each item
- **Runtime** - Sort by duration/runtime in minutes
- **Album Name** - Sort by album name (for music and music videos)
- **Artist** - Sort by artist name (for music and music videos)
- **Track Number** - Sort by album name, disc number, then track number (designed for music)
- **Similarity** - Sort by similarity score (highest first) - only available when using the "Similar To" field
- **Random** - Randomize the order of items

## Max Items

You can optionally set a maximum number of items for your smart playlist. This is useful for:

- Limiting large playlists to a manageable size
- Creating "Top 10" or "Best of" style playlists
- Improving performance for very large libraries

!!! warning "Performance"
    Setting this to unlimited (0) might cause performance issues or even crashes for very large playlists.

## Max Play Time

You can optionally set a maximum play time in minutes for your smart playlist. This is useful for:

- Creating workout playlists that match your exercise duration
- Setting up Pomodoro-style work sessions with music
- Ensuring playlists fit within specific time constraints

**How it works**: The plugin calculates the total runtime of items in the playlist and stops adding items when the time limit is reached. The last item that would exceed the limit is not included, ensuring the playlist stays within your specified duration.

This feature works with all media types (movies, TV shows, music) and uses the actual runtime of each item.

