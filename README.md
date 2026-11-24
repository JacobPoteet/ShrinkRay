# ShrinkRay

A Windows desktop app that re-encodes a Plex TV library from H.264 to H.265 on an NVIDIA GPU. It writes each new file back under the original filename, so Plex keeps your metadata and watch history.

Status: in development. The encode engine and the theme kit exist; the app itself does not yet. To see the look, run the theme gallery:

```powershell
dotnet run --project src/ShrinkRay.Gallery
```

## Planned

- Point it at a library folder, local or on a network share, and pick one show at a time.
- It probes every file first, skips anything already in HEVC, and estimates the space you'll save.
- HandBrakeCLI encodes with NVENC in a local scratch folder. ShrinkRay checks the result and only then replaces the original.

Requires Windows 10 or 11, an NVIDIA GPU with HEVC NVENC, HandBrakeCLI, and ffmpeg.

## License

MIT
