# Damage Meter — shipping it as a Windows `.exe`

Goal: a HorizonXI player who has never opened a terminal downloads one thing,
double-clicks it, and watches their damage. No repo clone, no execution policy,
no Python, no "trust this script".

Nothing is built yet; this file is the whole plan. `README.md` is the
human-facing overview and `CLAUDE.md` the operational detail.

---

## What is actually being shipped

**The `.exe` is a local web server, not a GUI application.** The UI is the
browser page in `web/`; the exe's job is to tail the addon's newest event file, serve
`web/` and `../shared-ui/` on `localhost`, and open a tab. Every decision below
follows from that:

- there is no window to design — the console *is* the app's window
- the assets have to travel inside the exe (188 KB across 10 files)
- "closing it" means Ctrl-C or closing the console, and that has to be obvious
- it binds `127.0.0.1` and nothing else, so Windows never shows a firewall
  prompt and nothing on the LAN can reach it

```
DamageMeter.exe ──tails──▶ %LOCALAPPDATA%\VibeXI\events\Hasaya_2026.09.04.jsonl
       │
       └──serves──▶ http://127.0.0.1:8731/  ──▶ the player's browser
```

No account, no network egress, no telemetry. That property is worth keeping
explicit: it is most of the answer when someone asks whether it is safe.

---

## The blocker, and why the Python port is Phase 0

`damage-meter.ps1` cannot become a good `.exe`. PS2EXE exists and would
technically work — it embeds the script in a small .NET host and runs it through
the PowerShell engine — but:

- **Defender flags PS2EXE output hard.** Script-wrapped-in-exe is what a large
  class of commodity malware looks like, and heuristics treat it that way. This
  is the whole reason to avoid it; the rest is minor.
- It still depends on Windows PowerShell 5.1 on the target machine, which is
  frozen, deprecated, and not something to build a distributable on in 2026.
- The `Add-Type` C# compile at startup (the window-alpha code) is a runtime
  compiler invocation inside a shipped binary — slow, and another AV signal.

So the port to Python was not a detour; it was step one, and it is done — see
Phase 0.

### Options considered

| Route | Verdict |
|---|---|
| **PS2EXE** over the existing script | rejected — AV, 5.1 dependency, runtime C# compile |
| **PyInstaller** over the ported Python | **chosen** — stdlib-only app, no native deps, mature tooling |
| **Nuitka** (compiles to C) | plausible fallback; better AV profile, needs a C toolchain and much longer builds. Revisit only if PyInstaller output keeps getting quarantined |
| rewrite as a C# / .NET single-file app | best AV story by far, and genuinely small — but it is a rewrite of a working parser host for packaging reasons. No |

---

## Decisions

Recorded so they don't get relitigated.

**1. PyInstaller, `--onedir`, shipped as a zip.** Not `--onefile`. Onefile
unpacks itself into `%TEMP%` on every launch, which is precisely the behaviour
heuristic AV scores against, and it costs a second of startup for no benefit
here. The audience already unzips an Ashita addon folder into a game directory;
a folder with `DamageMeter.exe` in it is not a hardship. Onefile is one flag
away in the same spec if it turns out to be wanted.

**2. Never UPX.** `--noupx`. Packed sections are one of the strongest AV
heuristics going, and the whole payload is 188 KB of text — there is nothing to
compress that matters.

**3. The console window stays.** `--noconsole` would need a tray icon (a
dependency) and would leave a running server with no visible way to stop it and
nowhere for an error to appear. The console is the stop button, the status line
and the error log.

