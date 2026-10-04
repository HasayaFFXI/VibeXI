# OverlaySpike in Electron

The overlay spike from `../SPIKE.md`, rebuilt in Electron 44 for the WPF / WinUI 3 /
Electron comparison. Plain HTML/CSS/JS, no framework. Throwaway.

## Setup (portable Node, nothing installed system-wide)

```bash
cd apps/zerg/poc/electron
mkdir -p .node && cd .node
curl -LO https://nodejs.org/dist/v24.21.0/node-v24.21.0-win-x64.zip
unzip -q node-v24.21.0-win-x64.zip && rm node-v24.21.0-win-x64.zip && cd ..
export PATH="$PWD/.node/node-v24.21.0-win-x64:$PATH"
npm install            # electron + koffi; Electron's ~158 MB zip downloads on first run
```

`.node/` and `node_modules/` are gitignored. Electron caches its zip in
`%LOCALAPPDATA%\electron\Cache`.

## Run

```bash
export PATH="$PWD/.node/node-v24.21.0-win-x64:$PATH"
npm start                                   # or: node_modules/electron/dist/electron.exe .
node_modules/electron/dist/electron.exe . --set=keyed,opacity=70,x=700   # start in a state
node_modules/electron/dist/electron.exe . --selftest                     # walk every knob, print Win32 state, quit
```

`--selftest` moves the real mouse cursor over the window briefly (SendInput) to test
mouse forwarding, then puts it back.

## Files

- `main.js` - the window, every knob, the native menu, the hotkey, the selftest
- `preload.js` - the contextBridge (`window.spike`); contextIsolation + sandbox, no nodeIntegration
- `win32.js` - read-only Win32 calls through koffi (FFI) for the readout card
- `spike.html` - the WinForms spike's page, adapted (drag strip, probes)

## Window kinds

`frame` and `transparent` are construction-only, and on Windows `transparent:true`
silently drops the native frame. So: framed = `frame:true, transparent:false` (opaque);
frameless = `frame:false, transparent:true`. "Transparent background" implies frameless,
and both toggles rebuild the window.
