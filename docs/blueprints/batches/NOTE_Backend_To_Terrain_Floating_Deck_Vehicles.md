<!--STATUS
state: LIVE — a NOTE (protocol §5): a finding in terrain's code, with the measurements · updated: 2026-11-25 · current-answer: the whole file
related-designs:
  - ../../designs/engineering/DESIGN_Bridge_Laying.md — §11 (BL-S1/S2 as built, terrain), §12f (BL-S3/S4 as built, backend)
-->
# NOTE — `backend` → `terrain`: the laid deck in the navmesh — ① vehicles never use the FLOATING deck in the editor (CE-4696) · ② a man walks the river bed along a deck's edge (CE-4695)

| | |
|---|---|
| what blocks | CE-4693's `bl-floating-bridge` (BL-S4): the floating engineer lays its 72 m span and it opens, but the column never leaves its start |
| the probe | one scenario on basic-desert, every deck PLACED in the file (no laying involved): a 72 m and a 14 m FLOATING deck (8826, 0.6 m high, 4.5 m wide), a 14 m ASSAULT deck (8825, 1.2 m, 4 m) |
| ✅ | men march across the 72 m floating deck (editor) · an M1 crosses the 14 m assault deck (editor) |
| ⛔ editor (the regression's live load; threaded AND serial) | an M1 ordered across the 14 m floating deck is routed 80 m east ONTO THE ASSAULT DECK and ends `FailedBlocked` there — `SimTransform` (160.05, 300.86, 1.2), `NavigationStatus.Result = FailedBlocked` |
| ✅ `--mode all` cluster | the same order: `Arrived` at (79.9, 340.4), over the floating deck |
| ⇒ | the 8826 deck is in the cluster's vehicle navmesh and not in the editor's. The difference from the assault deck: 0.6 m high with a 2.4 m ramp (`RampLengthFor(0.6)`), 4.5 m wide |
| ⛔ not measured | which bake input differs between the two hosts |
| the ask | find and fix it in the deck's vehicle bake; `bl-floating-bridge` is the rail (`scripts/scenario-regression.sh bl-floating-bridge`) |

## ② the same family, for MEN — CE-4695 (measured live, `bl-assault-crossing`, the 14 m assault deck laid at run time)

| | |
|---|---|
| what | 'Alpha Man 3' of 8 crossing walks the RIVER BED beside the deck: his path runs along **x = 78.000 — the deck's west edge** (deck x 78–82), y 57 → 65.9, with velocity x exactly 0, and his height stays **z = 0** |
| how he got there | crowded at the ramp FOOT, he stood beside the ramp at ground level (x 77.4–77.9, y 55–58); from there the path reaches the deck in PLAN along its edge without climbing the ramp, and the walker keeps the floor nearest his own height |
| the lean | the deck's walkable polygons enter the men's navmesh un-eroded (a path string-pulled ON the edge); eroding them by the agent radius keeps a path ~0.3 m inside and up the ramp |
| the rail | `scripts/scenario-regression.sh bl-assault-crossing` — row "no man stands in the river off the deck" |
