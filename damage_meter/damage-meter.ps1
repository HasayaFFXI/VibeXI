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
