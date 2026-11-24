using System.Diagnostics;
using System.Text;

namespace ShrinkRay.Core.Processes;

/// <param name="FileName">The executable. A bare name is looked up on PATH.</param>
/// <param name="Arguments">Passed one by one, so paths with spaces need no quoting.</param>
/// <param name="CaptureStandardOutput">Keep all of stdout in the result. Off for chatty tools like HandBrake.</param>
public sealed record ProcessSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    bool CaptureStandardOutput = false,
    Action<string>? OnStandardOutputLine = null,
    Action<string>? OnStandardErrorLine = null);

/// <param name="StandardOutput">All of stdout when captured, otherwise empty.</param>
/// <param name="StandardErrorTail">The last lines of stderr, for error messages.</param>
public sealed record ProcessResult(int ExitCode, string StandardOutput, IReadOnlyList<string> StandardErrorTail);

/// <summary>Runs an external tool and streams its output line by line.</summary>
public interface IProcessRunner
{
    /// <exception cref="ExternalToolException">The tool could not be started.</exception>
    /// <exception cref="OperationCanceledException">Cancelled. The process tree is killed first.</exception>
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken);
}

public sealed class ExternalToolException : Exception
{
    public ExternalToolException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }

    public static ExternalToolException Exited(string tool, ProcessResult result)
    {
        var tail = result.StandardErrorTail.Count > 0 ? ": " + string.Join(" | ", result.StandardErrorTail.TakeLast(5)) : "";
        return new ExternalToolException($"{tool} exited with code {result.ExitCode}{tail}");
    }
}

public sealed class ProcessRunner : IProcessRunner
{
    private const int StandardErrorTailLines = 40;
    private readonly ProcessGate? _gate;

    /// <param name="gate">Lets the Pause key suspend the tools this runner starts. Optional.</param>
    public ProcessRunner(ProcessGate? gate = null)
    {
        _gate = gate;
    }

    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo(spec.FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in spec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ExternalToolException($"Could not start {spec.FileName}: {ex.Message}", ex);
        }

        _gate?.Started(process);
        try
        {
            return await ReadToEndAsync(process, spec, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate?.Exited(process);
        }
    }

    private static async Task<ProcessResult> ReadToEndAsync(Process process, ProcessSpec spec, CancellationToken cancellationToken)
    {
        var stdout = new StringBuilder();
        var stderrTail = new Queue<string>();
        var stdoutTask = LineReader.ReadLinesAsync(process.StandardOutput, line =>
        {
            if (spec.CaptureStandardOutput)
            {
                stdout.Append(line).Append('\n');
            }

            spec.OnStandardOutputLine?.Invoke(line);
        });
        var stderrTask = LineReader.ReadLinesAsync(process.StandardError, line =>
        {
            stderrTail.Enqueue(line);
            if (stderrTail.Count > StandardErrorTailLines)
            {
                stderrTail.Dequeue();
            }

            spec.OnStandardErrorLine?.Invoke(line);
        });

        using (cancellationToken.Register(() => Kill(process)))
        {
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new ProcessResult(process.ExitCode, stdout.ToString(), stderrTail.ToList());
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Exiting while we tried to kill it.
        }
    }
}

public static class LineReader
{
    /// <summary>
    /// Calls <paramref name="onLine"/> for each line. Lines may end in <c>\n</c>, <c>\r\n</c> or a
    /// bare <c>\r</c>, which HandBrake uses to redraw its progress line.
    /// </summary>
    public static async Task ReadLinesAsync(TextReader reader, Action<string> onLine)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (line.Length > 0)
            {
                onLine(line);
            }
        }
    }
}
