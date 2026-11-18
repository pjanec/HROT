<!--STATUS
state: LIVE — dispatched — ⭐ UNATTENDED, under PROTOCOL_Autonomous_Lanes.md · updated: 2026-11-18 · current-answer: the whole file
related-designs:
  - STEER_Coordinator_To_Backend_Rows_Moved_And_Scenarios.md — why these rows moved
  - ../../designs/thermal/DESIGN_Thermal_Drivers.md §4b — the ground heat marks (CE-4664)
-->
# HANDOFF — coordinator → `terrain`: three rows moved from backend

🔒 User, `2026-11-18`: *"terrain and ui seems idle; anything for them? … the other lanes might take some of its tasks?"*. Protocol as before, plus the
new §6a (scenarios per row are proportionate, R-4294). The tracker row is the truth for each.

| order | row | what | done when |
|---|---|---|---|
| 1 | **CE-4664** (TD-S8) | ⭐ the GROUND HEAT MARKS — `GroundHeatMarks` cell record (the crater shape, `ICellRecord<T>`; component **493**, descriptor ordinal **242** — fixed at dispatch, see the backend thermal handoff's id table and its ⚠ on the test-local struct using 493), its translators in the shared pack, `MarkWarmth(mark, now, weather)` (the design's `ThermalDriversOps` — put it where backend's calculator will live, or a NOTE to agree), and a WRITER ENTRY POINT backend's `HeatSourceSystem` calls when a stand ≥ ~60 s with the engine on ends. ⛔ never drawn in the normal views | rails: a mark written on one node is on every node, a late joiner included; it cools over its `PowerClass` time. The scenario `th-vehicle-left-warm-spot` waits for backend's CE-4658 — leave it a NOTE with the entry point |
| 2 | **CE-3471** | a pig walked through its pen's brick wall (`farm-under-fire`) | its scenario passes; a rail at the wall |
| 3 | **CE-3424** | scale-1: at 1 000 men only 1 of 12 sampled movers moved | measured cause first (navmesh / path budget / brain); the scale rig's mover share as a check |

Design for CE-4664: [`DESIGN_Thermal_Drivers.md`](../../designs/thermal/DESIGN_Thermal_Drivers.md) §4b, TD-14 (approved, R-4293). Fold any deviation back into it.
