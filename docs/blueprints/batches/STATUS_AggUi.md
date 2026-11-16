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

**Binding:** `exp/agg-backend` and `exp/agg-behaviors` did NOT exist at start (measured `git fetch`, 2026-11-16) and `AggregateUnit` / `UnitSlotTable` are only
reserved ids (`GlobalComponentIds.cs:1351-1356`) ⇒ every slice that draws the unit's own state waits for the contract; what does not was built.

## Slices

| slice | id | state | what |
|---|---|---|---|
| mockup (R-4143) + UI design | CE-8600 | ✅ | [DESIGN_Stochastic_Unit_UI.md](../../designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md) — the picture, INVENTORY (graph re-indexed), three UML diagrams, UI-1…UI-9 with leans; `agg-ui-visuals.svg` by `scripts/gen-agg-ui-mockup.py` |
| ST-0 strength / ammunition rows | CE-8601 | ✅ built | the Unit tab's Strength (men present / authorized + the SITREP colour, `UnitStrength`) and Ammunition (`RoundsOf` / `AmmoOf`) for EVERY unit — a stochastic unit shows once `UnitStrength` reads its slot table (behaviors), with no UI change |
| ST-1 footprint geometry | CE-8602 | ✅ built | `UnitFootprint` (rectangle, front arrow, march-column segment) — pure, 0 B |
| ST-0 Resolution section (UI-1) | — | ⏳ waits for `AggregateUnit` + its attribute rows (`FormCatalogue.AttributeOf` throws on an unknown path) | |
| ST-1 the gizmo (UI-2, UI-5, UI-6) | — | ⏳ waits for `AggregateUnit` (the projector) | |
| ST-2 overlay (UI-8) · ST-3 Slots tab (UI-7) · ST-4 picture · ST-5 request + progress (UI-9) | — | ⏳ wait for their engine slices | |

**Lane decisions (for the user's review):** UI-1…UI-9 in the design §4 are the leans; the ones built here are UI-3 (one strength reader for every unit — R-8600)
and UI-4's geometry (a column is a band along its path — R-8601). What would change each is in the design's table.

**Ids allocated:** CE-8600, CE-8601, CE-8602 · R-8600, R-8601.

## Gates *(rule 8 — the gate-report contract)*

| gate | command | result |
|---|---|---|
| T-1 the Unit form's own suite | `xvfb-run -a dotnet test Hrot.Editor.AiShared.Tests --no-build --filter TheUnitFormDrawsTests` | **19 / 0** (18 before + `CE8601_…`); ⚠ first run 18 / 1 — my rows came first and `FieldIdx("Strength")` found them, so they moved after the man's Health row (not a test change) |
| the footprint rail | `dotnet test Hrot.Presentation.Tests --no-build --filter UnitFootprintTests` | **2 / 0** |
| red-proofs (each compiles) | `UnitFacts.IsUnit` answering false ⇒ `CE8601_…` red · `UnitFootprint.Forward` with sin / cos swapped ⇒ `AUnitHeadingEast_…` red; both green again restored | ✅ |
| 0 B steady | asserted in both rails (`GC.GetAllocatedBytesForCurrentThread`) | ✅ |
| build | both TEST projects built after `dotnet restore` (the coordinator merge — trap 3) | 0 errors |
| Mermaid | `scripts/mermaid-check.mjs DESIGN_Stochastic_Unit_UI.md` | 3 / 3 parse |
| n/a | `HROT_PARALLEL_SIM` / `READ_CHECK` / `WORLD_GUARD`, `stepped-hash.py`, `BrainReads`, recorded-component size | no per-entity step, no new component, no sim read — UI only |
