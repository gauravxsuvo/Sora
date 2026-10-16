param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\assets\branding\sora-spiral-v1')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$culture = [Globalization.CultureInfo]::InvariantCulture
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

# Original vector reconstruction of the supplied reference. No raster data or fonts
# are embedded in the exported SVGs; the wordmark is converted into outlines.
$bladeData = 'M186 0 C83 0 0 83 0 183 C0 252 46 301 111 301 C152 301 186 271 186 235 C186 211 172 195 152 190 C167 194 175 202 174 213 C172 228 155 238 139 234 C117 230 102 214 102 192 C102 157 128 129 162 121 Z'
$definitions = @"
  <defs>
    <linearGradient id="blade-tone" x1="0" y1="0" x2="0" y2="250" gradientUnits="userSpaceOnUse">
      <stop offset="0" stop-color="#ededed"/>
      <stop offset="0.43" stop-color="#878787"/>
      <stop offset="0.86" stop-color="#090909"/>
      <stop offset="1" stop-color="#000000"/>
    </linearGradient>
    <linearGradient id="curl-tone" x1="126" y1="185" x2="155" y2="233" gradientUnits="userSpaceOnUse">
      <stop offset="0" stop-color="#000000"/>
      <stop offset="0.36" stop-color="#333333"/>
      <stop offset="0.8" stop-color="#ffffff"/>
      <stop offset="1" stop-color="#ffffff"/>
    </linearGradient>
    <path id="blade" d="$bladeData"/>
  </defs>
"@
$symbol = @'
    <ellipse cx="144" cy="210" rx="49" ry="40" fill="#000000"/>
    <use href="#blade" fill="url(#blade-tone)"/>
    <use href="#blade" fill="url(#blade-tone)" transform="rotate(180 144 210)"/>
    <path d="M151 189 C125 169 101 166 101 192 C101 215 117 231 139 235 C156 239 173 229 175 213 C176 201 167 193 151 189 Z" fill="url(#curl-tone)"/>
'@

$wordmark = [Drawing.Drawing2D.GraphicsPath]::new()
$font = [Drawing.FontFamily]::new('Arial')
$format = [Drawing.StringFormat]::GenericTypographic
$wordmark.AddString('Sora', $font, 0, 188, [Drawing.PointF]::new(0,0), $format)
$bounds = $wordmark.GetBounds()
$placement = [Drawing.Drawing2D.Matrix]::new(
    [single](356 / $bounds.Width), 0, 0, [single](141 / $bounds.Height),
    [single](336 - $bounds.X * 356 / $bounds.Width),
    [single](140 - $bounds.Y * 141 / $bounds.Height))
$wordmark.Transform($placement)
$points = $wordmark.PathPoints
$types = $wordmark.PathTypes
$builder = [Text.StringBuilder]::new()
function Coordinate([single]$Value) { $Value.ToString('0.###', $culture) }
for ($index = 0; $index -lt $points.Length; $index++) {
    $kind = $types[$index] -band 7
    if ($kind -eq 0) {
        [void]$builder.Append("M$(Coordinate $points[$index].X) $(Coordinate $points[$index].Y) ")
    } elseif ($kind -eq 1) {
        [void]$builder.Append("L$(Coordinate $points[$index].X) $(Coordinate $points[$index].Y) ")
    } elseif ($kind -eq 3) {
        [void]$builder.Append("C$(Coordinate $points[$index].X) $(Coordinate $points[$index].Y) $(Coordinate $points[$index+1].X) $(Coordinate $points[$index+1].Y) $(Coordinate $points[$index+2].X) $(Coordinate $points[$index+2].Y) ")
        $index += 2
    }
    if (($types[$index] -band 128) -ne 0) { [void]$builder.Append('Z ') }
}
$wordmarkData = $builder.ToString().Trim()
$outline = "    <path fill=`"#000000`" d=`"$wordmarkData`"/>"
$logo = @"
<svg xmlns="http://www.w3.org/2000/svg" width="820" height="548" viewBox="0 0 820 548" role="img" aria-labelledby="title desc">
  <title id="title">Sora spiral logo</title>
  <desc id="desc">Two interlocking shaded curves form a spiral S beside the Sora wordmark. Transparent background; all artwork is vector.</desc>
$definitions
  <g transform="translate(64 64)">
$symbol
$outline
  </g>
</svg>
"@
$icon = @"
<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512" role="img" aria-labelledby="title desc">
  <title id="title">Sora spiral symbol</title>
  <desc id="desc">Two interlocking shaded curves form a spiral S. Transparent background; all artwork is vector.</desc>
