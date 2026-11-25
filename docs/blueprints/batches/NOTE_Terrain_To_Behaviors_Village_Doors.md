<!--STATUS
state: LIVE · updated: 2026-11-25 · current-answer: §1
related-designs:
  - ../../DESIGN_Terrain_Import_UTM.md — §11.5 the as-built (the causes, the fixes, the importer revision)
  - NOTE_Behaviors_To_Terrain_Village_Doors_Impassable.md — the note this answers
-->

# NOTE — terrain → behaviors: village-street's doors (your CE-8324) — 2 of 3 causes fixed

**From:** terrain · **Answers:** [NOTE_Behaviors_To_Terrain_Village_Doors_Impassable.md](NOTE_Behaviors_To_Terrain_Village_Doors_Impassable.md) · **Tracker:** `CE-5056`

## 1. What it was, and what changed

Your probe reproduced (my count, the ground doors only: **234 of 582** with no path through). Measured causes:

| # | cause | fix (importer) |
|---|---|---|
| ① | a 0.9 m door minus the infantry erosion (0.3 m each jamb) leaves a 0.3 m channel that breaks on a wall rotated off the grid — blocked 20 % at 0–9°, ~70 % at 27–45° | generated doors are **1.2 m** |
| ② | the stair ramp planned right behind a door (the gun house's EAST door) | the stair keeps a clear apron inside the input's ground doors |
| ③ | **not found yet** — 56 other doors | open (CE-5056) |

**After: 56 of 582.** ✅ **The gun house's SOUTH door (`f666eab30807`, the one your drill stalled at) now has a path through** (measured on the new import). No house turned solid for it (276 enterable, 1 solid, as before).

## 2. What you need to do

Nothing to merge on your side for ①/②: the importer now records a **revision** in `terrain.json`, so your machine **re-imports village-street (and Stechovice)
on first use** after you merge `terrain` (tens of seconds; the navmesh prefill included). ⚠ 56 doors stay blocked until ③ is found, so your `CE-8324` fallback
(give the room up, try the next way in) is still the right guard — please keep it.
