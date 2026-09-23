# VibeXI

Browser-based FFXI tooling. Three apps and the two shared layers they build on.

```
apps/
  ws-calculator/    Tachi: Jinpu weaponskill Monte-Carlo sim — open pages/ from disk
  penta-calculator/ the same engine aimed at Penta Thrust, plus PentaThrustDamageCalc.md
  damage-meter/     live damage meter over the addon's events — run Damage-Meter.cmd, open the served page
addons/VibeXI/      Ashita addon: reads the game's action packets, writes one JSON line per event
shared-ui/          THE design system every app loads: css/ffxi-theme.css + js/theme.js
shared-calc/        THE calculation layer both calculators load: engine, server data, components
addon-dev/          the addon's plan, API allowlist, checkers and generators — never shipped
.githooks/          pre-commit hook running addon-dev/check-apis.py
```

`addons/VibeXI/` holds only the Lua the addon loads at runtime: the folder is
copied verbatim into Ashita, so nothing else may live in it. `VibeXI` is the
name the addon was approved under, and Ashita takes the addon name from the
folder.

The addon is the meter's only data source. It replaced a chat-log parser, which
could not say who was a player, could not group an area attack, could not tell a
weaponskill from a job ability at the announcement, and could not flag a crit;
all of that is stated outright in the action packet.

It is a read-only Lua addon that must never send anything to the game server.
That constraint is enforced by an API allowlist rather than by convention — see
`addon-dev/PLAN.md` — and the bridge is one-way by construction: the addon
appends to a local file and the server reads it, so nothing downstream can reach
back into the game whatever happens to it.

```
FFXI process                    disk                        served page
[VibeXI addon] ──write──▶ events/<Char>_<date>.jsonl ──read──▶ [damage-meter.py] ──▶ [browser]
```

No build step, no bundler, no Node. Calculator pages are opened directly over
`file://`; `damage-meter` serves itself from a stdlib-only Python script (3.8+,
nothing to pip install). Each app has its own `README.md` (human overview) and
`CLAUDE.md` (operational detail), and so does each shared layer.

## Layout is load-bearing

`shared-ui/` and `shared-calc/` stay at the repo root, siblings of `apps/` rather
than members of it. Neither is an app: one is the design system, the other the
calculation layer. Both are reached by relative path —
`../../../shared-ui/...` and `../../../shared-calc/...` from `apps/<app>/pages/`,
and `SHARED_ROOT = HERE.parent.parent / 'shared-ui'` in `damage-meter.py`, which
mounts it at `/shared/`. Moving or renaming an app, or either shared directory,
breaks things silently: styling just stops (no console error on the calculators,
one yellow warning line on the meter), and a moved `shared-calc` takes the damage
engine with it.

They are two directories rather than one because `damage-meter` wants the design
system and has no use for the damage engine.

## Not in this repo

The LandSandBoat-derivative server source that every damage formula is validated
against is a separate upstream checkout, kept outside this repo at
`C:\Users\thadl\OneDrive\Documents\Claude\resources\server\`.

`apps/penta-calculator/PentaThrustDamageCalc.md` is the worked example of reading
it: one weaponskill traced to `file:line`, with every input tabulated.