$definitions
  <g transform="translate(112 46)">
$symbol
  </g>
</svg>
"@
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'sora-logo.svg'), $logo, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'sora-symbol.svg'), $icon, [Text.UTF8Encoding]::new($false))

# Render the same vector paths and gradients with Windows' native vector engine.
$bladePath = [Drawing.Drawing2D.GraphicsPath]::new()
$tokens = [regex]::Matches($bladeData, '[MCZ]|-?\d+(?:\.\d+)?')
$cursor = [Drawing.PointF]::new(0,0)
for ($i = 0; $i -lt $tokens.Count;) {
    $command = $tokens[$i++].Value
    if ($command -eq 'M') {
        $cursor = [Drawing.PointF]::new([single]$tokens[$i++].Value, [single]$tokens[$i++].Value)
        $bladePath.StartFigure()
    } elseif ($command -eq 'C') {
        $p1 = [Drawing.PointF]::new([single]$tokens[$i++].Value, [single]$tokens[$i++].Value)
        $p2 = [Drawing.PointF]::new([single]$tokens[$i++].Value, [single]$tokens[$i++].Value)
        $p3 = [Drawing.PointF]::new([single]$tokens[$i++].Value, [single]$tokens[$i++].Value)
        $bladePath.AddBezier($cursor, $p1, $p2, $p3)
        $cursor = $p3
    } elseif ($command -eq 'Z') { $bladePath.CloseFigure() }
}
$bitmap = [Drawing.Bitmap]::new(1640,1096)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([Drawing.ColorTranslator]::FromHtml('#f5f5f5'))
$graphics.ScaleTransform(2,2)
$graphics.TranslateTransform(64,64)
$tone = [Drawing.Drawing2D.LinearGradientBrush]::new(
    [Drawing.PointF]::new(0,0), [Drawing.PointF]::new(0,320),
    [Drawing.Color]::White, [Drawing.Color]::Black)
$toneStops = [Drawing.Drawing2D.ColorBlend]::new(5)
$toneStops.Positions = [single[]]@(0,0.3359375,0.671875,0.78125,1)
$toneStops.Colors = [Drawing.Color[]]@('#ededed','#878787','#090909','#000000','#000000' | ForEach-Object { [Drawing.ColorTranslator]::FromHtml($_) })
$tone.InterpolationColors = $toneStops
$curl = [Drawing.Drawing2D.LinearGradientBrush]::new(
    [Drawing.PointF]::new(97,137), [Drawing.PointF]::new(184,281),
    [Drawing.Color]::Black, [Drawing.Color]::White)
$curlStops = [Drawing.Drawing2D.ColorBlend]::new(6)
$curlStops.Positions = [single[]]@(0,0.3333333,0.4533333,0.6,0.6666667,1)
$curlStops.Colors = [Drawing.Color[]]@([Drawing.Color]::Black,[Drawing.Color]::Black,[Drawing.Color]::FromArgb(51,51,51),[Drawing.Color]::White,[Drawing.Color]::White,[Drawing.Color]::White)
$curl.InterpolationColors = $curlStops
$graphics.FillEllipse([Drawing.Brushes]::Black,95,170,98,80)
$graphics.FillPath($tone,$bladePath)
$saved = $graphics.Save()
$graphics.TranslateTransform(288,420)
$graphics.RotateTransform(180)
$graphics.FillPath($tone,$bladePath)
$graphics.Restore($saved)
$innerCurl = [Drawing.Drawing2D.GraphicsPath]::new()
$innerCurl.AddBezier(151,189,125,169,101,166,101,192)
$innerCurl.AddBezier(101,192,101,215,117,231,139,235)
$innerCurl.AddBezier(139,235,156,239,173,229,175,213)
$innerCurl.AddBezier(175,213,176,201,167,193,151,189)
$innerCurl.CloseFigure()
$graphics.FillPath($curl,$innerCurl)
$graphics.FillPath([Drawing.Brushes]::Black,$wordmark)
$bitmap.Save((Join-Path $OutputDirectory 'sora-logo-preview.png'), [Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()
$bladePath.Dispose()
$tone.Dispose()
$curl.Dispose()
$innerCurl.Dispose()
$wordmark.Dispose()
$placement.Dispose()
$font.Dispose()
$format.Dispose()
Get-ChildItem -LiteralPath $OutputDirectory | Select-Object Name, Length
