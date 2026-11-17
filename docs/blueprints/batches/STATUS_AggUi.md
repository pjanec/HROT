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

## Sync — R-4286 (the agg lanes merge each other)

⭐ `2026-11-17`: merged `coordinator@2154f2a2a` (`7b3f6eec9`), `exp/agg-backend@2de034f22` (`4edc96ae9`), `exp/agg-behaviors@13f498aae` (`90d8fbe5f`);
test projects restored. Conflicts were neighbouring lines (ownership groups, descriptors, translator pack, pack-count pins, ledgers) — both sides kept.
Follow-up `63117180f`: the unit card reads `UnitStrength` / `UnitAmmunition.TryShare` (RV-5 / R-8004 landed — its own slot-table branch removed);
the Brain descriptor lists gained `dtMineClearing` / `dtRamOrder` (missing on every branch: red on coordinator too).
⚠ **Not this lane's, reported:** `NedTkbBuilderCombatTests.CE3281_…` (the air lane's F-16C / MiG-29 fire unheard) and `CE4621_TheMotorcycle_…`
(expected 2, got 3) — both from coordinator's air content, red on the merged tree, no file of this lane.
⭐ **Agg scenarios re-run on the merged tree** (`scripts/scenario-regression.sh` over all 20 `agg-*`): **20 of 20 pass**, goldens unchanged.

## Slices

