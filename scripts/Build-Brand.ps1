# Builds Sora's brand assets from the cloud mark in assets\brand\sora-mark-source.svg:
# transparent SVG variants, the multi-size application icon (assets\Sora.ico) and a preview sheet.
param([string]$PreviewPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$TaskRoot = Split-Path $PSScriptRoot -Parent
$Brand = Join-Path $TaskRoot 'assets\brand'
$Source = Get-Content -Raw -LiteralPath (Join-Path $Brand 'sora-mark-source.svg')
$PathData = [regex]::Match($Source, ' d="([^"]+)"').Groups[1].Value
if (-not $PathData) { throw 'No path found in sora-mark-source.svg' }

# The source draws the mark at an offset inside a 756.913 square; keep that frame so the variants line up.
$Size = '756.913'
function Write-Variant([string]$Name, [string]$Defs, [string]$Fill, [string]$Title) {
    $svg = @"
<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 $Size $Size" role="img" aria-label="$Title">
  <title>$Title</title>$Defs
  <path transform="translate(138.5 198.5)" fill="$Fill" fill-rule="evenodd" d="$PathData"/>
</svg>
"@
    [IO.File]::WriteAllText((Join-Path $Brand $Name), $svg, [Text.UTF8Encoding]::new($false))
}
$SkyDefs = @"

  <defs>
    <linearGradient id="sky" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#8AD8FF"/>
      <stop offset="1" stop-color="#0A84FF"/>
    </linearGradient>
  </defs>
"@
Write-Variant 'sora-mark.svg' $SkyDefs 'url(#sky)' 'Sora'
Write-Variant 'sora-mark-white.svg' '' '#FFFFFF' 'Sora'
Write-Variant 'sora-mark-ink.svg' '' '#1D1D1F' 'Sora'

# Geometry for raster output, normalised to a 0..1 box.
$Geometry = [System.Windows.Media.Geometry]::Parse('F0 ' + $PathData).Clone()
$Bounds = $Geometry.Bounds
$Sky = New-Object System.Windows.Media.LinearGradientBrush
$Sky.StartPoint = New-Object System.Windows.Point 0, 0
$Sky.EndPoint = New-Object System.Windows.Point 1, 1
$Sky.GradientStops.Add((New-Object System.Windows.Media.GradientStop ([System.Windows.Media.Color]::FromRgb(0x8A, 0xD8, 0xFF)), 0))
$Sky.GradientStops.Add((New-Object System.Windows.Media.GradientStop ([System.Windows.Media.Color]::FromRgb(0x0A, 0x84, 0xFF)), 1))

function Draw-Mark($Context, [double]$X, [double]$Y, [double]$Pixels, $Brush) {
    # A small inset keeps the outline from touching the icon edge.
    $inset = [Math]::Max(0.5, $Pixels * 0.03)
    $scale = ($Pixels - 2 * $inset) / [Math]::Max($Bounds.Width, $Bounds.Height)
    $group = New-Object System.Windows.Media.TransformGroup
    $group.Children.Add((New-Object System.Windows.Media.TranslateTransform (-$Bounds.X), (-$Bounds.Y)))
    $group.Children.Add((New-Object System.Windows.Media.ScaleTransform $scale, $scale))
    $group.Children.Add((New-Object System.Windows.Media.TranslateTransform ($X + $inset + ($Pixels - 2 * $inset - $Bounds.Width * $scale) / 2), ($Y + $inset + ($Pixels - 2 * $inset - $Bounds.Height * $scale) / 2)))
    $copy = $Geometry.Clone(); $copy.Transform = $group
    $Context.DrawGeometry($Brush, $null, $copy)
}
function Render-Png([int]$Pixels) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    Draw-Mark $context 0 0 $Pixels $Sky
    $context.Close()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $Pixels, $Pixels, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = New-Object IO.MemoryStream
    $encoder.Save($memory)
    return , $memory.ToArray()
}

# Multi-size ICO with PNG entries (Windows Vista and later).
$Sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$Images = foreach ($pixels in $Sizes) { , (Render-Png $pixels) }
$Output = [IO.File]::Create((Join-Path $TaskRoot 'assets\Sora.ico'))
$Writer = New-Object IO.BinaryWriter $Output
try {
    $Writer.Write([uint16]0); $Writer.Write([uint16]1); $Writer.Write([uint16]$Sizes.Count)
    $offset = 6 + 16 * $Sizes.Count
    for ($i = 0; $i -lt $Sizes.Count; $i++) {
        $side = if ($Sizes[$i] -ge 256) { 0 } else { $Sizes[$i] }
        $Writer.Write([byte]$side); $Writer.Write([byte]$side); $Writer.Write([byte]0); $Writer.Write([byte]0)
        $Writer.Write([uint16]1); $Writer.Write([uint16]32)
        $Writer.Write([uint32]$Images[$i].Length); $Writer.Write([uint32]$offset)
        $offset += $Images[$i].Length
    }
    foreach ($image in $Images) { $Writer.Write($image) }
} finally { $Writer.Dispose(); $Output.Dispose() }
[IO.File]::WriteAllBytes((Join-Path $Brand 'sora-mark-256.png'), $Images[-1])

# Preview: every icon size on a dark and a light background, plus the white and ink marks.
if ($PreviewPath) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    $dark = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0x14, 0x15, 0x18))
    $light = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0xF2, 0xF2, 0xF7))
    $context.DrawRectangle($dark, $null, (New-Object System.Windows.Rect 0, 0, 760, 200))
    $context.DrawRectangle($light, $null, (New-Object System.Windows.Rect 0, 200, 760, 200))
    foreach ($row in 0, 1) {
        $x = 20
        foreach ($pixels in 16, 20, 24, 32, 48, 64, 128) { Draw-Mark $context $x (20 + $row * 200 + (128 - $pixels) / 2) $pixels $Sky; $x += $pixels + 18 }
        $ink = if ($row -eq 0) { [System.Windows.Media.Brushes]::White } else { New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0x1D, 0x1D, 0x1F)) }
        Draw-Mark $context 600 (20 + $row * 200) 128 $ink
    }
    $context.Close()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap 760, 400, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create($PreviewPath); $encoder.Save($stream); $stream.Dispose()
}
