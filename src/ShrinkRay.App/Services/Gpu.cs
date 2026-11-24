using System.Globalization;
using ShrinkRay.Core.Processes;

namespace ShrinkRay.App.Services;

/// <summary>Reads the NVIDIA card's name and NVENC load. The real one runs nvidia-smi.</summary>
public interface IGpuMonitor
{
    /// <summary>A short name such as <c>RTX 2070S</c>, or null without an NVIDIA card.</summary>
    Task<string?> NameAsync(CancellationToken cancellationToken);

    /// <summary>NVENC utilization in percent, or null when it cannot be read.</summary>
    Task<double?> EncoderLoadAsync(CancellationToken cancellationToken);
}

public sealed class NvidiaSmiMonitor : IGpuMonitor
{
    private readonly IProcessRunner _runner;

    public NvidiaSmiMonitor(IProcessRunner runner)
    {
        _runner = runner;
    }

    public async Task<string?> NameAsync(CancellationToken cancellationToken)
    {
        var line = await QueryAsync("name", cancellationToken);
        return line is null ? null : ShortName(line);
    }

    public async Task<double?> EncoderLoadAsync(CancellationToken cancellationToken)
    {
        var line = await QueryAsync("utilization.encoder", cancellationToken);
        return double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    /// <summary>Turns <c>NVIDIA GeForce RTX 2070 SUPER</c> into <c>RTX 2070S</c>, as the silkscreen prints it.</summary>
    public static string ShortName(string name)
    {
        var s = name.Trim();
        foreach (var prefix in new[] { "NVIDIA ", "GeForce " })
        {
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                s = s[prefix.Length..];
            }
        }

        return s.Replace(" SUPER", "S", StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
    }

    private async Task<string?> QueryAsync(string field, CancellationToken cancellationToken)
    {
        try
        {
            var spec = new ProcessSpec("nvidia-smi", ["--query-gpu=" + field, "--format=csv,noheader,nounits"], CaptureStandardOutput: true);
            var result = await _runner.RunAsync(spec, cancellationToken).ConfigureAwait(false);
            var line = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            return result.ExitCode == 0 && !string.IsNullOrEmpty(line) ? line : null;
        }
        catch (ExternalToolException)
        {
            return null;
        }
    }
}
