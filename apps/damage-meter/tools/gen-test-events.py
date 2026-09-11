"""Generates a synthetic VibeXI addon event file to exercise the meter without a
game running: several characters, a pet, multi-attack rounds, weaponskills,
crits, skillchains, magic bursts, ranged attacks, additional effects, an
unresolved target, monster damage on the party, and gaps between fights.

    python apps/damage-meter/tools/gen-test-events.py
    python apps/damage-meter/tools/gen-test-events.py --out somewhere/Hasaya_2026.09.04.jsonl

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
    table. The monster damage
    is a BACK-COMPATIBILITY case now rather than a live one -- the addon stopped
    recording it (`is_ours` in vibexi.lua) -- and it stays because files
    captured before that change still have to read correctly.
  * REACTION DAMAGE -- Counter, Retaliation, Spikes -- written with the two ends
    SWAPPED, which is what `record_reactions` does: the actor is whoever
    reacted, the target is the monster that swung into them. It is the one place
    a party member's damage arrives on a packet they were not the actor of, and
    one of the pet's reactions is in here too, so the pet-to-owner crediting
    gets exercised on a row that came in backwards.
  * AN UNRESOLVED TARGET -- name "Unknown", targetKind "other" -- which is what
    the addon writes when the entity table has no answer.
  * META LINES: the startup environment probe on line 1, and an unknown-message
    notice. Consumers must skip these.
  * JOB LINES -- `kind:"job"`, one per party member, written when the job is
    first read and again whenever it changes. Four things ride on these and each
    is here on purpose: a member who NEVER ACTS (the white mage) and therefore
    appears in no chart and no total, but must still be listed with her job; a
    MID-SESSION JOB CHANGE (Rhyllis, PLD/WAR to SAM/WAR, in the downtime after
    the second fight) which must take -- last write wins, unlike the roster's
    first-answer-wins rule for spawn kinds; the TWO-CHARACTERS-ONE-JOB case that
    change creates, which is what forces the meter to shade a shared job colour
    rather than draw two identical lines; and a SUB-JOB-LESS member (the pet's
    owner is not one, but the mule-style entry is -- see PARTY_JOBS), whose line
    omits the sub trio entirely the way `vx_emit.encode_job` does.
"""

import argparse
import json
import random
import re
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
SEED = 20260730

# The addon's own version, read out of the addon rather than copied here.
#
# It was copied here once, and it went stale twice -- the fixture's probe line
# claimed 0.1.0 through two releases, because a constant that has to be updated
# in two places by hand only ever gets updated in one. The probe line is supposed
# to be byte-identical to what `vibexi.lua` writes, and the version is the one
# field of it that moves, so it is read from the source of truth.
ADDON_LUA = HERE.parent.parent / 'addons' / 'VibeXI' / 'vibexi.lua'


def addon_version(default='0.0.0'):
    """`addon.version` as declared in vibexi.lua.

    Falls back rather than raising: the fixture is still worth generating from a
    copy of this script sitting outside the repo, and a wrong version string in a
    line nothing reads is not worth a hard failure over.
    """
    try:
        text = ADDON_LUA.read_text(encoding='utf-8')
    except OSError:
        return default
    m = re.search(r"""addon\.version\s*=\s*['"]([^'"]+)['"]""", text)
    return m.group(1) if m else default

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
    'counter': 33,      # AttackCounteredDamage
    'spikes': 44,       # SpikesEffectDmg      (the react/spike trailer)
    'retaliate': 536,   # RetaliateDamage
}

# Skillchain ids, from Metrics' Res.WS.Skillchains by way of
# addons/VibeXI/vx_enums.lua. A lookup, not a formula -- see the long note there.
# 385/386 are Light and Darkness a second time, not a different outcome.
SC_IDS = [
    ('Light', 288), ('Darkness', 289), ('Fusion', 293), ('Liquefaction', 295),
    ('Impaction', 301), ('Light', 385), ('Darkness', 386),
    ('Radiance', 767), ('Umbra', 768),
]


# Job ids, from addons/VibeXI/vx_enums.lua (E.Jobs), which takes them from
# Metrics' Res.Jobs.List. The addon writes both the id and the abbreviation, so
# the fixture has to know the pairing too.
JOB_IDS = {
    'NON': 0,
    'WAR': 1,  'MNK': 2,  'WHM': 3,  'BLM': 4,  'RDM': 5,  'THF': 6,
    'PLD': 7,  'DRK': 8,  'BST': 9,  'BRD': 10, 'RNG': 11, 'SAM': 12,
    'NIN': 13, 'DRG': 14, 'SMN': 15, 'BLU': 16, 'COR': 17, 'PUP': 18,
    'DNC': 19, 'SCH': 20, 'GEO': 21, 'RUN': 22, 'MON': 23,
}


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

# The party member who never acts. She heals all night, the addon records no
# damage for her, and she therefore reaches the app through her job line and
# nothing else -- which is the case that proves the meter lists the PARTY and not
# only the characters who dealt damage.
HEALER = 'Sylviane'

