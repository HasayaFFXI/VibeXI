// OverlaySpike (apps/zerg/poc/SPIKE.md) rebuilt in Electron.
//
// The same five questions as the WinForms spike, answered with Electron's own
// APIs instead of Win32 calls:
//   1. translucent + keeps repainting   setOpacity(), backgroundThrottling:false
//   2. stays above the game             setAlwaysOnTop(true, 'screen-saver')
//   3. background vanishes, panels solid transparent:true + a transparent <body>
//   4. click-through + Ctrl+Alt+Z        setIgnoreMouseEvents(), globalShortcut
//   5. frameless + movable               frame:false + -webkit-app-region:drag
//
// frame and transparent are construction-only options in Electron, so toggling
// either rebuilds the window (bounds and every other knob carry over). And on
// Windows they are not independent: transparent:true silently drops the native
// frame even with frame:true (no caption, no resize border; measured, see
// README). So the spike has two window kinds, as in WPF:
//   framed     frame:true,  transparent:false  native caption, opaque
//   frameless  frame:false, transparent:true   page draws everything
// "Transparent background" therefore implies frameless, and turning the frame
// back on turns it off.
const { app, BrowserWindow, Menu, globalShortcut, ipcMain, screen } = require('electron');
const path = require('path');
const win32 = require('./win32');

const TEST_PAGE = path.join(__dirname, 'spike.html');
const METER_URL = 'http://localhost:8731/';
const HOTKEY = 'Control+Alt+Z';
const LEVEL = 'screen-saver';   // see README: on Windows every level is just HWND_TOPMOST
const SELFTEST = process.argv.includes('--selftest');
// --set=keyed,frameless,click,opacity=70  start in a given state (for screenshots)
const SET = (process.argv.find((a) => a.startsWith('--set=')) || '').slice(6).split(',').filter(Boolean);
const t0 = Date.now();

const state = {
  opacity: 100,          // percent, what we asked for
  keyed: false,
  frameless: false,
  clickThrough: false,
  forward: true,         // setIgnoreMouseEvents(..., {forward}) while click-through is on
  topmost: true,
  hotkey: false,
  page: 'test',
};

let startX = 120;
for (const s of SET) {
  if (s === 'keyed') state.keyed = state.frameless = true;
  else if (s === 'frameless') state.frameless = true;
  else if (s === 'click') state.clickThrough = true;
  else if (s.startsWith('opacity=')) state.opacity = +s.slice(8);
  else if (s.startsWith('x=')) startX = +s.slice(2);
}

let win = null;
let bounds = { x: startX, y: 120, width: 440, height: 760 };
let firstLoadMs = null;
let probe = {};

function createWindow() {
  win = new BrowserWindow({
    ...bounds,
    title: 'Zerg overlay spike (Electron)',
    frame: !state.frameless,
    transparent: state.frameless,
    backgroundColor: state.frameless ? '#00000000' : '#14141a',
    resizable: true,
    alwaysOnTop: state.topmost,
    skipTaskbar: false,
    show: false,
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      // Without this Chromium throttles timers and rAF for a window that is not
      // focused / is hidden. An overlay is never the focused window (the game is).
      backgroundThrottling: !!process.env.SPIKE_THROTTLE,   // env only to test the default
      spellcheck: false,
    },
  });
  win.setMenu(null);
  if (state.topmost) win.setAlwaysOnTop(true, LEVEL);

  win.once('ready-to-show', () => {
    win.showInactive();     // don't steal focus from the game
    applyAll();
  });
  win.webContents.on('did-finish-load', () => {
    if (firstLoadMs === null) firstLoadMs = Date.now() - t0;
    report();
  });
  for (const ev of ['move', 'resize', 'focus', 'blur', 'always-on-top-changed'])
    win.on(ev, () => report());
  win.on('close', () => { bounds = win.getBounds(); });

  // Right-click on a -webkit-app-region:drag area is non-client on Windows, so
  // the page never sees it; Electron raises this instead of the system menu.
  win.on('system-context-menu', (e) => { e.preventDefault(); popMenu(); });

  // Keep the page on our origin; hand anything else to nobody (it's a spike).
  win.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));

  loadPage();
}

function loadPage() {
  if (state.page === 'meter') win.loadURL(METER_URL).catch(() => {});
  else win.loadFile(TEST_PAGE);
}

// ------------------------------------------------------------------ the knobs

