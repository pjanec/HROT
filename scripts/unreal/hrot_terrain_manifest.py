"""hrot_terrain_manifest.py - CE-4651 (U-18x): the IDENTITY of a terrain package - which HROT terrain it was built from, by content.

    python scripts/unreal/hrot_terrain_manifest.py <terrain name> <package folder> [--source <Unreal level>] [--terrains <HROT assets/terrain>]

Writes <package folder>/hrot-terrain.json:
    { "terrain": "<name>", "baseHash": "<sha256 hex>", "overlays": [], "source": "<level>", "written": "<UTC ISO>" }
The client (UHrotDpsSubsystem::ResolveTerrain) compares it with Session.Terrain / TerrainBaseHash / TerrainOverlayHash (gateway NOTE-2026-11-21-
Gateway-Terrain-Seats-Offers.md): a base hash the station states must match; an overlay the package does not carry fails the load with the reason.

baseHash = HROT's TerrainContentHash.OfTerrain (FDP/Toolkits/Fdp.Toolkits/Terrain/TerrainContentHash.cs), reproduced byte for byte: SHA-256 over
every file of the terrain folder except navtiles/, source/, versions/ and terrain.hash, in ordinal relative-path order, each as
UTF-8(path) 0x00 int64-LE(length) bytes; lowercase hex.
⚠ It is the hash of the HROT terrain AS IT IS NOW: run it when the package is built from a level imported from that same terrain version.
"""
import argparse, datetime, hashlib, json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))


def of_terrain(folder):
    files = []
    for dp, _, fns in os.walk(folder):
        for fn in fns:
            full = os.path.join(dp, fn)
            rel = os.path.relpath(full, folder).replace("\\", "/")
            if rel == "terrain.hash" or rel.startswith(("navtiles/", "source/", "versions/")):
                continue
            files.append((rel, full))
    files.sort(key=lambda f: f[0].encode("utf-16-be"))   # = C#'s string.CompareOrdinal (UTF-16 code units)
    h = hashlib.sha256()
    for rel, full in files:
        h.update(rel.encode("utf-8")); h.update(b"\x00")
        h.update(os.path.getsize(full).to_bytes(8, "little", signed=True))
        with open(full, "rb") as s:
            for chunk in iter(lambda: s.read(1 << 20), b""):
                h.update(chunk)
    return h.hexdigest()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("terrain"); ap.add_argument("package")
    ap.add_argument("--source", default="")
    ap.add_argument("--terrains", default=os.path.join(ROOT, "assets", "terrain"))
    a = ap.parse_args()
    folder = os.path.join(a.terrains, a.terrain)
    if not os.path.isfile(os.path.join(folder, "terrain.json")):
        sys.exit(f"no HROT terrain '{a.terrain}' at {folder}")
    m = {"terrain": a.terrain, "baseHash": of_terrain(folder), "overlays": [], "source": a.source,
         "written": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")}
    os.makedirs(a.package, exist_ok=True)
    with open(os.path.join(a.package, "hrot-terrain.json"), "w", encoding="utf-8") as f:
        json.dump(m, f, indent=1)
    print(f"{a.terrain}: base {m['baseHash']} -> {os.path.join(a.package, 'hrot-terrain.json')}")


if __name__ == "__main__":
    main()
