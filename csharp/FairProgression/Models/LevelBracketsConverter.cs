using System.Text.Json;
using System.Text.Json.Serialization;

namespace FairProgression.Models;

// Old config files stored a quoted array. Also accept the array shown in the documentation.
public sealed class LevelBracketsConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString()!,
            JsonTokenType.StartArray => JsonSerializer.Serialize(JsonSerializer.Deserialize<int[]>(ref reader)),
            _ => throw new JsonException("Use trader or an array of level limits.")
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (Services.ConfigLoader.TryReadLimits(value, out var limits))
            JsonSerializer.Serialize(writer, limits);
        else
            writer.WriteStringValue(value);
    }
}
