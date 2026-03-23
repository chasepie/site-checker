using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiteChecker.Backend.JsonConverters;

/// <summary>
/// Custom JsonConverter that converts nullable boolean values to integers (true = 1, false/null = 0)
/// </summary>
public class NullableBoolToIntConverter : JsonConverter<bool?>
{
    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        return reader.GetInt32() == 1;
    }

    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue((value ?? false) ? 1 : 0);
    }
}