# VibeXI

Browser-based FFXI tooling. Two apps and the design system they share.

```
ws_calculator/   weaponskill damage Monte-Carlo sim — open pages/ws-calculator.html from disk
addons/VibeXI/   Ashita addon: reads the game's action packets, writes one JSON line per event
damage_meter/    live damage meter over those events — run Damage-Meter.cmd, open the served page
shared-ui/       THE design system both apps load: css/ffxi-theme.css + js/theme.js
addon-dev/       the addon's plan, API allowlist, checkers and generators — never shipped
.githooks/       pre-commit hook running addon-dev/check-apis.py
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

No build step, no bundler, no Node. `ws_calculator` pages are opened directly
over `file://`; `damage_meter` serves itself from a stdlib-only Python script
(3.8+, nothing to pip install). Each app has its own `README.md` (human
overview) and `CLAUDE.md` (operational detail).

## Layout is load-bearing

The three directories must stay siblings. Both apps reach the design system by
relative path — `../../shared-ui/...` from `ws_calculator/pages/`, and
`SHARED_ROOT = HERE.parent / 'shared-ui'` in `damage-meter.py`, which mounts it
at `/shared/`. Renaming or nesting any of them breaks styling silently.

## Not in this repo

The LandSandBoat-derivative server source that every damage formula is validated
against is a separate upstream checkout, kept outside this repo at
`C:\Users\thadl\OneDrive\Documents\Claude\resources\server\`.