function applyOpacity() {
  win.setOpacity(state.opacity / 100);
}

function applyClickThrough() {
  if (state.clickThrough) win.setIgnoreMouseEvents(true, { forward: state.forward });
  else win.setIgnoreMouseEvents(false);
}

function applyTopmost() {
  win.setAlwaysOnTop(state.topmost, LEVEL);
}

function applyAll() {
  applyOpacity();
  applyClickThrough();
  applyTopmost();
  report();
}

function setFrameless(on) {
  if (!on) state.keyed = false;     // a framed window is opaque
  if (on === state.frameless) return report();
  state.frameless = on;
  bounds = win.getBounds();
  const old = win;
  createWindow();      // frame is construction-only: rebuild
  old.destroy();
}

function handle(msg) {
  if (msg && msg.cmd === 'probe') { probe = msg; return; }   // page-side measurements, no report
  switch (msg && msg.cmd) {
    case 'opacity': state.opacity = Math.min(100, Math.max(10, msg.value | 0)); applyOpacity(); break;
    case 'keyed':                    // page-side, but needs the transparent window
      state.keyed = !!msg.on;
      if (state.keyed) return setFrameless(true);
      break;
    case 'frameless': setFrameless(!!msg.on); return;
    case 'clickThrough': state.clickThrough = !!msg.on; applyClickThrough(); break;
    case 'forward': state.forward = !!msg.on; applyClickThrough(); break;
    case 'topmost': state.topmost = !!msg.on; applyTopmost(); break;
    case 'meter': state.page = 'meter'; loadPage(); return;
    case 'test': state.page = 'test'; loadPage(); return;
  }
  report();
}

// --------------------------------------------------------- what it really is

function snapshot() {
  const b = win.getBounds();
  const display = screen.getDisplayMatching(b);
  let w = {};
  try { w = win32.inspect(win); } catch (e) { w = { error: String(e.message || e) }; }
  const metrics = app.getAppMetrics();
  const wsKB = metrics.reduce((s, m) => s + (m.memory ? m.memory.workingSetSize : 0), 0);
  return {
    type: 'state',
    ...state,
    getOpacity: win.getOpacity(),
    isAlwaysOnTop: win.isAlwaysOnTop(),
    isResizable: win.isResizable(),
    isFocused: win.isFocused(),
    bounds: b,
    display: { id: display.id, scaleFactor: display.scaleFactor, size: display.size },
    win32: w,
    hotkey: globalShortcut.isRegistered(HOTKEY),
    versions: { electron: process.versions.electron, chrome: process.versions.chrome, node: process.versions.node },
    procs: metrics.map((m) => m.type + ':' + Math.round((m.memory ? m.memory.workingSetSize : 0) / 1024) + 'MB'),
    workingSetMB: Math.round(wsKB / 1024),
    firstLoadMs,
    gpu: app.getGPUFeatureStatus().gpu_compositing,
  };
}

let reportQueued = false;
function report() {
  // move/resize fire in bursts while dragging; coalesce to one per tick
  if (reportQueued || !win || win.isDestroyed()) return;
  reportQueued = true;
  setImmediate(() => {
    reportQueued = false;
    if (!win || win.isDestroyed()) return;
    const s = snapshot();
    win.setTitle('Zerg spike (Electron) - ' + state.opacity + '%' + (state.keyed ? ' keyed' : '') +
      (state.clickThrough ? ' click-through' : '') + (state.topmost ? '' : ' (not on top)'));
    win.webContents.send('spike:state', s);
    if (process.env.SPIKE_LOG) console.log('STATE ' + JSON.stringify(s));
  });
}

// ---------------------------------------------------------------- the menu

function popMenu() {
  const toggle = (label, key) => ({
    label, type: 'checkbox', checked: state[key],
    click: () => handle({ cmd: key, on: !state[key] }),
  });
  Menu.buildFromTemplate([
    { label: 'Test page', click: () => handle({ cmd: 'test' }) },
    { label: 'Real meter (needs damage-meter.py running)', click: () => handle({ cmd: 'meter' }) },
    { type: 'separator' },
    { label: 'Opacity', submenu: [100, 85, 70, 55, 40, 25].map((p) => ({
      label: p + '%', type: 'radio', checked: state.opacity === p,
      click: () => handle({ cmd: 'opacity', value: p }) })) },
    toggle('Keyed background', 'keyed'),
    toggle('Frameless', 'frameless'),
    toggle('Click-through  (Ctrl+Alt+Z)', 'clickThrough'),
    toggle('Forward mouse moves while click-through', 'forward'),
    toggle('Always on top', 'topmost'),
    { type: 'separator' },
    { label: 'Exit', click: () => app.quit() },
  ]).popup({ window: win });
}