# (name, main, main level, sub, sub level) at the start of the session.
#
# Parabellum is the beastmaster, because Fluffikins is his. Vermillion carries no
# sub-job at all -- a fresh mule parked in the alliance -- so his line omits the
# sub trio the way the emitter does, and he never swings either.
PARTY_JOBS = [
    ('Hasaya',     'SAM', 75, 'WAR', 37),
    ('Parabellum', 'BST', 75, 'NIN', 37),
    ('Rhyllis',    'PLD', 75, 'WAR', 37),
    (RANGER,       'RNG', 75, 'NIN', 37),
    (MAGE,         'BLM', 75, 'WHM', 37),
    (HEALER,       'WHM', 75, 'BLM', 37),
    ('Vermillion', 'WAR',  1, 'NON',  0),
]

# Rhyllis puts the shield away in the downtime after the second fight. Two things
# hang off this one line: the reader must take the LAST job line for a character
# rather than the first, and the party now holds two samurai -- so the meter has
# to tell Hasaya's SAM colour from Rhyllis's without a second hue to spend.
JOB_CHANGE = ('Rhyllis', 'SAM', 75, 'WAR', 37)

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

    def job(self, actor, main, main_lvl, sub, sub_lvl):
        """One party-member job line, field-for-field `vx_emit.encode_job`.

        No `seq` and no `use`: this is not an event and takes part in no
        ordering. The sub trio is dropped entirely when there is no sub-job,
        which is the same rule `owner` and `pet` follow on a damage row.
        """
        e = {
            'kind': 'job',
            't': self.t,
            'actor': actor,
            'main': main,
            'mainId': JOB_IDS[main],
            'mainLvl': main_lvl,
        }
        if JOB_IDS[sub]:
            e['sub'] = sub
            e['subId'] = JOB_IDS[sub]
            e['subLvl'] = sub_lvl
        self.raw(e)

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
        'kind': 'meta', 'v': addon_version(), 't': w.t,
        'chunkData': 'string', 'injected': 'boolean', 'dataLen': 104,
        'isZoningType': 'number', 'selfName': 'Hasaya', 'selfSpawnFlags': 525,
        'abilProbeId': 5, 'abilRaw': 'Combo', 'abilOffset': 'Berserk',
        'spellLookup': 'Cure', 'wsLookup': 'Combo',
        'path': 'C:\\Users\\thadl\\AppData\\Local\\VibeXI\\events\\Hasaya_2026.07.30.jsonl',
    })

    # ---- the party, before anybody swings
    #
    # The addon writes these the first time it reads the party table, which is on
    # the first action packet of the session -- so in a real file they land just
    # after the probe, exactly as they do here. It writes one again only when a
    # job CHANGES; there is no periodic re-statement to parse around.
    for name, main, main_lvl, sub, sub_lvl in PARTY_JOBS:
        w.job(name, main, main_lvl, sub, sub_lvl)

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

            # The monster's own damage. The addon no longer writes any of this
            # (see `is_ours`), so it is here as the back-compatibility case:
            # parsed, kept, shown in the roster -- and never counted, charted or
            # totalled anywhere.
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

            # Reaction damage: what our side dealt BACK on the monster's swing.
            # The ends are already swapped here, exactly as record_reactions()
            # writes them -- actor is the one who reacted, target is the monster
            # -- so nothing downstream has to know these arrived backwards.
            # Each is its own use: a reaction is one entity's answer to one
            # swing and must never collapse into another's.
            r = rand.random()
            if r < 0.11:
                who = rand.choice(MELEE)['n']
                w.write(w.next_use(), 'reaction', who, 'player', 'Counter',
                        MSG['counter'], mob, 'mob', roll(120, 30), True,
                        MSG['counter'])
            elif r < 0.18:
                who = rand.choice(MELEE)['n']
                w.write(w.next_use(), 'reaction', who, 'player', 'Retaliation',
                        MSG['retaliate'], mob, 'mob', roll(140, 35), True,
                        MSG['retaliate'])

            if rand.random() < 0.09:
                w.write(w.next_use(), 'reaction', MELEE[0]['n'], 'player', 'Spikes',
                        MSG['spikes'], mob, 'mob', roll(45, 12), True, MSG['spikes'])

            # The pet reacting. Arrives backwards AND has to be credited to its
            # owner, which is the two rules meeting on one row.
            if rand.random() < 0.05:
                w.write(w.next_use(), 'reaction', PET['n'], 'pet', 'Spikes',
                        MSG['spikes'], mob, 'mob', roll(38, 10), True, MSG['spikes'],
                        owner=PET['owner'], pet=PET['n'])

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

        # Rhyllis changes job in the downtime. Written AFTER the advance, so it
        # sits in the gap rather than on the last swing of the fight before it.
        if fight == 1:
            w.job(*JOB_CHANGE)

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
