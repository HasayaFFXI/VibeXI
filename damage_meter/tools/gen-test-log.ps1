# Generates a synthetic HorizonXI chat log to exercise the meter:
# multiple characters, weaponskills, crits, skillchains, magic bursts, ranged
# attacks, additional effects, chat noise, an article-less NM, and fight gaps.
param(
    [string] $Out = "$PSScriptRoot\logs\Hasaya_2026.07.30.log"
)

$dir = Split-Path $Out
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }

$rand = New-Object System.Random 20260730
$lines = New-Object System.Collections.Generic.List[string]
$t = New-TimeSpan -Hours 14 -Minutes 2

function Stamp { param([TimeSpan]$ts) '[{0:hh\:mm\:ss}]' -f $ts }
function Emit { param([string]$s, [switch]$Cont)
    if ($Cont) { $script:lines.Add($s) } else { $script:lines.Add((Stamp $script:t) + ' ' + $s) }
}
function Adv { param([int]$sec) $script:t = $script:t.Add([TimeSpan]::FromSeconds($sec)) }

# Normal-ish draw, clamped, integer.
function Roll { param([int]$mean, [double]$sd, [int]$floor = 1)
    $u1 = $rand.NextDouble(); $u2 = $rand.NextDouble()
    $z = [Math]::Sqrt(-2 * [Math]::Log($u1)) * [Math]::Cos(2 * [Math]::PI * $u2)
    [Math]::Max($floor, [int][Math]::Round($mean + $z * $sd))
}

$melee = @(
    @{ n = 'Hasaya';     dmg = 210; sd = 34; acc = 0.90; ws = 'Tachi: Jinpu';  wsD = 720; wsSd = 130; every = 5 }
    @{ n = 'Parabellum'; dmg = 165; sd = 40; acc = 0.85; ws = 'Rampage';       wsD = 590; wsSd = 160; every = 6 }
    @{ n = 'Rhyllis';    dmg = 128; sd = 22; acc = 0.93; ws = 'Vorpal Blade';  wsD = 430; wsSd = 90;  every = 7 }
)
$mobs = @('the Goblin Pathfinder', 'the Steelshell Crab', 'Leaping Lizzy')

$chatter = @(
    'Lollipops[LowJeuno]: {Garlaige Citadel} Coffer hunt @1 LFM ',
    'Woke[PortJeuno]: ISP {Alliance} {Do you need it?} {Looking for members.}',
    '[LuAshitacast] TP MaxDamage Set Equipped',
    'Moogle : Chaaange...job! Kupopopooo!',
    'Conscious[PortJeuno]: hit the crab for 9999 points of damage lol'
)

Emit '[Ashita] Loaded plugin: Deeps version: 1.06 - by: Relliko, kjLotus'
Emit '<<< Welcome to HorizonXI! >>>'
Adv 4
Emit '=== Area: Rolanberry Fields ==='
Adv 3

