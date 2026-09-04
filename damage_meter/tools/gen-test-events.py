"""Generates a synthetic VibeXI addon event file to exercise the meter without a
game running: several characters, a pet, multi-attack rounds, weaponskills,
crits, skillchains, magic bursts, ranged attacks, additional effects, an
unresolved target, monster damage on the party, and gaps between fights.

    python damage_meter/tools/gen-test-events.py
    python damage_meter/tools/gen-test-events.py --out somewhere/Hasaya_2026.09.04.jsonl

It writes exactly what `addons/VibeXI/vx_emit.lua` writes: one ASCII JSON object
per line, same field order, same field names, same message ids. If this file and
that one ever disagree the fixture is worthless, so `vx_emit.encode` is the thing
to diff against when either changes.

THE .NET RNG IS GONE, deliberately. The chat-log generator this replaces carried
a hand-written copy of .NET's `System.Random` so its output stayed byte-identical
to the PowerShell script it was ported from -- the fixture's regression cases were
identified by position in that stream. Nothing survives that mapping across a
change of output format, so there is no stream left to preserve and a seeded
`random.Random` is the honest choice. The cases below are identified by what they
are, not by where they land.

WHAT IS IN HERE ON PURPOSE, and what each case catches:

  * ONE `use` PER SWING, SHARED ACROSS TARGETS. An AoE nuke on three mobs is
    three rows sharing one use id and must collapse to a single cast -- the case
    the chat log could only guess at with a five-second window. A multi-attack
    round is the opposite: two or three genuine swings, each with its own use and
    its own hit-or-miss outcome, which must NOT collapse. Getting those two
    confused is what makes a party read as one that never misses, because the
    swing count is the denominator accuracy divides by.
  * SKILLCHAINS ARE THEIR OWN EVENT with their own use id, so a chain's damage
    is never folded into the weaponskill that closed it. The ids are Metrics'
    Res.WS.Skillchains, and the fixture draws from both of its ranges --
    288..301 and the 385/386 pair, which Metrics maps to Light and Darkness like
    any other chain rather than treating as a separate outcome.
  * AN ARTICLE-LESS NM. "Leaping Lizzy" reads no differently from "Goblin
    Pathfinder" here, because spawn flags classify both. It is kept as the case
    that proves the article heuristic is really gone.
  * A PET with `owner` set, and an NPC, and monster damage on the party. None of
    the three may reach the character chips, the bars chart or the actions
    table; all three must appear in the Diagnostics roster.
  * AN UNRESOLVED TARGET -- name "Unknown", targetKind "other" -- which is what
    the addon writes when the entity table has no answer.
  * META LINES: the startup environment probe on line 1, and an unknown-message
    notice. Consumers skip these, and Diagnostics shows them.
"""

import argparse
import json
import random
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
SEED = 20260730

# Message ids, from addons/VibeXI/vx_enums.lua. Named here so the fixture reads
# as game outcomes rather than as numbers.
MSG = {
    'hit': 1,           # AttackHits
    'magic': 2,         # MagicDamage
    'crit': 67,         # AttackCrit
    'miss': 15,         # AttackMisses
    'parry': 70,        # TargetParries
    'evade': 282,       # TargetEvades
    'ws_hit': 185,      # UsesSkillTakesDamage
    'ws_miss': 188,     # UsesSkillMisses
    'burst': 252,       # MagicBurstDamage
    'ability': 110,     # UsesAbilityTakesDamage
    'rng_hit': 352,     # RangedAttackHit
    'rng_crit': 353,    # RangedAttackCrit
    'rng_miss': 354,    # RangedAttackMiss
    'addl': 229,        # AddEffectAdditionalDamage
}

# Skillchain ids, from Metrics' Res.WS.Skillchains by way of
# addons/VibeXI/vx_enums.lua. A lookup, not a formula -- see the long note there.
# 385/386 are Light and Darkness a second time, not a different outcome.
SC_IDS = [
    ('Light', 288), ('Darkness', 289), ('Fusion', 293), ('Liquefaction', 295),
    ('Impaction', 301), ('Light', 385), ('Darkness', 386),
    ('Radiance', 767), ('Umbra', 768),
]


