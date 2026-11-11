<!--STATUS
state: LIVE
updated: 2026-11-11
current-answer: the tables below (§1 what was replayed, §2 the applied frame, §3 the render frame, §4 garbage, §5 how much of the apply rewrites unchanged bytes, §7 live vs replay side by side)
stale-below: nothing
known-rot: nothing known
known-conflict: none. CE-1478's row quotes "raw 14-65 MB, apply 17-125 ms, ~1.5 MB garbage" — this file replaces those averages with distributions and sites.
related-designs:
  - docs/blueprints/Blueprint_Issues_Tracker.md — OWNS rows CE-1478 (the redesign this feeds) and CE-4407 (the user finding)
  - docs/blueprints/batches/STATUS_Ui_User_Findings_Overnight.md — OWNS U-2, last night's garbage fix (commit 8d905af77)
-->

# MEASURE — the replay frame of a 1 000-man recording, attributed (`2026-11-11`, ui lane)

⚠ **Linux, 4 cores, `xvfb`, software GL** — draw time is NOT representative of the Windows machine; the CPU apply, the update and the garbage are. Nothing was fixed; the probes were temporary and are reverted.

## 1. What was replayed
| | |
|---|---|
| recording | `data/nodes/node-1/exercises/97405492-32a0-4d81-ae68-4e520cef92de/node_1.fdp` (97.7 MB, git-ignored) — scenario `scale-rig-demo-city-1000-2s`, terrain `demo-city`, **5 647 entities**, 96.3 s, **91 recorded frames** (keyframes at 0 and 60) |
| other candidate | `data/exercises/8fb38a0a-…` holds **2 frames** — unusable |
| shape | frames 0–24 are idle (0.1 MB raw each); frames 25–90 are **active: 55–64 MB raw each**, one every ~1.4 s of recording time |
| sample | 5 passes (seek 0 → ▶ → 95 s): **319 active deltas · 127 idle deltas · 18 keyframe applies** (seeks included); 887 s of playback |

## 2. One applied recorded frame (median / p95 / max)
| | active delta (n 319) | keyframe (n 18) | idle delta (n 127) |
|---|---|---|---|
| raw → LZ4 | 57.6 / 64.1 / 108 MB → 1.6 MB | 60.3 / 112 MB → 0.4 / 2.2 MB | 0.1 MB |
| file read | 0.5 / 2.6 / 106 ms | 0.1 / 1.9 | 0 |
| LZ4 decode | 11.8 / 18.2 / 56 ms | 11.3 / 70 | 0 |
| **ApplyFrame** | **61 / 89 / 139 ms** | 42 / 164 | **16.5 / 26 / 29 ms** |
| total | **74 / 107 / 207 ms** | 53 / 211 | 17 ms |
| chunks restored | 852 / 952 / 1 634 | 917 / 1 664 | 0 |
| garbage in ApplyFrame | **8.1 / 9.3 / 10.6 MB** | 1.6 / 7.3 | 0 |

**Inside `ApplyFrame`** (active delta, median ms · garbage KB):
| section | ms | garbage | |
|---|---|---|---|
| ⭐ `RepairManagedComponentMasks` | **33.6** (55 %) | 0 | every managed table × every entity (`GetMetadataUnsafe`, `GetComponentMaskUnsafe`, `GetRawObject`); **15 ms even for a frame with 0 chunks** (the idle row) |
| unmanaged chunk restores (memcpy) | 7.5 | 0 | |
| chunk read (`ChunkBuffer`, MemoryStream → pooled array) | 7.4 | **3 127** | the 64-length pool is exceeded by managed blobs of varying length ⇒ `ReadBytes` |
| `RepairEntityIndex` | 6.8 | 0 | |
| managed chunk restores | 2.4 | **4 675** | ~1 570 objects + one `T?[16384]` array per managed chunk; top: `SubordinateOrders` 1.8 MB, `BehaviorStartRecord` 1.2, `PausedTask` 0.75 |
| `RebuildMetadata` · destructions · entity-index chunks · events · singletons | 0.8 · 0.7 · 0.2 · 0 · 0.1 | 0 | a one-off 9.4 MB singleton allocation (max) |

