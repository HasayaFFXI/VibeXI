"""Tails the newest event file written by the VibeXI Ashita addon and serves a
live damage meter web UI on localhost.

The one data source is the addon (../addons/VibeXI/). It reads the game's own
action packets and appends one JSON object per line to

    %LOCALAPPDATA%\\VibeXI\\events\\<Character>_<YYYY.MM.DD>.jsonl

which is what this server tails. There is no chat-log reader: the log could not
say who was a player, could not group an AoE, and could not flag a crit, and all
three are ground truth in the packet.

The server is deliberately dumb: it finds the newest *.jsonl in the events
directory, hands the browser the raw lines it has not seen yet, and serves the
static files in .\\web. The browser does every bit of interpretation
(web/lib/source.js, web/lib/stats.js), so a change there is a refresh rather
than a restart.

The addon writes ASCII-only JSON (non-ASCII escaped as \\uXXXX), so the bytes
decode identically as ASCII, UTF-8 or CP932 and no encoding negotiation is
needed anywhere on this path.

    python damage-meter.py
    python damage-meter.py --port 9000 --no-browser
    python damage-meter.py --events-dir "D:\\some\\other\\events"

Stdlib only: no pip install, no venv, nothing to keep up to date.
"""

import argparse
import ctypes
import json
import os
import sys
import webbrowser
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, unquote, urlparse

import winalpha

HERE = Path(__file__).resolve().parent
WEB_ROOT = HERE / 'web'
# The design system is shared with ../ws_calculator, so it lives outside this
# project and is mounted at the /shared/ URL prefix rather than being copied in.
SHARED_ROOT = HERE.parent / 'shared-ui'
SHARED_PREFIX = '/shared/'

# Where the addon writes. LOCALAPPDATA, not APPDATA or TEMP -- the reasoning is
# in addons/VibeXI/vx_emit.lua's header, next to the code that picks it.
DEFAULT_EVENTS_DIR = Path(os.environ.get('LOCALAPPDATA', '')) / 'VibeXI' / 'events'

MAX_TAIL = 8 * 1024 * 1024   # one poll never reads more than this

MIME_TYPES = {
    '.html': 'text/html; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.js': 'application/javascript; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
    '.svg': 'image/svg+xml',
    '.ico': 'image/x-icon',
}

# Set from the command line in main(); read by the handler.
EVENTS_DIR = DEFAULT_EVENTS_DIR


# --------------------------------------------------------------- event access

def newest_events(directory):
    """The most recently modified *.jsonl, or None."""
    try:
        files = [p for p in Path(directory).glob('*.jsonl') if p.is_file()]
    except OSError:
        return None
    if not files:
        return None
    return max(files, key=lambda p: p.stat().st_mtime)


def _open_shared(path):
    """Open for reading with FILE_SHARE_READ|WRITE|DELETE.

    Python's own open() shares read and write but NOT delete, which would block
    the addon -- running inside the game process, on the game's thread -- from
    appending while a poll is in flight. The window is milliseconds, but the
    game's writer must never be blocked by this tool, so ask Windows for the
    sharing mode explicitly and fall back to plain open() anywhere that is not
    available.
    """
    if os.name != 'nt':
        return open(path, 'rb')

    GENERIC_READ = 0x80000000
    SHARE_ALL = 0x00000001 | 0x00000002 | 0x00000004   # READ | WRITE | DELETE
    OPEN_EXISTING = 3
    FILE_ATTRIBUTE_NORMAL = 0x80
    INVALID_HANDLE = -1

    try:
        import msvcrt

        kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)
        kernel32.CreateFileW.restype = ctypes.c_void_p
        handle = kernel32.CreateFileW(
            str(path), GENERIC_READ, SHARE_ALL, None,
            OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, None)
        if handle is None or ctypes.c_ssize_t(handle).value == INVALID_HANDLE:
            raise OSError(ctypes.get_last_error(), 'CreateFileW failed', str(path))
        # The fd owns the handle from here, and the file object owns the fd.
        fd = msvcrt.open_osfhandle(handle, os.O_RDONLY | os.O_BINARY)
        return os.fdopen(fd, 'rb')
    except (ImportError, AttributeError, OSError):
        return open(path, 'rb')


def read_tail(path, offset):
    """Read whole lines from *path* starting at byte *offset*.

    Only complete lines are returned; a partial trailing line (the addon is
    still writing it) is left for the next poll, which is why the caller must
    carry nextOffset forward rather than seeking to EOF. Half a JSON object is
    not parseable, so this is what makes the flush-per-event write safe.
    """
    result = {'lines': [], 'nextOffset': offset, 'size': 0, 'truncated': False}

    with _open_shared(path) as fh:
        fh.seek(0, os.SEEK_END)
        length = fh.tell()
        result['size'] = length

        # File shrank -> it was rotated or rewritten under us; restart from zero.
        if offset > length:
            offset = 0
            result['truncated'] = True
        if offset >= length:
            result['nextOffset'] = length
            return result

        fh.seek(offset)
        buf = fh.read(min(length - offset, MAX_TAIL))

    # Trim back to the last newline so we never emit a half-written line.
    last = buf.rfind(b'\n')
    if last < 0:
        result['nextOffset'] = offset
        return result

    # The addon escapes every non-ASCII byte as \\uXXXX, so this is ASCII in
    # practice; utf-8 with replace is the safe superset of that.
    text = buf[:last + 1].decode('utf-8', errors='replace')
    lines = text.replace('\r\n', '\n').split('\n')
    # split leaves a trailing empty element after the final newline.
    if lines and lines[-1] == '':
        lines.pop()

    result['lines'] = lines
    result['nextOffset'] = offset + last + 1
    return result


