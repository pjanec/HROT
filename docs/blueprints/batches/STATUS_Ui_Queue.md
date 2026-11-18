<!--STATUS
state: LIVE — running HANDOFF_Coordinator_To_Ui_Bug_Queue.md under PROTOCOL_Autonomous_Lanes.md
updated: 2026-11-18 10:45 UTC
current-answer: §1
-->

# STATUS — ui, the queue

self-wake-up: `ui self-wake-up` (trig_0141R92G1qK6SHY6mh3nnMms, hourly) — KEPT (R-4292); its message is §1a's text verbatim (set 2026-11-18 12:04).

## 1. Now
IN PROGRESS: CE-2438 (row 2, interleaved) — paused-preview + run-to does not repeat on pc-defend-house. Measured so far: the 2 s pair is EQUAL; F-3 (stepped never abandons a module) predates the report; a 60 s pair was CONTAMINATED by a mid-run hot reload (a build rewrote the runner's Hrot.AI.Behaviors.dll) — a candidate cause; rerun with no build running.

## 2. The queue (in order)

| row | state | sha | rail | scenario |
|---|---|---|---|---|
| CE-1133 RTS right-click orders deselected units | NOT REPRODUCED (original); the RTS right-click found BROKEN at the merge (Convoy took the ground-click gesture) — FIXED under CE-1978 | (this push) | `RtsCommandToolTests` 13/13 incl. `CE1133_EachClickGesture_…` (red-proved) | n/a (editor input) |
| CE-1954 press on an unselected unit drags it (threaded) | DONE — DECIDED A: a plain left body press drives the unit's move gizmo in the one routing (both editors) | (this push) | `DataDrivenGizmoSystemRoutingTests.CE1954_…` (red-proved); gizmo 21/0; Presentation drag/selection 61/0 | n/a (editor input) |
| CE-1470 gizmo buffer overflow drops primitives | DONE — grows after an overflowing frame; drops reported in [FrameStats] | (this push) | `DebugPrimitiveBufferLayerTests.CE1470_…` (red-proved); Diagnostics.Contracts.Tests 67/0; Presentation gizmo suites 415/0 | n/a |
| CE-1955 three Presentation reds (five with S1 + LR1) | DONE — all stale tests vs other lanes' deliberate changes | (this push) | Map3DTests · EffectTests · TerrainAddEntityFrameRail 286/0 | n/a |
| CE-1298 FPS bare-hand fire does nothing | DONE — empty hands: SIGHT (no lock box) and the press strikes the crosshair's unit (`Melee.Strike`); Entity Control §8s | `640d30871` | `EmbarkNodesTests.CE1298_…` (red-proved); SimHost EmbarkNodes 28/0; Toolkits fire-mode 153/0; PossessionStation 83/0 | n/a (no scenario holds a possessed bare hand) |
| CE-1283 Debug this unit on a commander | DONE — ①③ superseded (CE-1317 / CE-1329); ② one global "Observe all brain units" switch (DBG-2) | `640d30871` | `UnitDebugSettingsTests.CE1283_…` (red-proved); Editor brain routes 7/0 | n/a |
| CE-1067 remote terminal panel → global gizmo | CLOSED — DECIDED A: not a defect today | (this push) | n/a | n/a |
| CE-1239 .ogg clip missing from a film | DONE — `OggReader` (raylib LoadWave) for the track | `28ce2fa8a` | `AudioTests.CE1239_…` (red-proved); Audio + FilmDirector 80/0 (3 skips pre-existing) | n/a |
| CE-1241 15 seeded takes the scout cannot complete | PARTLY — 54/58 found (was 40/55); cc-platoon-bound fixed; the 4 left are scenario content (note to behaviors; casualty-care / police-line need the beat in the scenario) | (this push) | `scripts/director-check.py scout-all --headless` ×2 | the five scenarios' regressions: 3 PASS, 2 FAIL (behaviors) |
| CE-1280 Brain tab: why-this-choice one line | DONE — superseded by CE-1325 W5 (one line per decision by name, the sheet inside) | `b461d8fca` | `UtilityDecisionViewTests.CE1325_*` (existing) | n/a |
| CE-1281 Brain tab: Recent changes scroll / menu | DONE — 8-row scrolling grid, row menu Open graph of … / Copy | `b461d8fca` | `TheBrainViewTests.CE1281_…` (red-proved); TheBrainViewTests 26/0 (xvfb) | n/a |
| CE-1286 (taken while dry) a C# behaviour's fields in the Watch | DONE — DECIDED A: section `Occurrence`, read by the slot reader, written by R-1041's writer; Add to Watch in the dump | (this push) | `TheBrainViewTests.CE1286_…` (red-proved); AiShared Watch/Variable/Brain 597/0; Editor live-value 20/2 — the 2 reds (`LiveBlackboardValueProviderTests.CE1412_…`, `CE1430_…`) expect the shipped SOP's sense list WITHOUT `AirSense`, which CE-7029 (another lane, already in the tree) added | n/a |
| CE-1979 (§7, ui's own find) threaded map faults on a dead entity | DONE — `SyncFrom` moves the structural clock when the source's structure moved | (this push) | `EntityRepositorySyncTests.CE1979_…` (red-proved); Fdp.Core 6 reds are source scans flagging other lanes' files (env names, a shared fixture component id, JSON options, wall clock); ModuleHost snapshot/sync 61/0; Toolkits gizmo/gate 242/0 | `bt-window-duel`: 0 faults (was 20) |
| CE-1147 (§7) stance replicates like every one-owner datum | SKIPPED — needs decision (`DECISIONS_Ui.md`: the network pack cannot see the stance types; lean A: NED references the animation replication) | | | |
| CE-1246 Stride mode 2 host actions | DONE — node `HostActions` + shared `HostActionsPumpSystem`; inspector + Details get them | `8952f8c3d` | `GlobalActionDispatchSystemTests.CE1246_…` (red-proved) 4/0; `HrotStrideApp.Game` builds (restored first) | ⚠ not run on a Stride window (Windows only) |

### Queue 2 — HANDOFF_Coordinator_To_Ui_Moved_Rows (from backend)

| row | state | sha | rail | scenario |
|---|---|---|---|---|
| CE-3468 threaded scoring root sees no scored children | DONE | the editor's loaded AI copy never contributed its rules tree to the phase (AiAssemblyLoad hook) + the last-tick view's same-version miss | AiHotReloadCoordinatorTests.CE3468, PerEntityRuleTests.CE3468, ScenarioRulesTests.CE3468 (red-proved); bw-archery-contest 5/5 (2 new rows), sr-objectives-march, sr-triggers-march pass; 2 goldens recaptured (had pinned the bug) | DESIGN_Parallel_Simulation §18 |
| CE-2438 paused-preview + run-to does not repeat (pc-defend-house) | TODO | | | |
| CE-1473 + CE-3451 ClusterRunner.Tests does not build; six rails drifted | 6 / 9 DONE, ②③④ SKIPPED → DECISIONS_Ui | it builds; 9 reds measured (6 stale ledgers / expectations / declarations + 3 world-boundary audits grown to 21 systems / 480 fields / 45 caches) | ClusterRunner.Tests 434 / 437 (the 3 = ②③④); touched suites: Toolkits mines/embarked/convoy/aggregate/air 149, SimHost mine/resolution 34, Core shared registry 4; mf-strike-and-halt, cv-blocked-road | DESIGN_Replay_Time_Axis §12l table |
| CE-1475 SumoNetPlacementTests F5/E1 on a Czech Windows | DONE | the fixture wrote culture commas; one invariant helper | Traffic SumoNetPlacementTests 19/19 + a cs-CZ rail (red-proved) | — (test only) |
| CE-1046 + CE-1038 LiveFromReplay under load; SimHost test count varies | CE-1046 PARTLY (frame waits; still 1/11 red under load, stays FlakyFact); CE-1038 TODO | | LiveFromReplayTests 3/3 ×4 alone, 10/11 under full load | |

## 3. Taken from new handoffs
- HANDOFF_Coordinator_To_Ui_Moved_Rows.md at 2026-11-18 15:20 (merged coordinator `fa5748b18`): CE-3468 · CE-2438 · CE-1473+CE-3451 · CE-1475 · CE-1046+CE-1038, in that order; §6a scenarios per row proportionate.
- `NOTE_Terrain_To_Ui_Obstacle_Drawing.md` (coordinator, 11:03 wake-up): its question MEASURED — all seven cultural-feature obstacles were drawn twice (an unknown box over the terrain box); fixed in `EntityBodyLayer3D.LookOf` (no-body look). Rail `Map3DTests.CE4656_…` (red before). Map3DTests + frame rails + ObstacleRow 287/3: `CE4388` wire-sphere (known, CE-1978), `E2b_V` (red at base too), `CE4582` (green alone with the fix, order-dependent in the suite).
- HANDOFF_Coordinator_To_Ui_Bug_Queue.md at 2026-11-18 09:00 (merged coordinator `5c0…` into ui)

## 4. Notes sent / received
- sent `NOTE_Ui_To_Convoy_Order_Gesture.md` — ConvoyOrder's GroundClick gesture removed (cross-lane), letter V

## 5. Gates (rule 8)
- CE-1133/1978: `Hrot.Presentation.Tests` RtsCommandToolTests · TheMenusAreGolden · MenuOrdersTests · Keymap · OrderMenu 63/0; `Hrot.Editor.Tests` OrderVocabularyTests 10/0. Goldens re-captured: only order ids + Convoy rows.