**Active frame by component TYPE** (per active delta; 836 chunks / 54.8 MB; chunks · MB · ms):
| type | chunks | MB | ms | | type | chunks | MB | ms |
|---|---|---|---|---|---|---|---|---|
| `BlueprintBlackboard4096` | 251 | **16.4** | 2.4 | | `EntityIndex.cold` | 25 | 1.65 | 0.15 |
| `BrainJournal` | 73 | 4.75 | 0.66 | | `PathTrace` | 21 | 1.35 | 0.24 |
| `CommandOrderHistory` | 64 | 4.15 | 0.62 | | `SquadCognitiveState` | 20 | 1.33 | 0.19 |
| `UtilityDecisionLog` | 52 | 3.39 | 0.51 | | `ReportOutbox` | 17 | 1.09 | 0.16 |
| `TargetMemory` | 46 | 3.01 | 0.43 | | `CommandReportHistory` | 16 | 1.04 | 0.16 |

## 3. Recorded frames per render frame · the render frame · per system
- **Applies per render frame at 1×: ≈ 0.15.** The editor runs 5 fps while playing; the active recording advances ~0.7 frames/s ⇒ **1 apply on 14.7 % of ticks** (325 of 2 256), 2–4 on 6 ticks (catch-up after a seek). ⚠ Most ticks apply nothing — the cost is a ~75 ms **hitch every ~7th frame**, plus what runs on every tick.
- **Render frame** (`HROT_FRAME_STATS=1`, median of 2 s windows; `--target-fps 0`):
| | frame | update | draw (SW GL) | ui | garbage update / draw / ui |
|---|---|---|---|---|---|
| **playing** | **212 ms (4.7 fps)** | 89 | 79 | 24 | 1.8 / **5.9** / 0.4 MB |
| **paused at 0** (nothing changes, no apply) | 115 ms (8.7 fps) | 32 | 50 | 14 | 0.8 / 4.8 / 0.2 MB |
- **Where the main thread goes** (45 s `dotnet-trace` sampling, 195 frames, 231 ms/frame under tracing): update **44 %** · `DrawWorldAll` 36 % · `WindowManager.Render` 9.5 % · present/other 11 %.
  Under **update**: `GizmoInteractionModule` → `StatelessGizmoSystem` **41.7 %** (~43 ms: 5 647 entities × ~10 gizmo rules, match pass 4.6 ms) · the system phases 33.4 % · **`OnDemandProvider.AcquireView` → `EntityRepository.SyncFrom` 16.4 %** (~17 ms: copies the dirty chunks of the world into a view; every apply dirties ~all of them) · `PlaybackTickSystem` 14 % of update.
  Under **draw**: `EntityCardRenderer.Draw` 67 % (1 139 cards; `DrawTextEx` 17 %), `EffectLayer.Draw` 27 %.
- **Systems above 0.5 ms** (`/diagnostics/architecture`, avg ms/tick; all run every tick, ~85 % of which have no new frame): `PlaybackTickSystem` 4.4–5.9 (12.5 on the ticks that apply) · `MapLayerAssignmentSystem` 4.2–4.5 · `CycloneEgressSystem` 3.6–3.9 · `StyleResolutionSystem` 3.0–3.1 (77 KB garbage) · `MapCullingSystem` 2.2–2.3 · `AnimationRuntimeBridgeSystem` 1.5–1.6 · `TraceBufferLifecycleSystem` 1.4–1.5 · `NodeBreakpointEvaluator` 1.1 · `StaticObstacleBakeSystem` 0.75–0.8 · `CycloneNetworkCleanupSystem` 0.3–0.9. **133 systems sum 33 ms** (24.7 mean); the rest of update is the gizmo module and the view sync above, which that list does not show.

