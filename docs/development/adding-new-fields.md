# Adding New Rule Fields

When adding new rule fields to the plugin, ensure they are categorized correctly in the UI field types (`config.js`).

## Field Type Categories

### LIST_FIELDS

Multi-valued fields (Collections, People, Actors, Directors, Writers, Producers, GuestStars, Genres, Studios, Tags, Artists, AlbumArtists)

- **Operators**: Contains, NotContains, IsIn, IsNotIn, MatchRegex
- **Use for**: Fields that can have multiple values per item

### NUMERIC_FIELDS

Number-based fields (ProductionYear, CommunityRating, RuntimeMinutes, PlayCount, Framerate)

- **Operators**: Equal, NotEqual, GreaterThan, LessThan, GreaterThanOrEqual, LessThanOrEqual
- **Use for**: Fields with numeric values

### DATE_FIELDS

Date/time fields (DateCreated, ReleaseDate, LastPlayedDate)

- **Operators**: Equal, NotEqual, After, Before, NewerThan, OlderThan
- **Use for**: Date and timestamp fields

### BOOLEAN_FIELDS

True/false fields (IsPlayed, IsFavorite, NextUnwatched)

- **Operators**: Equal, NotEqual
- **Use for**: Boolean/checkbox fields

### SIMPLE_FIELDS

Single-choice fields (ItemType)

- **Operators**: Equal, NotEqual
- **Use for**: Dropdown/select fields with predefined options

!!! important "Important"
    Always add new fields to the correct category to ensure proper operator availability and UI behavior.

