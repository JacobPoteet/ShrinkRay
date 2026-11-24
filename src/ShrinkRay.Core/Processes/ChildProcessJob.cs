using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ShrinkRay.Core.Processes;

/// <summary>
/// A Windows job object set to kill every process in it when its last handle closes. ShrinkRay
/// holds the only handle, so when ShrinkRay exits for any reason, a crash or a Task Manager kill
/// included, Windows closes the handle and takes HandBrakeCLI, ffmpeg and ffprobe down with it.
/// Create one for the app's lifetime and keep a reference to it: disposing it, or letting it be
/// collected, kills the tools in it. On other systems it does nothing.
/// </summary>
public sealed class ChildProcessJob : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;

    private readonly SafeJobHandle? _job;

    /// <exception cref="System.ComponentModel.Win32Exception">Windows refused to create or configure the job.</exception>
    public ChildProcessJob()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create a job object for the tools.");
        }

        var info = new ExtendedLimitInformation { BasicLimitInformation = new BasicLimitInformation { LimitFlags = KillOnJobClose } };
        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, (uint)Marshal.SizeOf<ExtendedLimitInformation>()))
        {
            var error = Marshal.GetLastWin32Error();
            job.Dispose();
            throw new System.ComponentModel.Win32Exception(error, "Could not set the tools' job object to close with ShrinkRay.");
        }

        _job = job;
    }

    /// <summary>A new job, or null when Windows refuses one. The tools then run without it, as before.</summary>
    public static ChildProcessJob? TryCreate()
    {
        try
        {
            return new ChildProcessJob();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Puts a started process in the job. A child the process starts after this joins the job
    /// too. Returns false when the process has already exited or Windows refused, in which case
    /// the process still runs and Stop still kills it, but a crash of ShrinkRay would not.
    /// </summary>
    public bool Assign(Process process)
    {
        if (_job is null || _job.IsClosed)
        {
            return false;
        }

        try
        {
            return AssignProcessToJobObject(_job, process.Handle);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process exited before it could be assigned.
            return false;
        }
    }

    /// <summary>Closes the job, which kills every process still in it.</summary>
    public void Dispose() => _job?.Dispose();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeJobHandle CreateJobObject(IntPtr securityAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeJobHandle job, int infoClass, ref ExtendedLimitInformation info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeJobHandle job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
