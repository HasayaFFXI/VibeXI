"""Window transparency for the damage meter's pop-out windows.

A browser window is opaque no matter what CSS the page carries, so the pop-out
windows' opacity slider cannot on its own let the game show through -- only the
OS can do that, by making the window layered (WS_EX_LAYERED) and giving it an
alpha. That is what this module does, and what GET /api/alpha calls.

LWA_ALPHA IS THE ONLY EFFECT THAT LANDS. The colour key below is accepted and
ignored: Chrome presents its windows through DirectComposition, not through the
legacy redirection surface that LWA_COLORKEY operates on, so keyed pixels are
never dropped. SetLayeredWindowAttributes still returns success, which is why
nothing downstream ever noticed. Measured 2026-09-05 by keying a real Chrome
window painted entirely #010203 and sampling the screen underneath: alpha=128
moved the pixel, adding the key changed nothing at alpha 128 or 255. A synthetic
non-Chrome layered window keyed correctly at every alpha, so this is Chrome
specifically and not the alpha value; --disable-gpu does not help either.

The consequence worth writing down, because it is the question that keeps being
asked: a pop-out CANNOT have a solid header over a translucent panel. Alpha is
one byte for the whole HWND and there is no per-region form of it; the per-pixel
escape hatch, UpdateLayeredWindow, has to own the window's pixels, and those
belong to Chrome. Wanting that means drawing the overlay in the Ashita addon
instead of in a browser window.

FINDING THE WINDOW is the whole difficulty, because a Document Picture-in-Picture
window's caption is Chrome's to write, not the page's -- it is not reliably the
document title. So the client sends where its window *is* (the centre of it, in
screen coordinates) and the match is by position: WindowFromPoint, then
GetAncestor to the top-level window. Nothing but the window the slider lives in
can be at that point, and it is always-on-top so nothing can be over it. The
title is kept as a second try, and "the topmost browser window" as a third, so a
client that cannot report its position (or a screen coordinate lost to DPI
scaling) still lands somewhere sensible.

Refuses to touch the shell (desktop, taskbar) and returns which strategy hit, so
a wrong window is diagnosable rather than mysterious. If user32 cannot be loaded
at all, SUPPORTED is False, the endpoint still answers with applied:0, and the
client falls back to fading the document -- which cannot show the game, only
quieten the panel, and which says so in the window bar.

This is a straight port of the C# that damage-meter.ps1 compiled with Add-Type;
ctypes reaches the same user32 entry points without a compiler at startup.
"""

import ctypes
from ctypes import wintypes

# ------------------------------------------------------------------ constants

GWL_EXSTYLE = -20
WS_EX_LAYERED = 0x00080000
WS_EX_TOPMOST = 0x00000008
LWA_COLORKEY = 0x00000001
LWA_ALPHA = 0x00000002
GA_ROOT = 2

SM_XVIRTUALSCREEN = 76
SM_YVIRTUALSCREEN = 77
SM_CXVIRTUALSCREEN = 78
SM_CYVIRTUALSCREEN = 79

# The desktop and the taskbar are always under the mouse somewhere; dimming
# either would be spectacular and is never what was meant.
_SHELL_CLASSES = frozenset((
    'Progman', 'WorkerW', 'Shell_TrayWnd', 'Shell_SecondaryTrayWnd', '#32769',
))

_BROWSER_CLASSES = ('Chrome_WidgetWin', 'MozillaWindowClass')

# ------------------------------------------------------------------- bindings

SUPPORTED = False
_user32 = None
_ENUMPROC = None

try:
    _user32 = ctypes.WinDLL('user32', use_last_error=True)
    _ENUMPROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

    _user32.EnumWindows.argtypes = [_ENUMPROC, wintypes.LPARAM]
    _user32.EnumWindows.restype = wintypes.BOOL
    _user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    _user32.GetWindowTextW.restype = ctypes.c_int
    _user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    _user32.GetClassNameW.restype = ctypes.c_int
    _user32.IsWindowVisible.argtypes = [wintypes.HWND]
    _user32.IsWindowVisible.restype = wintypes.BOOL
    _user32.IsIconic.argtypes = [wintypes.HWND]
    _user32.IsIconic.restype = wintypes.BOOL
    _user32.GetSystemMetrics.argtypes = [ctypes.c_int]
    _user32.GetSystemMetrics.restype = ctypes.c_int
    _user32.WindowFromPoint.argtypes = [wintypes.POINT]
    _user32.WindowFromPoint.restype = wintypes.HWND
    _user32.GetAncestor.argtypes = [wintypes.HWND, wintypes.UINT]
    _user32.GetAncestor.restype = wintypes.HWND
    _user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
    _user32.GetWindowRect.restype = wintypes.BOOL
    # GWL_EXSTYLE is a 32-bit value even in a 64-bit process, so the non-Ptr
    # forms are the correct ones here.
    _user32.GetWindowLongW.argtypes = [wintypes.HWND, ctypes.c_int]
    _user32.GetWindowLongW.restype = ctypes.c_long
    _user32.SetWindowLongW.argtypes = [wintypes.HWND, ctypes.c_int, ctypes.c_long]
    _user32.SetWindowLongW.restype = ctypes.c_long
    _user32.SetLayeredWindowAttributes.argtypes = [
        wintypes.HWND, wintypes.COLORREF, ctypes.c_ubyte, wintypes.DWORD]
    _user32.SetLayeredWindowAttributes.restype = wintypes.BOOL

    SUPPORTED = True
except (OSError, AttributeError, ValueError):  # not Windows, or user32 refused
    SUPPORTED = False


# -------------------------------------------------------------------- helpers

