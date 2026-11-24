# ShrinkRay

Follows the workflow standard in the user CLAUDE.md: squash merges, `/ship` to land a PR,
`/release` to cut one, bugs fixed when found. Deviations are listed at the end.

## What this is

ShrinkRay is a Windows desktop app that re-encodes a Plex TV library from H.264 to H.265
with HandBrakeCLI and NVIDIA NVENC. It swaps each new file in under the original filename so
Plex keeps its metadata and watch history. The user picks one show at a time: a show folder
with season subfolders. Movies may follow later.

Planned stack: WPF on .NET 8 (`net8.0-windows`) with a fully custom theme. WPF-UI is dropped.
External tools: HandBrakeCLI encodes, ffprobe scans and checks, ffmpeg copies MKV font
attachments back in after an encode and runs the full-decode check.

The engine, `ShrinkRay.Core`, exists. The WPF app does not yet.

## Build and test

Run dotnet from PowerShell. The projects target `net8.0`; any SDK from 8 up builds them.

```powershell
dotnet build ShrinkRay.sln -c Release
dotnet test ShrinkRay.sln -c Release
```

`Directory.Build.props` turns warnings into errors. Two integration tests call the real
ffmpeg and ffprobe on a clip generated in a temp folder. They skip unless you set
`SHRINKRAY_INTEGRATION=1`, and they need both tools on PATH:

```powershell
$env:SHRINKRAY_INTEGRATION = '1'; dotnet test ShrinkRay.sln -c Release --filter "FullyQualifiedName~Integration"
```

CI (`.github/workflows/ci.yml`) builds and tests in Release on `windows-latest`.

## Architecture

`src/ShrinkRay.Core` keeps logic pure and puts every process call and file operation behind
an interface, so tests run against fakes. Folders match namespaces:

| Folder | Holds |
|---|---|
| `Media` | `MediaInfo`, `FfprobeParser`, `Container` detection, `Eligibility` rules, `IMediaProbe` |
| `Encode` | `EncodePlanner` (HandBrakeCLI arguments), `AudioPlanner`, `HandBrakeProgressParser`, `FontRestorePlanner` |
| `Verification` | `OutputVerifier` (pass, keep original, or fail with reasons), `IDecodeChecker` |
| `Swap` | `SwapJournal` state machine, `SwapExecutor`, `SwapRecovery`, `IFileSystem` |
| `Settings` | `ShrinkRaySettings`, `SettingsStore` (`%APPDATA%\ShrinkRay\settings.json`) |
| `Processes` | `IProcessRunner`, `ProcessRunner`, and the ffprobe, HandBrakeCLI and ffmpeg wrappers |
| `Pipeline` | `EpisodePipeline`, which runs one episode from probe to swap |

The swap journal is the part that protects the library. Keep these properties when you change it:

- The journal writes each state to disk before the step it names runs. An entry means the
  step started; recovery reads the disk to learn whether it finished.
- The swap order is fixed: copy to `name.shrinkray.tmp`, rename the original to
  `name.shrinkray.bak`, rename the temp to the original name, delete the backup.
- Recovery runs on startup, before any encode. Each branch orders its steps so a crash
  during recovery leaves a state the next run handles. When the disk matches no known
  state, recovery reports `NeedsReview` and changes nothing.
- `CrashRecoveryTests` kills the swap after each durable operation, and again during
  recovery. A change to the swap or recovery must keep those tests passing, and a new
  durable step needs a new crash point.

Test fixtures are synthetic ffprobe JSON in `tests/ShrinkRay.Core.Tests/Fixtures`. Fake
paths use drive letters such as `X:\TV`.

## Rules that protect the library

The app deletes originals on a live Plex library, so these hold everywhere:

- Delete or overwrite an original only after its new file passes every check: duration,
  stream counts, a full decode, and the minimum-savings threshold.
- Keep the filename and the container. An `.mp4` stays `.mp4`, an `.mkv` stays `.mkv`.
- Encode and verify in a local scratch folder. The share only ever receives a finished,
  verified file, written under a temporary name and renamed over the original.
- Test against copies in a local folder. Touch the real library only when Jacob asks.
- Keep share paths, hostnames and IP addresses out of tracked files. They belong in local
  settings.

## Verify a change

1. `dotnet build ShrinkRay.sln -c Release` with no warnings.
2. `dotnet test ShrinkRay.sln -c Release` with every test passing.
3. For changes to the probe, font restore or decode check, run the integration tests too.
4. For changes to encode arguments, run HandBrakeCLI by hand on a copied episode in a local
   folder and check the result with ffprobe. Never point a test at the share.

## Documentation

The wiki holds the spec, measured encode numbers and the Decision Log. Its path goes in the
gitignored `.claude/wiki-path.local`.

## Deviations from the standard

None.
