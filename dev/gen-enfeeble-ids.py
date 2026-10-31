"""Regenerates addons/VibeXI/vx_enfeeble_ids.lua -- the actions that may be enfeebles.

An enfeeble (a Sleep, a Slow, a Stun, a Dispel, a Lullaby) is an action one of
ours aims at an enemy that deals no damage. Today its result is either dropped
(it landed: 236, 237, ... are in neither E.Damage nor E.Attempt) or written as
a damage row with dmg 0 and hit=false (it was resisted or had no effect: 75,
85, 284, 655), which a reader cannot tell from a nuke that missed. This list is
what tells them apart, BY THE ACTION'S ID, as heals and support are decided:
the message cannot do it, since a resisted Sleepga and a resisted Stone answer
with the same 85.

The source is a server tree's sql/spell_list.sql and sql/abilities.sql, for
each action's id, name, job and VALID TARGETS, and the action's script under
scripts/actions, for whether it deals damage. An action is listed when it can
be aimed at an enemy and its script deals no damage:

  spells          a TARGET_ENEMY bit, a skill a party member casts with (not
                  summoning, geomancy or none), and a script that calls none
                  of DAMAGE_CALLS. Aspir is listed although it calls the
                  draining helper: its message (228) drains MP, not HP. Dia
                  and Bio are not: they deal damage when they land.
  abilities       job abilities (server id < 512), by name (ABILITIES below):
                  their scripts all go through xi.job_utils, which says
                  nothing about damage, and Light Shot shares Fire Shot's
                  helper.
  pets            a summoner's blood pacts (job 15) aimed at an enemy whose
                  script calls none of DAMAGE_CALLS, and three the server tree
                  has no script for (PACTS_WITHOUT_SCRIPT). A beastmaster's pet
                  moves (job 9) by the mob skill script they run as: a status
                  or gaze move with no damage call. The server marks every
                  one of them as aimed at self and pet, so the bits say
                  nothing about them.

A spell or pact with no script at all is not listed: the server cannot use it.
--report prints every enemy-aimed action left off, and why, for review.

Ids are the server's, which are the action packet's param: a spell's on
category 4, a job ability's on 6 (or 3, for the ones in E.WsAbilities), a
pet's on 13. Names are made from the server's names and are what a pet's row
is called (pets have no resource lookup).

    python dev/gen-enfeeble-ids.py --server path/to/server [--report]

Writes CRLF, so regenerating an unchanged table is a no-op in git.
"""

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent          # repo root

TARGET_ENEMY = 0x0004
SMN, BST = 15, 9

# A spell skill no party member enfeebles with: an avatar's or a luopan's
# spell calls something, and SKILL_NONE is trusts' and items'.
SKIP_SKILLS = {'@SKILL_SUMMONING', '@SKILL_GEOMANCY', '@SKILL_NONE'}

# Any one of these in an action's script and it deals damage.
DAMAGE_CALLS = (
    ':takeDamage', ':takeSpellDamage',
    'xi.spells.damage.useDamageSpell',
    'xi.spells.blue.usePhysicalSpell', 'xi.spells.blue.useMagicalSpell',
    'xi.spells.blue.useBreathSpell', 'xi.spells.blue.useDrainSpell',
    'xi.spells.blue.applySpellDamage',
    'xi.spells.absorb.doDrainingSpell',
    'xi.mobskills.mobPhysicalMove', 'xi.mobskills.mobMagicalMove',
    'xi.mobskills.mobBreathMove', 'xi.mobskills.processDamage',
)

# Aspir: the draining helper, but it drains MP (message 228), not HP.
MP_DRAIN_MESSAGE = 228

# A beastmaster pet's move is an enfeeble when its mob skill script makes one
# of these and deals no damage. A buff move (mobBuffMove) lands on the pet.
STATUS_CALLS = ('xi.mobskills.mobStatusEffectMove', 'xi.mobskills.mobGazeMove')

# Job abilities that put a status on an enemy and deal no damage. Steps and
# flourishes are category 14, which is not written at all.
ABILITIES = {
    'shadowbind',       # bind
    'light_shot',       # sleep (and Dia's effect)
    'dark_shot',        # dispel (and Bio's effect)
    'tomahawk',         # special defense down
    'feral_howl',       # terror
    'angon',            # defense down
}

# Pacts aimed at an enemy that the server tree has no script for. They deal
# no damage in the game, and a server that has them answers like any other.
PACTS_WITHOUT_SCRIPT = {'diamond_storm', 'shock_squall', 'ultimate_terror'}

SPELL_RX = re.compile(r"^INSERT INTO `spell_list` VALUES \((\d+),'([^']*)',0x[0-9A-Fa-f]+,"
                      r"\d+,\d+,[^,]+,\d+,(\d+),([^,]+),\d+,\d+,\d+,(\d+),")
ABILITY_RX = re.compile(r"^INSERT INTO `abilities` VALUES \((\d+),'([^']*)',(\d+),\d+,(\d+),")
CALL_RX = re.compile(r'xi\.[A-Za-z_.]+(?=\()|:takeDamage|:takeSpellDamage')

