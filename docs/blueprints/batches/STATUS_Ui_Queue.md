<!--STATUS
state: LIVE — running HANDOFF_Coordinator_To_Ui_Bug_Queue.md under PROTOCOL_Autonomous_Lanes.md
updated: 2026-11-19
current-answer: §1
-->

# STATUS — ui, the queue

self-wake-up: `ui self-wake-up` (trig_0141R92G1qK6SHY6mh3nnMms, hourly) — KEPT (R-4292); its message is §1a's text verbatim (set 2026-11-18 12:04).

## 1. Now
working — ⭐ the DRAGON (CE-4634 D-4, user `2026-11-19`: *"the 3D map visualization for the dragon and his dragon breath. And the passengers riding it"*): the 3-D look, the wingbeat bob and the sounds BUILT `2026-11-19` (DESIGN_Dragon §8, `DragonModelRail`, `AudioTests.Dragon`); next its 2-D symbol. · ⭐ the ui lane owns ALL of thermal (user, `2026-11-19`: *"You are the only one doing thermal"*). CE-1985 DONE `2026-11-19` (+ scenarios `sv-night-and-heat`, `th-vehicle-left-warm-spot`); CE-1987 DONE `2026-11-19` (R-1058, the AI sensor on °C contrast). Then CE-1990 scene lights and CE-1991 the cigarette ember (R-1057, user-approved `2026-11-19`); last: CE-1984 night vision + CE-1989 F4 DONE `2026-11-19`.

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
| CE-2438 paused-preview + run-to does not repeat (pc-defend-house) | DONE — not reproduced | stepped-hash 4,4 at 2 s and 60 s: STATE EQUAL; F-3 predates the report; a mid-run hot reload (a build into the runner bin) is the measured way to part a pair | stepped-hash pair ×2 (clean: 1 hot-swap per log) | RUNBOOK §9.0 |
| CE-1473 + CE-3451 ClusterRunner.Tests does not build; six rails drifted | 6 / 9 DONE, ②③④ SKIPPED → DECISIONS_Ui | it builds; 9 reds measured (6 stale ledgers / expectations / declarations + 3 world-boundary audits grown to 21 systems / 480 fields / 45 caches) | ClusterRunner.Tests 434 / 437 (the 3 = ②③④); touched suites: Toolkits mines/embarked/convoy/aggregate/air 149, SimHost mine/resolution 34, Core shared registry 4; mf-strike-and-halt, cv-blocked-road | DESIGN_Replay_Time_Axis §12l table |
| CE-1475 SumoNetPlacementTests F5/E1 on a Czech Windows | DONE | the fixture wrote culture commas; one invariant helper | Traffic SumoNetPlacementTests 19/19 + a cs-CZ rail (red-proved) | — (test only) |
| CE-1046 + CE-1038 LiveFromReplay under load; SimHost test count varies | CE-1046 PARTLY (frame waits; still 1/11 red under load, stays FlakyFact) · CE-1038 DONE — not reproduced | 2 full --blame runs: 3064/3064 reported, same failures; 9+1 pre-existing reds → CE-1980 (red at 1bc1cc911^) | LiveFromReplayTests 10/11 under full load | — |
| CE-4671 LK-S1 — Hrot.Look + EffectRules out of EffectSpawnSystem; HUD feeds out | DONE | a pure refactor: same types, places, order | Hrot.Look.Tests 3/3 (red-proved); Presentation effects + self-check 66/66; frame rails 240/246 (the 6 reds identical on the pre-change binary); CGF / editor pinned orders; full solution | design §7b |
| CE-4671 LK-S2 — PersonLook into Hrot.Look | DONE (KitFromTkb deferred to LK-S3, reason in §7c) | outfit rules + the seed / side / colour reads + Resolve's person half; Rgba; 20 map draw sites convert | PersonOutfitsRail parity (every person type × 4) + Look.Tests 4/4 (red-proved); outfit/kit rails 318/319 (MotorcycleModelRail: TKB seat count 3 vs 2, content not touched here); frame rails 262/267 (the pre-existing 5); full solution | design §7c |

