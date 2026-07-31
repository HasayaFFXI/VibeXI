<#
.SYNOPSIS
    Tails the most recently modified HorizonXI chat log and serves a live damage
    meter web UI on localhost.

.DESCRIPTION
    The server is deliberately dumb: it finds the newest *.log in the chat log
    directory, hands the browser the raw lines it has not seen yet, and serves the
    static files in .\web. ALL parsing and aggregation happens in the browser
    (web/lib/parser.js, web/lib/stats.js) so the parse rules can be iterated on
    without restarting anything.

    Chat logs are Shift-JIS (CP932) -- FFXI's auto-translate brackets are two-byte
    0x81xx sequences -- so bytes are decoded with codepage 932, not UTF-8.

.PARAMETER LogDir
    Directory holding the chat logs. Defaults to the HorizonXI launcher location.

.PARAMETER Port
    Local TCP port to listen on. Default 8731.

.PARAMETER NoBrowser
    Do not open a browser window on start.

.EXAMPLE
    .\damage-meter.ps1
    .\damage-meter.ps1 -Port 9000 -NoBrowser
#>
[CmdletBinding()]
param(
    [string] $LogDir = (Join-Path $env:APPDATA 'HorizonXI-Launcher\HorizonXI\Game\chatlogs'),
    [int]    $Port = 8731,
    [switch] $NoBrowser
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Web

$WebRoot = Join-Path $PSScriptRoot 'web'
# The design system is shared with ..\ws_calculator, so it lives outside this project
# and is mounted at the /shared/ URL prefix rather than being copied in.
$SharedRoot = Join-Path $PSScriptRoot '..\shared-ui'
$SharedPrefix = '/shared/'
$Cp932 = [System.Text.Encoding]::GetEncoding(932)
$Utf8 = New-Object System.Text.UTF8Encoding($false)

if (-not (Test-Path -LiteralPath $WebRoot)) {
    throw "Missing web root: $WebRoot"
}
if (-not (Test-Path -LiteralPath $SharedRoot)) {
    Write-Warning "Missing shared UI root: $SharedRoot"
    Write-Warning "The page will load unstyled until projects\shared-ui is restored."
}
if (-not (Test-Path -LiteralPath $LogDir)) {
    Write-Warning "Log directory does not exist yet: $LogDir"
    Write-Warning "The UI will start anyway and pick logs up as soon as they appear."
}

# ------------------------------------------------------------ window opacity

<#
    A browser window is opaque no matter what CSS the page carries, so the pop-out
    windows' opacity slider cannot on its own let the game show through -- only the
    OS can do that, by making the window layered (WS_EX_LAYERED) and giving it an
    alpha. That is what /api/alpha does.

    FINDING THE WINDOW is the whole difficulty, because a Document
    Picture-in-Picture window's caption is Chrome's to write, not the page's --
    it is not reliably the document title. So the client sends where its window
    *is* (the centre of it, in screen coordinates) and the match is by position:
    WindowFromPoint, then GetAncestor to the top-level window. Nothing but the
    window the slider lives in can be at that point, and it is always-on-top so
    nothing can be over it. The title is kept as a second try, and "the topmost
    browser window" as a third, so a client that cannot report its position (or
    a screen coordinate lost to DPI scaling) still lands somewhere sensible.

    Refuses to touch the shell (desktop, taskbar) and returns which strategy hit,
    so a wrong window is diagnosable rather than mysterious. If the compiler is
    unavailable the endpoint still answers, with applied:0, and the client falls
    back to fading the document -- which cannot show the game, only quieten the
    panel, and which says so in the window bar.
