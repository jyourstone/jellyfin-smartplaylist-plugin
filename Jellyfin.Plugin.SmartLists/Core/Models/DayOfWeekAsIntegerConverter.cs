using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SmartLists.Core.Models
{
    /// <summary>
    /// JSON converter that serializes DayOfWeek enum as integer instead of string
    /// </summary>
    public class DayOfWeekAsIntegerConverter : JsonConverter<DayOfWeek?>
    {
        public override DayOfWeek? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonTokenType.Number)
            {
                var intValue = reader.GetInt32();

                // Validate range: DayOfWeek enum is 0 (Sunday) through 6 (Saturday)
                if (intValue < 0 || intValue > 6)
                {
                    throw new JsonException($"Invalid DayOfWeek value '{intValue}'. Must be between 0 (Sunday) and 6 (Saturday).");
                }

                return (DayOfWeek)intValue;
            }

            // Handle string input for backward compatibility (e.g., "Friday" -> 5)
            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString();
                if (!string.IsNullOrEmpty(value) && Enum.TryParse<DayOfWeek>(value, true, out var dayOfWeek))
                {
                    return dayOfWeek;
                }

                throw new JsonException($"Unable to convert string '{value}' to DayOfWeek. Expected a day name (e.g., 'Monday') or numeric value (0-6).");
            }

            // For unexpected token types, build error message without accessing ValueSpan for structural tokens
            var tokenType = reader.TokenType;
            
            // Only access ValueSpan for token types that have values (avoid InvalidOperationException for structural tokens)
            var rawText = "";
            if (tokenType != JsonTokenType.StartObject && 
                tokenType != JsonTokenType.EndObject && 
                tokenType != JsonTokenType.StartArray && 
                tokenType != JsonTokenType.EndArray &&
                tokenType != JsonTokenType.True &&
                tokenType != JsonTokenType.False &&
                !reader.HasValueSequence && 
                reader.ValueSpan.Length > 0)
            {
                rawText = System.Text.Encoding.UTF8.GetString(reader.ValueSpan);
            }
            
            throw new JsonException($"Unable to convert token type '{tokenType}' (value: '{rawText}') to DayOfWeek. Expected a number (0-6) or string day name.");
        }

        public override void Write(Utf8JsonWriter writer, DayOfWeek? value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);

            if (value.HasValue)
            {
                writer.WriteNumberValue((int)value.Value);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}

