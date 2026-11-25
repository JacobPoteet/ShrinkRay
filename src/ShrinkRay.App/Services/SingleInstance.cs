using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ShrinkRay.App.Services;

/// <summary>
/// Lets one ShrinkRay run per data folder. Startup recovery settles whatever the journal shows
/// as unfinished, so a second copy would cancel the first one's episode, clear its scratch folder
/// and undo a swap partway through. Claim the folder before anything reads it.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>Held by every running copy, whatever its data folder. The installer checks it.</summary>
    public const string RunningMutexName = "ShrinkRay.Running";

    private const int AnyProcess = -1;

    private readonly Mutex _claim;
    private readonly Mutex _running;
    private readonly EventWaitHandle _show;
    private RegisteredWaitHandle? _listener;

    private SingleInstance(Mutex claim, EventWaitHandle show)
    {
        _claim = claim;
        _show = show;
        _running = new Mutex(initiallyOwned: false, RunningMutexName);
    }

    /// <summary>
    /// Claims <paramref name="dataFolder"/>. Null means another copy holds it; that copy has been
    /// asked to show itself, and this one should exit.
    /// </summary>
    public static SingleInstance? TryClaim(string dataFolder)
    {
        var name = NameFor(dataFolder);

        // Nobody takes ownership: the name existing is the claim, and Windows drops it with the
        // last handle, so a crashed copy leaves nothing behind.
        var claim = new Mutex(initiallyOwned: false, name, out var first);
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".show");
        if (first)
        {
            return new SingleInstance(claim, show);
        }

        // The user just started this copy, so it may hand the foreground to the first one.
        AllowSetForegroundWindow(AnyProcess);
        show.Set();
        show.Dispose();
        claim.Dispose();
        return null;
    }

    /// <summary>Calls <paramref name="show"/>, on a pool thread, each time a second copy is refused.</summary>
    public void OnShowRequested(Action show) =>
        _listener = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) => show(), null, Timeout.Infinite, executeOnlyOnce: false);

    public void Dispose()
    {
        _listener?.Unregister(null);
        _show.Dispose();
        _running.Dispose();
        _claim.Dispose();
    }

    private static string NameFor(string dataFolder)
    {
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataFolder)).ToUpperInvariant();
        return @"Global\ShrinkRay." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder)));
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
