#Requires -Version 5.1
<#
.SYNOPSIS
    Fails if addon/src/ calls any external API not in ALLOWED_APIS.txt.

.DESCRIPTION
    The VibeXI addon must never be able to send anything to the game server.
    That is enforced by the addon not CONTAINING the capability, and this script
    is what proves it stayed that way.

    Three independent checks, all must pass:

      1. ALLOWLIST  every external call in addon/src/**/*.lua must be listed in
                    ALLOWED_APIS.txt. This is the real gate -- it catches APIs
                    nobody here has heard of, which a denylist cannot.
      2. DENYLIST   a short list of known egress routes, checked separately so
                    the failure message names the actual problem rather than
                    saying "unlisted token".
      3. EVENTS     ashita.events.register may only subscribe to the event names
                    allowlisted as 'event:<name>'. packet_out is the injection
                    route and must never appear.

    Comments are scanned too, but a hit inside a comment is a WARNING, not a
    failure -- a comment cannot execute, and "-- deliberately no socket here" is
    a sentence we want to be able to write.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File addon/check-apis.ps1
#>
[CmdletBinding()]
param(
    [string] $SrcDir,
    [string] $Manifest
)

$ErrorActionPreference = 'Stop'

# $PSScriptRoot is not reliably populated in param() defaults across invocation
# styles, so resolve the addon root in the body with a fallback chain.
$Root = $PSScriptRoot
if (-not $Root) { $Root = Split-Path -Parent $PSCommandPath }
if (-not $Root) { $Root = Split-Path -Parent $MyInvocation.MyCommand.Definition }

if (-not $SrcDir)   { $SrcDir   = Join-Path $Root 'src' }
if (-not $Manifest) { $Manifest = Join-Path $Root 'ALLOWED_APIS.txt' }

# Known egress routes. This is the SECOND line of defence, not the first --
# the allowlist is what actually holds. These exist to produce a clear message.
$Denied = @(
    @{ Pattern = 'GetChatManager';                Why = 'the only route to QueueCommand' }
    @{ Pattern = 'QueueCommand';                  Why = 'issues a game command / chat message' }
    @{ Pattern = 'packet_out';                    Why = 'outgoing packet subscription' }
    @{ Pattern = 'add_outgoing_packet';           Why = 'packet injection' }
    @{ Pattern = 'AddOutgoingPacket';             Why = 'packet injection' }
    @{ Pattern = 'InjectPacket';                  Why = 'packet injection' }
    @{ Pattern = "require\s*\(?\s*['`"]socket";   Why = 'network capability (PLAN.md Decision 1)' }
    @{ Pattern = "require\s*\(?\s*['`"]ffi";      Why = 'foreign-function escape hatch (PLAN.md Decision 2)' }
    @{ Pattern = 'ffi\.load';                     Why = 'can load ws2_32' }
    @{ Pattern = 'ffi\.cdef';                     Why = 'can declare connect()/send()' }
    @{ Pattern = 'os\.execute';                   Why = 'shell execution' }
    @{ Pattern = 'io\.popen';                     Why = 'shell execution' }
    @{ Pattern = 'ashita\.memory\.write';         Why = 'writes to game memory; this addon is read-only' }
    @{ Pattern = 'loadstring';                    Why = 'arbitrary code execution' }
    @{ Pattern = 'dofile';                        Why = 'arbitrary code execution' }
)