## 3. Taken from new handoffs
- HANDOFF_Coordinator_To_Ui_Moved_Rows.md at 2026-11-18 15:20 (merged coordinator `fa5748b18`): CE-3468 · CE-2438 · CE-1473+CE-3451 · CE-1475 · CE-1046+CE-1038, in that order; §6a scenarios per row proportionate.
- `NOTE_Terrain_To_Ui_Obstacle_Drawing.md` (coordinator, 11:03 wake-up): its question MEASURED — all seven cultural-feature obstacles were drawn twice (an unknown box over the terrain box); fixed in `EntityBodyLayer3D.LookOf` (no-body look). Rail `Map3DTests.CE4656_…` (red before). Map3DTests + frame rails + ObstacleRow 287/3: `CE4388` wire-sphere (known, CE-1978), `E2b_V` (red at base too), `CE4582` (green alone with the fix, order-dependent in the suite).
- HANDOFF_Coordinator_To_Ui_Bug_Queue.md at 2026-11-18 09:00 (merged coordinator `5c0…` into ui)

## 4. Notes sent / received
- sent `NOTE_Ui_To_Convoy_Order_Gesture.md` — ConvoyOrder's GroundClick gesture removed (cross-lane), letter V

## 5. Gates (rule 8)
- CE-1133/1978: `Hrot.Presentation.Tests` RtsCommandToolTests · TheMenusAreGolden · MenuOrdersTests · Keymap · OrderMenu 63/0; `Hrot.Editor.Tests` OrderVocabularyTests 10/0. Goldens re-captured: only order ids + Convoy rows.


## Queue 3 — HANDOFF_Coordinator_To_Ui_Waiting_Work.md (taken 2026-11-18 23:05)