#>
$WinAlphaSrc = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class DpsWindowAlpha
{
    private delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder buf, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder buf, int max);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT pt);
    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int val);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte alpha, uint flags);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED  = 0x00080000;
    private const int WS_EX_TOPMOST  = 0x00000008;
    private const uint LWA_COLORKEY  = 0x00000001;
    private const uint LWA_ALPHA     = 0x00000002;
    private const uint GA_ROOT       = 2;

    public static string Caption(IntPtr h)
    {
        StringBuilder sb = new StringBuilder(600);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string Cls(IntPtr h)
    {
        StringBuilder sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    /* The desktop and the taskbar are always under the mouse somewhere; dimming
       either would be spectacular and is never what was meant. */
    private static bool IsShell(IntPtr h)
    {
        string c = Cls(h);
        return c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" ||
               c == "Shell_SecondaryTrayWnd" || c == "#32769";
    }

    /* The point has to be somewhere a window can actually be seen. A stale or
       mis-scaled coordinate otherwise wanders into the parking lot at -32000,
       where the minimised windows live. */
    private static bool OnDesktop(int x, int y)
    {
        const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77,
                  SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN), vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN), vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (vw <= 0 || vh <= 0) { return true; }
        return x >= vx && y >= vy && x < vx + vw && y < vy + vh;
    }

    private static IntPtr Root(IntPtr h)
    {
        if (h == IntPtr.Zero) { return IntPtr.Zero; }
        IntPtr r = GetAncestor(h, GA_ROOT);
        return r == IntPtr.Zero ? h : r;
    }

    /* One candidate point. Rejected unless the window it lands on is visible, is
       not the shell, and is about the size the caller said it was. The size check
       is not optional: this server may be DPI-unaware while the caller measures in
       CSS pixels, so one of the two candidate points below is usually wrong, and
       on a scaled desktop a wrong point still lands on *something* -- quite
       possibly the game. Size is what tells the two apart. */
    private static IntPtr FromPoint(int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0) { return IntPtr.Zero; }
        if (!OnDesktop(x, y)) { return IntPtr.Zero; }
        POINT pt; pt.X = x; pt.Y = y;
        IntPtr hit = Root(WindowFromPoint(pt));
        if (hit == IntPtr.Zero || !IsWindowVisible(hit) || IsShell(hit)) { return IntPtr.Zero; }
        // Minimised windows park themselves off at -32000 with a stub rect, where
        // any two of them look alike and an off-screen point "finds" one of them.
        if (IsIconic(hit)) { return IntPtr.Zero; }
        RECT r;
        if (!GetWindowRect(hit, out r)) { return IntPtr.Zero; }
        int rw = r.Right - r.Left, rh = r.Bottom - r.Top;
        // Generous: the caller measures its viewport, the OS measures the frame.
        if (Math.Abs(rw - w) > 160 || Math.Abs(rh - h) > 220) { return IntPtr.Zero; }
        return hit;
    }

    private static IntPtr ByTitle(string title)
    {
        if (title == null || title.Length < 8) { return IntPtr.Zero; }
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr unused)
        {
            if (!IsWindowVisible(h)) { return true; }
            if (Caption(h).IndexOf(title, StringComparison.Ordinal) < 0) { return true; }
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    /* Last resort: a Document PiP window is always-on-top, and almost nothing
       else on a desktop is both topmost and a browser widget. */
    private static IntPtr TopmostBrowser()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr unused)
        {
            if (!IsWindowVisible(h)) { return true; }
            if ((GetWindowLong(h, GWL_EXSTYLE) & WS_EX_TOPMOST) == 0) { return true; }
            string c = Cls(h);
            if (c.IndexOf("Chrome_WidgetWin", StringComparison.Ordinal) < 0 &&
                c.IndexOf("MozillaWindowClass", StringComparison.Ordinal) < 0) { return true; }
            RECT r;
            if (!GetWindowRect(h, out r)) { return true; }
            if (r.Right - r.Left < 120 || r.Bottom - r.Top < 80) { return true; }  // skip tooltips
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    /*
     * cx,cy      centre of the caller's window in CSS screen pixels
     * w,h        its size, for the sanity check (0 to skip)
     * dpr        device pixel ratio, since a DPI-aware desktop scales those
     * alpha      0..255
     * key        colour to punch out entirely, or -1 for none. Painting the page
     *            background this exact colour is what makes the *background*
     *            vanish while the text over it stays fully crisp; plain alpha
     *            fades everything evenly instead.
     *
     * Returns "method|hwnd|caption", or "" if nothing matched.
     */
    public static string Apply(string title, int cx, int cy, int w, int h,
                               double dpr, byte alpha, int key)
    {
        IntPtr hit = IntPtr.Zero;
        string how = "";

        if (cx != 0 || cy != 0)
        {
            // Both readings, each with its own size to check against: this
            // process may or may not be DPI-aware, and the two agree only when
            // the desktop is at 100%. The size check rejects the wrong one.
            hit = FromPoint((int)Math.Round(cx * dpr), (int)Math.Round(cy * dpr),
                            (int)Math.Round(w * dpr), (int)Math.Round(h * dpr));
            if (hit == IntPtr.Zero) { hit = FromPoint(cx, cy, w, h); }
            if (hit != IntPtr.Zero) { how = "point"; }
        }
        if (hit == IntPtr.Zero) { hit = ByTitle(title); if (hit != IntPtr.Zero) { how = "title"; } }
        if (hit == IntPtr.Zero) { hit = TopmostBrowser(); if (hit != IntPtr.Zero) { how = "topmost"; } }
        if (hit == IntPtr.Zero) { return ""; }

        int ex = GetWindowLong(hit, GWL_EXSTYLE);
        if ((ex & WS_EX_LAYERED) == 0) { SetWindowLong(hit, GWL_EXSTYLE, ex | WS_EX_LAYERED); }

        uint flags = LWA_ALPHA;
        uint crKey = 0;
        if (key >= 0) { crKey = (uint)key; flags |= LWA_COLORKEY; }
        if (!SetLayeredWindowAttributes(hit, crKey, alpha, flags)) { return ""; }

        return how + "|" + hit.ToInt64().ToString() + "|" + Caption(hit);
    }
}
'@