**4. Settings live at `%LOCALAPPDATA%\VibeXI\damage-meter.json`.** Not beside the
exe: the exe may land somewhere unwritable, and this matches the addon's
`%LOCALAPPDATA%\VibeXI\events\` for the same reasons (not `%TEMP%`, not synced
Roaming, not OneDrive). CLI flags still override the file, so the dev workflow
does not change.

**5. Find the event directory; do not make the player type a path.** Resolution
order: `--events-dir` → settings file → `%LOCALAPPDATA%\VibeXI\events`. There is
no search chain any more and there does not need to be one: the addon and the
server agree on that path in source (`vx_emit.lua`'s `ensure_dir`), so the only
way it is wrong is if the addon fell back to the Ashita install tree because
`%LOCALAPPDATA%` was unset. If nothing is found the server still starts and the
page says "waiting for the addon", which is the true diagnosis far more often
than a wrong path — the usual cause is that `/addon load VibeXI` was never run.

**6. Port 8731, then walk up.** One user on one machine can be told to pass
`-Port`; a thousand users cannot. Try 8731–8740, take the first free one, print
and open *that* URL.

**7. Bind `127.0.0.1` explicitly.** No firewall prompt, no LAN listener. This is
a property to defend in review, not a default to drift off.

**8. Ship a version, and put it three places** — the console banner, `--version`,
and the Windows file-properties resource. A bug report from a stranger is
useless without it.

**9. No auto-update.** Out of scope, and a whole trust surface of its own. The
banner prints the version and the release URL.

---

## What breaks the moment it is frozen

The real content of Phase 2. Every item is a known PyInstaller gotcha, not
speculation.

| Thing | Why it breaks | Fix |
|---|---|---|
| `web/` and `../shared-ui/` paths | in the dev tree they resolve relative to the script, and **shared-ui is outside the project directory** | one `roots()` helper, two layouts (below) |
| `__file__` | meaningless inside a bundle | `sys._MEIPASS` when `sys.frozen` |
| the `/shared/` URL prefix | it maps to `..\shared-ui`, which does not exist next to the exe | the bundle flattens both roots side by side |
| the containment check in `resolve_static_path` | it compares against a root that is now a temp path | it still holds, but the traversal test has to be re-run against the frozen layout |
| Ctrl-C | console control handling differs under a frozen host | explicit `KeyboardInterrupt` path with a "Stopped." line |
| `webbrowser.open` | fine frozen, but the URL must be the *actual* bound port | see Decision 6 |

The path helper, concretely:

```python
def roots():
    """(web_root, shared_root). The dev tree has shared-ui as a SIBLING of the
    project; the bundle has it as a sibling of web/ inside the payload."""
    if getattr(sys, 'frozen', False):
        base = Path(sys._MEIPASS)
        return base / 'web', base / 'shared-ui'
    here = Path(__file__).resolve().parent
    return here / 'web', here.parent / 'shared-ui'
