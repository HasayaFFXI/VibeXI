"""Structural sanity check for Lua 5.1 sources, for machines with no interpreter.

Ashita embeds LuaJIT inside Ashita.dll and ships nothing standalone, so on a dev
box with no Lua the addon's text is never parsed until `/addon load` reports a
line number. This closes most of that gap: it tokenizes strings and comments away
(including long-bracket forms), then balances block keywords and brackets.

WHAT IT CATCHES: a dropped or extra `end`, an unbalanced `repeat`/`until`, an
unclosed paren/brace/bracket, an unterminated string or long comment.

WHAT IT DOES NOT: it is not a parser. A misspelled identifier, a bad expression,
or a runtime error still surfaces only in-game.

Calibration: run over the 108 .lua files shipped in Ashita-v4beta/addons it
reports zero failures, and it does flag both mutations (one `end` removed, one
paren removed) of this repo's own sources.

    python addon-dev/check-lua.py addons/VibeXI/*.lua
"""

import argparse
import os
import re
import sys
from pathlib import Path


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
_CODES = {'red': '31', 'green': '32'}


def c(text, color):
    if not COLOR or color not in _CODES:
        return text
    return '\033[%sm%s\033[0m' % (_CODES[color], text)


# ---------------------------------------------------------------- tokenizing

_NOT_NEWLINE = re.compile(r'[^\r\n]')


def strip_lua(s):
    """Blank out comments and string literals, keeping newlines so line numbers
    stay usable. Returns (error, text)."""
    out = []
    i, n = 0, len(s)

    while i < n:
        ch = s[i]

        # comment: -- ... or long-bracket --[=*[ ... ]=*]
        if ch == '-' and i + 1 < n and s[i + 1] == '-':
            j = i + 2
            level = -1
            if j < n and s[j] == '[':
                k = j + 1
                eq = 0
                while k < n and s[k] == '=':
                    eq += 1
                    k += 1
                if k < n and s[k] == '[':
                    level = eq
                    j = k + 1
            if level >= 0:
                close = ']' + '=' * level + ']'
                e = s.find(close, j)
                if e < 0:
                    return 'unterminated long comment', ''.join(out)
                out.append(_NOT_NEWLINE.sub(' ', s[j:e]))
                i = e + len(close)
            else:
                while i < n and s[i] != '\n':
                    i += 1
            continue

        # long string [=*[ ... ]=*]
        if ch == '[':
            k = i + 1
            eq = 0
            while k < n and s[k] == '=':
                eq += 1
                k += 1
            if k < n and s[k] == '[':
                close = ']' + '=' * eq + ']'
                e = s.find(close, k + 1)
                if e < 0:
                    return 'unterminated long string', ''.join(out)
                out.append(_NOT_NEWLINE.sub(' ', s[k + 1:e]))
                i = e + len(close)
                continue

        # quoted string
        if ch in ('"', "'"):
            q = ch
            i += 1
            while i < n:
                if s[i] == '\\':
                    i += 2
                    continue
                if s[i] == q:
                    i += 1
                    break
                if s[i] == '\n':
                    return 'unterminated string', ''.join(out)
                i += 1
            out.append('""')
            continue

        out.append(ch)
        i += 1

    return None, ''.join(out)


BLOCK_RX = re.compile(r'\b(function|then|do|end|repeat|until|elseif)\b')


def check(path):
    """Returns (tokenizer_error, problems). A tokenizer error stops the file;
    problems is the list of balance failures, empty when the file is sound."""
    src = Path(path).read_text(encoding='utf-8', errors='replace')
    err, text = strip_lua(src)
    if err:
        return err, []

    msgs = []

    # bracket balance
    for opener, closer in (('(', ')'), ('{', '}'), ('[', ']')):
        o, cl = text.count(opener), text.count(closer)
        if o != cl:
            msgs.append('%s%s unbalanced: %d vs %d' % (opener, closer, o, cl))

    # block balance. openers are: function, if..then, do   (elseif/while/for are
    # not counted -- 'do'/'then' is what actually opens their block)
    depth = rep = min_depth = 0
    for m in BLOCK_RX.finditer(text):
        kw = m.group(1)
        if kw in ('function', 'then', 'do'):
            depth += 1
        elif kw == 'elseif':
            depth -= 1          # its 'then' will re-open
        elif kw == 'end':
            depth -= 1
        elif kw == 'repeat':
            rep += 1
        elif kw == 'until':
            rep -= 1
        min_depth = min(min_depth, depth)

    if depth != 0:
        msgs.append('block depth ends at %d (expected 0)' % depth)
    if rep != 0:
        msgs.append('repeat/until unbalanced: %d' % rep)
    if min_depth < 0:
        msgs.append("depth went negative (%d): an extra 'end'" % min_depth)

    return None, msgs


def main(argv=None):
    global COLOR

    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument('path', nargs='+', help='Lua files to check')
    args = ap.parse_args(argv)

    COLOR = _ansi_ok()
    fail = 0

    for p in args.path:
        name = Path(p).name
        err, msgs = check(p)
        if err:
            print(c('FAIL  %s: %s' % (name, err), 'red'))
            fail += 1
        elif msgs:
            print(c('FAIL  %s' % name, 'red'))
            for m in msgs:
                print(c('        %s' % m, 'red'))
            fail += 1
        else:
            print(c('ok    %s' % name, 'green'))

    if fail:
        return 1
    print('')
    print(c('All files structurally balanced.', 'green'))
    return 0


if __name__ == '__main__':
    sys.exit(main())
