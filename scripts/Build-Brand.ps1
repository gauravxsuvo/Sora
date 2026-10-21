# Export the editable reference-derived vector; no tracing/raster packages are required.
param([string]$PreviewPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$TaskRoot = Split-Path $PSScriptRoot -Parent
$Brand = Join-Path $TaskRoot 'assets\brand'
$Source = [xml](Get-Content -Raw -LiteralPath (Join-Path $Brand 'sora-mark-source.svg'))
$PathData = $Source.svg.path.d
$ViewBox = $Source.svg.viewBox
if (-not $PathData -or $ViewBox -ne '0 0 128 128') { throw 'Expected the canonical 128px, single-path logo source.' }
$Geometry = [System.Windows.Media.Geometry]::Parse('F0 ' + $PathData)
$ReferenceWhite = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.ColorConverter]::ConvertFromString('#F7F6F4'))
$Ink = [System.Windows.Media.SolidColorBrush]::new([System.Windows.Media.ColorConverter]::ConvertFromString('#111111'))
$White = [System.Windows.Media.Brushes]::White

function Write-Variant([string]$Name, [string]$Fill, [string]$Background = '') {
    $backdrop = if ($Background) { '<rect width="128" height="128" rx="20" fill="' + $Background + '"/>' } else { '' }
    $svg = @"
<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="$ViewBox" role="img" aria-label="Sora">
  <title>Sora</title>
  $backdrop
  <path fill="$Fill" fill-rule="evenodd" d="$PathData"/>
</svg>
"@
    [IO.File]::WriteAllText((Join-Path $Brand $Name), [regex]::Replace($svg, '(?m)[ \t]+$', ''), [Text.UTF8Encoding]::new($false))
}
Write-Variant 'sora-mark.svg' '#F7F6F4'
Write-Variant 'sora-mark-white.svg' '#FFFFFF'
Write-Variant 'sora-mark-ink.svg' '#111111'
Write-Variant 'sora-mark-current.svg' 'currentColor'
Write-Variant 'sora-mark-on-dark.svg' '#F7F6F4' '#111111'
Write-Variant 'sora-mark-on-light.svg' '#111111' '#F7F6F4'
Copy-Item -LiteralPath (Join-Path $Brand 'sora-mark-ink.svg') -Destination (Join-Path $TaskRoot 'soro_logo_light.svg')