# Four fights, with idle gaps between so "Latest fight" has something to find.
foreach ($fight in 0..3) {
    $mob = $mobs[$fight % $mobs.Count]
    $mobBare = $mob -replace '^the ', ''
    $swings = 26 + $rand.Next(0, 14)
    $wsCount = @{}

    foreach ($i in 1..$swings) {
        foreach ($p in $melee) {
            if ($rand.NextDouble() -gt 0.72) { continue }

            if ($rand.NextDouble() -lt $p.acc) {
                if ($rand.NextDouble() -lt 0.12) {
                    Emit ('{0} scores a critical hit!' -f $p.n)
                    Emit ('{0} takes {1} points of damage.' -f $mob, (Roll ([int]($p.dmg * 1.45)) $p.sd)) -Cont
                } else {
                    Emit ('{0} hits {1} for {2} points of damage.' -f $p.n, $mob, (Roll $p.dmg $p.sd))
                }
            } else {
                Emit ('{0} misses {1}.' -f $p.n, $mob)
            }

            $c = [int]$wsCount[$p.n]
            $wsCount[$p.n] = $c + 1
            if (($c + 1) % $p.every -eq 0) {
                Emit ('{0} readies {1}.' -f $p.n, $p.ws)
                Adv 1
                Emit ('{0} uses {1}.' -f $p.n, $p.ws)
                if ($rand.NextDouble() -lt 0.94) {
                    Emit ('{0} takes {1} points of damage.' -f $mob, (Roll $p.wsD $p.wsSd)) -Cont
                    if ($rand.NextDouble() -lt 0.30) {
                        # Most of the time another character's swing lands
                        # between the weaponskill and the skillchain message.
                        # The chain still belongs to the weaponskill user, so
                        # this is the case that catches an attribution that
                        # follows "last thing that dealt damage" instead.
                        if ($rand.NextDouble() -lt 0.7) {
                            $other = @($melee | Where-Object { $_.n -ne $p.n })[$rand.Next(0, $melee.Count - 1)]
                            Emit ('{0} hits {1} for {2} points of damage.' -f $other.n, $mob, (Roll $other.dmg $other.sd))
                        }
                        Emit 'Skillchain: Fusion.'
                        Emit ('{0} takes {1} points of damage.' -f $mob, (Roll 310 70)) -Cont
                        if ($rand.NextDouble() -lt 0.6) {
                            Emit 'Gillette casts Fire IV.'
                            Emit 'Magic Burst!'
                            Emit ('{0} takes {1} points of damage.' -f $mob, (Roll 880 150)) -Cont
                        }
                    }
                    if ($rand.NextDouble() -lt 0.18) {
                        Emit ('Additional effect: {0} points of damage.' -f (Roll 42 12)) -Cont
                    }
                } else {
                    Emit ('{0} evades the attack.' -f $mob) -Cont
                }
            }
        }

        # Ranged attacker
        if ($rand.NextDouble() -lt 0.34) {
            if ($rand.NextDouble() -lt 0.88) {
                Emit ("Xatsh's ranged attack hits {0} for {1} points of damage." -f $mob, (Roll 240 55))
            } else {
                Emit ('Xatsh misses {0}.' -f $mob)
            }
        }
        if ($rand.NextDouble() -lt 0.09) {
            Emit 'Xatsh uses Sidewinder.'
            Emit ('{0} takes {1} points of damage.' -f $mob, (Roll 810 190)) -Cont
        }

        # Black mage nukes
        if ($rand.NextDouble() -lt 0.14) {
            Emit 'Gillette casts Thunder III.'
            Emit ('{0} takes {1} points of damage.' -f $mob, (Roll 520 110)) -Cont
        }

        # Incoming damage, so the Monsters side has something to show
        if ($rand.NextDouble() -lt 0.45) {
            $victim = $melee[$rand.Next(0, $melee.Count)].n
            if ($rand.NextDouble() -lt 0.6) {
                Emit ('{0} hits {1} for {2} points of damage.' -f $mob, $victim, (Roll 58 18))
            } else {
                Emit ('{0} misses {1}.' -f $mob, $victim)
            }
        }
        if ($rand.NextDouble() -lt 0.07) {
            Emit ('{0} readies Bomb Toss.' -f $mob)
            Adv 1
            Emit ('{0} uses Bomb Toss.' -f $mob)
            Emit ('{0} takes {1} points of damage.' -f $melee[0].n, (Roll 190 45)) -Cont
        }

        if ($rand.NextDouble() -lt 0.10) { Emit $chatter[$rand.Next(0, $chatter.Count)] }
        Adv (2 + $rand.Next(0, 4))
    }

    Emit ('Hasaya defeats {0}.' -f $mob)
    Adv 1
    Emit ('Hasaya obtains {0} gil.' -f $rand.Next(60, 900))
    Adv (110 + $rand.Next(0, 200))   # downtime between fights
}

[System.IO.File]::WriteAllLines($Out, $lines, [System.Text.Encoding]::GetEncoding(932))
"wrote $($lines.Count) lines -> $Out"
