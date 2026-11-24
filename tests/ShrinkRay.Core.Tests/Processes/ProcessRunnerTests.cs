using System.Diagnostics;
using ShrinkRay.Core.Processes;

namespace ShrinkRay.Core.Tests.Processes;

/// <summary>Runs cmd.exe and PowerShell, which every Windows machine and CI runner has.</summary>
public class ProcessRunnerTests
{
    private static readonly ProcessRunner Runner = new();

    [Fact]
    public async Task Streams_stdout_lines_and_returns_the_exit_code()
    {
        var lines = new List<string>();
        var spec = new ProcessSpec("cmd.exe", ["/c", "echo one& echo two& exit 3"], CaptureStandardOutput: true, OnStandardOutputLine: lines.Add);

        var result = await Runner.RunAsync(spec, CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal(["one", "two"], lines);
        Assert.Equal("one\ntwo\n", result.StandardOutput);
    }

    [Fact]
    public async Task Keeps_stderr_tail()
    {
        var result = await Runner.RunAsync(new ProcessSpec("cmd.exe", ["/c", "echo bad 1>&2"]), CancellationToken.None);

        Assert.Equal(["bad "], result.StandardErrorTail);
        Assert.Equal("", result.StandardOutput);
    }

    [Fact]
    public async Task Arguments_with_spaces_are_quoted_as_one()
    {
        // cmd's echo prints its raw command line, so the quotes the runner added are visible.
        var spec = new ProcessSpec("cmd.exe", ["/c", "echo", @"C:\scratch dir\in file.mkv"], CaptureStandardOutput: true);

        var result = await Runner.RunAsync(spec, CancellationToken.None);

        Assert.Equal("\"C:\\scratch dir\\in file.mkv\"\n", result.StandardOutput);
    }

    [Fact]
    public async Task Missing_executable_throws_external_tool_exception()
    {
        await Assert.ThrowsAsync<ExternalToolException>(() =>
            Runner.RunAsync(new ProcessSpec("shrinkray-no-such-tool.exe", []), CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_kills_the_process()
    {
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource();
        var spec = new ProcessSpec("powershell.exe", ["-NoProfile", "-Command", "Write-Output ready; Start-Sleep -Seconds 60"],
            OnStandardOutputLine: _ => started.TrySetResult());

        var stopwatch = Stopwatch.StartNew();
        var run = Runner.RunAsync(spec, cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(45));
    }

    [Fact]
    public async Task Bare_carriage_returns_split_lines()
    {
        var lines = new List<string>();

        await LineReader.ReadLinesAsync(new StringReader("Encoding: 1 %\rEncoding: 2 %\r\nDone\n\nLast"), lines.Add);

        Assert.Equal(["Encoding: 1 %", "Encoding: 2 %", "Done", "Last"], lines);
    }
}