function Draw-Mark($Context, [double]$X, [double]$Y, [double]$Pixels, $Brush) {
    $transforms = [System.Windows.Media.TransformGroup]::new()
    $transforms.Children.Add([System.Windows.Media.ScaleTransform]::new($Pixels / 128, $Pixels / 128))
    $transforms.Children.Add([System.Windows.Media.TranslateTransform]::new($X, $Y))
    $copy = $Geometry.Clone(); $copy.Transform = $transforms
    $Context.DrawGeometry($Brush, $null, $copy)
}
function Encode-Png($Visual, [int]$Width, [int]$Height) {
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($Width, $Height, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($Visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = [IO.MemoryStream]::new()
    try { $encoder.Save($memory); return ,$memory.ToArray() } finally { $memory.Dispose() }
}
function Render-Png([int]$Pixels, $Brush = $ReferenceWhite, $Background = $null) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    if ($null -ne $Background) {
        $context.DrawRoundedRectangle($Background, $null, [System.Windows.Rect]::new(0, 0, $Pixels, $Pixels), $Pixels * 20 / 128, $Pixels * 20 / 128)
    }
    Draw-Mark $context 0 0 $Pixels $Brush
    $context.Close()
    return ,(Encode-Png $visual $Pixels $Pixels)
}

# All transparent pixels, including the capsule and circle cutouts, retain genuine alpha.
foreach ($pixels in 64, 128, 256, 512, 1024, 2048, 4096) {
    [IO.File]::WriteAllBytes((Join-Path $Brand "sora-mark-$pixels.png"), (Render-Png $pixels))
}
[IO.File]::WriteAllBytes((Join-Path $Brand 'sora-mark.png'), (Render-Png 4096))
[IO.File]::WriteAllBytes((Join-Path $Brand 'sora-mark-white.png'), (Render-Png 2048 $White))
[IO.File]::WriteAllBytes((Join-Path $Brand 'sora-mark-ink.png'), (Render-Png 2048 $Ink))
[IO.File]::WriteAllBytes((Join-Path $Brand 'sora-mark-on-dark.png'), (Render-Png 2048 $ReferenceWhite $Ink))
[IO.File]::WriteAllBytes((Join-Path $Brand 'sora-mark-on-light.png'), (Render-Png 2048 $Ink $ReferenceWhite))

# Keep genuine transparency outside the mark; no square backing around the logo.
$Sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$Images = foreach ($pixels in $Sizes) { ,(Render-Png $pixels $ReferenceWhite) }
$Output = [IO.File]::Create((Join-Path $TaskRoot 'assets\Sora.ico'))
$Writer = [IO.BinaryWriter]::new($Output)
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

# The app uses one native geometry and inherits the current theme's text color.
$themePath = Join-Path $TaskRoot 'src\Sora.App\Theme.xaml'
$theme = [IO.File]::ReadAllText($themePath)
$theme = [regex]::Replace($theme, '<!-- Brand:.*?-->', '<!-- Brand: geometric reference mark, generated from assets/brand/sora-mark-source.svg. -->')
$theme = [regex]::Replace($theme, '<Geometry x:Key="SoraMark">.*?</Geometry>', ('<Geometry x:Key="SoraMark">F0 ' + $PathData + '</Geometry>'))
$theme = [regex]::Replace($theme, '<(?:LinearGradientBrush|SolidColorBrush) x:Key="SoraBrandBrush"[^>]*(?:/>|>.*?</(?:LinearGradientBrush|SolidColorBrush)>)', '<SolidColorBrush x:Key="SoraBrandBrush" Color="{DynamicResource WindowCaptionTextColor}"/>')
[IO.File]::WriteAllText($themePath, $theme, [Text.UTF8Encoding]::new($false))

$web = Join-Path $TaskRoot 'web\sora-downloads\public'
Copy-Item -LiteralPath (Join-Path $Brand 'sora-mark-ink.svg') -Destination (Join-Path $web 'sora-mark.svg')
[IO.File]::WriteAllBytes((Join-Path $web 'sora-mark.png'), (Render-Png 512 $Ink))
[IO.File]::WriteAllBytes((Join-Path $web 'apple-touch-icon.png'), (Render-Png 256 $Ink))
Copy-Item -LiteralPath (Join-Path $TaskRoot 'assets\Sora.ico') -Destination (Join-Path $web 'favicon.ico')

if ($PreviewPath) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    $context.DrawRectangle($Ink, $null, [System.Windows.Rect]::new(0, 0, 1000, 320))
    $context.DrawRectangle($ReferenceWhite, $null, [System.Windows.Rect]::new(0, 320, 1000, 320))
    foreach ($row in 0, 1) {
        $brush = if ($row -eq 0) { $ReferenceWhite } else { $Ink }
        Draw-Mark $context 26 (32 + $row * 320) 256 $brush
        $x = 322
        foreach ($pixels in 16, 24, 32, 48, 64, 128) {
            Draw-Mark $context $x (32 + $row * 320 + (256 - $pixels) / 2) $pixels $brush
            $x += $pixels + 26
        }
    }
    $context.Close()
    $resolvedPreview = [IO.Path]::GetFullPath($PreviewPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedPreview)) | Out-Null
    [IO.File]::WriteAllBytes($resolvedPreview, (Encode-Png $visual 1000 640))
}
Write-Host 'Updated SVG, transparent PNG (through 4096px), app icon, WPF geometry and website icons.'
