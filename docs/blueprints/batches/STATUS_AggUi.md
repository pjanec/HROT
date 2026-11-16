<!--STATUS
state: LIVE
updated: 2026-11-16
current-answer: "Slices" then "Gates"
related-designs:
  - ../../designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md — OWNS this lane's UI design (mockup, inventory, UML, UI-1…UI-9)
  - ../../designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md — OWNS the stochastic unit and the §23 dispatch this lane builds the agg-ui column of
-->

# STATUS — exp/agg-ui: the stochastic unit's visuals and authoring (CE-8600…)

Handoff [HANDOFF_Coordinator_To_AggUi.md](HANDOFF_Coordinator_To_AggUi.md), dispatched at `3f33c0ee3`. Branch **`exp/agg-ui`**, created from `origin/coordinator`
(`24134926b`), started marker pushed. ⛔ Never merged into `coordinator` / `ui` (the user decides).

**Binding:** at start (later merged — see CE-8603) `exp/agg-backend` and `exp/agg-behaviors` did NOT exist (measured `git fetch`, 2026-11-16) and `AggregateUnit` / `UnitSlotTable` are only
reserved ids (`GlobalComponentIds.cs:1351-1356`) ⇒ every slice that draws the unit's own state waits for the contract; what does not was built.

## Slices

| slice | id | state | what |
|---|---|---|---|
| mockup (R-4143) + UI design | CE-8600 | ✅ | [DESIGN_Stochastic_Unit_UI.md](../../designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md) — the picture, INVENTORY (graph re-indexed), three UML diagrams, UI-1…UI-9 with leans; `agg-ui-visuals.svg` by `scripts/gen-agg-ui-mockup.py` |
| ST-0 strength / ammunition rows | CE-8601 | ✅ built | the Unit tab's Strength (men present / authorized + the SITREP colour, `UnitStrength`) and Ammunition (`RoundsOf` / `AmmoOf`) for EVERY unit — a stochastic unit shows once `UnitStrength` reads its slot table (behaviors), with no UI change |
| ST-1 footprint geometry | CE-8602 | ✅ built | `UnitFootprint` (rectangle, front arrow, march-column segment) — pure, 0 B |
| ST-1 / ST-3 the map gizmo (UI-2, UI-5, UI-6) | CE-8603 | ✅ built | `exp/agg-backend` merged (`64255766d`); `StochasticUnitGizmo` — footprint / column band, frame + echelon, strength label, loss mark; fidelity frame rendered and the mockup updated to the as-built (design §5) |
| O-1 split cards · UI-10 gauges · UI-11 the unit card | CE-8605 · CE-8606 · CE-8607 | ✅ built | design §5 rows; frames compared with mockups ① and ⑥ (deviations folded into §5) |
| ST-0 Resolution section (UI-1) | — | ⏳ **finding to agg-backend**: `AggregateUnit` fields need attribute-vocabulary rows (their component and wire) | |
| ST-2 overlay (UI-8) · ST-3 Slots tab (UI-7) · ST-4 picture · ST-5 request + progress (UI-9) | — | ⏳ wait for their engine slices | |

**Lane decisions (for the user's review):** UI-1…UI-9 in the design §4 are the leans; the ones built here are UI-3 (one strength reader for every unit — R-8600)
and UI-4's geometry (a column is a band along its path — R-8601). What would change each is in the design's table.

**Ids allocated:** CE-8600…CE-8615 (Area AGG-U in the tracker) · R-8600, R-8601, R-8602 (the user's approval of DESIGN_Unit_Symbols + O-1).

**Build order (approved `2026-11-16`):** CE-8605 split Labels → CE-8606 gauges → CE-8607 the unit card → CE-8608 symbol core → CE-8609 deriver + TKB → CE-8610 the NATO row → CE-8611 ORBAT icons → CE-8612 Slots tab → CE-8613 overlay. Each visual slice: its frame rendered and compared with the mockup before it is reported (R-4146).

## Gates *(rule 8 — the gate-report contract)*

| gate | command | result |
|---|---|---|
| T-1 the Unit form's own suite | `xvfb-run -a dotnet test Hrot.Editor.AiShared.Tests --no-build --filter TheUnitFormDrawsTests` | **19 / 0** (18 before + `CE8601_…`); ⚠ first run 18 / 1 — my rows came first and `FieldIdx("Strength")` found them, so they moved after the man's Health row (not a test change) |
| the footprint rail | `dotnet test Hrot.Presentation.Tests --no-build --filter UnitFootprintTests` | **2 / 0** |
| red-proofs (each compiles) | `UnitFacts.IsUnit` answering false ⇒ `CE8601_…` red · `UnitFootprint.Forward` with sin / cos swapped ⇒ `AUnitHeadingEast_…` red; both green again restored | ✅ |
| 0 B steady | asserted in both rails (`GC.GetAllocatedBytesForCurrentThread`) | ✅ |
| build | both TEST projects built after `dotnet restore` (the coordinator merge — trap 3) | 0 errors |
| Mermaid | `scripts/mermaid-check.mjs DESIGN_Stochastic_Unit_UI.md` | 3 / 3 parse |
| the gizmo rails | `xvfb-run -a dotnet test Hrot.Presentation.Tests --no-build --filter StochasticUnit\|UnitFootprint\|CommandDisplayGizmoTests` | **12 / 0** (the neighbouring command-display gizmos included); red-proof: the resolution guard inverted ⇒ the 3 new rails red |
| CE-8605 split cards | `DebugTraceGizmoTests` (+`CE8605_…`), SimHost `LayerControlGizmoTests` | **54 / 0** (with the gizmo rails) · **19 / 0**; red-proof: `CardLayerOf` → Labels ⇒ red |
| CE-8606 gauges | `TheUnitFormDrawsTests` (+ pixels, + `CE8606_…`), `Fdp.Presentation.Tests` Forms | **20 / 0** · **7 / 0**; red-proof: the vehicle count summing Count ⇒ red |
| CE-8607 unit card | `UnitCardGizmoTests` (2), `StochasticUnitLookFrameRail` (2, real frames), + Debug/Command/Map3D gizmo + `Map3DFrameRail` + shared-layers rails under `xvfb-run -a` | **127 / 2**: `CE4388` (the wire-sphere speed ratio — red earlier this session too) · `E2b_V_ARandomTerrain_TheRoadsStandOnTheHills` ("worst road vertex 1.13 m off the ground") — ⚠ deterministic, touches no file of this lane (terrain roads); ⛔ NOT proven at the base sha — a finding for the terrain owner. Red-proof: `WorstAlarm` ignoring broken men ⇒ red |
| n/a | `HROT_PARALLEL_SIM` / `READ_CHECK` / `WORLD_GUARD`, `stepped-hash.py`, `BrainReads`, recorded-component size | no per-entity step, no new component, no sim read — UI only |
