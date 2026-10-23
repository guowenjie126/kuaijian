param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $directory | Out-Null

$size = 256
$bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$graphics.Clear([System.Drawing.Color]::Transparent)

$brand = [System.Drawing.Color]::FromArgb(255, 33, 184, 153)
$graphics.FillEllipse((New-Object System.Drawing.SolidBrush($brand)), 8, 8, 240, 240)
$font = New-Object System.Drawing.Font("Microsoft YaHei", 126, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$format = New-Object System.Drawing.StringFormat
$format.Alignment = [System.Drawing.StringAlignment]::Center
$format.LineAlignment = [System.Drawing.StringAlignment]::Center
$white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$brandChar = [char]0x526A
$graphics.DrawString($brandChar, $font, $white, (New-Object System.Drawing.RectangleF(0, 0, $size, $size)), $format)

$iconHandle = $bitmap.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($iconHandle)
$stream = [System.IO.File]::Create($OutputPath)
try { $icon.Save($stream) }
finally {
    $stream.Dispose()
    $icon.Dispose()
    $bitmap.Dispose()
    $graphics.Dispose()
    $font.Dispose()
    $format.Dispose()
    $white.Dispose()
}
