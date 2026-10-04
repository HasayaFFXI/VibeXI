"""Plays an event file into a live one, for checking Zerg with no game running.

Zerg only counts what arrives after Start, so a fixture whose rows are in the
past can't be measured by pressing the button. This appends a fixture to the
file Zerg is following, re-timed to begin a second from now and squeezed so a
24-minute session is over in under half a minute:

    python apps/zerg/tools/replay.py <fixture.jsonl> <live.jsonl> [--squeeze 60] [--extras]

Start Zerg on a folder holding an empty <live.jsonl>, press Start, run this,
wait out the squeezed session, then press Pause. Every row is written at once
with its (future) time, so totals are complete straight away; the clock and
everything divided by it are right once the last row's time has passed.

--extras adds three characters the generated fixture has no reason to contain:
two small enough to be folded into one line of the cumulative chart, and one
whose only action dealt nothing, who must keep a filter chip and get no bar.
It also adds three pet heals, which the fixture has none of: two from the pet
of a character who heals too, and one from the pet of a character who does
not, who must be listed in the Healing section with no healing of their own.

Prints the first and last times written (seconds) and the line count.
"""
import argparse
import json
import time

p = argparse.ArgumentParser()
p.add_argument('source')
p.add_argument('live')
p.add_argument('--squeeze', type=int, default=60)
p.add_argument('--extras', action='store_true')
a = p.parse_args()

rows = [json.loads(line) for line in open(a.source, encoding='ascii') if line.strip()]
first = min(r['t'] for r in rows if r.get('kind') not in ('meta', 'job'))
base = int(time.time()) + 1
for r in rows:
    if 't' in r:
        r['t'] = base + max(0, r['t'] - first) // a.squeeze

if a.extras:
    for i, (who, dmg, hit) in enumerate((('Tinyone', 900, True), ('Tinytwo', 1400, True), ('Zeroth', 0, False))):
        rows.append({'t': base + 3 + i, 'seq': 1, 'use': 900000 + i, 'kind': 'magic', 'actor': who,
                     'actorKind': 'player', 'action': 'Stone', 'actionId': 159, 'target': 'Goblin Pathfinder',
                     'targetKind': 'mob', 'dmg': dmg, 'hit': hit, 'crit': False, 'burst': False, 'msg': 2})
    for i, (pet, owner, action, hp) in enumerate((('Fluffikins', 'Parabellum', 'Wild Carrot', 180),
                                                  ('Fluffikins', 'Parabellum', 'Wild Carrot', 164),
                                                  ('Carbuncle', 'Tinyone', 'Healing Ruby', 210))):
        rows.append({'kind': 'heal', 't': base + 6 + i, 'seq': 1, 'use': 900010 + i, 'via': 'pet', 'actor': pet,
                     'actorKind': 'pet', 'action': action, 'actionId': 900 + i, 'target': 'Hasaya',
                     'targetKind': 'player', 'hp': hp, 'msg': 7, 'owner': owner, 'pet': pet})

with open(a.live, 'a', encoding='ascii', newline='\n') as f:
    f.write('\n'.join(json.dumps(r, separators=(',', ':')) for r in rows) + '\n')
print(base, max(r['t'] for r in rows if 't' in r), len(rows))
