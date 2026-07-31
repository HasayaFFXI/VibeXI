# Structural sanity check for Lua 5.1 sources, for machines with no interpreter.
#
# Ashita embeds LuaJIT inside Ashita.dll and ships nothing standalone, so on a
# dev box with no Lua the addon's text is never parsed until `/addon load`
# reports a line number. This closes most of that gap: it tokenizes strings and
# comments away (including long-bracket forms), then balances block keywords and
# brackets.
#
# WHAT IT CATCHES: a dropped or extra `end`, an unbalanced `repeat`/`until`, an
# unclosed paren/brace/bracket, an unterminated string or long comment.
#
# WHAT IT DOES NOT: it is not a parser. A misspelled identifier, a bad
# expression, or a runtime error still surfaces only in-game.
#
# Calibration: run over the 108 .lua files shipped in Ashita-v4beta/addons it
# reports zero failures, and it does flag both mutations (one `end` removed, one
# paren removed) of this repo's own sources.
#
#     addon/tools/check-lua.ps1 -Path (Get-ChildItem addon/src/*.lua).FullName

param([Parameter(Mandatory=$true)][string[]] $Path)

function Strip-Lua {
    param([string] $s)
    $out = New-Object System.Text.StringBuilder
    $i = 0; $n = $s.Length
    while ($i -lt $n) {
        $c = $s[$i]
        # long bracket [=*[ ... ]=*]  (comment form handled below via --)
        if ($c -eq '-' -and $i+1 -lt $n -and $s[$i+1] -eq '-') {
            $j = $i + 2
            $lvl = -1
            if ($j -lt $n -and $s[$j] -eq '[') {
                $k = $j + 1; $eq = 0
                while ($k -lt $n -and $s[$k] -eq '=') { $eq++; $k++ }
                if ($k -lt $n -and $s[$k] -eq '[') { $lvl = $eq; $j = $k + 1 }
            }
            if ($lvl -ge 0) {
                $close = ']' + ('=' * $lvl) + ']'
                $e = $s.IndexOf($close, $j)
                if ($e -lt 0) { return @{ err = "unterminated long comment"; text = $out.ToString() } }
                # keep newlines so line numbers stay usable
                $chunk = $s.Substring($j, $e - $j)
                [void]$out.Append(($chunk -replace '[^\r\n]', ' '))
                $i = $e + $close.Length
            } else {
                while ($i -lt $n -and $s[$i] -ne "`n") { $i++ }
            }
            continue
        }
        # long string
        if ($c -eq '[') {
            $k = $i + 1; $eq = 0
            while ($k -lt $n -and $s[$k] -eq '=') { $eq++; $k++ }
            if ($k -lt $n -and $s[$k] -eq '[') {
                $close = ']' + ('=' * $eq) + ']'
                $e = $s.IndexOf($close, $k + 1)
                if ($e -lt 0) { return @{ err = "unterminated long string"; text = $out.ToString() } }
                $chunk = $s.Substring($k + 1, $e - $k - 1)
                [void]$out.Append(($chunk -replace '[^\r\n]', ' '))
                $i = $e + $close.Length
                continue
            }
        }
        # quoted string
        if ($c -eq '"' -or $c -eq "'") {
            $q = $c; $i++
            while ($i -lt $n) {
                if ($s[$i] -eq '\') { $i += 2; continue }
                if ($s[$i] -eq $q) { $i++; break }
                if ($s[$i] -eq "`n") { return @{ err = "unterminated string"; text = $out.ToString() } }
                $i++
            }
            [void]$out.Append('""')
            continue
        }
        [void]$out.Append($c)
        $i++
    }
    return @{ err = $null; text = $out.ToString() }
}

$fail = 0
foreach ($p in $Path) {
    $src = Get-Content -Raw -LiteralPath $p
    $r = Strip-Lua $src
    if ($r.err) { Write-Host ("FAIL  {0}: {1}" -f (Split-Path -Leaf $p), $r.err) -ForegroundColor Red; $fail++; continue }
    $t = $r.text

    # bracket balance
    $bad = $null
    foreach ($pair in @(@('(', ')'), @('{', '}'), @('[', ']'))) {
        $o = ([regex]::Matches($t, [regex]::Escape($pair[0]))).Count
        $c = ([regex]::Matches($t, [regex]::Escape($pair[1]))).Count
        if ($o -ne $c) { $bad = "{0}{1} unbalanced: {2} vs {3}" -f $pair[0], $pair[1], $o, $c }
    }

    # block balance. openers are: function, if..then, do   (elseif/while/for
    # are not counted -- 'do'/'then' is what actually opens their block)
    $kw = [regex]::Matches($t, '(?<![%w_])(function|if|do|end|repeat|until|elseif|then)(?![%w_])')
    $depth = 0; $rep = 0; $minDepth = 0
    foreach ($m in [regex]::Matches($t, '\b(function|then|do|end|repeat|until|elseif)\b')) {
        switch ($m.Value) {
            'function' { $depth++ }
            'then'     { $depth++ }
            'elseif'   { $depth-- }   # its 'then' will re-open
            'do'       { $depth++ }
            'end'      { $depth-- }
            'repeat'   { $rep++ }
            'until'    { $rep-- }
        }
        if ($depth -lt $minDepth) { $minDepth = $depth }
    }

    $msgs = @()
    if ($bad)         { $msgs += $bad }
    if ($depth -ne 0) { $msgs += "block depth ends at $depth (expected 0)" }
    if ($rep -ne 0)   { $msgs += "repeat/until unbalanced: $rep" }
    if ($minDepth -lt 0) { $msgs += "depth went negative ($minDepth): an extra 'end'" }

    if ($msgs.Count -gt 0) {
        Write-Host ("FAIL  {0}" -f (Split-Path -Leaf $p)) -ForegroundColor Red
        $msgs | ForEach-Object { Write-Host "        $_" -ForegroundColor Red }
        $fail++
    } else {
        Write-Host ("ok    {0}" -f (Split-Path -Leaf $p)) -ForegroundColor Green
    }
}
if ($fail -gt 0) { exit 1 }
Write-Host "`nAll files structurally balanced." -ForegroundColor Green
