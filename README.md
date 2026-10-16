# VibeXI

[![Latest release](https://img.shields.io/github/v/release/HasayaFFXI/VibeXI)](https://github.com/HasayaFFXI/VibeXI/releases/latest)

An Ashita v4 addon for Final Fantasy XI that records combat as it happens. It
reads the game's incoming action packets and writes one JSON line per event to
a file on your PC.

VibeXI is read-only. It never sends anything to the game server, has no
commands and draws nothing on screen. It works on any server you play through
Ashita v4, and it is approved for use on HorizonXI.

> **Not playing on HorizonXI?** Open `vibexi.ini` and set `Horizon=0` before
> you play. The default, `1`, is only right on HorizonXI. See
> [`Horizon`](#server-horizon) below.

## Install

1. Download the latest release:
   **[VibeXI.zip](https://github.com/HasayaFFXI/VibeXI/releases/latest/download/VibeXI.zip)**

   Older versions and release notes are on the
   [releases page](https://github.com/HasayaFFXI/VibeXI/releases).
2. Unzip it into the `addons` folder of your Ashita install. The zip holds a
   single `VibeXI` folder, so you end up with:

   ```
   <Ashita folder>\addons\VibeXI\vibexi.lua
   <Ashita folder>\addons\VibeXI\vibexi.ini
   ...
   ```

   On HorizonXI the Ashita folder is `HorizonXI\Game`. Don't rename the
   `VibeXI` folder: Ashita takes the addon's name from it.
3. If you're not on HorizonXI, set `Horizon=0` in `vibexi.ini`.
4. In game, load it:

   ```
   /addon load vibexi
   ```

   To load it every time you play, add that same line to
   `<Ashita folder>\scripts\default.txt`.

You can load it at character select or after you log in. Recording starts once
your character is known.

### Updating

Download the latest `VibeXI.zip` and unzip it over the old folder, but
**back up your `vibexi.ini` first**. The release ships a default one that would
overwrite your settings. Then reload:

```
/addon reload vibexi
```

## Output

Each load of the addon starts a new file named after your character and the
time it loaded:

```
%LOCALAPPDATA%\VibeXI\events\<Character>-<YYYYMMDDHHMMSS>.jsonl
```

For example, `C:\Users\you\AppData\Local\VibeXI\events\Hasaya-20261015193000.jsonl`.
To use a different folder, set `EventDir` (below).

## Settings: `vibexi.ini`

`vibexi.ini` sits next to `vibexi.lua`. The addon reads it once when it loads,
so **after changing a value, reload the addon** (`/addon reload vibexi`).

If a line or the whole file is missing, or a value can't be understood, the
default is used. A bad setting never stops the addon from loading or recording.

| Setting | Default | What it does |
|---|---|---|
| `Horizon` | `1` | Turns on a fix for how HorizonXI reports some job abilities. Set it to `0` on any other server. |
| `EventDir` | `%LOCALAPPDATA%\VibeXI\events` | The folder the event files are written to. |

### `[server]` `Horizon`

HorizonXI sends about a dozen job abilities as if they were weaponskills: the
Jumps, Shield Bash, Weapon Bash, Blade Bash and Eagle Eye Shot. With
`Horizon=1`, VibeXI spots these and records them as the job abilities they
really are.

- `1` (also `on`, `yes`, `true`): you play on HorizonXI. This is the default.
- `0` (also `off`, `no`, `false`): you play anywhere else. Other servers send
  job abilities normally, so the fix has nothing to correct and could misread a
  real weaponskill.

### `[output]` `EventDir`

The folder the `.jsonl` event files go to. It is created if it doesn't exist.

- It must be a **full path**, like `D:\FFXI\Logs` or `\\server\share\vibexi`.
  Relative paths are ignored.
- `%NAME%` is replaced with that Windows environment variable, for example
  `%USERPROFILE%\Documents\VibeXI`.
- Spaces are fine, and quotes around the path are optional.
- If the line is empty, or the folder can't be used, the addon falls back to
  `%LOCALAPPDATA%\VibeXI\events`.
- **Don't use OneDrive, Dropbox or any other synced folder.** The file is
  written several times a second for as long as you play.

### Writing the file

```ini
[server]
Horizon=0

[output]
EventDir=D:\FFXI\VibeXI Logs
```

- One `key=value` per line. Keys aren't case sensitive.
- Comments start with `;` or `#` and **must be on their own line**. A comment
  after a value becomes part of the value, because `;` and `#` are legal in
  folder names.

## For developers

`addons/VibeXI/` holds only the Lua the addon loads at runtime, because the
folder is copied into Ashita as-is. Tooling lives in `dev/`, and a pre-commit
hook in `.githooks/` runs `dev/check-apis.py`, which checks every external call
against `dev/ALLOWED_APIS.txt` so the addon can't gain a way to send to the
server.

### Releasing

Push a version tag (`1.2.3` or `v1.2.3`). The `Release` workflow in
`.github/workflows/release.yml` zips `addons/VibeXI/` as `VibeXI.zip`, sets
`addon.version` inside it from the tag, and publishes a GitHub Release with the
zip attached. The README's download link always points at the newest one. To
rebuild the release for an existing tag, run the workflow by hand from the
Actions tab.
