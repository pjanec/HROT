# Note: headless HROT spends half its main thread drawing map overlays nobody sees — measured on Stechovice (Windows, physical)

| | |
|---|---|
| **From** | `unreal-ig`, 2026-11-23 |
| **For** | `ui` (the editor / map presentation, the gizmos); FYI whoever owns the kernel's frame and the recorder |
| **User, verbatim** | *"headless performance is important (for running with Unreal as presentation layer later)"* · *"it should be way more than 37FPS for such small amount of thinking entities. can you measure where the bottleneck is?"* · *"yes please tell them"* |
| **Asks** | §3 — ① no map presentation step / gizmos on a headless node · ② the road-network gizmo caches and culls · ③ the per-frame garbage. Leans first; the decisions are yours and the user's |

## 1. The set-up (reproducible)

- **Code:** HROT `ui` `96722b016`, Release `Hrot.ClusterRunner`. **Scenario:** `showcases/stechovice-battle`, time scale 1.
- **Machine:** AMD Ryzen 7 6800H (8 cores / 16 threads), 32 GB, RTX 3070 Laptop.
- **Scenario size:** 4,887 entities. About 77 soldiers and 9 vehicles / aircraft think; 22 units have no body. The rest is terrain: 4,431 doors / building parts, 167 weapon parts, about 180 others.
- **Runs:**
  - **Headless:** `--mode editor --headless --domain 7`, `HROT_HIDDEN_WINDOW=1`, `HROT_DEBUG_API_PORT=8111`, then `POST /scenario/load/live`, `POST /sim/play`.
  - **UI:** the same command without `--headless`, plus `HROT_FRAME_STATS=1`.
- **Profile:** `dotnet-trace collect -p <pid> --profile dotnet-sampled-thread-time --duration 00:00:00:30`, then `dotnet-trace convert --format Speedscope`. Taken 30 s after play, with the battle running.

## 2. Measured

| | headless | UI (window visible) |
|---|---|---|
| load (terrain already imported, navmesh prefilled) | 25 s | 19 s after the load is accepted (navmesh 30,951 tiles ≈ 12 s of it) |
| real time kept at 1× | ✅ 0.99–1.00 | ✅ 1.00 |
| simulation ticks / s | **≈ 23** (one pass of the unthrottled inline loop ≈ 44 ms; the step = wall time since the last) | 37 fps, frame 26.9 ms (update 21.6 · draw 1.5 · UI 3.5) |
| cores used | 2.1–2.4 of 16; ⚠ **1.04 while PAUSED** | — |
| parallel sim off (`HROT_PARALLEL_SIM=0`) vs on with 14 threads | 23.7 vs 24.0 ticks/s, 2.21 vs 2.15 cores — **no difference** at this size | — |
| garbage | managed heap 3.4 GB, working set 5.2 GB after a few minutes; gen2 81 | update ≈ 2 MB + UI ≈ 1 MB per frame; gen2 487 in 5 min; 0.1–1 s hitches |

**Headless main thread, 30 s profile** (inclusive share of the thread's time):

| | share |
|---|---|
| ⛔ `StagedPresentation.Pull` → `MapPresentationStep.RunPerPulledTick` → `TogglablePostSimulationGroup` → `StatelessGizmoSystem` | **52 %** (gizmos alone 46 %) |
| ⛔ — `RoadNetworkGizmo.Draw` | **28.9 %** — of which `TerrainWorld.GroundHeightOf` **19.3 %** (a height per curve sample, every pass, the whole net) |
| ⛔ — `DoorStates.Of` 7.2 % + `DoorLeafGizmo.Draw` 3.7 % | ≈ 11 % (4,431 doors) |
| ⛔ — `GizmoFamilyVisibilityPolicy.IsEntityVisible` | 3.2 % |
| `ModuleHostKernel.UpdateInternal` (the simulation) | 39.8 % |
| — `SyncPresentationFrom` (sim → presentation world) | 7.3 % |
| — `BrainTickSystem` (parallel) | 5.2 % |
| — `RecorderTickSystem` + `RecordDeltaFrame` (live recording) | ≈ 7 % |
| — `StaticObstacleBakeSystem` (`SignatureNow` 3.2 %) | 3.6 % |
| — `StagingWorlds.Pull` / `Publish` | 4.0 / 3.8 % |
| — `BridgeClosureSystem.Recompute` | 1.3 % |

Per-system times over 60 s (`GET /diagnostics/architecture`, `ExecutionCount` deltas) agree: the leaf systems sum to **18.5 ms per sim tick**. ⇒ The simulation proper could tick at ≈ 50 Hz. The rest of each ≈ 44 ms pass is the presentation step above.

## 3. Asks (our leans)

| # | ask | lean |
|---|---|---|
| ① | **a headless node runs no map presentation step and no gizmos** (`MapPresentationStep` / `StatelessGizmoSystem` / `DoorStates` for drawing) | ⭐ nothing reads the draw buffer headless; skip the step where the host has no view (or when no client asked for gizmos — the DPS gizmo stream has its own layers). Expected: ≈ 2× the tick rate on the same work |
| ② | **`RoadNetworkGizmo` stops rebuilding the whole net every pass** | ⭐ build the road polyline (curve samples + ground heights) once per terrain / road change and cull to the view; today the net is ~63 k lines of SUMO XML, redrawn with a height lookup per point each frame — 12.3 ms/frame in the UI too |
| ③ | **the per-frame garbage** (R-2084, the hot-path rule) | update ≈ 2 MB/frame; the UI's inspector 412 KB, event browser 385 KB, message log 165 KB per frame — the measured hitches |
| — | FYI | a PAUSED headless node uses a whole core (a busy loop: `SubsystemOrchestrator.Run` sleeps only with `HROT_HEADLESS_MAX_HZ`); `KernelFrameTimings` (the split between systems) is on only with the window (`HROT_FRAME_STATS`), so a headless pass cannot be accounted without a profiler; the first-use Stechovice import time was not measured cleanly (our script's fault) |

## 4. For the record — Unreal on top

The Unreal client was run as a game (editor binary, `-game`, 1920 × 1080) on the same battle, over DPS from the headless node. It loaded `Stechovice_look` in 25 s, drew the 89 bodies, and ran at **38–40 fps** (from its log's frame counter; the CSV capture was lost). The headless node used 2.4 cores meanwhile. Unreal's own bottleneck is ours to measure next.

## Addendum (2026-11-23, after dispatch) — how the user wants this worked

🔒 User, verbatim: *"with report and push after each step"* — i.e. take the asks one at a time (① ② ③), and after EACH: measure it on the same
set-up (§1: the headless ticks per second and cores, the profile's shares; the UI frame for ②), report the before / after in a note to
`unreal-ig`, and push — before starting the next.
