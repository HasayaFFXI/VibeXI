"""Fails if addons/VibeXI/ calls any external API not in ALLOWED_APIS.txt.

The VibeXI addon must never be able to send anything to the game server. That is
enforced by the addon not CONTAINING the capability, and this script is what
proves it stayed that way.

Three independent checks, all must pass:

  1. ALLOWLIST  every external call in addons/VibeXI/**/*.lua must be listed in
                ALLOWED_APIS.txt. This is the real gate -- it catches APIs
                nobody here has heard of, which a denylist cannot.
  2. DENYLIST   a short list of known egress routes, checked separately so the
                failure message names the actual problem rather than saying
                "unlisted token".
  3. EVENTS     ashita.events.register may only subscribe to the event names
                allowlisted as 'event:<name>'. packet_out is the injection route
                and must never appear.

Comments are scanned too, but a hit inside a comment is a WARNING, not a failure
-- a comment cannot execute, and "-- deliberately no socket here" is a sentence
we want to be able to write.

    python addon-dev/check-apis.py

Stdlib only, and it is wired to .githooks/pre-commit.
"""

import argparse
import os
import re
import sys
from pathlib import Path

HERE  = Path(__file__).resolve().parent      # addon-dev/
ROOT  = HERE.parent                          # repo root; findings are reported relative to it
ADDON = ROOT / 'addons' / 'VibeXI'           # the Lua the addon actually loads

# Known egress routes. This is the SECOND line of defence, not the first -- the
# allowlist is what actually holds. These exist to produce a clear message.
# Matched case-insensitively, like the PowerShell -match operator they came from.
DENIED = [
    (r'GetChatManager', 'the only route to QueueCommand'),
    (r'QueueCommand', 'issues a game command / chat message'),
    (r'packet_out', 'outgoing packet subscription'),
    (r'add_outgoing_packet', 'packet injection'),
    (r'AddOutgoingPacket', 'packet injection'),
    (r'InjectPacket', 'packet injection'),
    (r"""require\s*\(?\s*['"]socket""", 'network capability (PLAN.md Decision 1)'),
    (r"""require\s*\(?\s*['"]ffi""", 'foreign-function escape hatch (PLAN.md Decision 2)'),
    (r'ffi\.load', 'can load ws2_32'),
    (r'ffi\.cdef', 'can declare connect()/send()'),
    (r'os\.execute', 'shell execution'),
    (r'io\.popen', 'shell execution'),
    (r'ashita\.memory\.write', 'writes to game memory; this addon is read-only'),
    (r'loadstring', 'arbitrary code execution'),
    (r'dofile', 'arbitrary code execution'),
]

# Extractors. Each returns the normalised tokens documented in ALLOWED_APIS.txt.
# Case-sensitive on purpose: Lua is, and so are the SDK names.
EXTRACTORS = [
    (r"""require\s*\(?\s*['"]([A-Za-z0-9_.]+)['"]""", "require('{0}')"),
    (r'(ashita\.[a-z_]+\.[a-z_]+)', '{0}'),
    (r'(AshitaCore:[A-Za-z0-9_]+)', '{0}'),
    (r':([A-Za-z0-9_]+)\s*\(', ':{0}'),
    (r'\b(GetEntity|GetPlayerEntity)\s*\(', '{0}()'),
    (r'\b((?:io|os|package|debug)\.[a-z_]+)', '{0}'),
]

EVENT_RX = re.compile(r"""ashita\.events\.register\s*\(\s*['"]([a-z0-9_]+)['"]""")

DENIED_RX = [(re.compile(p, re.IGNORECASE), why) for p, why in DENIED]
EXTRACTOR_RX = [(re.compile(p), fmt) for p, fmt in EXTRACTORS]


# --------------------------------------------------------------------- colour

def _ansi_ok():
    if not sys.stdout.isatty():
        return False
    if os.name != 'nt':
        return True
    try:
        import ctypes
        k = ctypes.WinDLL('kernel32', use_last_error=True)
        h = k.GetStdHandle(-11)
        mode = ctypes.c_uint32()
        if not k.GetConsoleMode(h, ctypes.byref(mode)):
            return False
        return bool(k.SetConsoleMode(h, mode.value | 0x0004))
    except (OSError, AttributeError, ImportError):
        return False


COLOR = False
_CODES = {'red': '31', 'green': '32', 'yellow': '33', 'grey': '90'}


def c(text, color):
    if not COLOR or color not in _CODES:
        return text
    return '\033[%sm%s\033[0m' % (_CODES[color], text)


