// Read-only Win32 calls, so the readout shows what Windows actually did rather
// than what Electron says it did. koffi is a prebuilt N-API FFI module: no
// node-gyp, no Visual Studio, and the same binary loads in Node and Electron.
// If it fails to load, the readout says so and everything else still works.
let api = null;
let loadError = null;
try {
  const koffi = require('koffi');
  const user32 = koffi.load('user32.dll');
  koffi.struct('POINT', { x: 'int32_t', y: 'int32_t' });
  koffi.struct('MOUSEINPUT', { dx: 'int32_t', dy: 'int32_t', mouseData: 'uint32_t',
    dwFlags: 'uint32_t', time: 'uint32_t', dwExtraInfo: 'uintptr_t' });
  koffi.struct('INPUT', { type: 'uint32_t', mi: 'MOUSEINPUT' });
  api = {
    GetWindowLongPtrW: user32.func('intptr_t __stdcall GetWindowLongPtrW(intptr_t hWnd, int nIndex)'),
    GetLayeredWindowAttributes: user32.func(
      'bool __stdcall GetLayeredWindowAttributes(intptr_t hwnd, _Out_ uint32_t *crKey, _Out_ uint8_t *bAlpha, _Out_ uint32_t *dwFlags)'),
    GetDpiForWindow: user32.func('uint32_t __stdcall GetDpiForWindow(intptr_t hwnd)'),
    GetCursorPos: user32.func('bool __stdcall GetCursorPos(_Out_ POINT *pt)'),
    SetCursorPos: user32.func('bool __stdcall SetCursorPos(int x, int y)'),
    SendInput: user32.func('uint32_t __stdcall SendInput(uint32_t n, INPUT *in, int cb)'),
    GetSystemMetrics: user32.func('int __stdcall GetSystemMetrics(int i)'),
    SendMessageW: user32.func('intptr_t __stdcall SendMessageW(intptr_t hWnd, uint32_t Msg, uintptr_t wParam, intptr_t lParam)'),
  };
} catch (e) {
  loadError = String(e && e.message || e);
}

const GWL_STYLE = -16, GWL_EXSTYLE = -20;
const WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
  WS_EX_LAYERED = 0x80000, WS_EX_NOREDIRECTIONBITMAP = 0x200000, WS_EX_NOACTIVATE = 0x8000000;
const WS_CAPTION = 0xC00000, WS_THICKFRAME = 0x40000;
const WM_NCHITTEST = 0x84;
const LWA_COLORKEY = 1, LWA_ALPHA = 2;
const HT = { 1: 'HTCLIENT', 2: 'HTCAPTION', 10: 'HTLEFT', 11: 'HTRIGHT', 12: 'HTTOP',
  13: 'HTTOPLEFT', 14: 'HTTOPRIGHT', 15: 'HTBOTTOM', 16: 'HTBOTTOMLEFT', 17: 'HTBOTTOMRIGHT',
  [-1]: 'HTTRANSPARENT', 0: 'HTNOWHERE' };

function hwndOf(win) {
  const buf = win.getNativeWindowHandle();
  return Number(buf.readBigUInt64LE(0));
}

function hex(n) { return '0x' + (n >>> 0).toString(16).toUpperCase().padStart(8, '0'); }

/** Everything the readout card wants from Win32, or {error} if koffi is missing. */
function inspect(win) {
  const hwnd = hwndOf(win);
  if (!api) return { hwnd: '0x' + hwnd.toString(16), error: 'koffi not loaded: ' + loadError };
  const style = Number(api.GetWindowLongPtrW(hwnd, GWL_STYLE));
  const ex = Number(api.GetWindowLongPtrW(hwnd, GWL_EXSTYLE));
  const out = {
    hwnd: '0x' + hwnd.toString(16).toUpperCase(),
    style: hex(style),
    exStyle: hex(ex),
    layered: !!(ex & WS_EX_LAYERED),
    transparent: !!(ex & WS_EX_TRANSPARENT),
    topmostBit: !!(ex & WS_EX_TOPMOST),
    noRedirBitmap: !!(ex & WS_EX_NOREDIRECTIONBITMAP),
    toolWindow: !!(ex & WS_EX_TOOLWINDOW),
    noActivate: !!(ex & WS_EX_NOACTIVATE),
    caption: (style & WS_CAPTION) === WS_CAPTION,
    thickFrame: !!(style & WS_THICKFRAME),
    dpi: api.GetDpiForWindow(hwnd),
  };
  if (out.layered) {
    const key = [0], alpha = [0], flags = [0];
    if (api.GetLayeredWindowAttributes(hwnd, key, alpha, flags)) {
      out.lwaAlpha = (flags[0] & LWA_ALPHA) ? alpha[0] : null;
      out.lwaKey = (flags[0] & LWA_COLORKEY) ? hex(key[0]) : null;
    } else {
      out.lwaAlpha = 'n/a (UpdateLayeredWindow?)';
    }
  }
  // Ask the window itself what is under its bottom-right corner (2 px in).
  // HTBOTTOMRIGHT = the user can resize there; HTCLIENT/HTNOWHERE = cannot.
  // Physical screen pixels, which is what WM_NCHITTEST takes.
  const b = win.getBounds();
  const sf = require('electron').screen.getDisplayMatching(b).scaleFactor;
  const pb = require('electron').screen.dipToScreenRect(win, b);
  const x = pb.x + pb.width - 2, y = pb.y + pb.height - 2;
  const lp = ((y & 0xFFFF) << 16) | (x & 0xFFFF);
  const hit = Number(api.SendMessageW(hwnd, WM_NCHITTEST, 0, lp));
  out.cornerHit = HT[hit] || String(hit);
  out.scaleFactorAtWindow = sf;
  return out;
}

/** Selftest only: move the real cursor across the window a few times with
 *  SendInput (so it looks like real mouse input: SetCursorPos would bypass the
 *  low-level mouse hook that Electron's forwarding relies on), then put it
 *  back. The page's mousemove counter shows whether forwarding works. */
function wiggle(win) {
  if (!api) return;
  const pt = {};
  api.GetCursorPos(pt);
  const cx = api.GetSystemMetrics(0), cy = api.GetSystemMetrics(1);   // primary, physical px
  const move = (x, y) => api.SendInput(1, [{ type: 0, mi: {
    dx: Math.round(x * 65535 / (cx - 1)), dy: Math.round(y * 65535 / (cy - 1)),
    mouseData: 0, dwFlags: 0x8001 /* MOVE|ABSOLUTE */, time: 0, dwExtraInfo: 0 } }], 40);
  const pb = require('electron').screen.dipToScreenRect(win, win.getBounds());
  let i = 0;
  const t = setInterval(() => {
    if (i++ >= 8) { clearInterval(t); move(pt.x, pt.y); return; }
    move(pb.x + 60 + i * 20, pb.y + 200 + i * 5);
  }, 60);
}

module.exports = { inspect, wiggle, available: () => !!api, loadError: () => loadError };
