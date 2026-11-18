<!--STATUS
state: LIVE — running HANDOFF_Coordinator_To_Ui_Bug_Queue.md under PROTOCOL_Autonomous_Lanes.md
updated: 2026-11-18 10:45 UTC
current-answer: §1
-->

# STATUS — ui, the queue

self-wake-up: `ui self-wake-up` (trig_0141R92G1qK6SHY6mh3nnMms, hourly at :02) — KEPT: the user wants it recurring every hour (2026-11-18); its prompt now says never delete it, and on an empty queue to check the other lanes for new ui handoffs first.

## 1. Now
**idle** — the queue is done (2 rows await decisions in `DECISIONS_Ui.md`: CE-1954, CE-1067; plus CE-1286 taken while dry). Self-wake-up kept (user).

## 2. The queue (in order)

| row | state | sha | rail | scenario |
|---|---|---|---|---|
| CE-1133 RTS right-click orders deselected units | NOT REPRODUCED (original); the RTS right-click found BROKEN at the merge (Convoy took the ground-click gesture) — FIXED under CE-1978 | (this push) | `RtsCommandToolTests` 13/13 incl. `CE1133_EachClickGesture_…` (red-proved) | n/a (editor input) |
| CE-1954 press on an unselected unit drags it (threaded) | SKIPPED — needs decision (`DECISIONS_Ui.md`, lean A: select + drag in both modes) | | | |
| CE-1470 gizmo buffer overflow drops primitives | DONE — grows after an overflowing frame; drops reported in [FrameStats] | (this push) | `DebugPrimitiveBufferLayerTests.CE1470_…` (red-proved); Diagnostics.Contracts.Tests 67/0; Presentation gizmo suites 415/0 | n/a |
| CE-1955 three Presentation reds (five with S1 + LR1) | DONE — all stale tests vs other lanes' deliberate changes | (this push) | Map3DTests · EffectTests · TerrainAddEntityFrameRail 286/0 | n/a |
| CE-1298 FPS bare-hand fire does nothing | DONE — empty hands: SIGHT (no lock box) and the press strikes the crosshair's unit (`Melee.Strike`); Entity Control §8s | `640d30871` | `EmbarkNodesTests.CE1298_…` (red-proved); SimHost EmbarkNodes 28/0; Toolkits fire-mode 153/0; PossessionStation 83/0 | n/a (no scenario holds a possessed bare hand) |
| CE-1283 Debug this unit on a commander | DONE — ①③ superseded (CE-1317 / CE-1329); ② one global "Observe all brain units" switch (DBG-2) | `640d30871` | `UnitDebugSettingsTests.CE1283_…` (red-proved); Editor brain routes 7/0 | n/a |
| CE-1067 remote terminal panel → global gizmo | SKIPPED — needs decision (`DECISIONS_Ui.md`, lean A: not a defect today) | | | |
| CE-1239 .ogg clip missing from a film | DONE — `OggReader` (raylib LoadWave) for the track | `28ce2fa8a` | `AudioTests.CE1239_…` (red-proved); Audio + FilmDirector 80/0 (3 skips pre-existing) | n/a |
| CE-1241 15 seeded takes the scout cannot complete | PARTLY — 54/58 found (was 40/55); cc-platoon-bound fixed; the 4 left are scenario content (note to behaviors; casualty-care / police-line need the beat in the scenario) | (this push) | `scripts/director-check.py scout-all --headless` ×2 | the five scenarios' regressions: 3 PASS, 2 FAIL (behaviors) |
| CE-1280 Brain tab: why-this-choice one line | DONE — superseded by CE-1325 W5 (one line per decision by name, the sheet inside) | `b461d8fca` | `UtilityDecisionViewTests.CE1325_*` (existing) | n/a |
| CE-1281 Brain tab: Recent changes scroll / menu | DONE — 8-row scrolling grid, row menu Open graph of … / Copy | `b461d8fca` | `TheBrainViewTests.CE1281_…` (red-proved); TheBrainViewTests 26/0 (xvfb) | n/a |
| CE-1246 Stride mode 2 host actions | DONE — node `HostActions` + shared `HostActionsPumpSystem`; inspector + Details get them | `8952f8c3d` | `GlobalActionDispatchSystemTests.CE1246_…` (red-proved) 4/0; `HrotStrideApp.Game` builds (restored first) | ⚠ not run on a Stride window (Windows only) |

## 3. Taken from new handoffs
- `NOTE_Terrain_To_Ui_Obstacle_Drawing.md` (coordinator, 11:03 wake-up): its question MEASURED — all seven cultural-feature obstacles were drawn twice (an unknown box over the terrain box); fixed in `EntityBodyLayer3D.LookOf` (no-body look). Rail `Map3DTests.CE4656_…` (red before). Map3DTests + frame rails + ObstacleRow 287/3: `CE4388` wire-sphere (known, CE-1978), `E2b_V` (red at base too), `CE4582` (green alone with the fix, order-dependent in the suite).
- HANDOFF_Coordinator_To_Ui_Bug_Queue.md at 2026-11-18 09:00 (merged coordinator `5c0…` into ui)

## 4. Notes sent / received
- sent `NOTE_Ui_To_Convoy_Order_Gesture.md` — ConvoyOrder's GroundClick gesture removed (cross-lane), letter V

## 5. Gates (rule 8)
- CE-1133/1978: `Hrot.Presentation.Tests` RtsCommandToolTests · TheMenusAreGolden · MenuOrdersTests · Keymap · OrderMenu 63/0; `Hrot.Editor.Tests` OrderVocabularyTests 10/0. Goldens re-captured: only order ids + Convoy rows.
