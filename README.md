# VibeXI

FFXI tooling for HorizonXI: two browser calculators and the shared layers they
build on, and Zerg, a native Windows app fed by an in-game addon.

```
apps/
  zerg/             Zerg: live damage and healing over the addon's events — a native Windows app (C#, WPF)
tools/
  ws-calculator/    Tachi: Jinpu weaponskill Monte-Carlo sim — open pages/ from disk
  penta-calculator/ the same engine aimed at Penta Thrust, plus PentaThrustDamageCalc.md
  shared-ui/        THE design system both calculators load: css/ffxi-theme.css + js/theme.js
addons/VibeXI/      Ashita addon: reads the game's action packets, writes one JSON line per event
shared-calc/        THE calculation layer both calculators load: engine, server data, components
addon-dev/          the addon's plan, API allowlist, checkers and generators — never shipped
.githooks/          pre-commit hook running addon-dev/check-apis.py
```

`addons/VibeXI/` holds only the Lua the addon loads at runtime: the folder is
copied verbatim into Ashita, so nothing else may live in it. `VibeXI` is the
name the addon was approved under, and Ashita takes the addon name from the
folder.

The addon is Zerg's only data source. It replaced a chat-log parser, which
could not say who was a player, could not group an area attack, could not tell a
weaponskill from a job ability at the announcement, and could not flag a crit;
all of that is stated outright in the action packet.

It is a read-only Lua addon that must never send anything to the game server.
That constraint is enforced by an API allowlist rather than by convention — see
`addon-dev/PLAN.md` — and the bridge is one-way by construction: the addon
appends to a local file and Zerg reads it, so nothing downstream can reach
back into the game whatever happens to it.

```
FFXI process                    disk                         desktop
[VibeXI addon] ──write──▶ events/<Char>_<date>.jsonl ──read──▶ [Zerg.exe]
```

The calculators have no build step, no bundler and no Node: their pages are
opened directly over `file://`. Zerg is C# on .NET 10, run with
`dotnet run --project apps/zerg/src/Zerg`. Each calculator and each shared
layer has its own `README.md` (human overview) and `CLAUDE.md` (operational
detail); Zerg has `CLAUDE.md`, and `RULES.md` for the event contract and the
reason behind every number it prints.

## Layout is load-bearing

`shared-ui/` sits in `tools/` beside the calculators; `shared-calc/` stays at the
repo root. Neither is a tool: one is the design system, the other the
calculation layer. Both are reached by relative path —
`../../shared-ui/...` and `../../../shared-calc/...` from `tools/<tool>/pages/`.
Moving or renaming a calculator, or either shared directory, breaks things
silently: styling just stops, with no console error, and a moved `shared-calc`
takes the damage engine with it. Zerg uses neither.

They are two directories rather than one because the design system does not
depend on the damage engine, and can be loaded without it.

## Not in this repo

The LandSandBoat-derivative server source that every damage formula is validated
against is a separate upstream checkout, kept outside this repo at
`C:\Users\thadl\OneDrive\Documents\Claude\resources\server\`.

`tools/penta-calculator/PentaThrustDamageCalc.md` is the worked example of reading
it: one weaponskill traced to `file:line`, with every input tabulated.
