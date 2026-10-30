# VibeXI

[![Latest release](https://img.shields.io/github/v/release/HasayaFFXI/VibeXI)](https://github.com/HasayaFFXI/VibeXI/releases/latest)

An Ashita v4 addon for Final Fantasy XI that records combat events as they happen in real time. 
It reads the game's incoming action packets and writes one JSON line per event to a file on your PC.

It works on any server you play through Ashita v4, and it is approved for use on HorizonXI.
Note that no approvals have been given for any other private servers.

> **Not playing on HorizonXI?**  See [`Horizon`](#server-horizon) below.

## Install

1. Download `VibeXI-<version>.zip` from the
   **[latest release](https://github.com/HasayaFFXI/VibeXI/releases/latest)**.

2. Unzip it into your Ashita folder (the one that has `addons` and `config` in
   it). The zip holds `addons\VibeXI\` and `config\addons\VibeXI\`, so you end
   up with:

   ```
   <Ashita folder>\addons\VibeXI\vibexi.lua
   ...
   <Ashita folder>\config\addons\VibeXI\vibexi.ini
   ```

3. If you're not on HorizonXI, set `Horizon=0` in `vibexi.ini`.
4. In game, load it:

   ```
   /addon load vibexi
   ```

   To load it every time you play, add that same line to
   `<Ashita folder>\scripts\default.txt`.

### Updating

Download the zip from the latest release and unzip it into your Ashita folder again.
When Windows asks whether to replace `vibexi.ini`, **choose Skip** to keep your
settings. Everything else should be replaced. Then reload:

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

`vibexi.ini` lives in `<Ashita folder>\config\addons\VibeXI\`, apart from the
addon's own files, so updates don't have to touch it. The addon reads it once
when it loads, so **after changing a value, reload the addon**
(`/addon reload vibexi`).

If a line or the whole file is missing, or a value can't be understood, the
default is used. A bad setting never stops the addon from loading or recording.

| Setting | Default | What it does |
|---|---|---|
| `Horizon` | `1` | Turns on a fix for how HorizonXI reports some job abilities. Set it to `0` on any other server. |
| `EventDir` | `%LOCALAPPDATA%\VibeXI\events` | The folder the event files are written to. |

### `[server]` `Horizon`

HorizonXI sends about a dozen job abilities as if they were weaponskills. With
`Horizon=1`, VibeXI spots these and records them as the job abilities they
really are.

- `1` (also `on`, `yes`, `true`): you play on HorizonXI. This is the default.
- `0` (also `off`, `no`, `false`): you play anywhere else. Other servers may
- send job abilities normally (unconfirmed)

### `[output]` `EventDir`

The folder the `.jsonl` event files go to. It is created if it doesn't exist.

- It must be a **full path**, like `D:\FFXI\Logs` or `\\server\share\vibexi`.
  Relative paths are ignored.
- `%NAME%` is replaced with that Windows environment variable, for example
  `%USERPROFILE%\Documents\VibeXI`.
- Spaces are fine, and quotes around the path are optional.
- If the line is empty, or the folder can't be used, the addon falls back to
  `%LOCALAPPDATA%\VibeXI\events`.
- **Don't use slow or network storage** The file is written several times a 
  second for as long as you play.

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

The event files are meant to be read by your own tools: a parser, a live meter,
a spreadsheet import. See [DEVELOPERS.md](DEVELOPERS.md) for the file format
and every field in it.
