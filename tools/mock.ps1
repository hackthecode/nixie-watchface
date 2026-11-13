# Composites a local test frame from the rendered layers (no watch needed).
param([string]$Digits = "1842", [int]$Battery = 76, [string]$Out)
Add-Type -AssemblyName System.Drawing
$d = (Resolve-Path (Join-Path $PSScriptRoot "..\res\drawable")).Path
$c = New-Object System.Drawing.Bitmap 466, 466; $g = [System.Drawing.Graphics]::FromImage($c)
$g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
function Full($n) { $im = [System.Drawing.Image]::FromFile("$d\$n.png"); $g.DrawImage($im, 0, 0, 466, 466); $im.Dispose() }
Full nx_bg; Full nx_back; Full nx_inner
$pad = 17; $bx = @(25, 123, 239, 337); for ($i = 0; $i -lt 4; $i++) { $im = [System.Drawing.Image]::FromFile("$d\nx_d$($Digits[$i]).png"); $g.DrawImage($im, $bx[$i] - $pad, 86 - $pad, $im.Width, $im.Height); $im.Dispose() }
$b = [System.Drawing.Image]::FromFile("$d\nx_bulb.png"); foreach ($y in 176, 234) { $g.DrawImage($b, 216, $y, 34, 40) }; $b.Dispose()
$bar = [System.Drawing.Image]::FromFile("$d\nx_bar.png"); $bw = [int](1.2 * $Battery)
$g.DrawImage($bar, (New-Object System.Drawing.Rectangle 173, 382, $bw, 14), 0, 0, $bw, 14, 'Pixel'); $bar.Dispose()
Full nx_mesh; Full nx_front; Full nx_hl
$f = New-Object System.Drawing.Font "Bahnschrift SemiBold Condensed", 17, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
$g.DrawString("$Battery%", $f, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 0, 0, 0))), (New-Object System.Drawing.RectangleF 173, 401, 120, 24), $sf)
$g.DrawString("$Battery%", $f, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(200, 168, 174, 186))), (New-Object System.Drawing.RectangleF 173, 402, 120, 24), $sf)
$path = New-Object System.Drawing.Drawing2D.GraphicsPath; $path.AddEllipse(0, 0, 466, 466)
$reg = New-Object System.Drawing.Region (New-Object System.Drawing.Rectangle 0, 0, 466, 466); $reg.Exclude($path); $g.FillRegion([System.Drawing.Brushes]::Black, $reg)
$c.Save($Out); $g.Dispose()
