<!--STATUS
state: LIVE — dispatched, not started (RE-STAMPED 2026-11-09, rule 1a: narrowed to the WIZARD only — the coordinator builds the library side, U-0…U-3, itself; first stamp d6c44f6cd never reached ui)
updated: 2026-11-09
current-answer: the whole file
-->

# HANDOFF — coordinator → ui: the editor's terrain-from-OpenStreetMap WIZARD (overnight, autonomous)

**For:** ui. **From:** coordinator. **Dispatched at `afd602395`** (re-stamped; first `d6c44f6cd`), scope frozen there. Start it when
[`Next_3`](HANDOFF_Coordinator_To_Ui_Next_3.md) is done or every remaining item there is blocked. Merge the other lanes first (⭐ **`coordinator`
every item** — the real pipeline lands there during the night); push `chore: started HANDOFF_Coordinator_To_Ui_Terrain_From_OSM at <sha>`; status
file `STATUS_Ui_Terrain_From_OSM.md`. Same [protocol](PROTOCOL_Two_Lanes_Self_Coordinating.md). ⛔⛔ **Never stop for a question** (R-4156):
every decision below carries a lean — take it; a premise that fails stops THAT item only (R-106).

**Who builds what** (🔒 user, `2026-11-09`): the **coordinator** builds the library side — the input-set library, the shared `Utm`, the OSM
pipeline, heights and roads (CE-4371…CE-4374); **backend** builds the importer (CE-4368); **ui builds only the wizard** (CE-4375).

**⭐ The contract is already code** — build against it, never around it:
- [`Hrot/Engine/Hrot.Terrain.Osm/OsmContract.cs`](../../../Hrot/Engine/Hrot.Terrain.Osm/OsmContract.cs) — `IOsmTerrainPipeline`, `OsmPreview`,
  `OsmCounts`, `BudgetVerdict`, `OsmImportOptions`, `OsmProgress`, `ITileSource`, `IPlaceSearch`, `GeoBox`, `UtmFrame`
- [`Hrot/Engine/Hrot.Terrain.InputSet/ITerrainImporter.cs`](../../../Hrot/Engine/Hrot.Terrain.InputSet/ITerrainImporter.cs) — the importer
- the diagrams: [DESIGN_Terrain_From_OSM.md §4a](../../DESIGN_Terrain_From_OSM.md) (class + sequence); the steps: §6; the budget: §5.7; failures: §7
- ⚠ a contract change you need: make it in those files in a commit of its own, named in the status file — the coordinator follows on merge.
  ⛔ never a parallel type in the editor

| # | id | what | done when |
|---|---|---|---|
| **W-1** | CE-4375 | 🖼 **MOCKUP FIRST** (R-4143): `scripts/gen-terrain-osm-wizard-mockup.py` over `scripts/mockup_kit.py` → `docs/UX/terrain-osm-wizard-visual.svg`, four states: **Area** (tile map, search field, the drag box with its size "4.8 × 5.1 km", the 6 × 6 km limit shown, attribution) · **Options** (name, Buildings / Trees / Walls / Water toggles, storey height, default storeys) · **Preview** (the geometry drawn: roads by class, footprints coloured by storeys, woods / scrub / water, walls; the counts; warnings; the budget verdict — Ok green, Heavy amber with the reason, Refused red with "pick a smaller box") · **Import** (a progress row per stage, Cancel; on success "Open in New Scenario"; without an importer "Input set written to … — the terrain importer is not built yet"). Icons as coloured FontAwesome glyphs (R-4139). Commit it with a §6 caption naming its scope | the SVG + generator committed; §6 links it |
| **W-2** | CE-4375 | 📐 **the wizard's design** in OSM §6: a `classDiagram` (the wizard's step state machine, its view models, what it holds of the contract), a `stateDiagram-v2` of the steps (Area → Options → Preview → Import, back moves, cancel, failure + Retry), the module graph (who opens it, who composes the pipeline / tiles / search / importer) — an `INVENTORY` of the graph queries you ran for the reused parts (modal host, worker + cancellation, map drawing) | the diagrams parse (`scripts/mermaid-check.mjs`) |
| **W-3** | CE-4375 | **build it**: an ImGui modal through `WindowManager.RegisterFrameOverlay` (as the existing modals), opened from the **New** menu beside New Scenario. Fetch / preview / write / import on a WORKER with a `CancellationTokenSource`, only texture uploads on the frame thread (the Replay Browser search panel's pattern); never block the frame. **Area**: a small slippy-map widget of its own (not the scenario map): XYZ tiles from `ITileSource` decoded to textures (a small LRU of textures), pan / zoom, a drag box, `Measure` shown live, `IPlaceSearch` in the search field. **Preview**: draw `OsmPreview`'s geometry over the tiles (local metres → the box's pixels through `Frame`), as retained meshes where the 2-D terrain layer does (CE-1227). **Import**: refused while the verdict is `Refused`; the folder `<content>/terrain-input/<name>/`; then `ITerrainImporter` if composed. Composition: the editor passes the pipeline / tiles / search / importer it has — ⭐ **while no real `IOsmTerrainPipeline` implementation exists on the merged tree, the New-menu item is shown GREYED with the tooltip "Terrain from OpenStreetMap — the pipeline is not built yet"**; when it appears (the coordinator's `OsmTerrainPipeline`), compose it — ⛔ never a stub pipeline in production code | the item, the modal, the four steps working against a **fake pipeline + fake tiles** in tests |
| **W-4** | CE-4375 | **rails**: a headless test of the step state machine with a fake pipeline (every transition, Cancel mid-stage, a failure + Retry, a Refused verdict blocks Import, no importer ⇒ the "not built yet" end); ⭐ **fidelity** (R-4138 / R-4146): frame rails under `xvfb-run -a` render each mockup state with the fakes, measure alignment (controls at one x, one frame height) and assert the named glyphs / colours; render, compare with the mockup, fix, render again — the side-by-side PNGs to the scratchpad and the chat (⛔ never git), the deviations listed in the status file | all rails green under Xvfb; the side-by-side reported |

**Not in this batch:** the map background / imagery (OSM §8 — the user decides). **Order:** W-1 → W-2 → W-3 → W-4. Gates: the touched test classes
+ each new rail red-proved once; frame rails under `xvfb-run -a` (a skipped display rail is not a pass).

---
_coordinator lane, 2026-11-09_