ROMAN = {'i', 'ii', 'iii', 'iv', 'v', 'vi', 'vii', 'viii'}


def english(name):
    """'horde_lullaby' -> 'Horde Lullaby', 'absorb-str' -> 'Absorb-Str'."""
    name = name.replace('_s_', "'s_")
    words = []
    for w in name.split('_'):
        if w in ROMAN:
            words.append(w.upper())
        else:
            words.append('-'.join(p[:1].upper() + p[1:] for p in w.split('-')))
    return ' '.join(words)


def calls(path):
    """What a script calls, its comments left out; None with no script."""
    if path is None or not path.is_file():
        return None
    body = '\n'.join(l for l in path.read_text(encoding='utf-8', errors='replace').splitlines()
                     if not l.strip().startswith('--'))
    return set(CALL_RX.findall(body))


def damages(found):
    return any(c in found for c in DAMAGE_CALLS)


def table(name, rows, lines):
    lines.append('    %s = {' % name)
    for i, n in sorted(rows.items()):
        lines.append('        [%d] = "%s",' % (i, n.replace('"', '\\"')))
    lines.append('    },')


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument('--server', required=True, help='a server tree with sql/ and scripts/')
    ap.add_argument('--out', default=None, help='default: addons/VibeXI/vx_enfeeble_ids.lua')
    ap.add_argument('--report', action='store_true', help='print what was left off, and why')
    args = ap.parse_args(argv)

    server = Path(args.server)
    sql, acts = server / 'sql', server / 'scripts' / 'actions'
    out = Path(args.out) if args.out else ROOT / 'addons' / 'VibeXI' / 'vx_enfeeble_ids.lua'
    for f in ('spell_list.sql', 'abilities.sql'):
        if not (sql / f).is_file():
            print('not found: %s' % (sql / f))
            return 1

    spell_files = {p.stem: p for p in (acts / 'spells').rglob('*.lua')}
    left = []

    spells, abilities, pets = {}, {}, {}
    for line in (sql / 'spell_list.sql').read_text(encoding='utf-8').splitlines():
        m = SPELL_RX.match(line)
        if not m:
            continue
        sid, name, targets, skill, message = int(m[1]), m[2], int(m[3]), m[4], int(m[5])
        if not targets & TARGET_ENEMY or skill in SKIP_SKILLS:
            continue
        found = calls(spell_files.get(name))
        if found is None:
            left.append('spell %d %s: no script' % (sid, name))
        elif damages(found) and message != MP_DRAIN_MESSAGE:
            left.append('spell %d %s: deals damage' % (sid, name))
        else:
            spells[sid] = english(name)

    seen = set()
    for line in (sql / 'abilities.sql').read_text(encoding='utf-8').splitlines():
        m = ABILITY_RX.match(line)
        if not m:
            continue
        aid, name, job, targets = int(m[1]), m[2], int(m[3]), int(m[4])
        if aid < 512:
            if name in ABILITIES:
                abilities[aid] = english(name)
                seen.add(name)
            elif targets & TARGET_ENEMY:
                left.append('ability %d %s: not on ABILITIES' % (aid, name))
        elif job == SMN and targets & TARGET_ENEMY:
            found = calls(acts / 'abilities' / 'pets' / (name + '.lua'))
            if name in PACTS_WITHOUT_SCRIPT:
                pets[aid] = english(name)
                seen.add(name)
            elif found is None:
                left.append('pact %d %s: no script' % (aid, name))
            elif damages(found):
                left.append('pact %d %s: deals damage' % (aid, name))
            else:
                pets[aid] = english(name)
        elif job == BST:
            found = calls(acts / 'mobskills' / (name + '.lua'))
            if found is None:
                left.append('pet move %d %s: no mob skill script' % (aid, name))
            elif damages(found):
                left.append('pet move %d %s: deals damage' % (aid, name))
            elif not any(c in found for c in STATUS_CALLS):
                left.append('pet move %d %s: puts no status on its target' % (aid, name))
            else:
                pets[aid] = english(name)

    missing = (ABILITIES | PACTS_WITHOUT_SCRIPT) - seen
    if missing:
        print('not in abilities.sql: %s' % ', '.join(sorted(missing)))
        return 1

    lines = [
        '-- Generated by dev/gen-enfeeble-ids.py -- do not hand-edit.',
        '--',
        '-- The actions that may be written as enfeebles, by the action packet\'s',
        '-- param: spells on category 4, job abilities on 6 (or 3), pets\' abilities',
        '-- on 13. Each can be aimed at an enemy and deals no damage. See',
        '-- E.enfeeble in vx_enums.lua, and the generator for how the list is chosen.',
        '',
        'return {',
    ]
    table('spells', spells, lines)
    table('abilities', abilities, lines)
    table('pets', pets, lines)
    lines.append('}')

    with open(out, 'w', encoding='utf-8', newline='') as fh:
        for line in lines:
            fh.write(line + '\r\n')

    if args.report:
        for l in left:
            print('  left off: ' + l)
    print('wrote %d spells, %d abilities, %d pet abilities -> %s'
          % (len(spells), len(abilities), len(pets), out))
    return 0


if __name__ == '__main__':
    sys.exit(main())
