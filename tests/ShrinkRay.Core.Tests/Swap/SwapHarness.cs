using ShrinkRay.Core.Settings;
using ShrinkRay.Core.Swap;
using ShrinkRay.Core.Tests.Support;
using ShrinkRay.Core.Verification;

namespace ShrinkRay.Core.Tests.Swap;

/// <summary>A fake share and scratch folder with one episode verified and ready to swap.</summary>
internal sealed class SwapHarness
{
    public const string ShowFolder = @"X:\TV\Example Show";
    public const string Original = ShowFolder + @"\Season 01\Example Show S01E01.mkv";
    public const string Encoded = @"C:\Scratch\job\final.mkv";
    public const string OriginalContent = "ORIGINAL h264 bytes, larger";
    public const string NewContent = "NEW hevc bytes";

    public static readonly VerificationResult Passed = new(VerificationOutcome.Pass, [], 0.5);

    public CrashSwitch Crash { get; } = new();
    public InMemoryFileSystem Fs { get; }
    public InMemoryJournalStore Store { get; }
    public FakeFinalCheck Check { get; } = new();
    public ShrinkRaySettings Settings { get; } = new();
    public SwapJournal Journal { get; private set; } = null!;
    public SwapRecovery Recovery { get; private set; } = null!;
    public SwapExecutor Executor { get; private set; } = null!;

    public SwapHarness(bool verified = true)
    {
        Fs = new InMemoryFileSystem(Crash);
        Store = new InMemoryJournalStore(Crash);
        Fs.Write(Original, OriginalContent);
        Fs.Write(Encoded, NewContent);
        Start();
        if (verified)
        {
            foreach (var state in new[] { SwapState.Queued, SwapState.CopiedToScratch, SwapState.Encoded, SwapState.Verified })
            {
                Journal.Record(Original, state);
            }
        }
    }

    /// <summary>Simulates a fresh process: the journal reloads from its store.</summary>
    public void Restart()
    {
        Crash.Restart();
        Start();
    }

    public Task<SwapResult> SwapAsync(CancellationToken cancellationToken = default) =>
        Executor.SwapAsync(Original, Encoded, Passed, Settings, cancellationToken);

    public Task<IReadOnlyList<RecoveryAction>> RecoverAsync() =>
        Recovery.RecoverAsync([ShowFolder], CancellationToken.None);

    public SwapState? State => Journal.CurrentState(Original);

    public bool TempExists => Fs.FileExists(SwapPaths.Temp(Original));

    public bool BackupExists => Fs.FileExists(SwapPaths.Backup(Original));

    private void Start()
    {
        Journal = new SwapJournal(Store);
        Recovery = new SwapRecovery(Fs, Journal, Check);
        Executor = new SwapExecutor(Fs, Journal, Recovery);
    }
}
