# Project page

`index.html` is the GitHub Pages site, served from `main /docs` with no build step. Open it
through a local server, since the quality comparison loads `cq/results.json` with `fetch`:

```powershell
python -m http.server 8765 --directory docs
```

| Path | Holds |
|---|---|
| `fonts/` | The theme's fonts, copied from `src/ShrinkRay.Theme/Fonts` with their license files |
| `img/app/` | Off-screen renders for the theme picker (the deck mid-encode in six themes) and the component gallery (two themes), as lossless WebP |
| `cq/results.json` | Sizes, VMAF and speed for each test clip and CQ, plus the label and kind the page shows |
| `cq/<id>/` | Matched frames for each clip: `src_<s>.webp` and `cq<NN>_<s>.webp`, where `<s>` is seconds into the clip |

## What the page is built from

The hero deck, the system diagram and the swap scrubber are plain HTML, CSS and SVG drawn with
the theme's tokens, not screenshots. The hero is a pure function of elapsed time, so
`index.html?t=4000` freezes it at four seconds, which helps when you capture a frame. With
`prefers-reduced-motion` it shows the finished state and hides the replay key. The phosphor and
chassis switch is remembered in `localStorage`.

## Swapping the comparison clips

The page builds the clip labels, the wipe tool and the table from `cq/results.json`. To use new
clips, replace the `cq/<id>/` folders and `results.json`. Each clip entry takes `id`, `name`,
`srcMB` and `encodes`, and optionally `label`, `kind`, `shots` (frame times, default
`[45, 120]`) and `zoom` (`shot`, plus `x` and `y` from 0 to 1 for the 2x crop centre). The line
under the table quotes the current numbers, so reread it after a swap.

## Refreshing the app captures

The captures come from a small WPF harness that opens the deck and the gallery off-screen
against an in-memory demo library at `D:\Media\TV`, named after the Blender open movies. It
renders each window's content to a 96 dpi bitmap and converts it to WebP with ffmpeg. Look at
every PNG before you commit the WebP files: no real show titles, share paths or hostnames.
The harness is not tracked. It needs an in-memory `DeckServices`, an `IMediaProbe` fake with
`DetectClosedCaptionsAsync`, and a runner that holds an episode mid-encode.