# -------------------------------------------------------------------- fixture

MELEE = [
    {'n': 'Hasaya',     'dmg': 210, 'sd': 34, 'acc': 0.90, 'ws': 'Tachi: Jinpu',
     'wsid': 32, 'wsD': 720, 'wsSd': 130, 'every': 5, 'multi': 0.28},
    {'n': 'Parabellum', 'dmg': 165, 'sd': 40, 'acc': 0.85, 'ws': 'Rampage',
     'wsid': 48, 'wsD': 590, 'wsSd': 160, 'every': 6, 'multi': 0.42},
    {'n': 'Rhyllis',    'dmg': 128, 'sd': 22, 'acc': 0.93, 'ws': 'Vorpal Blade',
     'wsid': 16, 'wsD': 430, 'wsSd': 90, 'every': 7, 'multi': 0.20},
]

RANGER = 'Xatsh'
MAGE = 'Gillette'
PET = {'n': 'Fluffikins', 'owner': 'Parabellum'}
NPC = 'Nomad Moogle'

# No articles anywhere. The packet carries the entity's real name and its spawn
# flags say what it is, so "Leaping Lizzy" needs no special handling at all --
# which is precisely why it is still in the list.
MOBS = ['Goblin Pathfinder', 'Steelshell Crab', 'Leaping Lizzy']

ADD_SETS = [
    ('Goblin Ambusher', 'Goblin Smithy'),
    ('Rock Crab', 'Land Crab'),
    ('Bigclaw', 'Snipper'),
]


class Writer:
    """Mints ids and stamps the clock the same way the addon does.

    `t` is whole seconds (the addon does not link LuaSocket for a finer clock --
    addon-dev/PLAN.md, Decision 1) and `seq` orders events inside one second, so
    the pair is what the reader sorts on.
    """

    def __init__(self, start):
        self.t = start
        self.use = 0
        self.seq = 0
        self.seq_at = 0
        self.lines = []
        self.counts = {}
        self.sc_turn = 0        # cursor into SC_IDS; see the skillchain block

    def advance(self, seconds):
        self.t += seconds

    def next_use(self):
        self.use += 1
        return self.use

    def _next_seq(self):
        if self.t != self.seq_at:
            self.seq_at = self.t
            self.seq = 0
        self.seq += 1
        return self.seq

    def raw(self, obj):
        self.lines.append(json.dumps(obj, ensure_ascii=True, separators=(',', ':')))

    def write(self, use, kind, actor, actor_kind, action, action_id,
              target, target_kind, dmg, hit, msg, crit=False, burst=False,
              owner=None, pet=None):
        # Field order matches vx_emit.encode, so a fixture line and a real line
        # are the same bytes for the same event.
        e = {
            't': self.t,
            'seq': self._next_seq(),
            'use': use,
            'kind': kind,
            'actor': actor,
            'actorKind': actor_kind,
            'action': action,
            'actionId': action_id,
            'target': target,
            'targetKind': target_kind,
            'dmg': int(dmg),
            'hit': bool(hit),
            'crit': bool(crit),
            'burst': bool(burst),
            'msg': msg,
        }
        if owner:
            e['owner'] = owner
        if pet:
            e['pet'] = pet
        self.raw(e)
        self.counts[kind] = self.counts.get(kind, 0) + 1


