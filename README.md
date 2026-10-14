# VibeXI

FFXI tooling for HorizonXI: an in-game addon that records combat events for a
desktop app kept outside this repo.

```
addons/VibeXI/      Ashita addon: reads the game's action packets, writes one JSON line per event
dev/                the addon's plan, API allowlist, checkers and generators — never shipped
.githooks/          pre-commit hook running dev/check-apis.py
```

`addons/VibeXI/` holds only the Lua the addon loads at runtime: the folder is
copied verbatim into Ashita, so nothing else may live in it. `VibeXI` is the
name the addon was approved under, and Ashita takes the addon name from the
folder.

The addon replaced a chat-log parser as the source of those events. The parser
could not say who was a player, could not group an area attack, could not tell a
weaponskill from a job ability at the announcement, and could not flag a crit;
all of that is stated outright in the action packet.

It is a read-only Lua addon that must never send anything to the game server.
That constraint is enforced by an API allowlist (`dev/ALLOWED_APIS.txt`) rather than by
convention, and the bridge is one-way by construction: the addon
appends to a local file and the desktop app reads it, so nothing downstream can reach
back into the game whatever happens to it.

```
FFXI process                    disk                         desktop
[VibeXI addon] ──write──▶ events/<Char>_<date>.jsonl ──read──▶ [desktop app]
```

## Not in this repo

The desktop app that reads the addon's events lives in its own repository.
