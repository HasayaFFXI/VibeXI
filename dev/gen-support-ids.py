"""Regenerates addons/VibeXI/vx_support_ids.lua -- the actions that may be support.

Support (a buff, a status removal, a ward) is written only when the ACTION's id
is on this list and its target is one of ours: see E.support in vx_enums.lua.
The list is what makes a debuff on the party impossible to write, whatever
message the server answers with (it answers Slow with the same 230 as Haste,
and Nightmare with the same 266 as a song's other targets).

The source is a server tree's sql/spell_list.sql and sql/abilities.sql, read
for each action's id, name, job or skill, and VALID TARGETS -- what the game
lets it be aimed at. An action is listed when nothing about it can be aimed at
an enemy, and something about it can be aimed at our side:

  spells          no TARGET_ENEMY bit, a friendly bit, and a skill that buffs
                  or removes: enhancing, singing, healing, ninjutsu, blue,
                  dark (Dread Spikes), divine (Enlight). Not summoning,
                  geomancy or trusts, which call something rather than give it.
  abilities       job abilities (server id < 512) by the same bits.
  pets            a summoner's blood pacts (job 15) and a wyvern's breaths and
                  removals (job 0, 639-654) by the same bits. NOT a beastmaster's
                  pet moves: the server marks every one as aimed at self and
                  pet, Sheep Song and Dream Flower included, so the bits say
                  nothing about them.

Heals are left out (they are decided first, by E.HealingSpells and friends,
and never reach this list), read from vx_enums.lua so the two cannot drift.

Ids are the server's, which are the action packet's param: a spell's on
category 4, a job ability's on 6 (the client's resource id is that plus 512),
a pet's on 13. Category 14 (dances) is not written at all, so is not asked. Names are made from the server's names and are what a pet's
row is called (pets have no resource lookup); a spell's or ability's row is
named by the resource manager, and its name here is only for reading.

    python dev/gen-support-ids.py --server path/to/server

Writes CRLF, so regenerating an unchanged table is a no-op in git.
"""

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent          # repo root

TARGET_SELF = 0x0001
TARGET_PLAYER_PARTY = 0x0002
TARGET_ENEMY = 0x0004
TARGET_PLAYER_ALLIANCE = 0x0008
TARGET_PLAYER = 0x0010
TARGET_PLAYER_PARTY_PIANISSIMO = 0x0080
TARGET_PET = 0x0100
TARGET_PLAYER_PARTY_ENTRUST = 0x0200
FRIENDLY = (TARGET_SELF | TARGET_PLAYER_PARTY | TARGET_PLAYER_ALLIANCE | TARGET_PLAYER
            | TARGET_PLAYER_PARTY_PIANISSIMO | TARGET_PET | TARGET_PLAYER_PARTY_ENTRUST)

SPELL_SKILLS = {'@SKILL_ENHANCING', '@SKILL_SINGING', '@SKILL_HEALING', '@SKILL_NINJUTSU',
                '@SKILL_BLUE', '@SKILL_DARK', '@SKILL_DIVINE'}
SMN, WYVERN = 15, 0
WYVERN_IDS = range(639, 655)

SPELL_RX = re.compile(r"^INSERT INTO `spell_list` VALUES \((\d+),'([^']*)',0x[0-9A-Fa-f]+,"
                      r"\d+,\d+,[^,]+,\d+,(\d+),([^,]+),")
ABILITY_RX = re.compile(r"^INSERT INTO `abilities` VALUES \((\d+),'([^']*)',(\d+),\d+,(\d+),")

ROMAN = {'i', 'ii', 'iii', 'iv', 'v', 'vi', 'vii', 'viii'}


def friendly(targets):
    return not targets & TARGET_ENEMY and targets & FRIENDLY


def english(name):
    """'altana_s_favor' -> "Altana's Favor", 'hastega_ii' -> 'Hastega II'."""
    name = name.replace('_s_', "'s_")
    words = []
    for w in name.split('_'):
        if w in ROMAN:
            words.append(w.upper())
        else:
            words.append('-'.join(p[:1].upper() + p[1:] for p in w.split('-')))
    return ' '.join(words)


def heal_ids(enums):
    """The ids vx_enums.lua already reads as heals, by packet param."""
    text = enums.read_text(encoding='utf-8')

    def block(name):
        m = re.search(r'^E\.%s = \{(.*?)^\}' % name, text, re.S | re.M)
        return {int(k) for k in re.findall(r'\[(\d+)\]', m.group(1))} if m else set()

    return (block('HealingSpells'),
            {k - 512 for k in block('HealingAbilities')},
            block('PetHealing'))


def table(name, rows, lines):
    lines.append('    %s = {' % name)
    for i, n in sorted(rows.items()):
        lines.append('        [%d] = "%s",' % (i, n.replace('"', '\\"')))
    lines.append('    },')


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument('--server', required=True, help='a server tree with sql/')
    ap.add_argument('--out', default=None, help='default: addons/VibeXI/vx_support_ids.lua')
    args = ap.parse_args(argv)

    sql = Path(args.server) / 'sql'
    out = Path(args.out) if args.out else ROOT / 'addons' / 'VibeXI' / 'vx_support_ids.lua'
    for f in ('spell_list.sql', 'abilities.sql'):
        if not (sql / f).is_file():
            print('not found: %s' % (sql / f))
            return 1

    heal_spells, heal_abilities, heal_pets = heal_ids(ROOT / 'addons' / 'VibeXI' / 'vx_enums.lua')

    spells, abilities, pets = {}, {}, {}
    for line in (sql / 'spell_list.sql').read_text(encoding='utf-8').splitlines():
        m = SPELL_RX.match(line)
        if not m:
            continue
        sid, name, targets, skill = int(m[1]), m[2], int(m[3]), m[4]
        if skill in SPELL_SKILLS and friendly(targets) and sid not in heal_spells:
            spells[sid] = english(name)

    for line in (sql / 'abilities.sql').read_text(encoding='utf-8').splitlines():
        m = ABILITY_RX.match(line)
        if not m:
            continue
        aid, name, job, targets = int(m[1]), m[2], int(m[3]), int(m[4])
        if not friendly(targets):
            continue
        if aid < 512:
            if aid not in heal_abilities:
                abilities[aid] = english(name)
        elif job == SMN or (job == WYVERN and aid in WYVERN_IDS):
            if aid not in heal_pets:
                pets[aid] = english(name)

    lines = [
        '-- Generated by dev/gen-support-ids.py -- do not hand-edit.',
        '--',
        '-- The actions that may be written as support, by the action packet\'s',
        '-- param: spells on category 4, job abilities on 6, pets\' abilities on 13.',
        '-- None of them can be aimed at an enemy. See E.support in vx_enums.lua,',
        '-- and the generator for how the list is chosen.',
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

    print('wrote %d spells, %d abilities, %d pet abilities -> %s'
          % (len(spells), len(abilities), len(pets), out))
    return 0


if __name__ == '__main__':
    sys.exit(main())
