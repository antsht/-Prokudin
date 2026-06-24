Add-Type -AssemblyName System.Drawing

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$graphics = [System.Drawing.Graphics]::FromImage($bmp)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(45, 85, 140))

$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(220, 235, 250))
$graphics.FillEllipse($brush, 48, 48, 160, 160)
$graphics.Dispose()

$assetsDir = $PSScriptRoot
$pngPath = Join-Path $assetsDir 'prokudin.png'
$bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

$icoPath = Join-Path $assetsDir 'prokudin.ico'
$icon = [System.Drawing.Icon]::FromHandle(([System.Drawing.Bitmap]::FromFile($pngPath)).GetHicon())
$stream = [System.IO.File]::Create($icoPath)
$icon.Save($stream)
$stream.Close()

Write-Host "Wrote $pngPath and $icoPath"
