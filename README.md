# ShrinkRay

A Windows desktop app that re-encodes a Plex TV library from H.264 to H.265 on an NVIDIA GPU. It writes each new file back under the original filename, so Plex keeps your metadata and watch history.

Status: planning. Nothing to run yet.

## Planned

- Point it at a library folder, local or on a network share, and pick shows or seasons from a tree.
- It probes every file first, skips anything already in HEVC, and estimates the space you'll save.
- HandBrakeCLI encodes with NVENC in a local scratch folder. ShrinkRay checks the result and only then replaces the original.

Requires Windows 10 or 11, an NVIDIA GPU with HEVC NVENC, HandBrakeCLI, and ffmpeg.

## License

MIT