## 4. Garbage — by allocation site
Process total **8.5 MB / frame** (render telemetry 7.9; AllocationTick on the main thread 1 658 MB / 195 frames). **Only ~15 % is the replay apply.**
| # | site (main thread, `AllocationTick` + stacks) | MB / frame |
|---|---|---|
| 1 | `EntityCardRenderer.Layout` (`Item[]`, `string`, `DebugPrimitive[]`) — draw | **4.15** |
| 2 | `DebugPrimitiveRenderer2D.Render` — draw | 0.94 |
| 3 | dynamic serializer lambdas ×10 (`lambda_method6xx`: the managed-chunk deserialisers, matches the §2 bracket 4.3 MB per apply) — update | ~0.75 |
| 4 | `GizmoFramePanel.Describe` — update | 0.71 |
| 5 | `PlaybackSystem.ChunkBuffer` (matches §2: 3.1 MB per apply) — update | 0.51 |
| 6 | `EffectLayer.Resolve` / `ParseRgba` — draw | 0.35 / 0.11 |
| 7 | `FixedString32.ToString` (card text) — draw | 0.24 |
| 8 | `AuthoredRules.Prop` / `.Parse` (mission summary window, JSON) — ui | 0.19 / 0.07 |
| 9 | `DebugGizmoLayer.CardAnchor2D` — draw | 0.10 |
| 10 | `StyleResolutionSystem.BuildStyle` — update | 0.02 |
- **GC** (887 s, 5 passes incl. seeks): gen-0 +48, gen-1 +17, gen-2 +7 ⇒ **3.2 / 1.1 / 0.5 per minute**; managed heap 1.0 → 2.0 GB, working set 2.1–3.0 GB, pause 0.06 %.

## 5. How much of the apply rewrites bytes that did not change
| (active deltas, n 62, unmanaged tables; the managed rows are not comparable) | |
|---|---|
| chunks byte-identical to the table before the restore | **3 %** (24 of 836 per frame) |
| **64-byte blocks that differ** | **5.4 %** — 2.8 MB of 51.4 MB per frame (`BlueprintBlackboard4096` 1.2 %, `BrainJournal` 2.8 %, `CommandOrderHistory` 2.2 %, `UtilityDecisionLog` 5.7 %, `TargetMemory` 4.5 %, `RecentSenses` 8.2 %) |
| keyframe over the current world | 56 % of chunks identical |
⇒ whole-chunk skipping buys nothing (chunks are shared by 64 KB of rows); **a row/block-level delta would shrink the apply ~20×**.

## 6. Also found (not fixed)
- ⚠ **Looping a replay** (`POST /replay/loop`) — after the first wrap the clock wrapped five times in the log but **no recorded frame was applied again** (probe silent, `PlaybackTickSystem` 0.01 ms); an explicit `/replay/seek` works. To confirm and file.
- ⚠ `/sim/play` straight after `/replay` leaves the replay paused; seek, then play.
- Probe cost: identity runs raise apply to ~85 ms (they read each chunk twice) — the §2 timings are from runs without them.

## 7. Live vs replay — the same scenario in four states (`2026-11-11`)
Same machine, same editor build, `HROT_FRAME_STATS=1`, `--target-fps 0`; live = `/scenario/load/edit scale-rig-demo-city-1000-2s` + `/sim/play` (5 647 entities, recording on), measured at ~56–115 s of sim time; replay paused = seek to 50 s, paused. Per-system ms are per tick (`/diagnostics/architecture`, interval means while paused; "traced" = from the sampling trace, ~7 % overhead).
| | live running (~60 s) | live paused | replay playing | replay paused (50 s) |
|---|---|---|---|---|
| render frame | **2 000–2 190 ms (0.5 fps)** — 155 ms (13 fps) in the first 30 s | 214 ms (4.7 fps) | 212 ms (4.7 fps) | 194 ms (5.2 fps) |
| update / draw / ui | **1 818–2 028** / 117–128 / 36 | 78 / 100 / 21 | 89 / 79 / 24 | 65 / 81 / 28 |
| garbage per frame (update / draw / ui) | **28 / 7.5 / 0.8 MB** (telemetry 35) | 0.9 / 7.8 / 0.8 (9.7) | 1.8 / 5.9 / 0.4 (8.5) | 0.8 / 5.7 / 0.4 (7.1) |
| GC per minute (gen 0 / 1 / 2) | 1.3 / 0 / 0 (2 s frames); 7.5 / 5 / 4.5 over the first 80 s incl. load | ~3 / 0 / 0 | 3.2 / 1.1 / 0.5 | not sampled |
| `StatelessGizmoSystem` (gizmo module) | 28 (46 traced) | 38 (traced 40–45) | ~43 traced | 41 traced |
| `OnDemandProvider.AcquireView` → `SyncFrom` | **36 traced** | **0** | **17 (~110 on the tick after an apply)** | **0** |
| `MapLayerAssignmentSystem` | 5.3 | 5.1 | 4.2–4.5 | 4.5 |
| `StyleResolutionSystem` (77 KB garbage live-paused/replay) | 3.6 | 3.7 | 3.0–3.1 | 3.4 |
| `MapCullingSystem` | 3.5 | 3.4 | 2.2–2.3 | 2.5 |
| `CycloneEgressSystem` | 4.3 | 4.1 | 3.6–3.9 | 4.0 |
| `PlaybackTickSystem` | — | — | 4.4–5.9 (12.5 on apply ticks) | 0.01 |
| simulation (live only) | `EqsSolverSystem` 937 · `WeaponDispatcherSystem` 497 · `BrainTickSystem` 262 · `FireProcessing` 96 · `Raycast` 71 · `Stress` 49 · `Callout` 34 · `SquadCoordination` 26 · `Ballistics` 14 · `EventHistoryCapture` 12 (**9 MB garbage per tick**) | `Embarked` 6.1 · `Genesis` 4.7 · `UnitCentroid` 3.8 · `MacroExpansion` 3.5 · `GroundClampWhilePaused` 3.0 · `MapSelfCheck` 2.6 (sim groups still tick paused) | sim + lifecycle disabled for a replay | same |