// ------------------------------------------------------------------- startup

ipcMain.on('spike:cmd', (e, msg) => { if (win && e.sender === win.webContents) handle(msg); });
ipcMain.on('spike:menu', (e) => { if (win && e.sender === win.webContents) popMenu(); });

app.whenReady().then(() => {
  // Only the main process (the browser process) ever registers this. The
  // renderer has no way to, and that is the point: once click-through is on,
  // the page can't be clicked, so the way out has to come from outside it.
  state.hotkey = globalShortcut.register(HOTKEY, () => handle({ cmd: 'clickThrough', on: !state.clickThrough }));
  createWindow();
  setInterval(report, 2000);    // memory and hit-test drift without any event
  if (SELFTEST) selftest();
});

app.on('will-quit', () => globalShortcut.unregisterAll());
app.on('window-all-closed', () => app.quit());

// --selftest: walk every knob, print what Windows reports after each, quit.
// Run with SPIKE_LOG=1 to see the full state lines.
function selftest() {
  const steps = [
    ['initial (framed, opaque window)', null],
    ['opacity 55', { cmd: 'opacity', value: 55 }],
    ['click-through on (forward)', { cmd: 'clickThrough', on: true }],
    ['forward off', { cmd: 'forward', on: false }],
    ['click-through off', { cmd: 'clickThrough', on: false }],
    ['opacity 100', { cmd: 'opacity', value: 100 }],
    ['topmost off', { cmd: 'topmost', on: false }],
    ['topmost on', { cmd: 'topmost', on: true }],
    ['keyed on (rebuilds: frameless+transparent)', { cmd: 'keyed', on: true }],
    ['opacity 70', { cmd: 'opacity', value: 70 }],
    ['click-through on (forward off)', { cmd: 'clickThrough', on: true }],
    ['  cursor wiggle, forward off', 'wiggle'],
    ['forward on', { cmd: 'forward', on: true }],
    ['  cursor wiggle, forward on', 'wiggle'],
    ['click-through off', { cmd: 'clickThrough', on: false }],
    ['  cursor wiggle, click-through off', 'wiggle'],
    ['moved to the other monitor', 'monitor2'],
    ['moved back to the primary', 'monitor1'],
    ['frameless off (rebuilds: framed again)', { cmd: 'frameless', on: false }],
  ];
  let i = 0;
  const next = () => {
    if (i >= steps.length) { setTimeout(() => app.quit(), 1500); return; }
    const [name, msg] = steps[i++];
    if (msg === 'wiggle') win32.wiggle(win);
    else if (msg === 'monitor2' || msg === 'monitor1') {
      const ds = screen.getAllDisplays();
      const d = msg === 'monitor1' ? screen.getPrimaryDisplay() : ds.find((x) => x.id !== screen.getPrimaryDisplay().id);
      if (d) win.setPosition(d.workArea.x + 100, d.workArea.y + 100);
    }
    else if (msg) handle(msg);
    setTimeout(() => {
      const s = snapshot();
      const w = s.win32;
      console.log(`SELFTEST ${name.padEnd(40)} getOpacity=${s.getOpacity.toFixed(2)} onTop=${s.isAlwaysOnTop} ` +
        `@${s.bounds.x},${s.bounds.y} ${s.bounds.width}x${s.bounds.height} ex=${w.exStyle} layered=${w.layered} transparent=${w.transparent} topmostBit=${w.topmostBit} ` +
        `noRedir=${w.noRedirBitmap} lwaAlpha=${w.lwaAlpha} caption=${w.caption} thick=${w.thickFrame} ` +
        `corner=${w.cornerHit} dpi=${w.dpi} sf=${s.display.scaleFactor} ws=${s.workingSetMB}MB load=${s.firstLoadMs}ms ` +
        `| page ${probe.ticks}t/s ${probe.fps}fps ${probe.vis} focus=${probe.focus} moves=${probe.moves}`);
      next();
    }, msg === 'wiggle' ? 1500 : msg && (msg.cmd === 'frameless' || msg.cmd === 'keyed') ? 2500 : 700);
  };
  setTimeout(next, 2500);
}
