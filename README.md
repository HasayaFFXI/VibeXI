# VibeXI

[![Latest release](https://img.shields.io/github/v/release/HasayaFFXI/VibeXI)](https://github.com/HasayaFFXI/VibeXI/releases/latest)

VibeXI is an Ashita v4 addon for Final Fantasy XI that records combat events (player damage/healing) as they happen in real time.

VibeXI is meant to serve as a pre-approved method of extracting data from FFXI, so that it can be used in developing personal applications.

It is approved for use on HorizonXI, but note that no approvals have been given for any other private servers.
> **Not playing on HorizonXI?**  See [`Horizon`](#server-horizon) below.


## Example application
I built VibeXI for my personal project [Zerg](https://github.com/HasayaFFXI/Zerg).

Feel free to download it and try it out to get some ideas for your own application!

## Installing VibeXI

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

| Setting | Default | What it does |
|---|---|---|
| `Horizon` | `1` | Turns on a fix for how HorizonXI reports some job abilities. Set it to `0` on any other server. |
| `EventDir` | `%LOCALAPPDATA%\VibeXI\events` | The folder the event files are written to. |

### `[server]` `Horizon`

HorizonXI sends about a dozen job abilities as if they were weaponskills. With
`Horizon=1`, VibeXI spots these and records them as the job abilities they
really are.

- `1` you play on HorizonXI. This is the default.
- `0` you play anywhere else. Other servers may send job abilities normally (unconfirmed)

### `[output]` `EventDir`

The folder the `.jsonl` event files go to. It is created if it doesn't exist.

- **Don't use slow or network storage** The file is written several times a 
  second for as long as you play.

## For developers

The event files are meant to be read by your own tools: a parser, a live meter,
a spreadsheet import. See [DEVELOPERS.md](DEVELOPERS.md) for the file format
and every field in it.
