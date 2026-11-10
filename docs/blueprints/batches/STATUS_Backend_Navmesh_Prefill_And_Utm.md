<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: the table below
stale-below: nothing
related-designs:
  - HANDOFF_Coordinator_To_Backend_Navmesh_Prefill_And_Utm.md — the frame (dispatched at bedca0325)
  - ../../DESIGN_Terrain_Import_UTM.md — the owning design; the details go into its §12
-->

# STATUS — backend: the navmesh baked at import (CE-4393) and `UtmTransform` (CE-4394)

Started at `a2c6f6fa0` (merged `coordinator@4b1cf15c5`), marker `639e54cc4`. ⚠ The off-frame egress (CE-3427 N-1) is PAUSED for this, at
`bcff5a430` — its resume point is RESUME_Backend_Large_Terrain_Perf.md §0.

| step | state |
|---|---|
| inventory (graph + grep) — navmesh bake / tile cache, the geo seam and heading sites | ⏳ running |
| design — UTM §12 (UML, INVENTORY, leans N-1…N-4, U-1…U-4) | ⏳ |
| build N | ⏳ |
| build U | ⏳ |
| gates + as-built | ⏳ |
