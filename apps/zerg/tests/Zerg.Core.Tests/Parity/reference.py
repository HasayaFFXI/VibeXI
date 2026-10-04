"""The reference half of Zerg's parity tests.

Loads apps/damage-meter/damage-meter.py as a module -- the real file, not a copy
-- and answers a batch of /api/events queries through exactly the calls its
handler makes:

    client_file = _first(query, 'file')
    events_payload(client_file, _int(_first(query, 'offset'), 0))

stdin:  {"server": "<path to damage-meter.py>", "dir": "<events dir>",
         "queries": ["file=...&offset=...", ...]}
stdout: [{"query": q, "payload": {...}} | {"query": q, "error": "..."}, ...]

Run by ParityTests.cs; nothing else needs it.
"""

import importlib.util
import json
import sys
from pathlib import Path
from urllib.parse import parse_qs


def main():
    req = json.load(sys.stdin)
    server = Path(req['server'])
    sys.path.insert(0, str(server.parent))          # damage-meter.py imports winalpha
    spec = importlib.util.spec_from_file_location('damage_meter', server)
    dm = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(dm)
    dm.EVENTS_DIR = Path(req['dir'])

    out = []
    for q in req['queries']:
        query = parse_qs(q)
        try:
            payload = dm.events_payload(dm._first(query, 'file'),
                                        dm._int(dm._first(query, 'offset'), 0))
            out.append({'query': q, 'payload': payload})
        except Exception as exc:                    # noqa: BLE001 - reported, not hidden
            out.append({'query': q, 'error': repr(exc)})
    sys.stdout.write(json.dumps(out, ensure_ascii=True))


if __name__ == '__main__':
    main()