| row | state | finding | gates | design |
|---|---|---|---|---|
| CE-5034 GW-3 the look of glass | DONE BY TERRAIN (the coordinator reassigned it; the ui handoff withdrawn) — ui's parallel build (5f845eeb4) REMOVED at the merge of terrain; ui's one addition kept: the breaking-glass SOUND (EffectSpawnSystem.TrackWindows → SoundEvents.Glass, built-in 7356) | GlassFrameRail + AudioTests (see the merge commit) | AudioTests.CE5034_AShatteredPane_IsHeardAsBreakingGlass_Once | DESIGN_Glass_Windows §5a (+ HISTORY) |
| CE-4670 drawing — openings, lights, head out, light switches | DONE — the socket rule in Hrot.Look (VehicleSockets), the body layer's fittings, the commander's periscope eye, the station's H / U + HUD lines | Look 23/0 (+VehicleSockets); Presentation CrewControls/Fittings/PossessionStation/Keymap/MineClearer/Map3D 318/0 under xvfb; frames vs the mockup (scratchpad) | VehicleSocketsTests, CrewControlsTests (red-proved), VehicleFittingsFrameRail | DESIGN_Vehicle_Appearance_State §7d (+ as built); mockup UX/vehicle-openings-lights.svg | | | |
| CE-3451 ②③④ (DECIDED: ② build, ③ the rule moves into code — R-4297, ④ once in the egress shape; the decision's "CE-1980" = this) | DONE — attributes on the fields (849 rows migrated, 358 classified), the ledger retired, 7 + 23 world-boundary fixes, ④ IL-checked (36 of 45 reds were stale rows), 11 C listed as CE-1981 | the three rails green (`Hrot.ClusterRunner.Tests`); full solution build; feature suites (see the commit) | `IngressPendingForgetsTheLastWorldTests` (red-proved); ② red-proved | DESIGN_System_State_Inventory §9; NOTE_Ui_To_All_Lanes_State_Attributes |
| CE-1980 the nine pre-existing `Hrot.SimHost.Tests` reds (handoff row 3 names it) | TRIAGED — 2 now pass, 1 fixed by ui (ReplayBrowser → Orchestrator: `NodeIdRanges` → `Hrot.Core`), 7 handed back to their lanes (NOTE), ST0 flaky (measured) | OneBootstrapImplementationRails 5/0; Orchestrator NodeId 8/0; ReplayBrowser builds | — | NOTE_Ui_To_All_Lanes_SimHost_Rails_Drifted |

## Queue 4 — HANDOFF_Coordinator_To_Ui_Run_Gate.md (taken 2026-11-19)

| row | state | finding | gates | design |
|---|---|---|---|---|
| CE-5039 the run gate waits for the first live-overlay commit | DONE — ScenarioLoadStep ⑤ via StaticObstacleBakery.AwaitsLiveOverlay; te-make-enterable's atEnd row re-judged (cross-lane, terrain's scenario: it measured Idle's fidget) | te-make-enterable PASS 3/3; te-remove-building PASS (waited 0.7 s) and ua-tank-ammo PASS (no overlay: no wait); LiveOverlayBakeTests + ScenarioLoadStepTests + LoadPhaseChainTests 88 pass + the new rail (its first run red on its own no-op leg, removed; then 1/1) | LiveOverlayBakeTests.CE5039_* (red-proved: ⑤ off) | DESIGN_Cluster_Load_Phase §11.10d |

## Queue 5 — HANDOFF_Coordinator_To_Ui_Shared_Creature_Components.md (taken 2026-11-19)

| row | state | finding | gates | design |
|---|---|---|---|---|
| CE-4628 (+ CE-2250) shared creature components: Fear, Home, GroupMember / Group, Mood, AnimalTasks | DONE — built (worker: the moves; lead: the playback skip, the Mood writer, the review). ⭐ corrections to §10, measured: `Mood` = `Demeanour` (written now — CE-2250); ids KEPT (the space is near-full; `Group` 139, `AnimalTasks` 163; NOTE to all lanes); `AnimalState.FearBand` KEPT (a live `Fear` read moved the herd goldens — the band is the replicated snapshot the panic breach reads); an authored animal gets a radius-0 spawn Home | Toolkits civilians/animals/crowd/herd/CarKinem 415/0; SimHost civilian/animal/people/demeanour 58/0; FlightRecorder/Schema/Playback 135/0 (+4 skip); worker: NED 8/5 (5 pre-existing RoleGroupOwnership), parallel+read-check 57/0 + 110/1 (1 red at base), ClusterRunner state rails 4/0; scenarios 35/37 (ua-armed-blend-back, ua-civ-surrender red at base `79acf0ed6` too); goldens: 28 behaviour-identical to a base capture (diff shape: +Home/Mood/GroupMember/Fear/AnimalTasks, -AnimalHome/HerdMember), 4 not repeatable at the base itself (CE-1982) | rails: FlightRecorderTests.CE4628_* (red-proved), CrowdSystemTests.CE4628_ACivilianSeems…, AnimalFearTests.CE4628_* (Mood, PanicBreaks), SpeechAndDemeanourTests (version, red-proved), worker's (a)–(d) + tie-break (red-proved) | designs/animals/DESIGN_Animals.md §10a / §10b |
| CE-4628 §10c — `Mood` + `Gesture` → ONE replicated `Demeanour` (user: "go with Demeanour"; and no animal Mood — "no extra component just for display") | DONE — worker: the merge (54 files; `Gesture` id 398 + `dtGesture` 143 BURNED, `dtDemeanour` 153 gains Kind); lead: review + a civilian's Demeanour from birth (its two writers — the deferred gesture add in the parallel brain phase and the serial mood — never add over each other) | Toolkits civ/gesture/custody/animals 676/1 (1 red at base) + 40/0 after the fix; SimHost 140/3 (3 ownership reds at base); NED 8/5 (5 at base); Presentation Map3D+Film 292/0 (+6 skip), frame rails 52/2 (CE4388 red at base; E2b terrain, unrelated); state rails 4/0; parallel+read-check 57/2 (both red at base); scenarios: the 11 with gesture checks — 10 pass (ua-civ-surrender red at base); goldens: all 10 behaviour-identical (6 vs the committed reference, 4 vs a pre-merge capture) | `GestureTests.CE4628_GesturesAndMoods_NeverClobberEachOthersFields`, `BodyPartReplicationTests.CE4628_TheDemeanour_ReachesTheOtherNode…` (both red-proved) | designs/animals/DESIGN_Animals.md §10c |

## NOTE_Coordinator_To_Ui_Recapture_Golden.md (taken 2026-11-19)

| row | state | finding | gates | design |
|---|---|---|---|---|
| re-capture `df-civilian-ordered` on the merged tree | DONE — merged coordinator (`98cc1fd89`; `cp-vbied` golden conflict: ours, then `GOLDEN same` on the merged tree) | diff shape: positions / health / shots / verdicts IDENTICAL; components: `Mood` → `Demeanour` (10 unit-snapshots — CE-4628 §10c's rename) and `FrustrationTicks` / `NavigationStatus` / `LocomotionChannel` (5 — backend's CE-3449 determinism fix); ⚠ cross-lane: `ComponentIdCatalogueTests` (coordinator's) now skips `/.claude/` — agent git worktrees there are second copies of the tree and doubled every id (both rails red on this machine, green after) | `df-civilian-ordered` PASS, `cp-vbied` PASS; `ComponentIdCatalogueTests` 2/2 | — |

## Queue 6 — HANDOFF_Backend_To_Ui_Flame_Visuals.md (taken 2026-11-19)

| row | state | finding | gates | design |
|---|---|---|---|---|
| U-1 the flame jet · U-2 every burning spot · U-3 a burning man · U-4 scorched ground · U-5 the effect data (CE-4635, CE-4637) | DONE — `EffectLayer.ResolveStateInto`: the state as a second source of the same `Resolved` items (jet, spots in their holder's frame, burning man, scorch); the wreck rule retired; a Flamethrower shot makes no muzzle flash (the EffectRules row); `SimClock.TotalTimeOf` (cross-lane, Fdp.Toolkits Time: a 24 B box per frame gone). Merged backend FL-1 (the look section is §8). NOTE to backend: the flamethrower is heard as a rifle 10×/s | worker: 7 logic rails + 5 GL frame rails (xvfb, 0 skipped) + the Look rail, red-proved (frame rotation, cone, kind filter, 3-D case, out-when-out, A-2 0 B, the flash); Presentation 409 pass (+19 skip, 1 red at base); frame rails 296 pass, 7 red — 5 red at base (LR1, ADeadJet, ColourIcons, LabelFirstForms, CE4388) + E2b; ⚠ CE4582 / P7B red in the full run only, green alone at base and here — NOT proven pre-existing (gizmo / card-occlusion rails, no effects); Time suites: 4 S0 type-rail reds name other lanes' members + 2 order-dependent (green alone 13/13); scenarios: th-burning-wreck 10/10 (golden re-captured — behaviour identical; layout: Gesture → Demeanour, HeatSource), fl-flamethrower-cone 7/7 (golden same) | frames compared with the mockup (truck, jet, man, napalm cell, building, scorch) — FYI sent | designs/weapons/DESIGN_Flame_Weapons.md §6 · §8 (as-built) · UX/flame-visuals.svg |
| the flame ROAR (NOTE_Backend_To_Ui_Flamethrower_Sound_Answer: `SoundSourceClass.Flame`, 200 m) — a held loop while `FlameJets.TryGet` is lit (the flail's precedent), no gunshot per fuel shot, a synthesized FlameRoar clip | DONE (`4d5d508f9`) — `EffectRules.IsFuelShot`: ONE predicate drops both the flash and the shot sample; `SoundSources`: a lit flamer held as a `FlameRoar` loop (7357, own mixer key, reach widened to the Flame range from the cached acoustic signature, re-asked every frame so it stops with the jet); `SoundSynth.FlameRoar` a seamless 1 s loop | Audio/Sound/Effect/Flame 104 pass (+9 opt-in device skips, 1 red at base: LR1); Look 39/39; 4 rails red-proved (held/unheld/stops, no shot sample, the loop seam, alloc vs an engine); ⚠ found CE-1983 — the audio layer allocates ~256 B every frame (pre-existing) | `AudioTests.Flame` (a)–(d) | DESIGN_Flame_Weapons §8 · DESIGN_FPS_Audio §10 |
