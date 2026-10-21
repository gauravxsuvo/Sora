# Sora logo

The current mark is a vector reconstruction of the central violet symbol supplied on 19 October 2026. The cream background and small corner symbol are excluded. The diagonal cuts are transparent and all SVG artwork consists of editable paths, with no embedded image.

| Asset | Purpose |
|---|---|
| `sora-mark-source.svg` | Canonical silhouette, 160 × 160 viewBox |
| `sora-mark.svg` | Default violet-to-lilac gradient |
| `sora-mark-current.svg` | Inherits `currentColor` when embedded inline |
| `sora-mark-white.svg`, `sora-mark-ink.svg` | Solid light/dark variants |
| `sora-mark.png`, `sora-mark-1024.png` | Transparent 1024 × 1024 PNG |
| `sora-mark-64.png` through `sora-mark-512.png` | Smaller transparent PNG exports |
| `sora-mark-white.png`, `sora-mark-ink.png` | Solid 512 × 512 PNGs |
| `../Sora.ico` | Windows icon: 16, 20, 24, 32, 40, 48, 64, 128 and 256 px |

Regenerate from the repository root on Windows:

```powershell
.\scripts\Build-Brand.ps1
# Optional alternate gradient, applied consistently to all generated assets:
.\scripts\Build-Brand.ps1 -StartColor '#6030F4' -EndColor '#9657FF' -PreviewPath artifacts/logo-preview.png
```

This also updates the desktop `SoraMark` geometry/`SoraBrandBrush`, website SVG/PNG, favicon and touch icon. The app's executable icon, taskbar, tray, shortcuts and dialog icons derive from `assets/Sora.ico`. The installer builder embeds the same ICO in the setup executable. Rebuild binaries and recapture the current website screenshot after changing the mark.

Artwork in `assets/branding/sora-spiral-v1` is historical and is not referenced by the application or website. The old export command now delegates to this current logo system.
