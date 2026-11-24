using ShrinkRay.Core.Media;
using ShrinkRay.Core.Settings;

namespace ShrinkRay.Core.Encode;

/// <summary>The ffmpeg run that copies MKV font attachments from the source into the encode.</summary>
public sealed record FontRestorePlan(string EncodedPath, string SourcePath, string OutputPath, int AttachmentCount, IReadOnlyList<string> Arguments);

public static class FontRestorePlanner
{
    /// <summary>
    /// HandBrake drops MKV attachments, and styled subtitles need their fonts. Returns null when
    /// nothing needs restoring: an MP4 output, a source with no attachments, or the setting off.
    /// </summary>
    public static FontRestorePlan? Plan(MediaInfo source, string encodedPath, string sourcePath, string outputPath, ShrinkRaySettings settings)
    {
        if (!settings.RestoreFonts || Container.Detect(source) != ContainerKind.Mkv)
        {
            return null;
        }

        var attachments = source.AttachmentStreams.Count();
        if (attachments == 0)
        {
            return null;
        }

        string[] args =
        [
            "-hide_banner", "-nostdin", "-v", "error", "-y",
            "-i", encodedPath,
            "-i", sourcePath,
            "-map", "0",
            "-map", "1:t",
            "-c", "copy",
            outputPath,
        ];
        return new FontRestorePlan(encodedPath, sourcePath, outputPath, attachments, args);
    }
}
