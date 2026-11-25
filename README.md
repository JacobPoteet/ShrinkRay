# ShrinkRay

A Windows desktop app that re-encodes a Plex TV library from H.264 to H.265 on an NVIDIA GPU. It writes each new file back under the original filename, so Plex keeps your metadata and watch history.

The [project page](https://jacobpoteet.github.io/ShrinkRay/) shows the deck at work, the system design, the crash-safe swap, the quality test behind the default setting, and the design. Its source is in `docs/`.

Status: early. Version 0.1 can replace your originals, so start with the trial run that first-run setup offers, which keeps them.

## Install

Download the installer or the zip from the [latest release](https://github.com/JacobPoteet/ShrinkRay/releases/latest). Both include .NET. The installer sets ShrinkRay up for your user with a Start menu shortcut and no admin prompt. The files are unsigned, so Windows SmartScreen may ask you to confirm.

## Build from source

Run it from a checkout:

```powershell
dotnet run --project src/ShrinkRay.App
```

Or build it and install it for your user, over a release install if you have one. Rerun this after you pull to pick up new changes:

```powershell
powershell -ExecutionPolicy Bypass -File tools/install-local.ps1
```

## How it works

- The first start walks you through setup on the deck: it checks for the tools, asks for your library and a scratch folder, asks whether to replace originals or keep them for a trial, gives a three-stop tour, and stops after one episode so you can check it.
- Choose a library folder, local or on a mapped network share, or drop a show folder on the window. Monitor A lists the shows that still have H.264 episodes.
- Pick a show, set its quality in Setup if the recommended CQ is not what you want, and press Encode. ShrinkRay probes every file first, skips anything already in HEVC, and estimates the space you'll get back.
- HandBrakeCLI encodes each episode with NVENC in a local scratch folder. ShrinkRay checks duration, streams and a full decode, and replaces the original only when every check passes and the file shrinks by at least 15 percent.

To see every control in the theme, run the gallery: `dotnet run --project src/ShrinkRay.Gallery`.

Requires Windows 10 or 11, an NVIDIA GPU with HEVC NVENC, HandBrakeCLI, and ffmpeg. Setup shows the `winget install` command for a missing tool.

## License

MIT
