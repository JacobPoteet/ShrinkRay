using ShrinkRay.Core.Processes;

namespace ShrinkRay.Core.Tests.Support;

/// <summary>Returns a canned result and feeds canned lines to the spec's callbacks.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    public List<ProcessSpec> Calls { get; } = [];
    public int ExitCode { get; set; }
    public List<string> StandardOutputLines { get; } = [];
    public List<string> StandardErrorLines { get; } = [];

    public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add(spec);
        foreach (var line in StandardOutputLines)
        {
            spec.OnStandardOutputLine?.Invoke(line);
        }

        foreach (var line in StandardErrorLines)
        {
            spec.OnStandardErrorLine?.Invoke(line);
        }

        var stdout = spec.CaptureStandardOutput ? string.Join("\n", StandardOutputLines) : "";
        return Task.FromResult(new ProcessResult(ExitCode, stdout, StandardErrorLines.ToList()));
    }
}
