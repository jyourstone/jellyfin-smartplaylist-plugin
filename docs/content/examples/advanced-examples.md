# Advanced Examples

Here are some more complex playlist and collection examples:

## Weekend Binge Queue
- **Next Unwatched** = True (excluding unwatched series) for started shows only

## Kids' Shows Progress
- **Next Unwatched** = True AND **Tags** contain "Kids" (with parent series tags enabled)

## Foreign Language Practice
- **Audio Languages** match `(?i)(ger|fra|spa)` AND **Is Played** = False

## Tagged Series Marathon
- **Tags** is in "Drama;Thriller" (with parent series tags enabled) AND **Runtime** < 50 minutes

## High-Quality FLAC Music
- **Audio Codec** = "FLAC" AND **Audio Bit Depth** >= 24 AND **Audio Sample Rate** >= 96000

## Lossless Audio Collection
- **Audio Codec** is in "FLAC;ALAC" (lossless formats)

## High Bitrate Music
- **Audio Bitrate** >= 320 (high-quality MP3 or lossless)

## Surround Sound Movies
- **Audio Channels** >= 6 (5.1 or higher)