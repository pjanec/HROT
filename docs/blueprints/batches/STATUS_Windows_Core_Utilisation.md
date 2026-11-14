<!--STATUS
state: LIVE — measure only, no fixes
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - HANDOFF_Coordinator_To_Windows_Core_Utilisation.md (on coordinator, dispatched c0219b5c8, re-stamped d69acee40) — the request (CE-4543 … CE-4553)
  - STATUS_Windows_Unified_Run.md — the previous real-core numbers (⚠ its W-2 editor ticks carry the write-stats instrument, see §0)
  - ../../DESIGN_Parallel_Simulation.md (on exp/threaded-build) — the pool, the per-entity phases
  - ../../DESIGN_Threaded_Simulation_Presentation.md (on exp/threaded-build) — the staging (Publish / Pull)
  - ../../DESIGN_Map_3D_Mode.md — §6ah the terrain by chunk and LOD, §6ai trees / buildings
-->

# STATUS — Windows: why the editor uses ~30 % of the CPU, and the 3-D view with the whole terrain in view (CE-4543 … CE-4553)

**Where:** the user's machine — Ryzen 7 6800H, 8 cores / 16 threads, 31 GB, Windows 11, on AC. **Build:** `exp/threaded-build` @ `a9955908e`
(no newer commit at the start), Release. **How:** the EDITOR (`-m editor`), hidden window, the defaults ON (no switch variable — `performance.json` at
its defaults: threaded node ON, parallel simulation ON, worker threads auto), the scenario loaded LIVE and playing, 40 s settle, then a 30 s window
with no profiler (per-thread CPU from `GetThreadTimes`, `dotnet-counters`) and a 20 s `dotnet-trace` window (`Microsoft-DotNETCore-SampleProfiler` +
the runtime's GC / allocation-tick events), stacks parsed per OS thread. Recorder OFF = `POST /run-settings {recordLiveRuns:false}` before the load
(restored to true after). Drivers and raw data: scratch only (`wtm/cu/`).

## 0. Read first — two measurement facts

| | |
|---|---|
| ⚠ **`HROT_FRAME_STATS=1` also turns on the write instrument** (`TableWriteStats.On`, `FDP/Engine/Fdp.Core/Diagnostics/TableWriteStats.cs:14`) — it copies every committed chunk once per tick | measured on demo-city: tick **89.2 ms with it, 75.8 ms without** (13.4 ms, 15 %). ⇒ every editor tick in `STATUS_Windows_Unified_Run.md` W-2 and the earlier reruns (`scripts/threaded-scale-measure.py` sets it) **includes this instrument**. This run never sets it |
| the sample profiler suspends the runtime ~370 × / s (7 533 suspensions, 5.8 s of suspended time in 21 s) | the traced tick was nonetheless **79.8 ms vs 84.1 ms untraced** (scale-1, recorder ON) — the shares below are from the trace, the absolute tick from the untraced window |

## 1. The answer — the ranked serial list (scale-1, recorder ON, traced tick 79.8 ms)

Of the sim worker's tick, **12.0 ms (15 %) is inside parallel sections**; **67.8 ms (85 %) is serial on one thread** — 46.8 ms inside systems, 20.9 ms
outside every system. Amdahl: with infinite cores the tick could not go below ≈ 68 ms.

| # | serial piece | ms / tick | share of tick | what it is |
|---|---|---|---|---|
| 1 | **the small-systems tail** — 94 systems, each ≤ 1 ms, none parallel | **21.7** | 27 % | C-4 below |
| 2 | **`RecorderTickSystem`** → `RecorderSystem.RecordDeltaFrame` | **11.6** | 14 % | 7.5 ms of it `Buffer.Memmove` (the delta copy), 1.8 ms `RecordManagedTable` / `FdpAutoSerializer` |
| 3 | **`OnDemandProvider.AcquireView`** (the module snapshots' sync) | **9.3** | 12 % | `SyncFromCore` 5.1 (`ManagedComponentTable.SyncDirtyChunks` → `_BulkMoveWithWriteBarrier` 3.0), `EntityIndex.ApplyComponentFilter` 0.8, own 4.2 |
| 4 | **`StagingWorlds.Publish`** (the sim → UI staging copy) | **4.7** | 6 % | `SyncPresentationCore` 4.4 (`SyncFromCore` 2.8, `CopyRowFrom` 0.9) |
| 5 | **`HrotNodeContext.StepTimeline`** (the live replay timeline) | **4.3** | 5 % | `TimelineFrameScanner.Step` 3.9: `ValueChangeLane.Scan` 1.6, `RingLane.Scan` 1.7 (brain journal, utility log), `StructuralLane` 0.3 |
| 6 | `BrainTickSystem` — its serial part | 3.3 | 4 % | of its 5.2 ms, 1.9 inside the parallel phase |
| 7 | **`DiagnosticEventHistoryService.Capture`** | **2.7** | 3 % | `GetGenericEventSummary` 2.4: LINQ `ToList` 1.1, `string.Concat` 0.7, `string.Join` 0.5 — every tick |
| 8 | serial systems 1–1.5 ms: `FireProcessingSystem` 1.4 · `MapLayerAssignmentSystem` 1.3 · `StyleResolutionSystem` 1.3 · `DroneImpactSystem` 1.1 | 5.1 | 6 % | |
| 9 | the converted phases' serial remainders (weapon 0.7, stress 0.6, callout 0.4, car 0.4, squad 0.3 …) | 2.4 | 3 % | the part of each system outside its `FdpWorkerPool.Run` |
| 10 | kernel bookkeeping: `ShouldRunThisFrame` 0.7 · `FdpEventBus.SwapBuffers` 0.5 · `CaptureLastTick` 0.3 · `PlaybackCommands` 0.3 · `ScenarioSaveGate.CanSave` 0.2 · `HarvestEntry` 0.2 · rest 0.3 | 2.6 | 3 % | |
| | **serial total** | **67.8** | **85 %** | |
| | parallel sections (the eight phases, C-3) | 12.0 | 15 % | |

**The recorder's own cost:** tick **84.1 ms ON vs 71.4 ms OFF** (12.7 ms, 15 %); ticks / s 11.9 → 14.0. The thread-pool workers allocate 16.8 MB/s
(`Byte[]`) ON vs 1.0 OFF (the async writer). `StepTimeline` is there with the recorder OFF too (4.8 ms) — it is the live timeline, not the recorder.

## 2. C-1 · CPU and threads (`CE-4543`) — per-thread busy % over 30 s, no profiler (`GetThreadTimes`; names from `GetThreadDescription`)

| | scale-1 rec ON | scale-1 rec OFF | demo-city rec ON |
|---|---|---|---|
| process: cores busy · % of the machine | **4.47 · 27.9 %** | **4.82 · 30.1 %** | **5.19 · 32.4 %** |
| tick ms (untraced) · ticks / s | 84.1 · 11.9 | 71.4 · 14.0 | 75.8 · 13.2 |
| `HROT sim (all kernels)` — the sim worker | **91.4 %** | 90.5 % | 91.3 % |
| main / UI thread | **81.7 %** | 81.0 % | 76.7 % |
| `FdpWorker1…13` — the pool (each) | **17.3 – 19.2 %** (Σ 240 %) | 20.0 – 22.0 % (Σ 277 %) | 23.9 – 25.3 % (Σ 322 %) |
| `.NET TP Worker` (the async modules: EQS, pathfinding; the recorder's writer) | Σ 29.6 % (5 threads) | Σ 22.2 % | Σ 18.5 % |
| GC threads (4 × `.NET Server GC`, 4 × `.NET BGC`) | Σ 2.0 % | Σ 2.5 % | < 1 % |

- **The pool: 13 workers** = `Environment.ProcessorCount − 2` = 14 threads with the calling sim worker (`FdpWorkerPool.cs:34`, `HROT_SIM_THREADS`
  unset). ⚠ The GC is **Server GC** (4 heaps).
- **What the workers do** (sampled, scale-1 ON): phase batches **6.2 %**, the EQS background lane (`TakeBackground` → `EqsSolverSystem.Solve`)
  **12.9 %**, waiting in `SemaphoreSlim.Wait` **80.8 %** — matches the OS busy % (≈ 19 %), so spinning is not significant.
- **The shape:** two threads are saturated (sim worker 91 %, UI 82 %) and the 13 pool workers idle ~80 %. ⇒ the ~28–32 % machine load is the two
  serial threads plus the pool's short bursts.

## 3. C-3 · the pool (`CE-4545`) — per phase, scale-1 rec ON (wall = the sim worker inside `FdpWorkerPool.Run`; work = non-waiting samples in the phase's batches on all 14 threads)

| phase (job) | runs / tick | wall ms / tick | work ms / tick | efficiency = work / (wall × 14) |
|---|---|---|---|---|
| `WeaponDispatcherSystem+DispatchJob` | 0.68 | 2.98 | 30.3 | **73 %** |
| `CarKinematicsSystem+MoveJob` | 0.46 | 2.37 | 3.8 | **11 %** |
| `BrainTickSystem+BrainJob` | 0.68 | 1.90 | 11.7 | 44 % |
| `StressSystem+StressJob` | 0.66 | 1.60 | 9.5 | 42 % |
| `CalloutSystem+DecideJob` | 0.46 | 0.87 | 6.3 | 52 % |
| `ThreatEvaluationSystem+EvaluateJob` | 0.36 | 0.83 | 2.7 | 23 % |
| `SquadCoordinationSystem+SquadJob` | 0.39 | 0.77 | 2.2 | 20 % |
| `StaffComposerSystem+ComposeJob` | 0.17 | 0.37 | 3.3 | 64 % |
| `EntityQuery.ForEachParallel` · `RaycastSolverSystem` | 0.06 · 0.06 | 0.19 · 0.15 | 0.05 · 0.06 | 2 % · 3 % |
| **all** | | **12.0** | **70.6** | **42 %** |

- A phase runs **less than once a tick** (0.2–0.7): the systems' own rates (`ShouldRunThisFrame`) skip ticks.
- **Too small to pay for the hand-off:** `CarKinematicsSystem` (11 %: 3.8 ms of work spread over a 2.4 ms wall), `RaycastSolverSystem`,
  `EntityQuery.ForEachParallel` (≤ 0.2 ms wall, ≤ 3 %), `ThreatEvaluation` / `SquadCoordination` (20–23 %).
- demo-city: all phases 12.3 ms wall, 89.1 ms work, **52 %**; recorder OFF scale-1: 12.6 / 76.1, 43 %.

## 4. C-4 · the small-systems tail (`CE-4546`) — 130 systems ≤ 1 ms (the editor's own timings, scale-1 rec ON), 22.4 ms / tick

Read / write sets from the access tracer (`HROT_ACCESS_TRACE=1`, 60 s playing, Tick context, readers `system:<Name>`): component reads / writes,
singletons, bus publish / read, entity create / destroy (as one shared resource). Conflict = one writes what the other reads or writes.

- traced: **121** systems (21.70 ms); **9** never entered by the tracer (0.67 ms: `EventHistoryCaptureSystem` ×3, `DataBreakpointSystem`, …).
- conflicting pairs: **296 of 7 260**.
- greedy conflict-free groups (heaviest first): **9** — group 1 holds most of the tail (`CgfThreatEvaluationSystem` 0.87, `WeaponAmmoMirrorSystem`
  0.82, `MapCullingSystem` 0.71, `TrafficBodyFeedSystem` 0.64, `ChannelArbitrationSystem` 0.58, `StaffComposerSystem` 0.57 … and ~60 more);
  groups 2–9 are 1–11 systems each (largest member 0.72 ms).
- ⚠ **ESTIMATE, not a measurement:** each group run side by side ⇒ the traced tail **21.7 → 4.6 ms (≈ 17 ms / tick)**, before any hand-off cost
  and ignoring the scheduler's ORDER between systems (only data conflicts are counted) and accesses the tracer does not see.
- Full group list: scratch `wtm/cu/acc/c4.md`.

## 5. C-5 · the UI thread (`CE-4547`) — scale-1 rec ON, 2-D map, 60 fps cap

Frame **32.5 ms mean / 49.5 p95 (30.8 fps)** — it never reaches the cap (main thread 82 % busy, 0 % waiting in the trace). demo-city: 19.6 / 27.8 ms.

| of the UI thread's sampled time | % |
|---|---|
| `EditorSubsystem.Update` | **37.3** |
| ↳ `StagedPresentation.Pull` (the staging pull + the map's per-pulled-tick step) | 23.6 — of it `StatelessGizmoSystem` ≈ 9–12, `StagingWorlds.Pull` → `SyncPresentationCore` ≈ 5 |
| ↳ `MapCanvas.Update` → `AudioLayer.Update` → `SoundSources.Collect` / `Movers` | 13.7 |
| `WindowManager.Render` (ImGui windows) | **21.5** — `MessageLogPanel` 8, `EntityInspector` 3, `MissionSummary` 2.5, `EventBrowser` 2, `Orbat` 1.6 |
| `DrawWorldAll` → `MapCanvas.DrawLayers2D` | **18.8** — `TerrainLayer2D.Draw` 16.0, `EffectLayer` 2.7 |
| native, outside managed frames (raylib swap / GL) | 17.1 |
| `ImGuiRenderer.RenderData` | 5.0 |

## 6. C-6 · the GC (`CE-4548`)

| | scale-1 ON | scale-1 OFF | demo-city ON |
|---|---|---|---|
| allocation rate (counters, 30 s) | **106 MB/s** (≈ 8.9 MB / tick) | 102 MB/s | 108 MB/s |
| GCs in the 21 s trace: gen0 / gen1 / gen2 · pause total · max | 1 / 1 / 0 · 0.3 ms · 0.3 | 2 / 0 / 1 · 5.4 ms · 4.1 | 18 / 7 / 1 · 7.5 ms · 2.9 |
| time paused by GC (counters, mean / max per s) | 1.7 / 16 ms | 1.9 / 31 ms | 1.1 / 7 ms |
| gen0 budget · GC heap | 564 MB · 3.5 GB | 711 MB · 2.8 GB | 227 MB · 2.3 GB |
| allocated per tick: UI · sim worker · pool (13) · TP workers | 2.9 MB · **2.2 MB** · 1.2 MB · 1.4 MB | 2.6 · 1.7 · 0.9 · 0.07 | 3.9 · 1.8 · 0.07 · 0 |

Top types (scale-1 ON): `Byte[]` 15.3 MB/s (the recorder), `String` 10.0, `TerrainPrism` 4.6, `Entry[]` 4.2, `Int32[]` 3.3, `PaddedVersion[]` 2.7.
⇒ **the GC is not the bottleneck** (≤ 0.2 % of the time paused); Server GC's large gen0 budget absorbs ~100 MB/s.

## 7. C-7 · demo-city vs scale-1 (`CE-4549`)

demo-city (5.6 k entities at load, ~9.9 k playing) vs scale-1 (12.6 k → 13.6 k): tick 75.8 vs 84.1 ms; machine 32.4 vs 27.9 %; the serial share is
the same (81 % vs 85 %), the same pieces in the same order (recorder 9.3, `AcquireView` 6.7, `Publish` 4.5, `StepTimeline` 3.7, event history 2.1).
The large terrain adds to the serial copies (`AcquireView` +2.6, recorder +2.2) and to the UI (2-D terrain draw 16 % vs 3 %; frame 32.5 vs 19.6 ms).

## 8. The rendering (`CE-4550` … `CE-4553`) — scale-1 live, playing, uncapped (`--target-fps 0`), hidden window (off-screen target, no vsync / compositor), 2200 × 1200

Poses by `POST /film/shot`: ① street = `lookAt [2750,2750,1.7]`, 30 m, pitch −8 (①b 12 m, pitch −5); ② whole terrain from above = `lookAt` the
centre, 7 500 m, pitch −60 (②b across: 5 000 m, pitch −18). GPU = `typeperf \GPU Engine(*)\Utilization Percentage`, this process's 3-D engine.

### R-1 · the frame at ① and ② (`CE-4550`)

| pose | fps | frame ms mean / p95 | UI thread busy | GPU 3-D (this process) | terrain triangles / meshes (= draw calls) | garbage / frame |
|---|---|---|---|---|---|---|
| ① street 30 m | 37.7 | 26.5 / 42.7 | **97.5 %** | 17.4 % | 0.28 M / 29 | 4.3 MB |
| ①b street 12 m | 36.5 | 27.4 / 43.6 | 97.1 % | 23.1 % | 0.36 M / 82 | 3.1 MB |
| ② whole, above (run 1) | **6.7** | **148.4** | 93.6 % | 23.1 % | 2.61 M / 1 311 | — |
| ② whole, above (run 2) | 9.1 | 110.0 / 115.9 | 62.2 %¹ | 21.5 % | 2.61 M / 1 311 | 8.6 MB |
| ②b whole, across | 6.7 | 148.3 | 93.7 % | 19.7 % | 2.65 M / 1 299 | — |

⇒ **CPU-bound** at both: the GPU's 3-D engine is ≤ 23 % busy, the UI thread is saturated. ¹ run 2 measured right after the pose change, 8.6 MB / frame
of garbage — a GC phase likely overlapped (unverified). Vsync: the hidden window has none; the visible window adds the 60 fps cap (`--target-fps`).

### R-2 / R-4 · what the frame does at ② (`CE-4551`, `CE-4553`) — the UI thread's sampled stacks, 10 s

| of the UI thread at ② (148 ms frame) | % |
|---|---|
| **`EntityBodyLayer3D.Draw3D`** — the bodies | **67.8** |
| ↳ `DrawParts` (one `Raylib.DrawMesh` per body PART, native) | **48.0** (43.5 native) |
| ↳ `DrawDoor` (one `DrawMesh` per door leaf) | **14.8** |
| ↳ `FigurePose` / `DrawFace` | 2.6 / 2.3 |
| `EditorSubsystem.Update` (staging pull 12.8 — gizmos 8.0; audio 2.4) | 15.5 |
| `MapCamera3D.Pick` → `EntityBoxPicker.TryPick` (the hover pick, every frame) | 3.7 |
| **`TerrainLayer3D.Draw3D`** → `TerrainChunks3D.Draw` (1 311 meshes) | **3.1** |
| `EffectLayer.Draw3D` | 1.4 |
| ImGui windows | 3.4 |

At ① (26.5 ms) the order is different: the hover pick **20.9 %** (`EntityBoxPicker.TryPick` 11, `DoorLeaf3D.TryLeaf` 7.4), `EffectLayer.Draw3D`
11.7, the update 31.8 (pull 17.9 — gizmos 8.9; audio 12.6), ImGui 19.6 + 8.3 — the body layer is under 2.5 %.

**Per layer, by switching each map row off at ② (`POST /map/layers`, 15 rows that were on):** no row moved the frame beyond the noise (146–154 ms
each, base ~148). ⚠ The bodies are drawn by `EntityBodyLayer3D`, which no row switches — `Units.Entities` OFF left the frame at 146–151 ms. The
3-D layers have no draw-call / triangle counters except the terrain's (`TerrainDrawStats`), so per-layer calls / triangles are not measurable over
HTTP; the per-layer table is the sampled time above.

### R-3 · the level of detail as measured vs Map 3D §6ah / §6ai (`CE-4552`)

| design | as measured |
|---|---|
| §6ah L-2: terrain chunk level by eye distance (< 1.5 chunk → 0 … else 3) | ② draws 1 311 meshes / 2.61 M triangles for the whole 5.5 km terrain; ① draws 29–82 meshes / 0.28–0.36 M ⇒ far chunks are frustum-culled and coarse. ⚠ The level picked per chunk is not exposed (`TerrainDrawStats` holds totals only) — the counts are consistent with mostly level 3 at ②, not proven |
| §6ah L-5: buildings / prisms / water — one objects mesh per chunk, built once | inside the 1 311 meshes; not separable from the ground in the counters |
| §6ah L-9: GPU budget 1.5 GB for finer levels | resident 209–388 MB |
| bodies / doors (not in §6ah): CE-4388 frustum cull, 40 m sphere (`EntityBodyLayer3D.cs:144-151`) | ⛔ **no distance cull, no far representation, no instancing**: at ② every man, vehicle and door in the frustum is drawn part by part — 68 % of the frame |
| the terrain's own cost at ② | 3.1 % of the UI thread — ⇒ the terrain is not what makes ② slow |

## Ids

`CE-4543` C-1 · `CE-4544` C-2 · `CE-4545` C-3 · `CE-4546` C-4 · `CE-4547` C-5 · `CE-4548` C-6 · `CE-4549` C-7 · `CE-4550` R-1 · `CE-4551` R-2 ·
`CE-4552` R-3 · `CE-4553` R-4 — no new ids allocated.

## Steps that failed or were redone

- The first two scale-1 runs fired before the cluster was operable (`notOperable`: no cluster state yet) — void; the launcher now waits for `canOperate`.
- The first set ran with `HROT_FRAME_STATS=1` (the launcher's default) — void except as the instrument's cost (§0); rerun without it.
- Rendering run 1's street pose followed no unit (the entity list's shape) — redone as a fixed street-level shot (run 2).

---
_Windows session, 2026-11-13_