**Top garbage sites (MB per frame, main thread, `AllocationTick` stacks):**
| live running | live paused | replay paused | replay playing (§4) |
|---|---|---|---|
| **`DiagnosticEventHistoryService.GetGenericEventSummary`** + closure + `Capture` + `FormatLeaf` **~19** · `EntityCardRenderer.Layout` 4.0 · `DebugPrimitiveRenderer2D.Render` 2.7 · `SquadManeuverRunner.Hold` 1.8 · `InspectReadBuffer` 0.7 · `GizmoFramePanel.Describe` 0.6 · `VectorJsonFormat.F` 0.5 · `GrowOnlyBuffer<TimelineEvent>.Add` 0.5 · `EffectLayer.Resolve` 0.4 | `Layout` 4.2 · `DebugPrimitiveRenderer2D.Render` 2.8 · `Describe` 0.6 · `EffectLayer.Resolve` 0.55 · `FixedString32.ToString` 0.2 · `RepoReflector.HasComponent` 0.12 · `NLog GetMessages` 0.11 | `Layout` 4.2 · `DebugPrimitiveRenderer2D.Render` 0.9 · `Describe` 0.7 · `EffectLayer.Resolve` 0.24 · `FixedString32.ToString` 0.24 · `AuthoredRules.Prop` 0.17 | `Layout` 4.15 · `DebugPrimitiveRenderer2D.Render` 0.94 · serializer lambdas 0.75 · `Describe` 0.71 · `ChunkBuffer` 0.51 |

