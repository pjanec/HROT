<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: §1 the conformance table · §2 the gates
stale-below: nothing
related-designs:
  - ../../DESIGN_Terrain_From_OSM.md — §6 / §6a the approved mockup, §6b the wizard's design (classes, states, modules) and its as-built ⚠ list
  - HANDOFF_Coordinator_To_Ui_Osm_Wizard.md — the dispatch (C-1 … C-15, W-1 … W-5)
-->

# STATUS — the terrain-from-OSM wizard (ui, CE-4375)

Handoff: [HANDOFF_Coordinator_To_Ui_Osm_Wizard.md](HANDOFF_Coordinator_To_Ui_Osm_Wizard.md) (dispatched at `f473cab3a`; started at `c53ea3ceb`).
Design: [DESIGN_Terrain_From_OSM.md](../../DESIGN_Terrain_From_OSM.md) §6b. Mockup: `docs/UX/terrain-osm-wizard-visual.svg`, `docs/UX/terrain-osm-preview-visual.svg`.

| item | state |
|---|---|
| W-1 design (INVENTORY, classDiagram, stateDiagram, module graph) | ✅ §6b |
| W-2 fakes (the mockup's `world()` in metres, a holdable pipeline, the search, painted PNG tiles) | ✅ `Hrot.Presentation.Tests/TerrainOsm/OsmWizardFakes.cs` |
| W-3 the wizard + File ▸ New ▸ Terrain from OpenStreetMap… (greyed: no pipeline yet) | ✅ `Hrot.Presentation/TerrainOsm/*`, `ScenarioMenuCommands`, `EditorSubsystem` |
| W-4 rails | ✅ `OsmWizardModelTests` (10) · `TheOsmWizardMatchesItsMockupTests` (6 frame rails, xvfb) |
| W-5 fidelity pass | ✅ four states rendered beside the mockup, differences fixed, the rest below as ⚠ |

## 1. The conformance table

⭐ "rail" = the asserting frame rail in `TheOsmWizardMatchesItsMockupTests` (measured in mockup units: (screen − window) ÷ s).

| row | | rail / evidence | ⚠ the difference, and why |
|---|---|---|---|
| **C-1** window | ✅ | `Frame()` — 1200 × 600 on every step; the item in `File/New` right after Scenario… (`FileMenuLayout.InOrder`, `ScenarioMenuTests`) | ⚠ **one size for all four steps** — the mockup draws Options (330) and Import (470) shorter to save page space; C-1 asks for the SAME window, so it keeps one size. ⚠ **greyed item says why in its LABEL** — *"Terrain from OpenStreetMap… (unavailable — the pipeline is not built yet)"*: the shell's menu items carry no tooltip; the label is the house way (VC-3, the same as Load Scenario… / New Scenario…). Not on the Start surface (it draws every Idle item as a button; the mockup scopes the menu only — R-4151) |
| **C-2** step bar | ✅ | `Frame()` — chip fill / border / state per step, top 30, height 24, left 12, joined left to right; done = `CHECK` in `OK` | — |
| **C-3** regions | ✅ | `Canvas()` — canvas (10, 64) 830 × 476, right panel at 852; the footer separator at 558 | ⚠ **Fit centres the box** in the canvas; the mockup draws it off-centre (150 … 600 × 140 … 470). Same size share (≈ 70 % of the height) |
| **C-4** footer | ✅ | `Frame()` — Cancel right-aligned at 1180, primary before it, Back before that (absent in Area / Import); glyph colours: Next `TXT`, Preview `EYE` teal, Import `CHECK` green, greyed ⇒ `DIS` | — |
| **C-5** map controls | ✅ | `Canvas()` — three square buttons top-left; attribution strip bottom-right (dimmed in Preview) | — |
| **C-6** search | ✅ | `Area_MatchesTheMockup` — field at canvas + (46, 10), 330 × 24, right of the zoom buttons, `SEARCH` in `SUB`; results 23 px rows, `LOCATION` red, name `TXT`, kind `NOTE` | — |
| **C-7** the box | ✅ | `Area_*` — 8 handles, the chip above the top edge; > 6 km ⇒ chip border `RED`, Next greyed | — |
| **C-8** Area panel | ✅ | `Area_MatchesTheMockup` — Width · Height · Centre · UTM zone fields in ONE column at 852 + 80; the info row at 852 with `INFO` | — |
| **C-9** Options | ✅ | `Options_MatchesTheMockup` — name field 170 / 390 wide, `CIRCLE_CHECK` green; the four Include glyphs in their colours in one column at 50, rows 28 apart; right column fields aligned at 770; the amber row (620, 224) 540 wide, `WARNING` amber, Preview greyed until Replace | ⚠ **the name check shows `WARNING` amber (not the green tick) while the name exists** — the mockup draws both at once only to show both cases; its own note says "green tick = new, amber row = exists" |
| **C-10** Preview map | ✅ | `Preview_MatchesTheMockup` frame — tiles dimmed, data clipped to the box, dashed `ACC` box; buildings by `TerrainPlan2D.HeightColor`, solid grey; woods / scrub / water / walls / roads by class, rail dashed | ⚠ **no lone trees drawn** — the contract has `Counts.LoneTrees` but no positions (`PreviewLine` "tree_row" only) |
| **C-11** Layers | ✅ | `Preview_MatchesTheMockup` — panel top-right of the canvas, 204 wide, ten swatches | — |
| **C-12** hover | ✅ | `Preview_MatchesTheMockup` — the mouse on a building's centroid ⇒ `Hovered` is it, outlined white, tooltip beside it with its id | ⚠ **the tooltip says id · enterable/solid · storeys · ≈ height** — the mockup's type, roof, door rule, window count and shared walls are not in the contract's `PreviewBuilding` |
| **C-13** Preview panel | ✅ | `Preview_MatchesTheMockup` / `Preview_TheVerdictBox_ReadsHeavyAndRefused` — six rows, glyphs in colour, values right-aligned at 1190; three bars at 926 … 1190, 14 high, zones at ⅔ / ⅚, marks 1× · 2× · 3×; verdict box at 354, 28 high: Ok green, Heavy amber, Refused red + Import greyed; note glyph by severity | ⚠ **notes are not clickable** and the header reads "Notes (n)" — the contract's `Warnings` are strings with no location, so there is nothing to zoom to. ⚠ **severity from the words** (dropped / refused / failed / skipped ⇒ warning). ⚠ **the data line shows the attribution, not a timestamp** — the contract has no data timestamp |
| **C-14** Import | ✅ | `Import_MatchesTheMockup` — stage glyphs by state, bars at 270 × 300, rows 30 apart; the done box (780, 76) 400 wide; Run in background at 910; Cancel `XMARK` red; no importer ⇒ the input-set line; finished ⇒ Open in New Scenario (`FILE` blue) + Show folder (`FOLDER_OPEN` amber) | ⚠ **the stage rows follow the contract's `OsmStage`**: "Read the map data" (Parse) where the mockup has "Elevation (Copernicus 30 m)" — the elevation download is inside the pipeline's Heights stage. ⚠ **the right box shows only the state that is TRUE** — the mockup's inset illustrates both ends at once while running; the live box says "When it finishes" until it has |
| **C-15** behaviour | ✅ | `OsmWizardModelTests` — every slow step a `Task` + `CancellationTokenSource`, landed by `Pump` on the frame thread; Back / Cancel cancel the token; a failure names the stage and Retry runs again; tiles decode + upload on the frame thread only (`OsmTileTextures.Pump`) | — |

## 2. Gates

| gate | result |
|---|---|
| `dotnet test Hrot.Presentation.Tests --no-build --filter OsmWizardModelTests` | 10 / 0 |
| `xvfb-run -a dotnet test Hrot.Presentation.Tests --no-build --filter TheOsmWizardMatchesItsMockupTests` | 6 / 0 (ran — not skipped) |
| `dotnet test Hrot.Editor.Tests --no-build --filter ScenarioMenu\|FileMenu\|StartSurface` (the File menu tables) | 42 / 0 |
| red-proofs | see §3 |

## 3. Red-proofs (compiling mutations, one key rail per class)

| mutation (compiles; `0 Error(s)` checked before the run) | rail | result |
|---|---|---|
| A — `PrimaryRefusal` in Area ignores `BoxRefusal` | `OsmWizardModelTests.Area_NeedsABox_WithinTheLimit_ThenNextGoesToOptions` | 🔴 red (and `Area_ABoxOverTheLimit_…` frame rail red too) |
| B — the Water include glyph drawn `GREY` instead of `BLUE` | `TheOsmWizardMatchesItsMockupTests.Options_MatchesTheMockup` | 🔴 red: *glyph 'include:3': expected colour ⟨0.35, 0.66, 1⟩, drawn ⟨0.63, 0.63, 0.67⟩* |

Both reverted; the suites green again on a fresh build (§2).

## 4. Found on the way

- ⚠ **[CE-1468](Blueprint_Issues_Tracker.md)** — `OsmTileTextures` duplicates `PickerImageCache`'s LRU concept; not shared yet (a `null` load is "failed forever" there; `string` keys allocate per frame). Filed with the unification.
- ⚠ **The stale-binary trap, hit once and caught:** the merge of `coordinator` (`a5b802fe8`) moved `UtmFrame` → `Hrot.Terrain.InputSet` and `Utm` → `Fdp.Modules.Geographic.Transforms`; the pushed merge commit `e91d7e658` did not compile until `13f069be2`. A test run in between printed green from the old binary — not counted; every row in §2 is from a build whose `0 Error(s)` was checked first.
