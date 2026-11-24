using System.Globalization;
using System.Text.Json;

namespace ShrinkRay.Core.Media;

/// <summary>Parses the JSON from <c>ffprobe -v error -show_format -show_streams -of json</c>.</summary>
public static class FfprobeParser
{
    /// <exception cref="FormatException">The text is not ffprobe JSON.</exception>
    public static MediaInfo Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException("ffprobe output is not valid JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("ffprobe output is not a JSON object.");
            }

            var streams = new List<MediaStream>();
            if (root.TryGetProperty("streams", out var streamArray) && streamArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streamArray.EnumerateArray())
                {
                    streams.Add(ParseStream(stream));
                }
            }

            var info = new MediaInfo { Streams = streams };
            if (root.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.Object)
            {
                info = info with
                {
                    FileName = GetString(format, "filename"),
                    FormatName = GetString(format, "format_name") ?? "",
                    DurationSeconds = GetDouble(format, "duration"),
                    SizeBytes = GetLong(format, "size"),
                    BitRate = GetLong(format, "bit_rate"),
                };
            }

            return info;
        }
    }

    private static MediaStream ParseStream(JsonElement stream)
    {
        var tags = stream.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Object ? t : default;
        var disposition = stream.TryGetProperty("disposition", out var d) && d.ValueKind == JsonValueKind.Object ? d : default;

        return new MediaStream
        {
            Index = (int)(GetLong(stream, "index") ?? -1),
            Type = ParseType(GetString(stream, "codec_type")),
            CodecName = GetString(stream, "codec_name"),
            Profile = GetString(stream, "profile"),
            Width = (int?)GetLong(stream, "width"),
            Height = (int?)GetLong(stream, "height"),
            PixelFormat = GetString(stream, "pix_fmt"),
            Channels = (int?)GetLong(stream, "channels"),
            Language = GetTag(tags, "language"),
            AttachmentFileName = GetTag(tags, "filename"),
            AttachmentMimeType = GetTag(tags, "mimetype"),
            IsAttachedPicture = (GetLong(disposition, "attached_pic") ?? 0) == 1,
        };
    }

    private static StreamType ParseType(string? codecType) => codecType switch
    {
        "video" => StreamType.Video,
        "audio" => StreamType.Audio,
        "subtitle" => StreamType.Subtitle,
        "attachment" => StreamType.Attachment,
        "data" => StreamType.Data,
        _ => StreamType.Unknown,
    };

    // Matroska tag keys arrive in either case depending on the muxer, so match without case.
    private static string? GetTag(JsonElement tags, string name)
    {
        if (tags.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in tags.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    // ffprobe writes most numbers as strings ("duration": "1420.512000") and a few as numbers.
    private static double? GetDouble(JsonElement element, string name)
    {
        var text = GetString(element, name);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value)
            ? value
            : null;
    }

    private static long? GetLong(JsonElement element, string name)
    {
        var text = GetString(element, name);
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d)
            ? (long)d
            : null;
    }
}
