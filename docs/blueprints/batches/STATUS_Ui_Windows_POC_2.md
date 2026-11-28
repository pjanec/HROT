<!--STATUS
state: LIVE — measured, done
updated: 2026-11-12
current-answer: the whole file (§0 is the verdict)
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — OWNS Part 1 (§11 the aggregate, §12 POC 2 as-built)
  - ../../DESIGN_Threaded_Simulation_Presentation.md — OWNS Part 2 (§14 the target shape, §14.3 on exp/threaded-poc-2: POC 2's verdict)
  - HANDOFF_Coordinator_To_Ui_Windows_POC_2_Measure.md (on `coordinator`, dispatched at 0db8f26a6) — the handoff this answers
  - STATUS_Ui_Windows_Parallel_Sim.md (on exp/parallel-sim) — POC 1 on the same machine, the same method
-->

# STATUS — POC 2 measured on the Windows machine (Part 1 parallel simulation, Part 2 threaded editor)

Measure only, no code changed. Every process ran hidden (`HROT_HIDDEN_WINDOW=1`). Part 1 and Part 2 ran one after the other, never at once.

## 0. Verdict

| | |
|---|---|
| ⭐ **Part 1 — everything on (`rec-all`)** | whole CGF frame **266.1 → 181.1 ms (1.47×)**, **8.6 → 11.3 fps** (recorded). Noise on that baseline: **0.5 ms** (a repeat ran 265.6) |
| ⭐ **the brain pre-score (Q-4) carries it** | BrainTick **104 → 56 ms** (`rec-q4`), **50 ms** with everything. Thread curve (unrecorded, Throughput): **101 → 85 → 57 → 51 ms** at 1 / 2 / 4 / 8 threads ⇒ **2.0×**, flattening after 4 (CGF is pinned to 7 logical CPUs ≈ 3.5 cores). The VM's 92 → 57 ms on 2 threads reaches **51 ms** here |
| the per-query timer (Q-0) | **−12.5 ms of the brain** (104.1 → 91.6), −16.5 ms whole frame — the VM's ~13 ms confirmed |
| Q-1 stamp on a real change | **−9 ms** whole frame (266.1 → 256.9); the systems show no single source. Smaller than POC 1's whole-frame noise (±2.3 %) ⇒ **not established** by one run |
| Q-2 off-frame egress | CGF whole frame **unchanged** (266.2); CGF `CycloneEgressSystem` 33.7 → 28.1 ms. ⭐ **SimHost's frame 92 → 40 ms** — its egress is what leaves the frame |
| ⭐ **Part 2 — the threaded editor's main thread** | switch OFF: **~90 ms/frame** (update 69, of which the kernel 62; draw 15; ui 7). `=2`: **drawing frames mean 15.8 ms, p95 24.7 ms** (X-4F), FrameStats mean 19.7 / median 16.5 ms. ⇒ the main thread's own cost (draw + panels, no simulation) is **~16 ms on real hardware**, vs 600–730 ms in the VM's software rendering |
| Part 2 — the sync S → A | **tables 4.3 ms, rows + map 1.7 ms** per sync (VM ≈ 9 and 3–4 ms) — but only **10 syncs** happen before the UI world freezes (§3) |

## 1. Machine, branches, commands

| | |
|---|---|
| CPU | AMD Ryzen 7 6800H — **8 cores / 16 logical**, 31 GB RAM, Windows 11 (laptop). Czech locale (decimal commas in the editor logs, see §5) |
| Part 1 | **`exp/parallel-sim-2` @ `b06adac9f`** — Release build 0 errors |
| Part 2 | **`exp/threaded-poc-2` @ `23dc7797d`** — Release build 0 errors |
| pinning (Part 1) | `--pin` ⇒ orchestrator 2 · **cgf 7** · simhost 7 logical CPUs |

```
# Part 1 (exp/parallel-sim-2)
python scripts/parallel-sim-measure.py --pin --poc2 --out <scratch>/P1
HROT_BRAIN_PRESCORE=1 python scripts/parallel-sim-measure.py --pin --threads 1 2 4 8 --mode throughput --out <scratch>/P1-threads
python scripts/scale-rig.py --terrain demo-city --men 1000 --vehicles 50 --aircraft 10 --warmup 30 --seconds 30 --pin --record --json <scratch>/P1-noise-rec-serial.json   # the noise repeat
# Part 2 (exp/threaded-poc-2)
python scripts/scale-rig.py --terrain scale-1 --men 1000 --vehicles 50 --aircraft 10 --write-only
python scripts/threaded-poc2-measure.py --out <scratch>/P2 --no-guard
python scripts/threaded-poc2-measure.py --out <scratch>/P2-guard
```

## 2. Part 1 — ms per frame, CGF node (every system now from the rig's `all_ms`, no top-8 cut-off)

**`--poc2`, every column RECORDED:**

| system | rec-serial | rec-qtoff (Q-0) | rec-q1 | rec-q2 | rec-thr (POC 1) | rec-q4 | **rec-all** |
|---|---|---|---|---|---|---|---|
| **whole frame** | **266.1** | 249.6 | 256.9 | 266.2 | 246.7 | 194.4 | **181.1** |
| speed-up vs rec-serial | 1.00 | 1.07 | 1.04 | 1.00 | 1.08 | 1.37 | **1.47** |
| frames/s | 8.6 | 9.0 | 8.8 | 8.1 | 9.1 | 11.4 | **11.3** |
| **BrainTickSystem** | 104.11 | 91.59 | 103.80 | 101.96 | 105.41 | **56.09** | **50.37** |
| StressSystem | 16.09 | 15.02 | 15.53 | 15.12 | 6.96 | 7.52 | 7.09 |
| CgfThreatEvaluationSystem | 6.85 | 6.80 | 6.74 | 6.01 | 2.27 | 2.37 | 2.20 |
| CalloutSystem | 14.72 | 14.58 | 13.96 | 12.98 | 14.39 | 14.42 | 12.24 |
| WeaponDispatcherSystem | 20.50 | 19.34 | 18.79 | 22.06 | 19.77 | 19.37 | 17.68 |
| SquadCoordinationSystem | 5.93 | 4.82 | 5.96 | 6.00 | 6.03 | 6.37 | 5.16 |
| RecorderTickSystem | 20.42 | 22.90 | 19.05 | 21.43 | 19.76 | 19.66 | 19.30 |
| CycloneEgressSystem | 33.70 | 32.08 | 33.01 | 28.12 | 32.70 | 31.33 | 25.88 |
| CycloneNetworkIngressSystem | 15.98 | 14.37 | 12.41 | 16.17 | 11.23 | 9.32 | 6.31 |
| garbage B/frame | 2.71 M | 2.73 M | 2.67 M | 2.60 M | 2.66 M | 2.68 M | 2.66 M |
| SimHost whole frame | 92.4 | 99.2 | 102.5 | **40.4** | 99.2 | 104.6 | **36.7** |

**The thread curve, `HROT_BRAIN_PRESCORE=1`, Throughput, NOT recorded** (⚠ the variable is set for the whole run, so the serial column has it too —
it has no effect there, the pre-score runs on the parallel path: serial BrainTick 101.1 ms):

| system | serial | 1 thr | 2 thr | 4 thr | **8 thr** |
|---|---|---|---|---|---|
| **whole frame** | 236.2 | 240.9 | 214.1 | 173.0 | **164.1** |
| speed-up vs serial | 1.00 | 0.98 | 1.10 | 1.37 | **1.44** |
| frames/s | 9.6 | 9.5 | 10.2 | 12.5 | **13.0** |
| **BrainTickSystem** | 101.14 | 103.53 | 84.72 | 57.24 | **51.18** |
| StressSystem | 16.15 | 17.39 | 13.65 | 8.54 | 6.53 |
| CgfThreatEvaluationSystem | 7.08 | 7.75 | 5.36 | 3.01 | 2.21 |
| CalloutSystem | 14.85 | 16.54 | 15.61 | 14.80 | 15.75 |
| WeaponDispatcherSystem | 18.23 | 19.04 | 17.66 | 17.95 | 16.77 |
| CycloneEgressSystem | 30.89 | 30.29 | 30.63 | 30.28 | 30.18 |
| CycloneNetworkIngressSystem | 12.19 | 10.55 | 10.20 | 6.38 | 5.79 |
| garbage B/frame | 1.04 M | 1.04 M | 1.03 M | 0.93 M | 0.88 M |

⚠ `CycloneNetworkIngressSystem` falls with the thread count too (12.2 → 5.8 ms) though it is not converted — not explained here.
Unrecorded serial **236.2 ms** matches POC 1's Windows baseline (**242 ms** mean, 235–247).

## 3. Part 2 — the threaded editor (scale-1, 1 000 men, `--mask ui-no-inspector`, 120 s playing)

FrameStats: the second half of each run (loaded and playing), re-parsed by hand (§5).

| | switch OFF | `HROT_THREADED_KERNEL=2` | OFF (guard run) | `=2` + `HROT_WORLD_GUARD=1` |
|---|---|---|---|---|
| frames in the run | 12 330 | 21 443 | 11 342 | 19 206 |
| **frame mean (mean of the periods)** | **89.9 ms** | **19.7 ms** | 83.5 | 20.7 |
| frame median of the period means | 110.5 | 16.5 | 96.8 | 17.6 |
| worst period p95 | 171.4 | 54.7 | 139.0 | 61.7 |
| main thread: update / draw / ui ms | 68.6 / 14.6 / 6.6 | **3.1 / 10.8 / 5.7** | 63.2 / 14.0 / 6.2 | 3.5 / 11.1 / 6.0 |
| `(kernel Update, whole)` | 62.2 | 19.6 (on the worker) | 57.2 | 20.6 |
| **X-4F drawing frames** (with the world) | — | **318 frames, mean 15.8, p95 24.7 ms** | — | 304, mean 16.5, p95 26.8 |
| **[POC2] sync S → A** | — | **10 syncs: tables 4.32 ms, rows + map 1.69 ms** | — | 10 syncs: tables 4.52, rows + map 1.79 |
| worker faults | 0 | **20** | 0 | **20** |
| WorldGuard hits | — | (off) | — | **1** |

⚠ **The `=2` numbers are NOT a working threaded editor**, as the handoff said: the UI world stops after **10 syncs**. The worker throws, every
time, at the same place:

```
[X-4] worker fault: System.InvalidOperationException: Entity Entity(13390, v1) is not alive
   at Fdp.Core.EntityRepository.GetDisType(Entity entity)                                   FDP/Engine/Fdp.Core/EntityRepository.cs:597
   at Hrot.Presentation.Map.MapLayerAssignmentSystem.Execute(ISimulationView view, …)       Hrot/Engine/Hrot.Presentation/Map/MapLayerAssignmentSystem.cs:79
   at Fdp.ModuleHost.Scheduling.SystemScheduler.ExecuteSystem(…)                             FDP/Engine/Fdp.ModuleHost/Scheduling/SystemScheduler.cs:278
   at Fdp.ModuleHost.ModuleHostKernel.UpdateInternalCore(…)                                   FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs:945
   at Hrot.Editor.EditorSubsystem.TickNodeFrame(Single deltaTime)                            Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs:3072
   at Hrot.Editor.EditorSubsystem.KernelWorkerLoop()                                         Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs:2874
```
(the guard run: the same, `Entity(13392, v1)`). ⇒ what `=2` measures is the main thread's own draw + panels and the sync cost.

**The guard's one hit:**
`A written by the UI inside the sync (lost at the next sync — a command) | Write | Fdp.Core.SimTransform | layer.Update: Map camera` —
`MapCameraLayer.Commit` (`Hrot/Engine/Hrot.Presentation/Map3D/MapCameraLayer.cs:225`, from `MapCameraLayer.Update` :216 ← `MapCanvas.Update`
← `EditorSubsystem.Update` :2991). The map camera writes its `SimTransform` into world A directly.

## 4. Noise

| column | samples | spread |
|---|---|---|
| Part 1 `rec-serial` whole frame | 266.1 · 265.6 (repeat) | **0.5 ms** |
| Part 1 `rec-serial` BrainTick | 104.1 · 101.3 | 2.8 ms |
| unrecorded serial whole frame, POC 1 + POC 2 (5 runs, same machine) | 246.3 · 235.4 · 240.6 · 246.7 · 236.2 | **11.3 ms (±2.3 %)** |
| Part 2 switch OFF, frame mean | 89.9 · 83.5 | 6.4 ms |
| Part 2 `=2`, X-4F drawing frame | 15.8 · 16.5 | 0.7 ms (the guard run carries the guard's cost) |

⇒ the brain pre-score (−48 ms), Q-0 (−12.5 ms of the brain) and the Q-2 SimHost drop (−52 ms) are well above every noise figure.
Q-1's −9 ms is above the one recorded repeat but inside POC 1's ±2.3 % ⇒ not established.

## 5. What failed or looked wrong

| | |
|---|---|
| ⚠ **`threaded-poc2-measure.py` prints no frame time on this machine** | its `[FrameStats]` regex reads `[0-9.]+`; the editor logs under the Czech locale write `mean 14,3 ms`. ⇒ the summary showed only faults, X-4F and the sync. The §3 frame rows were parsed from `editor-1.log` by hand with commas accepted. Fix: parse `[0-9.,]+` (or log invariant culture) — not done (measure only) |
| ⚠ **Part 2's threaded mode freezes after 10 syncs** | 20 worker faults per run at `MapLayerAssignmentSystem.cs:79` (§3) — known before the run (POC 2's verdict) |
| ⚠ budgets MISS (frame, garbage) in every Part 1 run | expected at 1 000 men on one CGF node |
| ⚠ only 1–6 of 12 sampled movers moved > 1 m per window, in every column | the same as POC 1, serial included — the rig, not POC 2 |
| ✅ otherwise clean | both builds 0 errors; every Part 1 node started and loaded, every column has a result, real-time factor 1.0; Part 2 ran both modes to the end with no editor restart |

Raw outputs (logs, JSON reports) kept outside the repository; no screenshots or recordings in git.

---
_ui session on the Windows machine, 2026-11-12_
