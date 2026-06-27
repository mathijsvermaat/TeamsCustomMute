Add-Type -AssemblyName System.Drawing
$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.InterpolationMode = 'HighQualityBicubic'
$g.Clear([System.Drawing.Color]::Transparent)

function New-RoundedRect($x,$y,$w,$h,$r) {
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = 2*$r
  $path.AddArc($x, $y, $d, $d, 180, 90)
  $path.AddArc($x+$w-$d, $y, $d, $d, 270, 90)
  $path.AddArc($x+$w-$d, $y+$h-$d, $d, $d, 0, 90)
  $path.AddArc($x, $y+$h-$d, $d, $d, 90, 90)
  $path.CloseFigure()
  return $path
}

# Background rounded square with vertical gradient (Teams purple)
$bg = New-RoundedRect 8 8 240 240 52
$brushBg = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0,8)), ([System.Drawing.Point]::new(0,248)), ([System.Drawing.Color]::FromArgb(98,100,210)), ([System.Drawing.Color]::FromArgb(67,70,160))
$g.FillPath($brushBg, $bg)

# Speech bubble (white)
$bubble = New-RoundedRect 56 64 144 96 28
$tail = New-Object System.Drawing.Drawing2D.GraphicsPath
$tail.AddPolygon( @([System.Drawing.Point]::new(90,150), [System.Drawing.Point]::new(90,200), [System.Drawing.Point]::new(130,150)) )
$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
$g.FillPath($white, $bubble)
$g.FillPath($white, $tail)

# Three dots
$dot = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(91,95,199))
$g.FillEllipse($dot, 90,104,18,18)
$g.FillEllipse($dot, 119,104,18,18)
$g.FillEllipse($dot, 148,104,18,18)

# Mute slash with white halo
$penHalo = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 36
$penHalo.StartCap = 'Round'; $penHalo.EndCap = 'Round'
$g.DrawLine($penHalo, 64,200, 200,52)
$penSlash = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(232,72,72)), 24
$penSlash.StartCap = 'Round'; $penSlash.EndCap = 'Round'
$g.DrawLine($penSlash, 64,200, 200,52)
$g.Dispose()

$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $ms.ToArray()
$bmp.Dispose()

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1)
$bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([Byte]0)
$bw.Write([UInt16]1); $bw.Write([UInt16]32)
$bw.Write([UInt32]$png.Length); $bw.Write([UInt32]22)
$bw.Write($png); $bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path (Get-Location) 'app.ico'), $out.ToArray())
"Wrote app.ico ($($png.Length) bytes PNG)"
