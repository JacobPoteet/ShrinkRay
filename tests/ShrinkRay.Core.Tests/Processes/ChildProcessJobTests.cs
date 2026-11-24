using System.Diagnostics;
using ShrinkRay.Core.Processes;

namespace ShrinkRay.Core.Tests.Processes;

/// <summary>
/// Closing the job stands in for ShrinkRay dying: Windows closes the handle either way. The
/// children are PowerShell sleeps, which every Windows machine and CI runner has.
/// </summary>
public class ChildProcessJobTests
{
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Closing_the_job_kills_a_process_assigned_to_it()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var job = new ChildProcessJob();
        using var child = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -Command Start-Sleep -Seconds 60")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        try
        {
            Assert.True(job.Assign(child));

            job.Dispose();

            await child.WaitForExitAsync().WaitAsync(ExitWait);
            Assert.True(child.HasExited);
        }
        finally
        {
            KillIfRunning(child);
        }
    }

    [Fact]
    public async Task A_tool_the_runner_starts_dies_with_the_job()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var job = new ChildProcessJob();
        var runner = new ProcessRunner(job: job);
        var started = new TaskCompletionSource();
        var spec = new ProcessSpec("powershell.exe", ["-NoProfile", "-Command", "Write-Output ready; Start-Sleep -Seconds 60"],
            OnStandardOutputLine: _ => started.TrySetResult());

        var run = runner.RunAsync(spec, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        job.Dispose();

        // The sleep had nearly a minute left, so returning inside the wait means the job killed it.
        await run.WaitAsync(ExitWait);
    }

    [Fact]
    public void Assigning_after_the_job_closed_returns_false()
    {
        var job = new ChildProcessJob();
        job.Dispose();

        using var self = Process.GetCurrentProcess();
        Assert.False(job.Assign(self));
    }

    private static void KillIfRunning(Process process)
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
        }
    }
}