def events_payload(client_file, client_offset):
    """The /api/events response body."""
    src = newest_events(EVENTS_DIR)
    if src is None:
        return {'ok': True, 'file': None, 'reset': True, 'offset': 0,
                'nextOffset': 0, 'size': 0, 'lines': [], 'dir': str(EVENTS_DIR)}

    # A different file is newest now (day rollover, character switch, new
    # session) -> tell the client to drop its state and replay from the top.
    reset = False
    offset = client_offset
    if client_file != src.name:
        reset = True
        offset = 0

    tail = read_tail(src, offset)
    if tail['truncated']:
        reset = True

    return {
        'ok': True,
        'file': src.name,
        'dir': str(EVENTS_DIR),
        'mtime': datetime.fromtimestamp(src.stat().st_mtime).astimezone().isoformat(),
        'reset': reset,
        'offset': offset,
        'nextOffset': tail['nextOffset'],
        'size': tail['size'],
        'lines': tail['lines'],
    }


# --------------------------------------------------------------- static assets

def resolve_static_path(url_path):
    """Map a URL path onto a file.

    Everything resolves under ./web except the /shared/ prefix, which resolves
    under ../shared-ui -- the design system both this app and ws_calculator link
    against. Each root gets the same containment check, so a traversal out of one
    cannot land in the other or anywhere else.
    """
    root = WEB_ROOT
    rel = unquote(url_path)
    if rel.lower().startswith(SHARED_PREFIX):
        root = SHARED_ROOT
        rel = rel[len(SHARED_PREFIX):]

    rel = rel.lstrip('/')
    if not rel.strip():
        rel = 'index.html'

    try:
        root_full = root.resolve()
        # An absolute rel ("/C:/Windows/...") would replace the root entirely;
        # the containment check below is what catches that.
        full = (root_full / rel).resolve()
    except (OSError, ValueError):
        return None

    if os.path.normcase(str(full)) != os.path.normcase(str(root_full)) and \
            not os.path.normcase(str(full)).startswith(os.path.normcase(str(root_full)) + os.sep):
        return None
    if not full.is_file():
        return None
    return full


# ------------------------------------------------------------------- http loop

class Handler(BaseHTTPRequestHandler):
    protocol_version = 'HTTP/1.1'
    server_version = 'FFXIDamageMeter'
    sys_version = ''

    def log_message(self, fmt, *args):
        pass  # one line per poll, once a second, is noise

    def _write(self, status, content_type, body):
        self.send_response(status)
        self.send_header('Content-Type', content_type)
        self.send_header('Cache-Control', 'no-store, no-cache, must-revalidate')
        self.send_header('Content-Length', str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _write_json(self, obj, status=200):
        # ensure_ascii, so the payload is plain ASCII on the wire whatever the
        # event file held -- matching how the addon writes it in the first place.
        body = json.dumps(obj, ensure_ascii=True).encode('ascii')
        self._write(status, 'application/json; charset=utf-8', body)

    def do_GET(self):
        parsed = urlparse(self.path)
        path = unquote(parsed.path)
        query = parse_qs(parsed.query)

        try:
            if path == '/api/events':
                self._api_events(query)
            elif path == '/api/alpha':
                self._api_alpha(query)
            else:
                self._static(parsed.path)
        except Exception as exc:                      # noqa: BLE001 - the loop must survive
            sys.stderr.write('WARNING: %s: %s\n' % (path, exc))
            try:
                self._write_json({'ok': False, 'error': str(exc)}, status=500)
            except OSError:
                pass

    # ------------------------------------------------------------- endpoints

    def _api_events(self, query):
        client_file = _first(query, 'file')
        self._write_json(events_payload(client_file, _int(_first(query, 'offset'), 0)))

    def _api_alpha(self, query):
        title = _first(query, 'title') or ''
        pct = max(10, min(100, _int(_first(query, 'value'), 100)))

        cx = _int(_first(query, 'x'), 0)
        cy = _int(_first(query, 'y'), 0)
        cw = _int(_first(query, 'w'), 0)
        ch = _int(_first(query, 'h'), 0)
        dpr = _float(_first(query, 'dpr'), 1.0)
        if dpr <= 0:
            dpr = 1.0

        # The punch-out colour arrives as RRGGBB; a COLORREF is 0x00BBGGRR.
        key = -1
        raw = _first(query, 'key') or ''
        if len(raw) == 6 and all(c in '0123456789abcdefABCDEF' for c in raw):
            rgb = int(raw, 16)
            key = ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF)

        hit = winalpha.apply(title, cx, cy, cw, ch, dpr,
                             round(255 * pct / 100), key)
        self._write_json({
            'ok': True,
            'applied': 1 if hit else 0,
            'supported': winalpha.SUPPORTED,
            'method': hit[0] if hit else '',
            'window': hit[2] if hit else '',
        })

    def _static(self, url_path):
        full = resolve_static_path(url_path)
        if full is None:
            self._write(404, 'text/plain; charset=utf-8', b'404 Not Found')
            return
        ctype = MIME_TYPES.get(full.suffix.lower(), 'application/octet-stream')
        self._write(200, ctype, full.read_bytes())