$WinAlpha = $false
try {
    if (-not ('DpsWindowAlpha' -as [type])) { Add-Type -TypeDefinition $WinAlphaSrc }
    $WinAlpha = $true
}
catch {
    Write-Warning "Window transparency unavailable: $($_.Exception.Message)"
    Write-Warning "The pop-out opacity slider will fade the panel instead of the window."
}

# ---------------------------------------------------------------- JSON helpers

function ConvertTo-JsonString {
    param([string] $Value)
    if ($null -eq $Value) { return '""' }
    '"' + [System.Web.HttpUtility]::JavaScriptStringEncode($Value) + '"'
}

# ------------------------------------------------------------------ log access

function Get-NewestLog {
    param([string] $Dir)
    if (-not (Test-Path -LiteralPath $Dir)) { return $null }
    Get-ChildItem -LiteralPath $Dir -Filter '*.log' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

<#
    Reads whole lines from $Path starting at byte $Offset.

    Only complete lines are returned; a partial trailing line (the game is still
    writing it) is left for the next poll, which is why the caller must carry
    nextOffset forward rather than seeking to EOF.

    FileShare Read+Write+Delete keeps the game's own writer unblocked.
#>
function Read-LogTail {
    param(
        [string] $Path,
        [long]   $Offset
    )

    $result = @{ lines = @(); nextOffset = $Offset; size = 0L; truncated = $false }

    $fs = [System.IO.File]::Open(
        $Path,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]'ReadWrite,Delete')
    try {
        $len = $fs.Length
        $result.size = $len

        # File shrank -> it was rotated or rewritten under us; restart from zero.
        if ($Offset -gt $len) {
            $Offset = 0
            $result.truncated = $true
        }
        if ($Offset -ge $len) {
            $result.nextOffset = $len
            return $result
        }

        $count = [int][Math]::Min($len - $Offset, 8MB)
        $fs.Position = $Offset
        $buf = New-Object byte[] $count
        $read = $fs.Read($buf, 0, $count)
    }
    finally {
        $fs.Dispose()
    }

    # Trim back to the last newline so we never emit a half-written line.
    $last = -1
    for ($i = $read - 1; $i -ge 0; $i--) {
        if ($buf[$i] -eq 10) { $last = $i; break }
    }
    if ($last -lt 0) {
        $result.nextOffset = $Offset
        return $result
    }

    $text = $Cp932.GetString($buf, 0, $last + 1)
    $result.lines = $text -split "`r?`n"
    # -split leaves a trailing empty element after the final newline.
    if ($result.lines.Length -gt 0 -and $result.lines[-1] -eq '') {
        $result.lines = $result.lines[0..($result.lines.Length - 2)]
    }
    $result.nextOffset = $Offset + $last + 1
    return $result
}