```

Nothing else in the app touches the filesystem except the log tailer, which
reads an absolute path the user supplied, and the settings file.

---

## Phases

### Phase 0 — port the server to Python — **DONE 2026-09-04**

Every PowerShell component is gone. `damage-meter.py` is the server, with the
Win32 layered-window code in `winalpha.py` via `ctypes` — no `Add-Type`, no
compiler at startup, which was one of the three reasons an exe could not be cut
from the old script. The four dev-tool scripts went with it (`check-apis.py`,
`tools/check-lua.py`, `tools/gen-ws-names.py`, `tools/gen-test-events.py`); they
never ship inside the exe but a half-ported repo would have been worse than
either end state.

Everything is stdlib-only, which is what keeps Phase 2 simple: no hidden
imports, no native wheels, nothing for PyInstaller to mis-collect.

**Verified:** byte-offset tailing checked against the real fixture;
traversal attempts still refused; the allowlist checker produces output
identical to the PowerShell one on the real tree and on all seven adversarial
cases; the generators reproduce their outputs byte-for-byte; the page renders
and polls live in a browser. The one thing still untested is the *mutation*
half of `/api/alpha` — `SetLayeredWindowAttributes` needs a real pop-out
window, which this environment cannot open. The window-*finding* half is
verified against real desktop windows.

### Phase 1 — make the source frozen-ready (still runs from source)

1. `roots()` helper; every asset path goes through it.
2. Settings file at `%LOCALAPPDATA%\VibeXI\damage-meter.json`; CLI overrides it.
3. Event-directory resolution, and a friendly message when it finds nothing.
4. Port walk 8731–8740; banner and browser both use the port actually bound.
5. `__version__`, `--version`, version in the banner.
6. Startup self-check: asset roots exist, event dir state.

Every one of these is testable from source, before any build tooling exists.
That ordering is deliberate — a frozen binary is the worst possible place to
debug a path bug.

**Done when:** the script behaves identically whether it runs from the repo or
from a copy of `damage_meter/` moved somewhere else entirely.

### Phase 2 — the build

1. Confirm **PyInstaller supports Python 3.14** — the interpreter here is
   3.14.7, new enough that the toolchain may lag. If it does not, install 3.12
   or 3.13 alongside and build against that; the app is stdlib-only and version-
   agnostic. **Check this first: it decides the day.**
2. `build/damage-meter.spec` with `datas` for `web/`, `shared-ui/css`,
   `shared-ui/js`; `console=True`, `upx=False`, icon, version resource.
3. `build/version-info.txt` (product name, version, company, copyright) — this
   is what shows in file properties, and it is part of looking legitimate.
4. An icon. There isn't one yet; it needs making.
5. `build/build.py` — one command, from a clean venv, producing
   `dist/DamageMeter/`, zipped with the player README and a `SHA256SUMS`.
6. `.gitignore`: `build/venv/`, `dist/`.

**Done when:** `python build/build.py` on a clean checkout produces
`DamageMeter-<version>.zip`, and the exe inside it runs on **a Windows machine
with no Python installed** — that last clause is the entire point, and it cannot
be verified on this machine.

### Phase 3 — distribution hygiene

1. Test matrix on the clean machine: Defender on and default; SmartScreen's
   "unrecognised app" prompt (document the Run-anyway path, with a screenshot);
   non-admin user; a path with spaces; a second instance already on 8731; no
   events directory at all (i.e. the addon has never been loaded); 125% display
   scaling with the opacity slider; Chrome not the default browser.
2. Player-facing `README.txt` in the zip: what it does, that it reads a local
   file the addon writes and talks to nothing, how to install the addon, how to
   stop the server, where settings live, and what SmartScreen will say and why.
3. Player-facing docs, not developer ones: `README.md` still assumes a reader
   who cloned the repo. A zip's README starts from "you downloaded a file".
4. GitHub release on `HasayaFFXI/VibeXI` with the zip and its SHA256.
5. If Defender quarantines it, submit the sample to Microsoft as a false
   positive — that route works and usually turns around in a day or two.

**Done when:** someone who is not you, on their own machine, unzips it,
double-clicks, and sees their own numbers.

### Phase 4 — optional, only if it earns it

- **Code signing.** The real fix for SmartScreen. OV certificates now require
  hardware-token or cloud-HSM key storage and run a few hundred dollars a year,
  and only EV buys instant reputation — OV still has to accrue it. Price it
  before promising it; the numbers move.
- **Inno Setup installer** with a Start Menu shortcut, if the zip proves to be
  too much friction.
- **One exe, both apps.** `ws_calculator` is static pages that already load the
  same `shared-ui`; the server could mount them at `/calc/` and the exe becomes
  the whole VibeXI toolset rather than one app. Cheap, and probably the highest
  value item on this list.
- **Bundle the addon.** Ship `addons/VibeXI/` in the zip with a one-line installer,
  so a player gets packet-accurate data instead of chat-log parsing. Gated on
  the addon actually running — Phase 1 of `addon-dev/PLAN.md` has never executed.
- Version string on the page footer, so a screenshot becomes a bug report.

---

## Risks

| Risk | Severity | Mitigation |
|---|---|---|
| **AV / SmartScreen** flags an unsigned, freshly built exe | high — and it is the one that decides whether strangers can use this at all | onedir, no UPX, version resource, published hashes, false-positive submission, signing if it persists |
| PyInstaller does not yet support 3.14 | medium | build against 3.12/3.13; the app is stdlib-only |
| Support burden from strangers' machines | medium | the auto-detect chain and the port walk exist for exactly this; the console prints what it decided |
| Someone runs it against a server that forbids parsers | low here | it reads a log file the game itself writes: no injection, no memory access, no packets. Distinct from `addons/VibeXI/`, which *is* approved (ticket addon-0032, Aerec, 2026-09-04). Still worth telling HorizonXI staff before publishing a tool to their players |
| The zip is edited and re-shared | low | `SHA256SUMS` on the release; the source is public anyway |

---

## Open questions

1. **Distribution channel.** GitHub releases on `HasayaFFXI/VibeXI`, the
   HorizonXI Discord, or both? The Discord reaches the actual audience; GitHub
   is what a hash and a changelog can hang off.
2. **Name and branding.** `DamageMeter.exe`, or `VibeXI.exe` if Phase 4's
   both-apps idea happens? Renaming after a release is worse than deciding now.
3. **Signing budget** — see Phase 4.
4. **Does HorizonXI want a word before this is published to their players?** The
   addon needed a ticket; a log reader almost certainly does not, but asking
   costs nothing and the channel is already open.
5. **Support expectations.** Publishing to a game community means bug reports
   from people who cannot describe their setup. Is that wanted, or is this a
   "here is the zip, no promises" release?

---

## Appendix — spec sketch

```python
# build/damage-meter.spec
a = Analysis(
    ['../damage_meter/damage-meter.py'],
    pathex=['../damage_meter'],
    datas=[
        ('../damage_meter/web', 'web'),
        ('../shared-ui/css',    'shared-ui/css'),
        ('../shared-ui/js',     'shared-ui/js'),
    ],
    hiddenimports=[],
    excludes=['tkinter', 'unittest', 'pydoc', 'email', 'xml'],
)
exe = EXE(pyz, a.scripts, exclude_binaries=True,
          name='DamageMeter', console=True, upx=False,
          icon='meter.ico', version='version-info.txt')
coll = COLLECT(exe, a.binaries, a.datas, upx=False, name='DamageMeter')
```

`excludes` is not cosmetic: it is the difference between a ~9 MB payload and a
~20 MB one, and every megabyte is one more thing for a scanner to chew on.
