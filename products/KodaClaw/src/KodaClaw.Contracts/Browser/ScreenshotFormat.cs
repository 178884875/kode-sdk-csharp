using System.Text.Json.Serialization;

namespace KodaClaw.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<ScreenshotFormat>))]
public enum ScreenshotFormat
{
    Jpeg,
    Png
}
