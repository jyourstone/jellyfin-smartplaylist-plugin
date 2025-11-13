using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SmartLists.Core.Models
{
    /// <summary>
    /// DTO for user-specific smart playlists
    /// </summary>
    [Serializable]
    public class SmartPlaylistDto : SmartListDto
    {
        public SmartPlaylistDto()
        {
            Type = Core.Enums.SmartListType.Playlist;
        }

        // Playlist-specific properties
        // UserId - Frontend sends as string, so we need a custom converter
        [System.Text.Json.Serialization.JsonConverter(typeof(GuidStringConverter))]
        public Guid UserId { get; set; }
        
        // Custom converter to handle string-to-Guid conversion
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Class is instantiated by JsonConverter attribute")]
        private sealed class GuidStringConverter : System.Text.Json.Serialization.JsonConverter<Guid>
        {
            public override Guid Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
            {
                if (reader.TokenType == System.Text.Json.JsonTokenType.String)
                {
                    var stringValue = reader.GetString();
                    if (string.IsNullOrEmpty(stringValue))
                    {
                        return Guid.Empty;
                    }
                    if (Guid.TryParse(stringValue, out var guid))
                    {
                        return guid;
                    }
                    throw new System.Text.Json.JsonException($"Unable to convert '{stringValue}' to Guid");
                }
                else if (reader.TokenType == System.Text.Json.JsonTokenType.Null)
                {
                    return Guid.Empty;
                }
                else
                {
                    // Try to read as Guid directly (for backward compatibility)
                    try
                    {
                        return reader.GetGuid();
                    }
                    catch
                    {
                        throw new System.Text.Json.JsonException($"Unexpected token type {reader.TokenType} when parsing Guid");
                    }
                }
            }

            public override void Write(System.Text.Json.Utf8JsonWriter writer, Guid value, System.Text.Json.JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.ToString());
            }
        }
        
        public string? JellyfinPlaylistId { get; set; }  // Jellyfin playlist ID for reliable lookup
        public bool Public { get; set; } = false; // Default to private

        // Legacy support - for migration from old User field
        // Also used by frontend which sends UserId as string
        [Obsolete("Use UserId instead. This property is for backward compatibility only.")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? User
        {
            get => UserId == Guid.Empty ? null : UserId.ToString();
            set
            {
                if (!string.IsNullOrEmpty(value) && Guid.TryParse(value, out var parsedUserId))
                {
                    UserId = parsedUserId;
                }
            }
        }
    }
}

