# Builds Zerg's icon from the user's mockup: crops the tile interior (the Z and
# its glow on #060911), redraws it as a clean rounded tile with a soft rim, and
# writes a multi-size .ico (PNG entries) plus a 1024 px master PNG.
param([string]$Source, [string]$OutDir)

Add-Type -AssemblyName PresentationCore, WindowsBase
$src = [Windows.Media.Imaging.BitmapDecoder]::Create([Uri]$Source, 'None', 'OnLoad').Frames[0]
$bgra = New-Object Windows.Media.Imaging.FormatConvertedBitmap($src, [Windows.Media.PixelFormats]::Bgra32, $null, 0)
# Tile interior, measured: rim at x 431-434 / 973-976, y 119 / 648-651.
$crop = New-Object Windows.Media.Imaging.CroppedBitmap($bgra, (New-Object Windows.Int32Rect(436, 121, 536, 526)))
$crop.Freeze()

function Render([int]$S) {
    $dv = New-Object Windows.Media.DrawingVisual
    [Windows.Media.RenderOptions]::SetBitmapScalingMode($dv, 'HighQuality')
    $dc = $dv.RenderOpen()
    $rim = [Math]::Max($S * 0.012, 0.6)            # rim width, ~1.2% of the tile
    $r = $S * 0.165                                # corner radius, measured ~16%
    $rect = New-Object Windows.Rect(($rim / 2), ($rim / 2), ($S - $rim), ($S - $rim))
    $geo = New-Object Windows.Media.RectangleGeometry($rect, $r, $r)
    # Tile colour underneath, so the clip edge never shows anything else.
    $tile = New-Object Windows.Media.SolidColorBrush([Windows.Media.Color]::FromRgb(0x06, 0x09, 0x11))
    $dc.DrawGeometry($tile, $null, $geo)
    $dc.PushClip($geo)
    $dc.DrawImage($crop, (New-Object Windows.Rect(0, 0, $S, $S)))
    $dc.Pop()
    $grad = New-Object Windows.Media.LinearGradientBrush(
        [Windows.Media.Color]::FromRgb(0x34, 0x38, 0x3E),
        [Windows.Media.Color]::FromRgb(0x18, 0x1B, 0x21),
        (New-Object Windows.Point(0, 0)), (New-Object Windows.Point(0.3, 1)))
    $dc.DrawGeometry($null, (New-Object Windows.Media.Pen($grad, $rim)), $geo)
    $dc.Close()
    $rtb = New-Object Windows.Media.Imaging.RenderTargetBitmap($S, $S, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object IO.MemoryStream
    $enc.Save($ms)
    return , $ms.ToArray()
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
[IO.File]::WriteAllBytes((Join-Path $OutDir 'zerg-1024.png'), (Render 1024))

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) { , (Render $s) }

# ICO: 6-byte header, 16-byte directory entry per image, then the PNG data.
$ico = New-Object IO.MemoryStream
$bw = New-Object IO.BinaryWriter($ico)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
[IO.File]::WriteAllBytes((Join-Path $OutDir 'zerg.ico'), $ico.ToArray())
"wrote zerg.ico ($($sizes -join ', ')) and zerg-1024.png to $OutDir"
