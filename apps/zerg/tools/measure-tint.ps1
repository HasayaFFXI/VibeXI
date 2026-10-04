# Measures how much a floating panel darkens what is behind it, from real screen
# pixels -- the only way to check colour keying, opacity and the tint window,
# none of which a CDP screenshot can see.
#
#   powershell -ExecutionPolicy Bypass -File apps/zerg/tools/measure-tint.ps1 [-Out <png>]
#
# For every tint window Zerg has open, it captures the panel's client area plus
# a strip to its LEFT, and prints mean brightness inside vs that strip.
# inside/outside ~ (1 - slider%) when keyed: 85% -> ~0.15, 50% -> ~0.5.
# A panel without the key has no tint window and is not listed.
#
# Caveats: the strip left of the panel must show the same thing as behind it
# (the game is ideal; a window edge is not). PowerShell is DPI-unaware, so use
# a panel on the PRIMARY monitor. ASCII only: Windows PowerShell 5.1 reads
# UTF-8 without a BOM as ANSI.
param([string]$Out)

Add-Type -AssemblyName UIAutomationClient, System.Drawing
$proc = Get-Process Zerg -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { Write-Output 'Zerg is not running.'; exit 1 }

$cond = New-Object Windows.Automation.PropertyCondition(
    [Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
# A tint is the unnamed top-level window; its panel is listed under it, because
# the tint owns the panel.
$tints = [Windows.Automation.AutomationElement]::RootElement.FindAll(
    [Windows.Automation.TreeScope]::Children, $cond) | Where-Object { $_.Current.Name -eq '' }
if (-not $tints) { Write-Output 'No keyed panel is open.'; exit 1 }

foreach ($t in $tints) {
    $panel = $t.FindFirst([Windows.Automation.TreeScope]::Children,
                          [Windows.Automation.Condition]::TrueCondition)
    $r = $t.Current.BoundingRectangle
    $pad = 40
    $x = [int]$r.X - $pad; $y = [int]$r.Y; $w = [int]$r.Width + $pad; $h = [int]$r.Height
    $bmp = New-Object Drawing.Bitmap $w, $h
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, (New-Object Drawing.Size $w, $h))
    if ($Out) { $bmp.Save($Out) }

    # Inside skips the panel's top bar (~90 px) and edges, which carry content.
    $in = 0; $ni = 0
    for ($i = $pad + 30; $i -lt $w - 30; $i += 4) {
        for ($j = 90; $j -lt $h - 20; $j += 4) { $p = $bmp.GetPixel($i, $j); $in += ($p.R + $p.G + $p.B) / 3; $ni++ }
    }
    $o = 0; $no = 0
    for ($j = 90; $j -lt $h - 20; $j += 4) {
        for ($i = 2; $i -lt $pad - 14; $i += 3) { $p = $bmp.GetPixel($i, $j); $o += ($p.R + $p.G + $p.B) / 3; $no++ }
    }
    $g.Dispose(); $bmp.Dispose()
    $inside = $in / $ni; $outside = [Math]::Max($o / $no, 0.01)
    "{0}: inside {1:N1}  outside {2:N1}  inside/outside {3:N2}" -f $panel.Current.Name, $inside, $outside, ($inside / $outside)
}
