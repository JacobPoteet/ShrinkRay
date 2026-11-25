namespace ShrinkRay.Core.Setup;

/// <summary>The commands setup shows for a missing tool. ShrinkRay never runs them; the user does.</summary>
public static class ToolInstall
{
    public const string HandBrakeCli = "winget install --id HandBrake.HandBrake.CLI -e";

    /// <summary>One package carries both ffmpeg and ffprobe.</summary>
    public const string Ffmpeg = "winget install --id Gyan.FFmpeg -e";
}
