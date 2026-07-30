# VibeXI

Browser-based FFXI tooling. Two apps and the design system they share.

```
ws_calculator/   weaponskill damage Monte-Carlo sim — open pages/ws-calculator.html from disk
damage_meter/    live chat-log damage meter — run Damage-Meter.cmd, then open the served page
shared-ui/       THE design system both apps load: css/ffxi-theme.css + js/theme.js
```

No build step, no bundler, no Node and no Python. `ws_calculator` pages are
opened directly over `file://`; `damage_meter` serves itself from a PowerShell
`HttpListener`. Each app has its own `README.md` (human overview) and
`CLAUDE.md` (operational detail).

## Layout is load-bearing

The three directories must stay siblings. Both apps reach the design system by
relative path — `../../shared-ui/...` from `ws_calculator/pages/`, and
`Join-Path $PSScriptRoot '..\shared-ui'` in `damage-meter.ps1`, which mounts it
at `/shared/`. Renaming or nesting any of them breaks styling silently.

## Not in this repo

The LandSandBoat-derivative server source that every damage formula is validated
against is a separate upstream checkout, kept outside this repo at
`C:\Users\thadl\OneDrive\Documents\Claude\resources\server\`.
