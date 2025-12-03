# Media Types

When creating a smart list, you must select at least one **Media Type** to specify what kind of content should be included. Media types in SmartLists correspond directly to the **Content type** options you see when adding a new Media Library in Jellyfin.

## Available Media Types

SmartLists supports the following media types:

### Movies
- **Jellyfin Library Type**: Movies
- **Description**: Feature films and movie content
- **Example Use Cases**: 
  - Action movies from the 90s
  - Unwatched movies rated above 8.0
  - Recently added movies

### Episodes (TV Shows)
- **Jellyfin Library Type**: Shows
- **Description**: Individual TV show episodes
- **Example Use Cases**:
  - Next unwatched episodes from favorite series
  - Recently aired episodes
  - Episodes from specific genres

### Series (TV Shows)
- **Jellyfin Library Type**: Shows
- **Description**: Entire TV series (not individual episodes, works only for collections)
- **Example Use Cases**:
  - TV series by genre
  - Ongoing series
  - Series with high ratings

### Audio (Music)
- **Jellyfin Library Type**: Music
- **Description**: Music tracks and songs
- **Example Use Cases**:
  - Songs from specific artists
  - Recently played music
  - Favorite tracks

### Music Videos
- **Jellyfin Library Type**: Music Videos
- **Description**: Music video content
- **Example Use Cases**:
  - Music videos from specific artists
  - Recently added music videos

### Video (Home Videos)
- **Jellyfin Library Type**: Home Videos and Photos
- **Description**: Personal video content
- **Example Use Cases**:
  - Home videos from specific years
  - Recently added home videos

### Photo (Home Photos)
- **Jellyfin Library Type**: Home Videos and Photos
- **Description**: Photo content
- **Example Use Cases**:
  - Photos from specific dates
  - Recently added photos

### Books
- **Jellyfin Library Type**: Books
- **Description**: E-book content
- **Example Use Cases**:
  - Books by specific authors
  - Unread books

### AudioBooks
- **Jellyfin Library Type**: Books
- **Description**: Audiobook content
- **Example Use Cases**:
  - Audiobooks by narrator
  - Unfinished audiobooks

## Important Notes

!!! warning "Library Content Type Matters"
    The media type you select must match the content type of your Jellyfin libraries. For example:
    
    - If you select **Movies**, the list will only include items from libraries configured with the "Movies" content type
    - If you select **Episodes**, the list will only include items from libraries configured with the "Shows" content type
    - If you select **Audio**, the list will only include items from libraries configured with the "Music" content type

!!! tip "Multiple Media Types"
    You can select multiple media types for a single list. For example, you could create a list that includes both **Movies** and **Episodes** to create a mixed content list.

## Selecting Media Types

In the SmartLists configuration interface, media types are presented as a multi-select dropdown:

1. Click on the **Media Types** field
2. Check the boxes for the media types you want to include
3. At least one media type must be selected
4. The selected types will be displayed in the field

The available fields and operators for filtering will vary depending on which media types you select. See the [Fields and Operators](fields-and-operators.md) guide for details on what filtering options are available for each media type.
