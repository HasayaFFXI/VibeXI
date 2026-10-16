# VibeXI

An Ashita v4 addon for HorizonXI that records combat as it happens. It reads the
game's incoming action packets and writes one JSON line per event to a file on
your PC, where a desktop app picks it up.

VibeXI is read-only. It never sends anything to the game server, has no
commands and draws nothing on screen. It is a Horizon approved addon.

## Install

Releases are git tags. Each tag (for example `1.0.0`) is a version you can
download.

1. Download the release zip:
   - Go to the repo's [tags](https://github.com/HasayaFFXI/VibeXI/tags) and pick
     a version, or
   - download 1.0.0 directly:
     <https://github.com/HasayaFFXI/VibeXI/archive/refs/tags/1.0.0.zip>
2. Unzip it. Inside `VibeXI-1.0.0\` you'll find `addons\VibeXI\`.
3. Copy that `VibeXI` folder into your game's addons folder, so you end up with:

   ```
   HorizonXI\Game\addons\VibeXI\vibexi.lua
   HorizonXI\Game\addons\VibeXI\vibexi.ini
   ...
   ```

   Don't rename the folder. Ashita takes the addon's name from it.
4. In game, load it:

   ```
   /addon load vibexi
   ```

   To load it every time you play, add that same line to
   `HorizonXI\Game\scripts\default.txt`.

You can load it at character select or after you log in. Recording starts once
your character is known.

### Updating

Download the newer tag and copy its `VibeXI` folder over the old one, but
**back up your `vibexi.ini` first**. The release ships a default one that would
overwrite your settings. Then reload:

```
/addon reload vibexi
```

### Installing with git (optional)

```bash
git clone --branch 1.0.0 https://github.com/HasayaFFXI/VibeXI.git
```

Copy `addons\VibeXI` from the clone the same way as above.

## Output

Each load of the addon starts a new file named after your character and the
time it loaded:

```
%LOCALAPPDATA%\VibeXI\events\<Character>-<YYYYMMDDHHMMSS>.jsonl
```

For example, `C:\Users\you\AppData\Local\VibeXI\events\Hasaya-20261015193000.jsonl`.
Point the desktop app at the same folder. To use a different folder, set
`EventDir` (below).

## Settings: `vibexi.ini`

`vibexi.ini` sits next to `vibexi.lua`. The addon reads it once when it loads,
so **after changing a value, reload the addon** (`/addon reload vibexi`).

If a line or the whole file is missing, or a value can't be understood, the
default is used. A bad setting never stops the addon from loading or recording.

| Setting | Default | What it does |
|---|---|---|
| `Horizon` | `1` | Turns on Horizon's job ability fix. Leave it at `1` on HorizonXI. |
| `EventDir` | `%LOCALAPPDATA%\VibeXI\events` | The folder the event files are written to. |

### `[server]` `Horizon`

HorizonXI sends about a dozen job abilities as if they were weaponskills: the
Jumps, Shield Bash, Weapon Bash, Blade Bash and Eagle Eye Shot. With
`Horizon=1`, VibeXI spots these and records them as the job abilities they
really are.

- `1` (also `on`, `yes`, `true`): you are on HorizonXI. This is the default.
- `0` (also `off`, `no`, `false`): you are on a server that sends job abilities
  normally. There the fix has nothing to correct and could misread a real
  weaponskill.

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

Whatever reads the events has to be looking at the same folder.

### Writing the file

```ini
[server]
Horizon=1

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
