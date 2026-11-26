using ShrinkRay.Core.Processes;

namespace ShrinkRay.App.Services;

/// <summary>
/// Logs what a tool said when it failed. The journal keeps the last few lines; the log keeps the
/// whole tail and the command that produced it.
/// </summary>
public sealed class LoggingProcessRunner(IProcessRunner inner, Action<string> log) : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
    {
        var result = await inner.RunAsync(spec, cancellationToken).ConfigureAwait(false);
        var tool = Path.GetFileNameWithoutExtension(spec.FileName);

        // ffmpeg runs at the error level, so anything it wrote is an error. HandBrake is chatty.
        var complained = tool.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase) && result.StandardErrorTail.Count > 0;
        if (result.ExitCode != 0 || complained)
        {
            log($"{tool} ended with code {result.ExitCode}\n{string.Join(' ', spec.Arguments)}\n{string.Join('\n', result.StandardErrorTail)}");
        }

        return result;
    }
}
