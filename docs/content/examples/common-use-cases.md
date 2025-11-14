# Common Use Cases

Here are some popular playlist types you can create:

## TV Shows & Movies

### Continue Watching
- **Next Unwatched** = True
- Shows next episodes to watch for each series

### Family Movie Night
- **Next Unwatched** = True AND **Parental Rating** = "PG" or "G"

### Unwatched Action Movies
- **Is Played** = False AND **Genre** contains "Action"

### Recent Additions
- **Date Created** newer than "2 weeks"

### Holiday Classics
- **Tags** contain "Christmas" AND **Production Year** before "2000"

### Complete Franchise Collection
- **Collections** contains "Movie Franchise" (includes all movies in the franchise)

### Unplayed Sitcom Episodes
- **Tags** contains "Sitcom" (with parent series tags enabled) AND **Is Played** = False

## Music

### Workout Mix
- **Genre** contains "Electronic" OR "Rock" AND **Max Playtime** 45 minutes

### Discover New Music
- **Play Count** = 0 AND **Date Created** newer than "1 month"

### Top Rated Favorites
- **Is Favorite** = True AND **Community Rating** greater than 8

### Rediscover Music
- **Last Played** older than 6 months

## Home Videos & Photos

### Recent Family Memories
- **Date Created** newer than "3 months" (both videos and photos)

### Vacation Videos Only
- **Tags** contain "Vacation" (select Home Videos media type)

### Photo Slideshow
- **Production Year** = 2024 (select Home Photos media type)

### Birthday Memories
- **File Name** contains "birthday" OR **Tags** contain "Birthday"