| slice | id | state | what |
|---|---|---|---|
| mockup (R-4143) + UI design | CE-8600 | ✅ | [DESIGN_Stochastic_Unit_UI.md](../../designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md) — the picture, INVENTORY (graph re-indexed), three UML diagrams, UI-1…UI-9 with leans; `agg-ui-visuals.svg` by `scripts/gen-agg-ui-mockup.py` |
| ST-0 strength / ammunition rows | CE-8601 | ✅ built | the Unit tab's Strength (men present / authorized + the SITREP colour, `UnitStrength`) and Ammunition (`RoundsOf` / `AmmoOf`) for EVERY unit — a stochastic unit shows once `UnitStrength` reads its slot table (behaviors), with no UI change |
| ST-1 footprint geometry | CE-8602 | ✅ built | `UnitFootprint` (rectangle, front arrow, march-column segment) — pure, 0 B |
| ST-1 / ST-3 the map gizmo (UI-2, UI-5, UI-6) | CE-8603 | ✅ built | `exp/agg-backend` merged (`64255766d`); `StochasticUnitGizmo` — footprint / column band, frame + echelon, strength label, loss mark; fidelity frame rendered and the mockup updated to the as-built (design §5) |
| O-1 split cards · UI-10 gauges · UI-11 the unit card | CE-8605 · CE-8606 · CE-8607 | ✅ built | design §5 rows; frames compared with mockups ① and ⑥ (deviations folded into §5) |
| the NATO symbol core (UI-12, S-5/S-6) | CE-8608 | ✅ built | `SymbolCode` + `SymbolGeometry` (`0cf2b4bb7`, self-contained) · `SymbolRenderer` + the `MilStd2525` / `Icon` routes (`36d59c2b1`, self-contained) — R-8603: a per-symbol texture cache (500 symbols 40 ms → 0.08 ms) |
| the derivation (UI-12, S-1…S-3) | CE-8609 | ✅ built | `UnitSymbolDeriver` (Hrot.Core, `2f3d9da0b`) — from the unit type's composition; `MapVisualDef.SymbolRole` override |
| the NATO row (UI-14, P-2/P-3) | CE-8610 | ✅ built | layer bit 31; the card's left cell; standalone by echelon band (`MinMetresPerPixel`); the selection's children (`0278bed57`) |
| the ORBAT icons (UI-13) | CE-8611 | ✅ built | unit ⇒ its symbol, man ⇒ role, vehicle ⇒ class (`23942af56`, self-contained) · a stochastic unit's slots as rows (`5d7c5e973`) |
| the Slots tab (UI-7) | CE-8612 | ✅ built | `SlotsForm` (`724b81033`) — after merging `exp/agg-backend` again (`6876428b8`: ST-2…ST-5 + CE-8015's attribute rows) |
| the detection overlay (UI-8) | CE-8613 | ✅ built | `DetectionLevelsGizmo`, layer bit 32 off by default (`3bf688b49`) |
| the Resolution section (UI-1) | CE-8614 | ✅ built | the Unit tab's section through CE-8015's rows (`2386a6b12`); ⚠ frontage / depth / heading read-only — a finding for agg-backend (no attribute rows) |
| the request + progress (UI-9) | CE-8615 | ✅ built | the Resolution › menu writing `Aggregate.Pin`; the card's `SPLIT 2/3` / `MERGE`; held men as dashed rings; the Unit tab's Changing / Stage / Progress rows |

**Lane decisions:** ✅ UI-1…UI-9 APPROVED `2026-11-16` (R-4278 — [NOTE_Coordinator_To_All_Lanes_Decisions_Approved](NOTE_Coordinator_To_All_Lanes_Decisions_Approved.md)); ⭐ the NATO symbols (UI-12…UI-14, CE-8608…CE-8611) go in SELF-CONTAINED commits — the symbol geometry, the `MilStd2525Renderer` route, the ORBAT icon — so `ui` can take them on their own when the user says; the leans were the ones built here are UI-3 (one strength reader for every unit — R-8600)
and UI-4's geometry (a column is a band along its path — R-8601). What would change each is in the design's table.

**Ids allocated:** CE-8600…CE-8615 (Area AGG-U in the tracker) · R-8600, R-8601, R-8602 (the user's approval of DESIGN_Unit_Symbols + O-1), R-8603 (the symbol texture cache, measured) · action ids 2440–2444 (Resolution ›).

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
| CE-8608 symbol core | `SymbologyTests` (Contracts), `SymbolRendererFrameRail` (Xvfb), `GizmoMap.Presentation.Tests` | **18 / 0** · **1 / 0** · **47 / 0**; red-proofs: the friend frame as a diamond · no north-up row flip |
| CE-8609 deriver | `UnitSymbolDeriverTests`, `Hrot.Core.Tests` | **3 / 0** · **383 / 0**; red-proof: the carrier rule dropped |
| CE-8610 NATO row | `UnitSymbolGizmoTests`, `UnitSymbolFrameRail`, `StatelessGizmoSystemTests`, `GZH011` | **4 / 0** · **1 / 0** · **15 / 0** · **18 / 0**; red-proofs: platoon band → squad · rows not shifted past the cell |
| CE-8611 ORBAT | `TheSharedOrbatPanelDrawsTests` (Xvfb) | **6 / 0**; red-proofs: the unit branch → `orbat/unit` · `SlotCountOf` → 0 |
| CE-8612 / 8614 / 8615 forms | `TheUnitFormDrawsTests` (Xvfb) | **23 / 0**; red-proofs: `FlagOf` never Down · `IsAggregate` false · `MenCreated` counting every child |
| CE-8613 overlay | `DetectionLevelsFrameRail` (Xvfb), `GZH011` | **2 / 0** · **18 / 0**; red-proof: `StateOf` ignoring Reported |
| CE-8615 menu / card | `MenuOrdersTests`, `UnitCardGizmoTests`, the menu goldens (`TheMenusAreGoldenTests`, `MapEntityMenuHandlerTests`) | **59 / 0** (+4 display-only skipped headless, run under Xvfb above); red-proofs: `PinFor` → null · the ring for a released man |
| after merging `exp/agg-backend` (`6876428b8`) | restore + build of Core / Presentation / Editor / AiShared tests; this lane's rails re-run | Core **7 / 0** · Presentation **38 / 0** · Editor **6 / 0** · AiShared **21 / 0** |
| n/a | `HROT_PARALLEL_SIM` / `READ_CHECK` / `WORLD_GUARD`, `stepped-hash.py`, `BrainReads`, recorded-component size | no per-entity step, no new component, no sim read — UI only |
