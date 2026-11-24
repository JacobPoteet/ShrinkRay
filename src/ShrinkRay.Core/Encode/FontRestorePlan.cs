using ShrinkRay.Core.Media;
using ShrinkRay.Core.Settings;

namespace ShrinkRay.Core.Encode;

/// <summary>The ffmpeg run that copies MKV font attachments from the source into the encode.</summary>
public sealed record FontRestorePlan(string EncodedPath, string SourcePath, string OutputPath, int AttachmentCount, IReadOnlyList<string> Arguments);

public static class FontRestorePlanner
{
    /// <summary>
    /// Styled subtitles need the source's font attachments. HandBrakeCLI 1.11.2 keeps them, so
    /// the restore runs only when the encode came out with fewer attachments than the source.
    /// Returns null when nothing needs restoring: MP4 output, a source with no attachments, an
    /// encode that kept them all, or the setting off.
    /// </summary>
    /// <param name="encoded">The probe of HandBrake's output.</param>
    public static FontRestorePlan? Plan(
        MediaInfo source,
        MediaInfo encoded,
        string encodedPath,
        string sourcePath,
        string outputPath,
        ShrinkRaySettings settings)
    {
        if (!settings.RestoreFonts || Container.Detect(source) != ContainerKind.Mkv)
        {
            return null;
        }

        var attachments = source.AttachmentStreams.Count();
        if (attachments == 0 || encoded.AttachmentStreams.Count() >= attachments)
        {
            return null;
        }

        // -map 0 keeps the encode's streams, including any attachments it kept, so map only
        // the source's attachments when the encode has none. Otherwise copy the encode's video,
        // audio and subtitles and take every attachment from the source.
        string[] args =
        [
            "-hide_banner", "-nostdin", "-v", "error", "-y",
            "-i", encodedPath,
            "-i", sourcePath,
            .. encoded.AttachmentStreams.Any()
                ? new[] { "-map", "0", "-map", "-0:t", "-map", "1:t" }
                : new[] { "-map", "0", "-map", "1:t" },
            "-c", "copy",
            outputPath,
        ];
        return new FontRestorePlan(encodedPath, sourcePath, outputPath, attachments, args);
    }
}
