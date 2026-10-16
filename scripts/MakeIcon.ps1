Add-Type -AssemblyName System.Drawing
$TaskRoot = Split-Path $PSScriptRoot -Parent
$Bitmap = [System.Drawing.Bitmap]::new(256, 256)
$Graphics = [System.Drawing.Graphics]::FromImage($Bitmap)
$Shape = [System.Drawing.Drawing2D.GraphicsPath]::new()
$Font = [System.Drawing.Font]::new('Segoe UI', 155, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$Blue = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#769BFF'))
$Ink = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#0C111D'))
$Memory = [IO.MemoryStream]::new()
try {
    $Graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $Graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $Graphics.Clear([System.Drawing.Color]::Transparent)
    $Shape.AddArc(8, 8, 64, 64, 180, 90)
    $Shape.AddArc(184, 8, 64, 64, 270, 90)
    $Shape.AddArc(184, 184, 64, 64, 0, 90)
    $Shape.AddArc(8, 184, 64, 64, 90, 90)
    $Shape.CloseFigure()
    $Graphics.FillPath($Blue, $Shape)
    $Format = [System.Drawing.StringFormat]::new()
    $Format.Alignment = [System.Drawing.StringAlignment]::Center
    $Format.LineAlignment = [System.Drawing.StringAlignment]::Center
    try { $Graphics.DrawString('S', $Font, $Ink, [System.Drawing.RectangleF]::new(0, -3, 256, 256), $Format) } finally { $Format.Dispose() }
    $Bitmap.Save($Memory, [System.Drawing.Imaging.ImageFormat]::Png)
    $Payload = $Memory.ToArray()
    $Output = [IO.File]::Create((Join-Path $TaskRoot 'assets\Sora.ico'))
    $Writer = [IO.BinaryWriter]::new($Output)
    try {
        $Writer.Write([uint16]0); $Writer.Write([uint16]1); $Writer.Write([uint16]1)
        $Writer.Write([byte]0); $Writer.Write([byte]0); $Writer.Write([byte]0); $Writer.Write([byte]0)
        $Writer.Write([uint16]1); $Writer.Write([uint16]32)
        $Writer.Write([uint32]$Payload.Length); $Writer.Write([uint32]22); $Writer.Write($Payload)
    } finally { $Writer.Dispose(); $Output.Dispose() }
} finally { $Memory.Dispose(); $Ink.Dispose(); $Blue.Dispose(); $Font.Dispose(); $Shape.Dispose(); $Graphics.Dispose(); $Bitmap.Dispose() }