**Why — what costs more in replay than in live**
- ⭐ **View sync (`AcquireView` → `SyncFrom`): 0 when nothing changes (live paused, replay paused), 17 ms per frame while a replay plays.** It is change-driven (copies the chunks whose version moved); `RestoreChunkFromBuffer` / `SetChunk` bump the version of every chunk it writes, and an apply writes ~840 of them (§2) — of which 5 % of the bytes really differ (§5). So the tick after each apply copies the whole world (~110 ms), once per ~7 frames. Live running pays more (36 ms) because the sim dirties much of the world every tick. ⇒ fixed by the §5 row/block delta (write only what differs, so only that is dirty).
- ⭐ **`PlaybackTickSystem` — replay only:** 4.4–5.9 ms average, 12.5 on apply ticks (and the 61 ms apply behind it, §2).
- **Update when playing vs paused (+24–32 ms) is exactly those two** (traced: playing 103 = gizmos 43 + systems 34 [of which PlaybackTick 14.6] + sync 17 + misc; paused 71 = gizmos 41 + systems ~17 + misc). Draw and ui are the same playing or paused.
- ❌ **Not more in replay:** `StatelessGizmoSystem` (~40 ms), `MapLayerAssignment` (4–5), `StyleResolution` (3–3.7), `MapCulling` (2–3.5), `CycloneEgress` (3.6–4.3) cost the SAME in all four states, including live paused where nothing changes — **they are not change-driven and not gated at all** (`StatelessGizmoSystem` evaluates every visible entity every tick: no version/dirty test in it). Their per-tick cost is a constant ~55 ms floor under every state; a replay is not worse, it just never pays for the simulation. Egress keeps running in replay (design) at its live-paused cost.
- Live paused costs MORE than replay paused (update 78 vs 65) because the sim groups still tick; the replay disables them.
- ⚠ A live run of this scenario is **not** a 1 000-man frame budget reference: at ~60 s the sim alone is 1.8–2.0 s per frame on these 4 cores (`EqsSolverSystem` + `WeaponDispatcherSystem` + `BrainTickSystem`), and the live-only garbage (`DiagnosticEventHistoryService`, ~19 MB per frame) dwarfs the replay's.
- Caveats: the editor binary still carried the (env-off) probe from §Method; live recording was on; `/diagnostics/architecture` "recent" values of sim systems are stale while paused (hence interval means); GC/min over 20–46 s windows is coarse.

## 8. The Replay Browser, before and after P1 + P2 (`2026-11-11`, the user's actual path)
Same recording (1 000-man, 91 frames, 5 647 entities), the **Replay Browser** host (`-m replaybrowser`, `xvfb`, `--target-fps 0`, `HROT_FRAME_STATS=1`, `POST /replay {path}` → `POST /sim/play` from 0 to frame 90, two passes); "before" = `HROT_REPLAY_PREFETCH=0 HROT_REPLAY_FULL_REPAIR=1` (the pre-P1/P2 path, same build); per-apply numbers from `HROT_REPLAY_STATS=1` (`[ReplayApply]` lines, active frames ≥ 25).
| | before | P1 + P2 (first cut) | P1 + P2 (final) |
|---|---|---|---|
| **ApplyFrame, median / p95 / max** (active deltas, n 132) | **109 / 160 / 215 ms** | 63 / 145 / 602 | **32 / 47 / 87 ms** |
| of which repair (entity index + managed masks) | 74 ms (whole index: 1 005 364 entity visits) | 34 ms (partial, but 110 000–800 000 visits) | **7 ms** (110 000 visits, dead slots skipped) |
| read + LZ4 decode on the applying thread | 12 ms median | 0 (worker) | **0** (frame 61, the first inline one after a keyframe, is the only exception) |
| browser plays the 91 frames in | 11.7 s | 11.3 s | **5.9 s** |
| render frame while playing (the windows that hold applies) / update | 1 615 / 1 488 ms | 1 624 / 1 426 | **682 / 554 ms** |
| garbage per apply | 10.9 MB | 10.8 MB | 10.8 MB |

📐 **The first P2 cut was slower than the whole-world pass** (measured, kept in the record): it also scanned every managed table over the entities of every restored mask chunk — 500 000–800 000 entity-table pairs per frame, 50–90 ms. The restored mask chunk already carries the masks next to the data the recorder saw, so that scan was dropped (`PlaybackSystem.RepairManagedComponentMasksPartial`); the other half of the win is that `RepairEntityIndex` / the full mask pass no longer visit the ~500 000 DEAD slots of the index (5 647 live).