def _first(query, name):
    v = query.get(name)
    return v[0] if v else None


def _int(value, default):
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def _float(value, default):
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


# ---------------------------------------------------------------------- banner

def _ansi_ok():
    """Turn on VT sequences in the Windows console, so the banner can have
    colour. Returns False if the console will not have it, in which case the
    banner prints plain rather than littered with escape codes."""
    if not sys.stdout.isatty():
        return False
    if os.name != 'nt':
        return True
    try:
        kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)
        handle = kernel32.GetStdHandle(-11)            # STD_OUTPUT_HANDLE
        mode = ctypes.c_uint32()
        if not kernel32.GetConsoleMode(handle, ctypes.byref(mode)):
            return False
        return bool(kernel32.SetConsoleMode(handle, mode.value | 0x0004))
    except (OSError, AttributeError):
        return False


COLOR = False
_CODES = {'cyan': '36', 'yellow': '33', 'grey': '90', 'red': '31'}


def c(text, color):
    if not COLOR or color not in _CODES:
        return text
    return '\033[%sm%s\033[0m' % (_CODES[color], text)


def banner(url, events_dir):
    print('')
    print('  ' + c('FFXI DPS Meter', 'cyan'))
    print('  serving   %s' % url)
    print('  watching  %s' % events_dir)
    src = newest_events(events_dir)
    if src:
        print('  newest    %s  (%s KB)' % (src.name, round(src.stat().st_size / 1024, 1)))
    else:
        print('  ' + c('newest    (no .jsonl files yet -- is the addon loaded?)', 'yellow'))
    print('  ' + c('Ctrl-C to stop.', 'grey'))
    print('')


# ------------------------------------------------------------------------ main

def main(argv=None):
    global EVENTS_DIR, COLOR

    parser = argparse.ArgumentParser(
        description="Serve a live damage meter from the VibeXI addon's newest event file.")
    # argparse %-expands every help string, so the environment variable's own
    # percent signs have to be doubled or add_argument raises on import.
    parser.add_argument('--events-dir', default=str(DEFAULT_EVENTS_DIR),
                        help='directory the addon writes into '
                             '(default: %%LOCALAPPDATA%%\\VibeXI\\events)')
    parser.add_argument('--port', type=int, default=8731, help='local TCP port (default: 8731)')
    parser.add_argument('--no-browser', action='store_true', help='do not open a browser on start')
    args = parser.parse_args(argv)

    EVENTS_DIR = Path(args.events_dir)
    COLOR = _ansi_ok()

    if not WEB_ROOT.is_dir():
        raise SystemExit('Missing web root: %s' % WEB_ROOT)
    if not SHARED_ROOT.is_dir():
        print(c('WARNING: missing shared UI root: %s' % SHARED_ROOT, 'yellow'))
        print(c('The page will load unstyled until shared-ui is restored.', 'yellow'))
    if not EVENTS_DIR.is_dir():
        print(c('WARNING: events directory does not exist yet: %s' % EVENTS_DIR, 'yellow'))
        print(c('The addon creates it on /addon load VibeXI. The UI will start', 'yellow'))
        print(c('anyway and pick the file up as soon as it appears.', 'yellow'))
    if not winalpha.SUPPORTED:
        print(c('WARNING: window transparency unavailable on this platform.', 'yellow'))
        print(c('The pop-out opacity slider will fade the panel instead of the window.', 'yellow'))

    # Loopback only: no firewall prompt, and nothing on the LAN can reach it.
    try:
        httpd = ThreadingHTTPServer(('127.0.0.1', args.port), Handler)
    except OSError as exc:
        raise SystemExit(
            'Could not listen on port %d. Another instance may be running. '
            'Try --port %d.  (%s)' % (args.port, args.port + 1, exc))
    httpd.daemon_threads = True

    # The browser is sent to localhost, not 127.0.0.1: the page's saved settings
    # (theme, excluded characters, pop-out opacity) are localStorage keyed by
    # origin, and switching hostname would silently orphan every one of them.
    url = 'http://localhost:%d/' % args.port
    banner(url, EVENTS_DIR)

    if not args.no_browser:
        webbrowser.open(url)

    try:
        httpd.serve_forever(poll_interval=0.25)
    except KeyboardInterrupt:
        pass
    finally:
        httpd.server_close()
        print(c('Stopped.', 'grey'))


if __name__ == '__main__':
    main()