def caption(hwnd):
    if not hwnd:
        return ''
    buf = ctypes.create_unicode_buffer(600)
    _user32.GetWindowTextW(hwnd, buf, len(buf))
    return buf.value


def _cls(hwnd):
    if not hwnd:
        return ''
    buf = ctypes.create_unicode_buffer(256)
    _user32.GetClassNameW(hwnd, buf, len(buf))
    return buf.value


def _is_shell(hwnd):
    return _cls(hwnd) in _SHELL_CLASSES


def _on_desktop(x, y):
    """The point has to be somewhere a window can actually be seen. A stale or
    mis-scaled coordinate otherwise wanders into the parking lot at -32000,
    where the minimised windows live."""
    vx = _user32.GetSystemMetrics(SM_XVIRTUALSCREEN)
    vy = _user32.GetSystemMetrics(SM_YVIRTUALSCREEN)
    vw = _user32.GetSystemMetrics(SM_CXVIRTUALSCREEN)
    vh = _user32.GetSystemMetrics(SM_CYVIRTUALSCREEN)
    if vw <= 0 or vh <= 0:
        return True
    return vx <= x < vx + vw and vy <= y < vy + vh


def _root(hwnd):
    if not hwnd:
        return None
    return _user32.GetAncestor(hwnd, GA_ROOT) or hwnd


def _rect(hwnd):
    r = wintypes.RECT()
    if not _user32.GetWindowRect(hwnd, ctypes.byref(r)):
        return None
    return r


def _from_point(x, y, w, h):
    """One candidate point. Rejected unless the window it lands on is visible, is
    not the shell, and is about the size the caller said it was. The size check
    is not optional: this process may be DPI-unaware while the caller measures in
    CSS pixels, so one of the two candidate points in apply() is usually wrong,
    and on a scaled desktop a wrong point still lands on *something* -- quite
    possibly the game. Size is what tells the two apart."""
    if w <= 0 or h <= 0:
        return None
    if not _on_desktop(x, y):
        return None

    pt = wintypes.POINT(int(x), int(y))
    hit = _root(_user32.WindowFromPoint(pt))
    if not hit or not _user32.IsWindowVisible(hit) or _is_shell(hit):
        return None
    # Minimised windows park themselves off at -32000 with a stub rect, where any
    # two of them look alike and an off-screen point "finds" one of them.
    if _user32.IsIconic(hit):
        return None

    r = _rect(hit)
    if r is None:
        return None
    # Generous: the caller measures its viewport, the OS measures the frame.
    if abs((r.right - r.left) - w) > 160 or abs((r.bottom - r.top) - h) > 220:
        return None
    return hit


def _enum(predicate):
    """EnumWindows with a Python predicate; returns the first window it accepts.

    The callback object is held in a local for the duration of the call --
    letting ctypes collect it while user32 still holds the pointer is the
    classic way to crash this."""
    found = []

    def cb(hwnd, _lparam):
        if predicate(hwnd):
            found.append(hwnd)
            return False
        return True

    proc = _ENUMPROC(cb)
    _user32.EnumWindows(proc, 0)
    return found[0] if found else None


def _by_title(title):
    if not title or len(title) < 8:
        return None
    return _enum(lambda h: bool(_user32.IsWindowVisible(h)) and title in caption(h))


def _topmost_browser():
    """Last resort: a Document PiP window is always-on-top, and almost nothing
    else on a desktop is both topmost and a browser widget."""

    def ok(hwnd):
        if not _user32.IsWindowVisible(hwnd):
            return False
        if not _user32.GetWindowLongW(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST:
            return False
        c = _cls(hwnd)
        if not any(b in c for b in _BROWSER_CLASSES):
            return False
        r = _rect(hwnd)
        if r is None:
            return False
        return (r.right - r.left) >= 120 and (r.bottom - r.top) >= 80  # skip tooltips

    return _enum(ok)


# ---------------------------------------------------------------------- apply

def apply(title, cx, cy, w, h, dpr, alpha, key):
    """Make one window layered and set its alpha (and optionally a colour key).

    cx, cy   centre of the caller's window in CSS screen pixels
    w, h     its size, for the sanity check (0 to skip)
    dpr      device pixel ratio, since a DPI-aware desktop scales those
    alpha    0..255
    key      colour to punch out entirely, or -1 for none. INERT against Chrome
             -- see the module docstring. Kept because the flag is harmless, the
             client still sends it, and a host that did honour it (a non-Chromium
             browser, or an overlay this app drew itself) would want it. Do not
             build behaviour on it taking effect.

    Returns (method, hwnd, caption), or None if nothing matched.
    """
    if not SUPPORTED:
        return None

    hit = None
    how = ''

    if cx or cy:
        # Both readings, each with its own size to check against: this process
        # may or may not be DPI-aware, and the two agree only when the desktop
        # is at 100%. The size check rejects the wrong one.
        hit = _from_point(round(cx * dpr), round(cy * dpr),
                          round(w * dpr), round(h * dpr))
        if not hit:
            hit = _from_point(cx, cy, w, h)
        if hit:
            how = 'point'
    if not hit:
        hit = _by_title(title)
        if hit:
            how = 'title'
    if not hit:
        hit = _topmost_browser()
        if hit:
            how = 'topmost'
    if not hit:
        return None

    ex = _user32.GetWindowLongW(hit, GWL_EXSTYLE)
    if not ex & WS_EX_LAYERED:
        _user32.SetWindowLongW(hit, GWL_EXSTYLE, ex | WS_EX_LAYERED)

    flags = LWA_ALPHA
    cr_key = 0
    if key >= 0:
        cr_key = key
        flags |= LWA_COLORKEY

    if not _user32.SetLayeredWindowAttributes(hit, cr_key, alpha, flags):
        return None

    return how, int(hit), caption(hit)
