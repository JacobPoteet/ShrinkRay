# Project page

`index.html` is the GitHub Pages site, served from `main /docs` with no build step. Open it
through a local server, since the comparison loads `cq/results.json` with `fetch`:

```powershell
python -m http.server 8765 --directory docs
```

| Path | Holds |
|---|---|
| `fonts/` | The theme's fonts, copied from `src/ShrinkRay.Theme/Fonts` with their license files |
| `img/app/` | Off-screen renders of the deck and the component gallery, as lossless WebP |
| `cq/results.json` | Sizes, VMAF and speed for each test clip and CQ, plus the label, kind and reason the page shows |
| `cq/<id>/` | Matched frames for each clip: `src_<s>.webp` and `cq<NN>_<s>.webp`, where `<s>` is seconds into the clip |

## Swapping the comparison clips

The page builds the clip labels, the wipe tool, the table and the 2x crops from
`cq/results.json`. To use new clips, replace the `cq/<id>/` folders and `results.json`. Each
clip entry takes `id`, `name`, `srcMB` and `encodes`, and optionally `label`, `kind`, `why`,
`shots` (frame times, default `[45, 120]`) and `zoom` (`shot`, plus `x` and `y` from 0 to 1 for
the 2x crop centre). The prose around the table quotes the current numbers, so reread it after
a swap.

## Refreshing the app captures

The captures come from a small WPF harness that opens the deck and the gallery off-screen
against an in-memory demo library at `D:\Media\TV`, named after the Blender open movies. It
renders each window's content to a 96 dpi bitmap and converts it to WebP with ffmpeg. Look at
every PNG before you commit the WebP files: no real show titles, share paths or hostnames.
