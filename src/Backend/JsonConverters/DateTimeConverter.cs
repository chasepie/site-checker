using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiteChecker.Backend.JsonConverters;

public class DateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return DateTime.SpecifyKind(reader.GetDateTime(), DateTimeKind.Utc);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var date = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        var str = date.ToString("o", CultureInfo.InvariantCulture);
        writer.WriteStringValue(str);
    }
}