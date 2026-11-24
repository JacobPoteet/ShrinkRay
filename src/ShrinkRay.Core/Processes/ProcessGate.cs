using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ShrinkRay.Core.Processes;

/// <summary>Holds and releases the external tools a run has started, for the Pause key.</summary>
public interface IProcessGate
{
    bool IsPaused { get; }

    /// <summary>Suspends every running tool, and any tool started while paused.</summary>
    void Pause();

    void Resume();
}

/// <summary>
/// Suspends tool processes that <see cref="ProcessRunner"/> starts. A paused HandBrakeCLI
/// stops mid-frame and carries on from the same frame when resumed. File copies are managed
/// code, so a copy in progress finishes and the next tool then starts suspended.
/// </summary>
public sealed class ProcessGate : IProcessGate
{
    private readonly object _gate = new();
    private readonly HashSet<Process> _running = [];
    private bool _paused;

    public bool IsPaused
    {
        get
        {
            lock (_gate)
            {
                return _paused;
            }
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_paused)
            {
                return;
            }

            _paused = true;
            foreach (var process in _running)
            {
                Suspend(process);
            }
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (!_paused)
            {
                return;
            }

            _paused = false;
            foreach (var process in _running)
            {
                ResumeProcess(process);
            }
        }
    }

    internal void Started(Process process)
    {
        lock (_gate)
        {
            _running.Add(process);
            if (_paused)
            {
                Suspend(process);
            }
        }
    }

    internal void Exited(Process process)
    {
        lock (_gate)
        {
            _running.Remove(process);
        }
    }

    private static void Suspend(Process process) => Call(process, NtSuspendProcess);

    private static void ResumeProcess(Process process) => Call(process, NtResumeProcess);

    private static void Call(Process process, Func<IntPtr, int> action)
    {
        try
        {
            if (!process.HasExited)
            {
                action(process.Handle);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process exited between the check and the call.
        }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);
}
