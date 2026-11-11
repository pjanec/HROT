<!--STATUS
state: LIVE
updated: 2026-11-11
current-answer: the tables below (§1 what was replayed, §2 the applied frame, §3 the render frame, §4 garbage, §5 how much of the apply rewrites unchanged bytes)
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

## Method — re-run
1. Build once: `dotnet restore` + `dotnet build Hrot/Runner/Hrot.ClusterRunner -v q --no-restore`. Temporary probe (env-gated, not committed): stopwatch + `GC.GetAllocatedBytesForCurrentThread()` brackets in `PlaybackController.ApplyFrame` (read / decode / apply) and `PlaybackSystem.ApplyFrame` (each section, each chunk by type; identity = compare the table's bytes with the blob before `RestoreChunkFromBuffer`, 64-byte block diff), a per-tick record in `PlaybackTickSystem`, and an `EventListener` on `Microsoft-Windows-DotNETRuntime` keyword `0x1` verbose.
2. Launch (parts, never the literal): `M=editor; HROT_DEBUG_API_PORT=8131 HROT_HIDDEN_WINDOW=1 HROT_FRAME_STATS=1 setsid nohup xvfb-run -a dotnet …/Hrot.ClusterRunner.dll --mode $M --target-fps 0 &`; kill by recorded PID.
3. Drive (`curl --noproxy '*'`, JSON bodies): `POST /replay {"exerciseId":"97405492-…","waitForReady":true}` → per pass `POST /replay/seek {"atSeconds":0}`, `POST /sim/play {}`, wait 95 s; sample `GET /diagnostics/architecture` and `/diagnostics/render` mid-pass.
4. Stacks: `dotnet tool install --tool-path <dir> dotnet-trace`; `dotnet-trace collect -p <pid> --profile dotnet-sampled-thread-time,gc-verbose --duration 00:00:00:45`; `dotnet-trace report <f> topN --inclusive`, `convert --format Speedscope` for the call tree; allocation stacks by `TraceLog.CreateFromEventPipeDataFile` (TraceEvent 3.1.16), `GC/AllocationTick` events of the main thread, bucketed by the first non-framework frame.