# Extractors. Each returns the normalised tokens documented in ALLOWED_APIS.txt.
$Extractors = @(
    @{ Rx = "require\s*\(?\s*['`"]([A-Za-z0-9_.]+)['`"]"; Fmt = "require('{0}')" }
    @{ Rx = '(ashita\.[a-z_]+\.[a-z_]+)';                 Fmt = '{0}' }
    @{ Rx = '(AshitaCore:[A-Za-z0-9_]+)';                 Fmt = '{0}' }
    @{ Rx = ':([A-Za-z0-9_]+)\s*\(';                      Fmt = ':{0}' }
    @{ Rx = '\b(GetEntity|GetPlayerEntity)\s*\(';         Fmt = '{0}()' }
    @{ Rx = '\b((?:io|os|package|debug)\.[a-z_]+)';       Fmt = '{0}' }
)

function Split-LuaLine {
    # Crude but adequate: everything after the first '--' is comment. A '--'
    # inside a string literal would truncate early, which can only LOSE code
    # coverage on that line's tail -- so pair this with the denylist, which
    # scans the comment side too and warns.
    param([string] $Line)
    $i = $Line.IndexOf('--')
    if ($i -lt 0) { return @{ Code = $Line;                Comment = '' } }
    return          @{ Code = $Line.Substring(0, $i); Comment = $Line.Substring($i) }
}

# ---------------------------------------------------------------- load manifest

if (-not (Test-Path $Manifest)) {
    Write-Host "FAIL  manifest not found: $Manifest" -ForegroundColor Red
    exit 1
}

$allowed = @{}
foreach ($line in (Get-Content $Manifest)) {
    $t = $line.Trim()
    if (-not $t -or $t.StartsWith('#')) { continue }
    $allowed[$t] = $true
}

$allowedEvents = @{}
foreach ($k in $allowed.Keys) {
    if ($k -like 'event:*') { $allowedEvents[$k.Substring(6)] = $true }
}

# ------------------------------------------------------------------- find source

if (-not (Test-Path $SrcDir)) {
    Write-Host "PASS  no addon/src/ yet -- nothing to check." -ForegroundColor Green
    Write-Host "      $($allowed.Count) tokens allowlisted, $($allowedEvents.Count) events."
    exit 0
}

$files = @(Get-ChildItem -Path $SrcDir -Recurse -Filter *.lua -File -ErrorAction SilentlyContinue)
if ($files.Count -eq 0) {
    Write-Host "PASS  no Lua sources in addon/src/ yet -- nothing to check." -ForegroundColor Green
    Write-Host "      $($allowed.Count) tokens allowlisted, $($allowedEvents.Count) events."
    exit 0
}

# ------------------------------------------------------------------------- scan

$violations = New-Object System.Collections.Generic.List[object]
$warnings   = New-Object System.Collections.Generic.List[object]
$seen       = @{}

foreach ($file in $files) {
    $rel = $file.FullName.Substring((Resolve-Path $Root).Path.Length + 1)
    $n = 0
    foreach ($raw in (Get-Content $file.FullName)) {
        $n++
        $parts   = Split-LuaLine $raw
        $code    = $parts.Code
        $comment = $parts.Comment

        # 1. denylist -- code side fails, comment side warns
        foreach ($d in $Denied) {
            if ($code -match $d.Pattern) {
                $violations.Add([pscustomobject]@{
                    Kind = 'DENIED'; File = $rel; Line = $n
                    Token = $Matches[0]; Note = $d.Why; Text = $raw.Trim() })
            }
            elseif ($comment -match $d.Pattern) {
                $warnings.Add([pscustomobject]@{
                    File = $rel; Line = $n; Token = $Matches[0]; Text = $raw.Trim() })
            }
        }

        # 2. allowlist -- every external call in code must be listed
        foreach ($ex in $Extractors) {
            foreach ($m in [regex]::Matches($code, $ex.Rx)) {
                $token = $ex.Fmt -f $m.Groups[1].Value
                $seen[$token] = $true
                if (-not $allowed.ContainsKey($token)) {
                    $violations.Add([pscustomobject]@{
                        Kind = 'UNLISTED'; File = $rel; Line = $n
                        Token = $token; Note = 'not in ALLOWED_APIS.txt'; Text = $raw.Trim() })
                }
            }
        }

        # 3. event subscriptions must be allowlisted by name
        foreach ($m in [regex]::Matches($code, "ashita\.events\.register\s*\(\s*['`"]([a-z0-9_]+)['`"]")) {
            $ev = $m.Groups[1].Value
            $seen["event:$ev"] = $true
            if (-not $allowedEvents.ContainsKey($ev)) {
                $violations.Add([pscustomobject]@{
                    Kind = 'EVENT'; File = $rel; Line = $n
                    Token = "event:$ev"; Note = 'event not allowlisted'; Text = $raw.Trim() })
            }
        }
    }
}

# ----------------------------------------------------------------------- report

Write-Host ''
Write-Host "addon API check -- $($files.Count) file(s), $($seen.Count) distinct external call(s)"

if ($warnings.Count -gt 0) {
    Write-Host ''
    Write-Host "WARN  denylisted name mentioned in a comment (not a failure):" -ForegroundColor Yellow
    foreach ($w in $warnings) {
        Write-Host ("      {0}:{1}  {2}" -f $w.File, $w.Line, $w.Token) -ForegroundColor Yellow
    }
}

if ($violations.Count -gt 0) {
    Write-Host ''
    Write-Host "FAIL  $($violations.Count) violation(s)" -ForegroundColor Red
    foreach ($v in $violations) {
        Write-Host ''
        Write-Host ("  [{0}] {1}:{2}" -f $v.Kind, $v.File, $v.Line) -ForegroundColor Red
        Write-Host ("      {0}  --  {1}" -f $v.Token, $v.Note)
        Write-Host ("      | {0}" -f $v.Text) -ForegroundColor DarkGray
    }
    Write-Host ''
    Write-Host "  The addon must never be able to reach the game server." -ForegroundColor Red
    Write-Host "  If a call is genuinely safe and needed, add it to ALLOWED_APIS.txt"
    Write-Host "  deliberately -- and say why in the comment above it."
    Write-Host ''
    exit 1
}

Write-Host ''
Write-Host "PASS  every external call is allowlisted." -ForegroundColor Green
Write-Host ''
exit 0