def generate(seed=SEED, start=None):
    rand = random.Random(seed)
    if start is None:
        # A fixed wall-clock start, so the fixture is reproducible.
        start = int(time.mktime((2026, 7, 30, 14, 2, 0, 0, 0, -1)))
    w = Writer(start)

    def roll(mean, sd, floor=1):
        return max(floor, int(round(rand.gauss(mean, sd))))

    # ---- line 1: the startup environment probe, exactly as vibexi.lua writes it
    w.raw({
        'kind': 'meta', 'v': '0.1.0', 't': w.t,
        'chunkData': 'string', 'injected': 'boolean', 'dataLen': 104,
        'isZoningType': 'number', 'selfName': 'Hasaya', 'selfSpawnFlags': 525,
        'abilProbeId': 5, 'abilRaw': 'Combo', 'abilOffset': 'Berserk',
        'spellLookup': 'Cure', 'wsLookup': 'Combo',
        'path': 'C:\\Users\\thadl\\AppData\\Local\\VibeXI\\events\\Hasaya_2026.07.30.jsonl',
    })

    def melee_round(p, mob):
        """One attack round: one `use` PER SWING.

        A multi-attack round arrives as several results on one target inside a
        single packet, and each result is a real swing with its own outcome. It
        gets its own use id for exactly that reason: sharing one across the round
        would let collapse() fold a landed swing and a whiffed one into a single
        hit, and the swing count is the denominator accuracy divides by. Compare
        aoe_nuke(), where several TARGETS genuinely are one use.
        """
        swings = 1
        while swings < 3 and rand.random() < p['multi']:
            swings += 1

        for _ in range(swings):
            use = w.next_use()
            if rand.random() < p['acc']:
                crit = rand.random() < 0.12
                dmg = roll(round(p['dmg'] * (1.45 if crit else 1.0)), p['sd'])
                w.write(use, 'melee', p['n'], 'player', 'Attack', 0,
                        mob, 'mob', dmg, True,
                        MSG['crit'] if crit else MSG['hit'], crit=crit)
            else:
                # Miss, parry and evade are separate messages, so the accuracy
                # denominator is exact rather than "everything that was not a
                # damage line".
                msg = rand.choice([MSG['miss'], MSG['miss'], MSG['parry'], MSG['evade']])
                w.write(use, 'melee', p['n'], 'player', 'Attack', 0,
                        mob, 'mob', 0, False, msg)

    def weaponskill(p, mob):
        use = w.next_use()
        if rand.random() >= 0.94:
            w.write(use, 'ws', p['n'], 'player', p['ws'], p['wsid'],
                    mob, 'mob', 0, False, MSG['ws_miss'])
            return

        w.write(use, 'ws', p['n'], 'player', p['ws'], p['wsid'],
                mob, 'mob', roll(p['wsD'], p['wsSd']), True, MSG['ws_hit'])

        # An additional effect rides on the weaponskill's result but is its own
        # row with its own use, so it never inflates the weaponskill's damage.
        if rand.random() < 0.18:
            w.write(w.next_use(), 'addl', p['n'], 'player', 'Additional Effect',
                    MSG['addl'], mob, 'mob', roll(42, 12), True, MSG['addl'])

        if rand.random() < 0.30:
            # Round-robin, not rand.choice: with a handful of chains drawn from
            # nine ids, a random draw leaves some of them out of any given seed
            # — Radiance went missing that way. A fixture that only sometimes
            # covers a case is not a regression test, so every id in Metrics'
            # table gets used in order and the fixture's coverage is a property
            # of the generator rather than of the seed.
            name, sc_id = SC_IDS[w.sc_turn % len(SC_IDS)]
            w.sc_turn += 1
            w.write(w.next_use(), 'skillchain', p['n'], 'player',
                    'Skillchain: ' + name, sc_id,
                    mob, 'mob', roll(310, 70), True, sc_id)

            if rand.random() < 0.6:
                w.advance(1)
                w.write(w.next_use(), 'magic', MAGE, 'player', 'Fire IV', 148,
                        mob, 'mob', roll(880, 150), True, MSG['burst'], burst=True)

    def aoe_nuke(mob, adds):
        """ONE cast, three victims, ONE use id.

        The packet hands over the whole target list, so the grouping is stated
        rather than inferred. Uncollapsed this reads as three casts of Firaga III
        and drags the histogram down onto the splash damage.
        """
        use = w.next_use()
        w.write(use, 'magic', MAGE, 'player', 'Firaga III', 177,
                mob, 'mob', roll(700, 120), True, MSG['magic'])
        for add in adds:
            w.write(use, 'magic', MAGE, 'player', 'Firaga III', 177,
                    add, 'mob', roll(660, 120), True, MSG['magic'])

    fights = 4
    for fight in range(fights):
        mob = MOBS[fight % len(MOBS)]
        adds = ADD_SETS[fight % len(ADD_SETS)]
        rounds = 26 + rand.randrange(14)
        ws_count = {}

        for _ in range(rounds):
            for p in MELEE:
                if rand.random() > 0.72:
                    continue
                melee_round(p, mob)

                c = ws_count.get(p['n'], 0) + 1
                ws_count[p['n']] = c
                if c % p['every'] == 0:
                    w.advance(1)
                    weaponskill(p, mob)

            # Ranged. A ranged miss is its own message now, so a ranged attacker
            # no longer has to share the melee Attack bucket to show misses.
            if rand.random() < 0.34:
                use = w.next_use()
                if rand.random() < 0.88:
                    crit = rand.random() < 0.10
                    w.write(use, 'ranged', RANGER, 'player', 'Ranged Attack', 0,
                            mob, 'mob', roll(240 * (1.5 if crit else 1), 55), True,
                            MSG['rng_crit'] if crit else MSG['rng_hit'], crit=crit)
                else:
                    w.write(use, 'ranged', RANGER, 'player', 'Ranged Attack', 0,
                            mob, 'mob', 0, False, MSG['rng_miss'])

            if rand.random() < 0.09:
                w.write(w.next_use(), 'ws', RANGER, 'player', 'Sidewinder', 144,
                        mob, 'mob', roll(810, 190), True, MSG['ws_hit'])

            if rand.random() < 0.14:
                w.write(w.next_use(), 'magic', MAGE, 'player', 'Thunder III', 165,
                        mob, 'mob', roll(520, 110), True, MSG['magic'])

            if rand.random() < 0.11:
                aoe_nuke(mob, adds)

            # A pet. Its own row, with `owner` naming its master -- which the
            # chat log could never say. Counted as the party's damage.
            if rand.random() < 0.16:
                w.write(w.next_use(), 'pet', PET['n'], 'pet', 'Big Scissors', 720,
                        mob, 'mob', roll(180, 40), True, MSG['ability'],
                        owner=PET['owner'], pet=PET['n'])

            # The monster's own damage. Parsed, kept, shown in the roster -- and
            # never counted, charted or totalled anywhere.
            if rand.random() < 0.45:
                victim = rand.choice(MELEE)['n']
                use = w.next_use()
                if rand.random() < 0.6:
                    w.write(use, 'melee', mob, 'mob', 'Attack', 0,
                            victim, 'player', roll(58, 18), True, MSG['hit'])
                else:
                    w.write(use, 'melee', mob, 'mob', 'Attack', 0,
                            victim, 'player', 0, False, MSG['miss'])

            if rand.random() < 0.07:
                w.write(w.next_use(), 'mobtp', mob, 'mob', 'Bomb Toss', 592,
                        MELEE[0]['n'], 'player', roll(190, 45), True, MSG['ws_hit'])

            w.advance(2 + rand.randrange(5))

        # An unresolved target: the entity table had no answer for this id, so
        # the addon writes "Unknown"/"other". It must not join the party.
        if fight == 1:
            w.write(w.next_use(), 'magic', MAGE, 'player', 'Stone III', 167,
                    'Unknown', 'other', roll(300, 60), True, MSG['magic'])

        # An NPC taking a stray hit, so one is in the roster.
        if fight == 2:
            w.write(w.next_use(), 'melee', NPC, 'npc', 'Attack', 0,
                    mob, 'mob', roll(30, 8), True, MSG['hit'])

        # One unknown-message notice, exactly as note_unknown() writes it.
        if fight == 0:
            w.raw({'kind': 'meta', 'unknownMsg': 431, 'category': 4, 'actionId': 245})

        w.advance(110 + rand.randrange(200))   # downtime between fights

    return w


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument('--out', default=str(HERE / 'events' / 'Hasaya_2026.07.30.jsonl'))
    ap.add_argument('--seed', type=int, default=SEED)
    args = ap.parse_args(argv)

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)

    w = generate(args.seed)
    # ASCII and LF, exactly as vx_emit.lua appends.
    with open(out, 'w', encoding='ascii', newline='') as fh:
        for line in w.lines:
            fh.write(line + '\n')

    print('wrote %d lines -> %s' % (len(w.lines), out))
    print('  events by kind: %s'
          % ', '.join('%s %d' % kv for kv in sorted(w.counts.items())))
    print('  %d distinct use ids' % w.use)
    return 0


if __name__ == '__main__':
    sys.exit(main())