function Get-LogPayload {
    param(
        [string] $ClientFile,
        [long]   $ClientOffset
    )

    $log = Get-NewestLog -Dir $LogDir
    if ($null -eq $log) {
        return '{"ok":true,"file":null,"reset":true,"offset":0,"nextOffset":0,"size":0,"lines":[],' +
               '"dir":' + (ConvertTo-JsonString $LogDir) + '}'
    }

    # A different file is newest now (day rollover, character switch, new session)
    # -> tell the client to drop its state and replay the new file from the top.
    $reset = $false
    $offset = $ClientOffset
    if ($ClientFile -ne $log.Name) {
        $reset = $true
        $offset = 0
    }

    $tail = Read-LogTail -Path $log.FullName -Offset $offset
    if ($tail.truncated) { $reset = $true }

    $encoded = @()
    if ($tail.lines.Length -gt 0) {
        $encoded = foreach ($line in $tail.lines) { ConvertTo-JsonString $line }
    }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('{"ok":true,"file":').Append((ConvertTo-JsonString $log.Name))
    [void]$sb.Append(',"dir":').Append((ConvertTo-JsonString $LogDir))
    [void]$sb.Append(',"mtime":').Append((ConvertTo-JsonString $log.LastWriteTime.ToString('o')))
    [void]$sb.Append(',"reset":').Append($(if ($reset) { 'true' } else { 'false' }))
    [void]$sb.Append(',"offset":').Append($offset)
    [void]$sb.Append(',"nextOffset":').Append($tail.nextOffset)
    [void]$sb.Append(',"size":').Append($tail.size)
    [void]$sb.Append(',"lines":[').Append(($encoded -join ',')).Append(']}')
    $sb.ToString()
}

# --------------------------------------------------------------- static assets

$MimeTypes = @{
    '.html' = 'text/html; charset=utf-8'
    '.css'  = 'text/css; charset=utf-8'
    '.js'   = 'application/javascript; charset=utf-8'
    '.json' = 'application/json; charset=utf-8'
    '.svg'  = 'image/svg+xml'
    '.ico'  = 'image/x-icon'
}

<#
    Maps a URL path onto a file. Everything resolves under .\web except the
    /shared/ prefix, which resolves under ..\shared-ui -- the design system both
    this app and ws_calculator link against. Each root gets the same containment
    check, so a traversal out of one can't land in the other or anywhere else.