**What the Browser pays that the editor does not (replay-only):**
| cost | where | size |
|---|---|---|
| ⭐ **one recorded frame is applied per 1/60 s of playback, up to 10 per render frame** — playback is paced by FRAME COUNT, not recorded time | `ReplayBrowserSubsystem.cs:445-464` (`frameTime = 1/60`, accumulator clamp `frameTime * 10`) → `TryStepForwardViaManager` → `FederatedReplayManager.StepForwardAll` | the editor's replay clock applies ~0.15 frames per render frame (§3); the Browser applies up to **10** — at 5 fps that is 10 × 32 ms ≈ 320 ms of the render frame's 550 (before: 10 × 109 ms = 1.1 s of 1.5 s — the user's < 1 fps). A recording whose frames are 1.4 s apart plays at ~84× |
| the sequential step per frame: `ClearCurrentBuffers` → apply → `OnTimeChanged` (view rebind) → `DeliverTheSteppedFrameEvents` | `ReplayBrowserContext.StepForward` · `ReplayBrowserSubsystem.cs:1355-1358` | measured inside the 32 ms apply + ~20 ms/render frame of the rest of update |
| garbage per apply ~10.8 MB (managed-chunk deserialisers + `ChunkBuffer`), 11 MB × up to 10 per render frame | `PlaybackSystem.RestoreManagedTable`, `ChunkBuffer` | ~110 MB per Browser render frame while playing |
Filed: **CE-1479** (pace the Browser's playback by recorded time, as the editor's replay clock does).

Method: `HROT_REPLAY_STATS=1` prints one `[ReplayApply]` line per apply (stderr); `HROT_REPLAY_PREFETCH=<depth>` / `HROT_REPLAY_FULL_REPAIR=1` switch P1 / P2 off in the same build; the scripts were a seek → `/sim/play` → poll `/replay/status` loop over the debug API (port via `HROT_DEBUG_API_PORT`).

## 9. P6 — block-run deltas (format v8), live recording and replay (`2026-11-11`)
Same scenario recorded live twice in the editor host (`/scenario/load/live scale-rig-demo-city-1000-2s`, `startPaused:false`, ~85 s wall, `HROT_REPLAY_STATS=1`; "whole" = `HROT_RECORD_BLOCK_DELTAS=0`):
| | whole-prefix deltas (v7 body) | block runs (v8) |
|---|---|---|
| delta raw size, median (max) | 55.6 MB (64.6) | **8.1 MB** (64.5 — the first delta after a keyframe has no base) |
| recorder MAIN-thread cost per delta, median / p95 | 48.1 / 71.7 ms | **44.2 / 60.8 ms** (no regression) |
| file for the run | 76 MB (59 deltas) | 44.6 MB (73 deltas) |
| Browser replay: apply of a delta, median / p95 (prefetch + partial repair on) | 31 / 54 ms | **20 / 59 ms** |
| garbage per apply | 10.6 MB | 8.3 MB (managed chunks unchanged) |
The view copy after an apply is per-chunk-version and unchanged (every changed chunk still stamps) — P3. Recorded to `data/nodes/node-1/exercises/` (git-ignored).

## Method — re-run
1. Build once: `dotnet restore` + `dotnet build Hrot/Runner/Hrot.ClusterRunner -v q --no-restore`. Temporary probe (env-gated, not committed): stopwatch + `GC.GetAllocatedBytesForCurrentThread()` brackets in `PlaybackController.ApplyFrame` (read / decode / apply) and `PlaybackSystem.ApplyFrame` (each section, each chunk by type; identity = compare the table's bytes with the blob before `RestoreChunkFromBuffer`, 64-byte block diff), a per-tick record in `PlaybackTickSystem`, and an `EventListener` on `Microsoft-Windows-DotNETRuntime` keyword `0x1` verbose.
2. Launch (parts, never the literal): `M=editor; HROT_DEBUG_API_PORT=8131 HROT_HIDDEN_WINDOW=1 HROT_FRAME_STATS=1 setsid nohup xvfb-run -a dotnet …/Hrot.ClusterRunner.dll --mode $M --target-fps 0 &`; kill by recorded PID.
3. Drive (`curl --noproxy '*'`, JSON bodies): `POST /replay {"exerciseId":"97405492-…","waitForReady":true}` → per pass `POST /replay/seek {"atSeconds":0}`, `POST /sim/play {}`, wait 95 s; sample `GET /diagnostics/architecture` and `/diagnostics/render` mid-pass.
4. Stacks: `dotnet tool install --tool-path <dir> dotnet-trace`; `dotnet-trace collect -p <pid> --profile dotnet-sampled-thread-time,gc-verbose --duration 00:00:00:45`; `dotnet-trace report <f> topN --inclusive`, `convert --format Speedscope` for the call tree; allocation stacks by `TraceLog.CreateFromEventPipeDataFile` (TraceEvent 3.1.16), `GC/AllocationTick` events of the main thread, bucketed by the first non-framework frame.
