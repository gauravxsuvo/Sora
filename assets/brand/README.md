# Sora logo

The active logo is an editable geometric reconstruction of the reference supplied on **21 October 2026**. It preserves the rounded square, vertical capsule, small upper disk, and lower circular cutout. It is monochrome, with no gradient. The supplied raster is preserved unchanged as `reference.png`.

The SVG is the scalable master; the PNGs are rendered directly from its geometry, rather than enlarging the small reference bitmap. SVG files contain native paths, not embedded PNGs. Transparent variants have genuine alpha outside the mark and inside both cutouts; the small upper disk remains part of the mark.

| Asset | Purpose |
| --- | --- |
| `sora-mark-source.svg` | Canonical path, 128 × 128 viewBox with consistent padding |
| `sora-mark.svg` | Transparent reference off-white (`#F7F6F4`) |
| `sora-mark-white.svg`, `sora-mark-ink.svg` | Transparent white / charcoal (`#111111`) |
| `sora-mark-current.svg` | Inherits `currentColor` when embedded inline |
| `sora-mark-on-dark.svg`, `sora-mark-on-light.svg` | Versions with contrasting rounded background tiles |
| `sora-mark.png`, `sora-mark-4096.png` | Transparent 4096 × 4096 PNG masters |
| `sora-mark-64.png` through `sora-mark-2048.png` | Transparent exports at 64, 128, 256, 512, 1024, and 2048 px |
| `sora-mark-white.png`, `sora-mark-ink.png` | Transparent 2048 px white / charcoal variants |
| `sora-mark-on-dark.png`, `sora-mark-on-light.png` | 2048 px versions with contrasting backgrounds |
| `reference.png` | Original uploaded reference; unchanged |
| `../Sora.ico` | Windows icon with 16, 20, 24, 32, 40, 48, 64, 128, and 256 px entries |

Use the light mark on dark backgrounds and the ink mark on light backgrounds. The Windows ICO and website icons use the dark-backed version so the white silhouette stays visible on either theme. The WPF sidebar uses native vector geometry and follows the application text color.

Regenerate all assets on Windows:

```powershell
.\scripts\Build-Brand.ps1 -PreviewPath artifacts/brand-refresh-20261021/preview.png
```

This updates the SVG/PNG exports, desktop geometry, application ICO, website mark, favicon, and touch icon. Rebuild binaries to embed the new application/tray/dialog/installer icon. Existing Windows shortcuts or pinned items may retain a cached icon until refreshed.

`scripts/export-sora-logo.ps1` copies the current master PNG and SVG into an export folder. Historical marks are kept under `assets/branding/sora-flow-v2-20261019` and `assets/branding/sora-spiral-v1`; they are not referenced by the current app or website.
