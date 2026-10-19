# Builds Sora's brand assets from the vector mark in assets\brand\sora-mark-source.svg:
# transparent SVG variants, the multi-size application icon (assets\Sora.ico) and a preview sheet.
param([string]$PreviewPath, [string]$StartColor = '#6030F4', [string]$EndColor = '#9657FF')
$ErrorActionPreference = 'Stop'
if ($StartColor -notmatch '^#[0-9a-fA-F]{6}$' -or $EndColor -notmatch '^#[0-9a-fA-F]{6}$') { throw 'Use six-digit hex gradient colours.' }
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$TaskRoot = Split-Path $PSScriptRoot -Parent
$Brand = Join-Path $TaskRoot 'assets\brand'
$Source = Get-Content -Raw -LiteralPath (Join-Path $Brand 'sora-mark-source.svg')
$PathData = [regex]::Match($Source, ' d="([^"]+)"').Groups[1].Value
if (-not $PathData) { throw 'No path found in sora-mark-source.svg' }

$Size = '160'
function Write-Variant([string]$Name, [string]$Defs, [string]$Fill, [string]$Title) {
    $svg = @"
<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 $Size $Size" role="img" aria-label="$Title">
  <title>$Title</title>$Defs
  <path fill="$Fill" fill-rule="evenodd" d="$PathData"/>
</svg>
"@
    [IO.File]::WriteAllText((Join-Path $Brand $Name), $svg, [Text.UTF8Encoding]::new($false))
}
$SkyDefs = @"

  <defs>
    <linearGradient id="brand" x1="0" y1="1" x2="1" y2="0">
      <stop offset="0" stop-color="$StartColor"/>
      <stop offset="1" stop-color="$EndColor"/>
    </linearGradient>
  </defs>
"@
Write-Variant 'sora-mark.svg' $SkyDefs 'url(#brand)' 'Sora'
Write-Variant 'sora-mark-white.svg' '' '#FFFFFF' 'Sora'
Write-Variant 'sora-mark-ink.svg' '' '#1D1D1F' 'Sora'
Write-Variant 'sora-mark-current.svg' '' 'currentColor' 'Sora'

# Geometry for raster output, normalised to a 0..1 box.
$Geometry = [System.Windows.Media.Geometry]::Parse('F0 ' + $PathData).Clone()
$Bounds = New-Object System.Windows.Rect 0, 0, 160, 160
$Sky = New-Object System.Windows.Media.LinearGradientBrush
$Sky.StartPoint = New-Object System.Windows.Point 0, 1
$Sky.EndPoint = New-Object System.Windows.Point 1, 0
$Sky.GradientStops.Add((New-Object System.Windows.Media.GradientStop ([System.Windows.Media.ColorConverter]::ConvertFromString($StartColor)), 0))
$Sky.GradientStops.Add((New-Object System.Windows.Media.GradientStop ([System.Windows.Media.ColorConverter]::ConvertFromString($EndColor)), 1))

function Draw-Mark($Context, [double]$X, [double]$Y, [double]$Pixels, $Brush) {
    # The source viewBox already has transparent padding. Keep SVG/PNG/icon proportions identical.
    $inset = 0
    $scale = ($Pixels - 2 * $inset) / [Math]::Max($Bounds.Width, $Bounds.Height)
    $group = New-Object System.Windows.Media.TransformGroup
    $group.Children.Add((New-Object System.Windows.Media.TranslateTransform (-$Bounds.X), (-$Bounds.Y)))
    $group.Children.Add((New-Object System.Windows.Media.ScaleTransform $scale, $scale))
    $group.Children.Add((New-Object System.Windows.Media.TranslateTransform ($X + $inset + ($Pixels - 2 * $inset - $Bounds.Width * $scale) / 2), ($Y + $inset + ($Pixels - 2 * $inset - $Bounds.Height * $scale) / 2)))
    $copy = $Geometry.Clone(); $copy.Transform = $group
    $Context.DrawGeometry($Brush, $null, $copy)
}
function Render-Png([int]$Pixels, $Brush = $Sky) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    Draw-Mark $context 0 0 $Pixels $Brush
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
foreach ($pixels in 64, 128, 512, 1024) {
    [IO.File]::WriteAllBytes((Join-Path $Brand "sora-mark-$pixels.png"), (Render-Png $pixels))
}
[IO.File]::WriteAllBytes((Join-Path $Brand 'sora-mark.png'), (Render-Png 1024))
[IO.File]::WriteAllBytes((Join-Path $Brand 'sora-mark-white.png'), (Render-Png 512 ([System.Windows.Media.Brushes]::White)))
$inkBrush = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb(0x1D, 0x1D, 0x1F))
[IO.File]::WriteAllBytes((Join-Path $Brand 'sora-mark-ink.png'), (Render-Png 512 $inkBrush))

# The desktop geometry is generated from the same source, so it cannot drift from the exported assets.
$themePath = Join-Path $TaskRoot 'src\Sora.App\Theme.xaml'
$theme = [IO.File]::ReadAllText($themePath)
$theme = [regex]::Replace($theme, '<!-- Brand:.*?-->', '<!-- Brand: circular flowing cuts, generated from assets/brand/sora-mark-source.svg. -->')
$theme = [regex]::Replace($theme, '<Geometry x:Key="SoraMark">.*?</Geometry>', ('<Geometry x:Key="SoraMark">' + $PathData + '</Geometry>'))
$brush = '<LinearGradientBrush x:Key="SoraBrandBrush" StartPoint="0,1" EndPoint="1,0"><GradientStop Color="' + $StartColor + '" Offset="0"/><GradientStop Color="' + $EndColor + '" Offset="1"/></LinearGradientBrush>'
$theme = [regex]::Replace($theme, '<LinearGradientBrush x:Key="Sora(?:Sky|Brand)Brush"[^>]*>.*?</LinearGradientBrush>', $brush)
[IO.File]::WriteAllText($themePath, $theme, [Text.UTF8Encoding]::new($false))

$web = Join-Path $TaskRoot 'web\sora-downloads\public'
Copy-Item -LiteralPath (Join-Path $Brand 'sora-mark.svg') -Destination (Join-Path $web 'sora-mark.svg')
Copy-Item -LiteralPath (Join-Path $Brand 'sora-mark-512.png') -Destination (Join-Path $web 'sora-mark.png')
Copy-Item -LiteralPath (Join-Path $Brand 'sora-mark-256.png') -Destination (Join-Path $web 'apple-touch-icon.png')
Copy-Item -LiteralPath (Join-Path $TaskRoot 'assets\Sora.ico') -Destination (Join-Path $web 'favicon.ico')

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
