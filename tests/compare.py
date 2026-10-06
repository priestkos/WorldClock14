"""Diff the two grid dumps produced by tests/LogicTests (--dump) and tests/reference.mjs.

    python compare.py grid.net.tsv grid.icu.tsv

Both sides are keyed by (zone, unix second); a row is a mismatch if the local wall clock or the
offset in minutes differs. Prints per-zone agreement and the first few disagreements.
"""
import collections
import sys


def load(path):
    rows = {}
    with open(path, "r", encoding="utf-8") as handle:
        for line in handle:
            zone, unix, local, offset = line.rstrip("\n").split("\t")
            rows[(zone, unix)] = (local, offset)
    return rows


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        return 2

    net = load(sys.argv[1])
    icu = load(sys.argv[2])
    print(f".NET rows: {len(net):,}   ICU rows: {len(icu):,}")

    only_net = set(net) - set(icu)
    only_icu = set(icu) - set(net)
    if only_net or only_icu:
        print(f"KEY MISMATCH: {len(only_net)} only in .NET, {len(only_icu)} only in ICU")
        for key in list(only_net)[:5]:
            print("   only .NET:", key)
        for key in list(only_icu)[:5]:
            print("   only ICU :", key)

    common = set(net) & set(icu)
    mismatches = [(key, net[key], icu[key]) for key in common if net[key] != icu[key]]
    print(f"compared: {len(common):,} rows   mismatches: {len(mismatches):,}")

    per_zone = collections.Counter(key[0] for key in common)
    bad_zone = collections.Counter(key[0] for key, _, _ in mismatches)
    for zone in sorted(per_zone):
        bad = bad_zone.get(zone, 0)
        flag = "OK " if bad == 0 else "BAD"
        print(f"  {flag} {zone:22s} {per_zone[zone]:>7,} rows, {bad} mismatches")

    for key, a, b in mismatches[:15]:
        print(f"  {key[0]} t={key[1]}: .NET {a[0]} (off {a[1]}m) | ICU {b[0]} (off {b[1]}m)")

    return 1 if (mismatches or only_net or only_icu) else 0


if __name__ == "__main__":
    sys.exit(main())
