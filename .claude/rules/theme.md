---
paths:
  - "src/ShrinkRay.Theme/**"
  - "src/ShrinkRay.Gallery/**"
---

# Theme rules

The look is cassette futurism: 1980s industrial hardware. Beige or graphite ABS, charcoal
recessed plates, piano-key transport, amber phosphor CRTs, seven-segment readouts, tape reels,
VU needles, Dymo labels.

## House rules

- Phosphor is the only light source. Screens, digits and live readouts glow. Nothing else
  does, so never add a glow to chassis, keys or labels.
- Signal red marks Encode and anything that deletes. Use it nowhere else.
- Pair every status lamp with a word. Never rely on color alone.
- Silkscreen labels are uppercase Barlow Condensed 600 or 700, tracked about +18%. Use
  `TrackedText`; TextBlock has no letter spacing.
- Michroma is for the wordmark, the badge and kit headings only.
- Only parts that move on real hardware animate: reels, needles, lamps, keys, counters and
  stamps. Reel spin, lamp blink, CRT flicker, counter rolls, the stamp and the power-on self-test
  stop when `MotionPolicy.IsMotionEnabled` is false.
- The feel is tactile and retro, never futuristic: moulded parts, glass, ink and clicks in
  steps. Counters tick rather than glide.
- Glass over a readout is `GlassPane`: a domed window with a crisp curved edge. A soft diagonal
  gradient reads as a rendering fault, not a reflection.
- Glow comes from the lit parts only. A readout draws unlit segments on itself and lit ones on
  the layers `LitReadout` carries, so never put an effect on the whole control.

## scientifica

- scientifica is a bitmap font drawn at 11 px. Set it at exactly 22 px on any CRT or LCD so
  each font pixel covers 2 by 2 screen pixels.
- Render it with `TextOptions.TextFormattingMode="Display"`,
  `TextOptions.TextRenderingMode="Aliased"` and no smoothing. `SR.Text.Pixel` and `CrtPanel`
  set all of this, and `PixelText.Size` snaps the size to a multiple of 11 device pixels on
  scaled displays.
- Never put a blur or drop shadow on an ancestor of scientifica text except the phosphor glow
  `CrtPanel` applies.

## Tokens

- Reference tokens with DynamicResource. `ThemeManager` rewrites the phosphor and chassis keys
  in `ThemeKeys` at runtime.
- Inks printed on the chassis come from `ChassisPalette`. `PaletteContrastTests` checks them
  against WCAG on both chassis; keep it passing when you change a color.
- The J-card, meter faces, cassette label and tape-run parts take their colors from the chassis's
  `PaperPalette`. Beige is cream stock throughout. Graphite has a charcoal J-card and meter faces
  so no large sheet glares, and keeps the cassette label cream because ink on a dark label read
  badly. Reference those keys with DynamicResource, never a hex value, and keep
  `PaletteContrastTests` passing on both.

## Building controls

- Put geometry and arithmetic in `Mechanics/` as static, testable helpers.
- Build depth from stacked borders. Keep blur effects on content-free layers so text never
  renders through an effect.
- Give interactive controls real Button, ToggleButton or RadioButton semantics, an automation
  name, and `SR.FocusVisual`.
