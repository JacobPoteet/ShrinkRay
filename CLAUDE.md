# ShrinkRay

Follows the workflow standard in the user CLAUDE.md: squash merges, `/ship` to land a PR,
`/release` to cut one, bugs fixed when found. Deviations are listed at the end.

## What this is

ShrinkRay is a Windows desktop app that re-encodes a Plex TV library from H.264 to H.265
with HandBrakeCLI and NVIDIA NVENC. It swaps each new file in under the original filename so
Plex keeps its metadata and watch history. Movies may follow later.

Planned stack: WPF + [WPF-UI](https://github.com/lepoco/wpfui) on .NET 8 (`net8.0-windows`)
with CommunityToolkit.Mvvm, the same stack as ExplorerHelper. External tools: HandBrakeCLI
encodes, ffprobe scans and checks, ffmpeg copies MKV font attachments back in after an encode.

No code exists yet. Add build, test and architecture sections when the solution lands.

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

## Documentation

The wiki holds the spec, measured encode numbers and the Decision Log. Its path goes in the
gitignored `.claude/wiki-path.local`.

## Deviations from the standard

None.