# ---------------------------------------------------------------------- scan

def split_lua_line(line):
    """Crude but adequate: everything after the first '--' is comment. A '--'
    inside a string literal would truncate early, which can only LOSE code
    coverage on that line's tail -- so pair this with the denylist, which scans
    the comment side too and warns."""
    i = line.find('--')
    if i < 0:
        return line, ''
    return line[:i], line[i:]


def load_manifest(path):
    allowed = set()
    for line in path.read_text(encoding='utf-8', errors='replace').splitlines():
        t = line.strip()
        if not t or t.startswith('#'):
            continue
        allowed.add(t)
    events = {t[6:] for t in allowed if t.startswith('event:')}
    return allowed, events


def scan(files, allowed, allowed_events):
    violations, warnings, seen = [], [], set()

    for path in files:
        rel = os.path.relpath(path, ROOT)
        text = path.read_text(encoding='utf-8', errors='replace')
        for n, raw in enumerate(text.splitlines(), start=1):
            code, comment = split_lua_line(raw)

            # 1. denylist -- code side fails, comment side warns
            for rx, why in DENIED_RX:
                m = rx.search(code)
                if m:
                    violations.append(('DENIED', rel, n, m.group(0), why, raw.strip()))
                    continue
                m = rx.search(comment)
                if m:
                    warnings.append((rel, n, m.group(0), raw.strip()))

            # 2. allowlist -- every external call in code must be listed
            for rx, fmt in EXTRACTOR_RX:
                for m in rx.finditer(code):
                    token = fmt.format(m.group(1))
                    seen.add(token)
                    if token not in allowed:
                        violations.append(('UNLISTED', rel, n, token,
                                           'not in ALLOWED_APIS.txt', raw.strip()))

            # 3. event subscriptions must be allowlisted by name
            for m in EVENT_RX.finditer(code):
                ev = m.group(1)
                seen.add('event:' + ev)
                if ev not in allowed_events:
                    violations.append(('EVENT', rel, n, 'event:' + ev,
                                       'event not allowlisted', raw.strip()))

    return violations, warnings, seen


# --------------------------------------------------------------------- report

def main(argv=None):
    global COLOR

    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument('--src-dir', default=None, help='default: addons/VibeXI')
    ap.add_argument('--manifest', default=None, help='default: addon-dev/ALLOWED_APIS.txt')
    args = ap.parse_args(argv)

    COLOR = _ansi_ok()
    src_dir = Path(args.src_dir) if args.src_dir else ADDON
    manifest = Path(args.manifest) if args.manifest else HERE / 'ALLOWED_APIS.txt'

    if not manifest.is_file():
        print(c('FAIL  manifest not found: %s' % manifest, 'red'))
        return 1

    allowed, allowed_events = load_manifest(manifest)

    if not src_dir.is_dir():
        print(c('PASS  no addons/VibeXI/ yet -- nothing to check.', 'green'))
        print('      %d tokens allowlisted, %d events.' % (len(allowed), len(allowed_events)))
        return 0

    files = sorted(p for p in src_dir.rglob('*.lua') if p.is_file())
    if not files:
        print(c('PASS  no Lua sources in addons/VibeXI/ yet -- nothing to check.', 'green'))
        print('      %d tokens allowlisted, %d events.' % (len(allowed), len(allowed_events)))
        return 0

    violations, warnings, seen = scan(files, allowed, allowed_events)

    print('')
    print('addon API check -- %d file(s), %d distinct external call(s)' % (len(files), len(seen)))

    if warnings:
        print('')
        print(c('WARN  denylisted name mentioned in a comment (not a failure):', 'yellow'))
        for rel, n, token, _text in warnings:
            print(c('      %s:%d  %s' % (rel, n, token), 'yellow'))

    if violations:
        print('')
        print(c('FAIL  %d violation(s)' % len(violations), 'red'))
        for kind, rel, n, token, note, text in violations:
            print('')
            print(c('  [%s] %s:%d' % (kind, rel, n), 'red'))
            print('      %s  --  %s' % (token, note))
            print(c('      | %s' % text, 'grey'))
        print('')
        print(c('  The addon must never be able to reach the game server.', 'red'))
        print('  If a call is genuinely safe and needed, add it to ALLOWED_APIS.txt')
        print('  deliberately -- and say why in the comment above it.')
        print('')
        return 1

    print('')
    print(c('PASS  every external call is allowlisted.', 'green'))
    print('')
    return 0


if __name__ == '__main__':
    sys.exit(main())
