# ShrinkRay

Follows the workflow standard in the user CLAUDE.md: squash merges, `/ship` to land a PR,
`/release` to cut one, bugs fixed when found. Deviations are listed at the end.

## What this is

ShrinkRay is a Windows desktop app that re-encodes a Plex TV library from H.264 to H.265
with HandBrakeCLI and NVIDIA NVENC. It swaps each new file in under the original filename so
Plex keeps its metadata and watch history. The user picks one show at a time: a show folder
with season subfolders. Movies may follow later.

Stack: WPF on .NET 8 (`net8.0-windows`) with a fully custom theme. WPF-UI is dropped.
External tools: HandBrakeCLI encodes, ffprobe scans and checks, ffmpeg runs the full-decode
check and copies MKV font attachments back in when an encode comes out with fewer than the
source. HandBrakeCLI 1.11.2 keeps them, so that remux usually does not run.

`ShrinkRay.Core` is the engine, `ShrinkRay.Theme` the control kit (shown off by
`ShrinkRay.Gallery`), and `ShrinkRay.App` the application, `ShrinkRay.exe`: one deck window
that loads a show and runs its episodes through the engine.

## Build and test

Run dotnet from PowerShell. Core targets `net8.0`; the theme, gallery, app and their tests
target `net8.0-windows`. Any SDK from 8 up builds them.

```powershell
dotnet build ShrinkRay.sln -c Release
dotnet test ShrinkRay.sln -c Release
```

`Directory.Build.props` turns warnings into errors. The integration tests call the real
ffmpeg, ffprobe and HandBrakeCLI on clips generated in a temp folder. They skip unless you set
`SHRINKRAY_INTEGRATION=1`, need ffmpeg and ffprobe on PATH, and the HandBrakeCLI ones skip when
HandBrakeCLI is not found and need an NVENC card. One more encodes a clip whose video carries
closed captions, read in place from the path in `SHRINKRAY_CC_CLIP`; it skips when that is unset.
Keep that clip out of the repo:

```powershell
$env:SHRINKRAY_INTEGRATION = '1'; dotnet test ShrinkRay.sln -c Release --filter "FullyQualifiedName~Integration"
```

CI (`.github/workflows/ci.yml`) checks that every embedded font has its license file, then
builds and tests in Release on `windows-latest`.

Open the theme gallery, one window with every control:

```powershell
dotnet run --project src/ShrinkRay.Gallery
```

It takes `--phosphor Amber|Green|Ice`, `--chassis Beige|Graphite` and `--still` (reduced
motion), which helps when you screenshot a variant.

Run the app against a test library, never your real settings:

```powershell
dotnet run --project src/ShrinkRay.App -- --data scratch\main-check\data --still
```

`--data` keeps settings, the journal, the probe cache and the ledger in that folder instead
of `%LOCALAPPDATA%\ShrinkRay`. Build a test library under `scratch\fakelib` (gitignored) from
copied clips, laid out as `Show\Season 01\Show - S01E01.mkv`, and choose it as the library.

Install the current checkout for Jacob, with no release or version bump:

```powershell
powershell -ExecutionPolicy Bypass -File tools/install-local.ps1
```

It publishes Release into `%LOCALAPPDATA%\Programs\ShrinkRay`, adds a Start menu shortcut and
records the commit in `install.txt` and the exe's version. It refuses while ShrinkRay is running,
since an encode may be mid-swap, and never touches `%LOCALAPPDATA%\ShrinkRay`, where settings and
the journal live. `gitview.toml` lists the build, test, run and install tasks GitView shows.

## Architecture

`src/ShrinkRay.Core` keeps logic pure and puts every process call and file operation behind
an interface, so tests run against fakes. Folders match namespaces:

| Folder | Holds |
|---|---|
| `Media` | `MediaInfo`, `FfprobeParser`, `Container` detection, `Eligibility` rules, `IMediaProbe` |
| `Encode` | `EncodePlanner` (HandBrakeCLI arguments), `AudioPlanner`, `HandBrakeProgressParser`, `FontRestorePlanner` |
| `Verification` | `OutputVerifier` (pass, keep original, or fail with reasons), `IDecodeChecker` |
| `Swap` | `SwapJournal` state machine, `SwapExecutor`, `SwapRecovery`, `IFileSystem` |
| `Settings` | `ShrinkRaySettings`, `SettingsStore` (`%LOCALAPPDATA%\ShrinkRay\settings.json`) |
| `Processes` | `IProcessRunner`, `ProcessRunner`, `ProcessGate` (Pause), `ToolLocator`, and the ffprobe, HandBrakeCLI and ffmpeg wrappers |
| `Pipeline` | `EpisodePipeline`, which runs one episode from probe to swap, and `EncodeQueue`, which runs a show's episodes one at a time |
| `Library` | `LibraryScanner` and `ProbeCache`, `EpisodeName` parsing, `ShowSummary` and episode status, `SizeEstimate`, `ShrinkLedger` |

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

### App

`src/ShrinkRay.App` is MVVM without a framework. `MainViewModel` holds the deck's state and
talks to the engine only through `DeckServices`, so `tests/ShrinkRay.App.Tests` drives it
against an in-memory library, a scripted episode runner and a fake gate. `Composition` wires
the real services. Keep logic in Core or the view model; the window's code-behind handles
chrome, drag and drop, and the half-second tick only.

Startup order matters: `StartAsync` runs journal recovery, then scans the library, and Encode
stays off until recovery ends. Each run takes a snapshot of the settings, so the knob and the
service panel lock while an encode runs.

### Theme

`src/ShrinkRay.Theme` holds the cassette-futurism look: tokens, embedded fonts, panel styles
and custom controls. An app merges `<sr:ShrinkRayTheme />` (xmlns `urn:shrinkray-theme`) and
switches palettes with `ThemeManager`. Geometry and arithmetic for the controls live in
`Mechanics` so `tests/ShrinkRay.Theme.Tests` can cover them without a window.
`.claude/rules/theme.md` carries the house rules and loads when you touch the theme.
Keep the house rules intact: phosphor is the only light, and signal red marks only Encode and
anything that deletes.

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
3. For changes to the probe, font restore, decode check or verifier, run the integration tests
   too, with `SHRINKRAY_CC_CLIP` set when captions are involved.
4. For changes to encode arguments, run the integration tests, then HandBrakeCLI by hand on a
   copied episode in a local folder and check the result with ffprobe. Never point a test at
   the share.
5. For changes to the theme, open the gallery, screenshot it, and compare it with the design
   boards. Check Amber on Beige and Green on Graphite at least.
6. For changes to the app, render the main window and compare it with the main deck board,
   in Amber on Beige and Green on Graphite at least. On-screen captures of WPF windows can
   come out white here, so render off-screen: a small exe that references the app, opens
   `MainWindow` at Left/Top -10000 and draws `window.Content` to a `RenderTargetBitmap` at
   96 dpi. For changes to the run, drive one real encode through the view model on a copy
   of the test library.

## Documentation

The wiki holds the spec, measured encode numbers and the Decision Log. Its path goes in the
gitignored `.claude/wiki-path.local`.

## Deviations from the standard

None.