#>
function Resolve-StaticPath {
    param([string] $UrlPath)

    $root = $WebRoot
    $rel = $UrlPath
    if ($rel.StartsWith($SharedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        $root = $SharedRoot
        $rel = $rel.Substring($SharedPrefix.Length)
    }

    $rel = $rel.TrimStart('/')
    if ([string]::IsNullOrWhiteSpace($rel)) { $rel = 'index.html' }
    $rel = $rel -replace '/', '\'

    $rootFull = [System.IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
    $full = [System.IO.Path]::GetFullPath((Join-Path $rootFull $rel))
    # Reject anything that escapes the root it was resolved against.
    if (-not $full.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) { return $null }
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { return $null }
    return $full
}

function Write-Response {
    param(
        [System.Net.HttpListenerResponse] $Response,
        [int]    $Status,
        [string] $ContentType,
        [byte[]] $Body
    )
    $Response.StatusCode = $Status
    $Response.ContentType = $ContentType
    $Response.Headers['Cache-Control'] = 'no-store, no-cache, must-revalidate'
    $Response.ContentLength64 = $Body.Length
    $Response.OutputStream.Write($Body, 0, $Body.Length)
    $Response.OutputStream.Close()
}

# ------------------------------------------------------------------- http loop

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:$Port/")

try {
    $listener.Start()
}
catch {
    throw "Could not listen on port $Port. Another instance may be running. Try -Port 8732.  ($($_.Exception.Message))"
}

$url = "http://localhost:$Port/"
Write-Host ''
Write-Host '  FFXI DPS Meter' -ForegroundColor Cyan
Write-Host "  serving   $url"
Write-Host "  watching  $LogDir"
$startLog = Get-NewestLog -Dir $LogDir
if ($startLog) {
    Write-Host "  newest    $($startLog.Name)  ($([math]::Round($startLog.Length / 1KB, 1)) KB)"
}
else {
    Write-Host '  newest    (no .log files found yet)' -ForegroundColor Yellow
}
Write-Host '  Ctrl-C to stop.' -ForegroundColor DarkGray
Write-Host ''

if (-not $NoBrowser) { Start-Process $url | Out-Null }

try {
    while ($listener.IsListening) {
        # Async accept polled on a short timeout so Ctrl-C stays responsive;
        # a blocking GetContext() would swallow it.
        $task = $listener.GetContextAsync()
        while (-not $task.Wait(250)) { }
        $ctx = $task.GetAwaiter().GetResult()

        $req = $ctx.Request
        $res = $ctx.Response

        try {
            if ($req.Url.AbsolutePath -eq '/api/log') {
                $qFile = $req.QueryString['file']
                $qOff = 0L
                [void][long]::TryParse($req.QueryString['offset'], [ref]$qOff)
                $json = Get-LogPayload -ClientFile $qFile -ClientOffset $qOff
                Write-Response $res 200 'application/json; charset=utf-8' $Utf8.GetBytes($json)
            }
            elseif ($req.Url.AbsolutePath -eq '/api/alpha') {
                # Parsed explicitly as UTF-8: the titles carry an em dash, and
                # HttpListener's own QueryString does not always decode it.
                $q = [System.Web.HttpUtility]::ParseQueryString($req.Url.Query, $Utf8)
                $title = $q['title']
                $pct = 100
                [void][int]::TryParse($q['value'], [ref]$pct)
                if ($pct -lt 10) { $pct = 10 }
                if ($pct -gt 100) { $pct = 100 }

                $cx = 0; $cy = 0; $cw = 0; $ch = 0; $dpr = 1.0
                [void][int]::TryParse($q['x'], [ref]$cx)
                [void][int]::TryParse($q['y'], [ref]$cy)
                [void][int]::TryParse($q['w'], [ref]$cw)
                [void][int]::TryParse($q['h'], [ref]$ch)
                [void][double]::TryParse($q['dpr'], [ref]$dpr)
                if ($dpr -le 0) { $dpr = 1.0 }

                # The punch-out colour arrives as RRGGBB; a COLORREF is 0x00BBGGRR.
                $key = -1
                if ($q['key'] -match '^[0-9a-fA-F]{6}$') {
                    $rgb = [Convert]::ToInt32($q['key'], 16)
                    $key = (($rgb -band 0xFF) -shl 16) -bor ($rgb -band 0xFF00) -bor (($rgb -shr 16) -band 0xFF)
                }

                $hit = ''
                if ($WinAlpha) {
                    $hit = [DpsWindowAlpha]::Apply($title, $cx, $cy, $cw, $ch, $dpr,
                                                  [byte][math]::Round(255 * $pct / 100), $key)
                }
                $parts = $hit -split '\|', 3
                $json = '{"ok":true,"applied":' + $(if ($hit) { '1' } else { '0' }) +
                        ',"supported":' + $(if ($WinAlpha) { 'true' } else { 'false' }) +
                        ',"method":' + (ConvertTo-JsonString $(if ($hit) { $parts[0] } else { '' })) +
                        ',"window":' + (ConvertTo-JsonString $(if ($hit) { $parts[2] } else { '' })) + '}'
                Write-Response $res 200 'application/json; charset=utf-8' $Utf8.GetBytes($json)
            }
            else {
                $path = Resolve-StaticPath $req.Url.AbsolutePath
                if ($null -eq $path) {
                    Write-Response $res 404 'text/plain; charset=utf-8' $Utf8.GetBytes('404 Not Found')
                }
                else {
                    $ext = [System.IO.Path]::GetExtension($path).ToLowerInvariant()
                    $type = $MimeTypes[$ext]
                    if (-not $type) { $type = 'application/octet-stream' }
                    Write-Response $res 200 $type ([System.IO.File]::ReadAllBytes($path))
                }
            }
        }
        catch {
            Write-Warning "$($req.Url.AbsolutePath): $($_.Exception.Message)"
            try {
                $msg = ConvertTo-JsonString $_.Exception.Message
                Write-Response $res 500 'application/json; charset=utf-8' `
                    $Utf8.GetBytes("{`"ok`":false,`"error`":$msg}")
            }
            catch { }
        }
    }
}
finally {
    $listener.Stop()
    $listener.Close()
    Write-Host 'Stopped.' -ForegroundColor DarkGray
}
