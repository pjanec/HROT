<!--STATUS
state: LIVE — a STEER (pre-empts the setup in HANDOFF_Coordinator_To_Terrain_Street_Fight_Scenario.md; its rows and checks are unchanged) · updated: 2026-11-19 · current-answer: the whole file
related-designs:
  - HANDOFF_Coordinator_To_Terrain_Street_Fight_Scenario.md — CE-4672 / CE-4673, whose GROUND this corrects
-->
# STEER — coordinator → `terrain`: the street fight was a WIDE OPEN CORRIDOR (CE-4672, CE-4673)

🔒 User, `2026-11-19`: *"i can not get real-1 but it was a wide corridor (actually a railway, i.e. maybe 30 m wide) surrounded by family houses not
very dense. you need to find such place in existing terrains or build a terrain for that"* · ⭐ then: *"no railway needed just wide corridor"*.

## What changes
The ground of `pc-street-fight` (rename it `pc-rail-corridor` if you like) is NOT a dense 18 m town street: it is a **straight, WIDE OPEN corridor ~30 m
wide, ~400 m long (⛔ no railway needed — no track, no rails), with detached family houses on both sides, not dense** (gardens, gaps, fences between houses). Everything else in the handoff stands —
the platoon setup, the expect rows, and CE-4673's cover measure (now: cover along the corridor's edges — fences / hedges / the nearest house walls —
and how far from a post on the corridor's centreline the nearest cover is).

## How
1. ⭐ **Find it first in the existing terrains** — a wide straight open strip (a wide road, a park strip, a field) between detached houses may exist (`demo-city`, `test-town`, `city-battle`, `berlin`) — check before building.
2. **Else build a small terrain `wide-corridor`** (~600 × 400 m, committable): authored `world.geojson` (the corridor as an open surface — grass or
   gravel, flat; houses with doors and windows from the templates; fences / hedges as walls) or the importer on a real suburb with such a strip. ⭐ Make the houses ENTERABLE (≥ 1 door — your
   BE-3 rule) and the gardens open, so defenders CAN use cover — the test is whether they do.
3. A `preview.jpg` by `scripts/thumbnails.py`; no other image in git.

Priority: unchanged (next, before BE-4). ⚠ CE-4665 (infantry stop after a reload) was also seen on real-1: when the corridor terrain exists, load
the scenario four times in one editor and count movers — a NOTE to behaviors if it reproduces.
