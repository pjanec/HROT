<!--STATUS
state: LIVE — re-dispatched unstarted 2026-11-13 (rule 1a: + the rendering measurements R-1 … R-4)
updated: 2026-11-13
current-answer: the whole file
related-designs:
  - STATUS_Windows_Unified_Run.md (on exp/windows-threaded-measure) — the last real-core numbers (W-2: editor tick 129.7 ms at 1 thread, 69.7 at 8)
  - ../../DESIGN_Parallel_Simulation.md (on exp/threaded-build) — §13 / §16 the one pool and the dependency-scheduler question (POC 4 measured it then rejected it), §18 the per-entity phases
  - ../../DESIGN_Threaded_Simulation_Presentation.md (on exp/threaded-build) — §0a the threaded editor (the staging, the sim worker)
  - ../../DESIGN_Map_3D_Mode.md — §6ah the 3-D terrain by chunk and level of detail (CE-3423): what the renderer is MEANT to do with a far view
  - ../../DESIGN_Terrain_World.md — the terrain, its 2-D sibling of §6ah (§10a)
-->

# HANDOFF — coordinator → WINDOWS session: WHY THE EDITOR USES ~35–40 % OF THE CPU, AND WHY THE 3-D VIEW IS SLOW WITH THE WHOLE TERRAIN IN VIEW — measure only

**For:** the Windows session (hidden window, always). **From:** coordinator. **Dispatched at `<stamp>`**.
🔒 User, `2026-11-13`: *"cpu utilization when i run the scale-rig-scale-1-1000-2s scenario is just aroung 40%. very bad parallelization"* ·
*"i am running editor so no networks whatsoever. without the recorder the cpu load is around 35-37%. yes pls measure whatever needed to help
utilize the cores, this is enormous waste."* · *"and also, that scenario has very poor rendering framerate when all terrain in frustum. some graphical
performance optimization is a must"* ⛔ Autonomous, MEASURE ONLY (no fix): a failing step gets one retry, then its error in the status
file, and the next step runs.

## What we know, and what we do not

Amdahl on the last run (demo-city, 1 000 men): tick 129.7 ms at 1 thread, 69.7 at 8 ⇒ ≈ 69 ms parallelises, **≈ 61 ms stays serial** ⇒ ≤ 2.1×
whatever the cores. At 8 threads the systems account for ~40 ms of the 69.7 — ⛔ **~30 ms of the tick is OUTSIDE every system and was never
profiled on real cores.** This run names every millisecond of it, on the user's scenario.

## Where to work

Build `origin/exp/threaded-build` at its head, **Release**; results on `exp/windows-threaded-measure` (merge first), status
`docs/blueprints/batches/STATUS_Windows_Core_Utilisation.md`. The user's scenario **`scale-rig-scale-1-1000-2s`** (regenerate with
`scripts/scale-rig.py --terrain scale-1 --men 1000 --write-only` if absent), the EDITOR (`-m editor`), playing, the defaults ON (no env vars —
the settings file), recorder ON and OFF.

## The measurements

| # | measure | report |
|---|---|---|
| **C-1** · `CE-4543` | ⭐ **CPU and threads**: process CPU % and busy cores, recorder ON / OFF; per THREAD (main / UI, the sim worker, each pool worker, the EQS background lane, the GC threads): busy % over 30 s (`dotnet-counters` / PerfView / Process Explorer — name the tool) | the table; how many pool workers exist vs `HROT_SIM_THREADS` auto |
| **C-2** · `CE-4544` | ⭐⭐ **the sim worker's tick, millisecond by millisecond** — `dotnet-trace collect --providers Microsoft-DotNETCore-SampleProfiler` (or PerfView CPU stacks) on the sim worker over 20 s playing: every frame of the tick, **especially everything OUTSIDE the systems** (staging publish, the tick-start view capture, the module snapshots sync, the recorder, the kernel's phase dispatch, the batch merges / follow-ups, waits on the pool or EQS, locks, the GC) | ⭐ a ranked table: piece · ms per tick · serial or parallel · on the critical path or overlappable; the sum equals the tick |
| **C-3** · `CE-4545` | ⭐ **the pool**: how long workers sit IDLE inside a tick (between phases, waiting on a serial step) — per phase: its wall time vs the sum of its batch times × threads (efficiency) | per phase: wall ms, work ms, efficiency %; which phases are too small to pay for the hand-off |
| **C-4** · `CE-4546` | ⭐ **the small-systems tail**: every system ≤ 1 ms, its ms, and the component types it reads and writes (the scheduler's declarations / the read-check survey `HROT_READ_CHECK=2`) — ⇒ which could run SIDE BY SIDE (no write conflict) | the list + a count of conflict-free groups; the ms a concurrent run of the tail would save (an estimate, stated as such) |
| **C-5** · `CE-4547` | **the UI thread**: its frame, and what it waits on (the pull, ImGui render, vsync) — so a fix to the tick is not mistaken for a UI cost | the UI frame's breakdown |
| **C-6** · `CE-4548` | the GC: gen0/1/2 counts and pause time per 30 s, allocated bytes per tick (sim worker and pool) | the numbers — ⚠ a GC pause stops every thread |
| **C-7** · `CE-4549` | the same C-1 / C-2 on `scale-rig-demo-city-1000-2s` (the earlier runs' scenario) — what the large terrain adds | the delta |

## The rendering — the 3-D view with the WHOLE terrain in the frustum

The same scenario and build, the editor's 3-D view: ① close to the ground (a street), ② the whole terrain in view (high, looking down / across).
The existing instruments: `GET /diagnostics/render` (`RenderTelemetry`), the map's stats overlay (`MapStatsOverlay`), `[FrameStats]`
(`HROT_FRAME_STATS=1`: update / draw / ui / ImGui render, the largest draw list).

| # | measure | report |
|---|---|---|
| **R-1** · `CE-4550` | ⭐ **the frame at ① and ②**: UI frame ms, vsync OFF if a switch exists (else say so); CPU-bound or GPU-bound — the UI thread's busy % vs the GPU's (Task Manager's GPU engine / PIX / RenderDoc frame timing — name the tool) | the table ① vs ② |
| **R-2** · `CE-4551` | ⭐ **what is drawn at ②**: draw calls, triangles / vertices, per LAYER (terrain chunks, buildings, trees / vegetation, units, traffic, roads, overlays / gizmos, labels) and each layer's draw ms | the per-layer table, the heaviest first |
| **R-3** · `CE-4552` | ⭐ **the level of detail the terrain actually uses at ②** (Map 3D §6ah, CE-3423): which chunk LOD levels are picked, how many chunks drawn, whether far chunks are culled / coarsened as the design says; the same for buildings / trees (instanced? culled by distance? a far representation?) | the as-measured vs the design's intent, row by row |
| **R-4** · `CE-4553` | per-frame CPU work that scales with what is visible (mesh rebuilds, culling loops, per-entity draw prep) — the UI thread's profile at ② (`dotnet-trace`, 10 s), and the bytes allocated per frame | the ranked list on the UI thread |

## Report (top of the status file)

⭐ **The ranked serial list — every millisecond of the tick that is NOT spread over the cores, with its share** — then C-1's per-thread table,
C-3's phase efficiency, C-4's tail groups, and the recorder's own cost; ⭐ then the RENDERING: ① vs ②, the per-layer table at ②, the LOD as measured vs §6ah, CPU- or GPU-bound. ⛔ No fixes, no conclusions beyond what the numbers say.

---
_coordinator lane, 2026-11-13_
