"""Spike S2's data: an 18-character alliance, 30 minutes of swings ending now.

Both the WPF chart spike and the web build read this same file, so their CPU
can be compared on the same data. Only what the cumulative chart needs is
written: job lines, melee rounds and weaponskills, all players on one mob.
Format as the addon writes it (one JSON object per line, t in seconds).

    python gen-alliance.py --out <dir>/Hasaya_2026.10.02.jsonl [--minutes 30]
"""
import argparse, json, random, time

PARTY = [
    ("Hasaya", "SAM", "WAR"), ("Clarice", "DRK", "SAM"), ("Bryx", "WAR", "NIN"),
    ("Kyrias", "RNG", "NIN"), ("Selene", "BLM", "RDM"), ("Kidtony", "THF", "NIN"),
    ("Angermanagement", "MNK", "WAR"), ("Tavros", "NIN", "WAR"), ("Pestii", "DRG", "WAR"),
    ("Rhyllis", "PLD", "WAR"), ("Sylviane", "WHM", "SCH"), ("Vermillion", "RDM", "BLM"),
    ("Orrin", "BST", "WHM"), ("Mavet", "COR", "NIN"), ("Quill", "BRD", "WHM"),
    ("Tessaly", "SMN", "WHM"), ("Harrow", "PUP", "WAR"), ("Ilsa", "BLU", "NIN"),
]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--minutes", type=float, default=30)
    ap.add_argument("--seed", type=int, default=18)
    a = ap.parse_args()
    rng = random.Random(a.seed)

    end = int(time.time())
    start = end - int(a.minutes * 60)
    lines = []
    for name, main_job, sub in PARTY:
        lines.append({"kind": "job", "t": start - 60, "actor": name, "main": main_job, "mainId": 1,
                      "mainLvl": 75, "sub": sub, "subId": 1, "subLvl": 37})

    # A wide spread of strength, so a few characters fall under 5% of the total.
    power = [rng.uniform(0.25, 1.6) for _ in PARTY]
    events, seq, use = [], 0, 0
    for i, (name, _, _) in enumerate(PARTY):
        t = start + rng.uniform(0, 4)
        next_ws = t + rng.uniform(15, 40)
        while t < end:
            use += 1
            for _swing in range(rng.choice([1, 1, 1, 2, 3])):
                hit = rng.random() > 0.1
                events.append((int(t), {"kind": "melee", "actor": name, "action": "Attack", "actionId": 1,
                                        "dmg": int(rng.uniform(40, 260) * power[i]) if hit else 0,
                                        "hit": hit, "msg": 1 if hit else 15, "use": use}))
                use += 1
            if t >= next_ws:
                events.append((int(t), {"kind": "ws", "actor": name, "action": "Weaponskill", "actionId": 32,
                                        "dmg": int(rng.uniform(500, 2600) * power[i]), "hit": True,
                                        "msg": 185, "use": use}))
                use += 1
                next_ws = t + rng.uniform(15, 40)
            t += rng.uniform(2.2, 3.6)

    events.sort(key=lambda e: e[0])
    for t, e in events:
        seq += 1
        lines.append({"t": t, "seq": seq, "use": e["use"], "kind": e["kind"], "actor": e["actor"],
                      "actorKind": "player", "action": e["action"], "actionId": e["actionId"],
                      "target": "Kirin", "targetKind": "mob", "dmg": e["dmg"], "hit": e["hit"],
                      "crit": False, "burst": False, "msg": e["msg"]})

    with open(a.out, "w", encoding="ascii", newline="\n") as f:
        for ln in lines:
            f.write(json.dumps(ln, separators=(",", ":")) + "\n")
    print(f"{len(lines)} lines, {start} .. {end}")


if __name__ == "__main__":
    main()
