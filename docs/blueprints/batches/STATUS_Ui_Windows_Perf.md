<!--STATUS
state: LIVE — measured, done
updated: 2026-11-12
current-answer: the whole file (§0 is the verdict)
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — OWNS the simulation experiments (§12–§17 POC 2–4 as built, §17 the managed reads)
  - ../../DESIGN_Threaded_Simulation_Presentation.md — OWNS the threaded editor (§14 the target shape, §14.4 POC 3)
  - HANDOFF_Coordinator_To_Ui_Windows_Perf_Measure.md (on `coordinator`, dispatched at 07e178275) — the handoff this answers
  - STATUS_Ui_Windows_POC_2.md (on exp/parallel-sim-2) — the previous Windows run, same method; its baseline 266 ms
-->

# STATUS — `exp/perf` and the threaded editor POC 3, measured on the Windows machine

Measure only, no code changed. Every process ran hidden (`HROT_HIDDEN_WINDOW=1`). Part 1, then Part 2 — never together.

## 0. Verdict

| | |
|---|---|
| ⭐ **CGF frame, everything on** | serial **256 ms** (mean of 3 recorded, 252–261) → everything on **163 / 173 ms** (two runs) = **1.52×**, **8.8 → 12.2 fps**. POC 2 on this machine: 266 → 181 (1.47×) |
| ⭐ **the brains** | `BrainTickSystem` **96 → 42 ms** with the POC 3 pre-score (posture + ranking). POC 2's pre-score reached 56 ms |
| ⭐ **the one worker pool (POC 4)** | everything on with **one** thread: 255 ms. With the pool at its budget: 173 ms ⇒ **the pool buys 81 ms**. CGF threads 27 → 32 |
| ⭐ **SimHost with off-frame egress** | **96–104 → 42–51 ms**, 20 → 40–45 fps (POC 2: 92 → 40) |
| ⚠ **T-5 alone made CGF slower** | `rec-t5` (off-frame egress + parallel replica sync, nothing else) **339 ms** vs 252 serial — Brain +36, Weapons +12, Ingress +12 ms. **Repeated: 337 ms — reproducible.** With everything on, the same switches give 163 ms. See §3a |
| ⚪ **the timer, T-2, Q-1** | sampled / every call / no timer: **252 / 262 / 260 ms**, inside the noise (9 ms). T-2 (EQS config gate): **252.5 vs 252.0, no effect** — `EqsSensorConfigTopic` still sends ~18 k samples, 11–12 ms/frame of egress (consistent with design §17: the egress gate skips 0) |
| ⭐ **threaded editor (POC 3), playing 2-D** | drawing frame **97 ms → 15.6 ms** (p95 ~105 → ~17.7). OFF: update 83 · draw 7.5 · ui 6.3. `=3`: update 3.0 · draw 7.6 · ui 4.8. **0 faults, no freeze, pause and step work** |
| ⚠ **but the simulation ticks half as often** | on its thread the node ticks **~4.8 times/s (210 ms period)** while playing — OFF ticks once per 97 ms frame. Paused, the worker still ticks every **100–130 ms** (OFF paused update: 14.5 ms). Sim time still reached 120 s in 120 s. **Unexplained** — see §5 |
| **sync S → A** | playing: tables **~4.5 ms** + rows/map **~0.65 ms** per sync, one sync per tick. Paused: ~1.1 + 0.55 ms |

## 1. Machine, branches, commands

| | |
|---|---|
| CPU | AMD Ryzen 7 6800H — **8 cores / 16 logical**, 31 GB RAM, Windows 11 (laptop), Czech locale |
| Part 1 | **`exp/perf` @ `06a3bc69e`**, Release `Hrot.ClusterRunner`, 0 errors |
| Part 2 | **`exp/threaded-poc-3` @ `e674ce914`**, Release, 0 errors; scenario `scale-rig-scale-1-1000-2s` (1 263 authored, 12 566 entities loaded) |
| pinning | `--pin` ⇒ orchestrator 2 · cgf 7 · simhost 7 logical CPUs |
| scenario (Part 1) | demo-city, 1 000 men, 50 vehicles, 10 aircraft, warm-up 30 s, measured 30 s, ~12.5 k entities on CGF |

```
python scripts/parallel-sim-measure.py --pin --poc3 --out <scratch>/P1-poc3
python scripts/parallel-sim-measure.py --pin --poc4 --out <scratch>/P1-poc4
HROT_BRAIN_PRESCORE=1 python scripts/parallel-sim-measure.py --pin --threads 1 2 4 8 --mode throughput --out <scratch>/P1-threads
python scripts/scale-rig.py --terrain demo-city --men 1000 --vehicles 50 --aircraft 10 --warmup 30 --seconds 30 --pin --record   # noise: serial repeat
FDP_EGRESS_OFFFRAME=1 HROT_PARALLEL_SYNC=1 python scripts/scale-rig.py … --pin --record                                           # extra: rec-t5 repeat
python scripts/scale-rig.py --terrain scale-1 --men 1000 --vehicles 50 --aircraft 10 --write-only
python scripts/threaded-poc2-measure.py --out <scratch>/P2 --mode 3 --no-guard
```
Raw outputs stay outside the repository (session scratchpad).

## 2. Part 1 — `--poc3`, ms per frame, CGF node (recorded)

| system | serial (sampled timer) | every call timed | no timer | T-2 EQS gate | POC 1 Throughput | T-3 + pre-score | T-5 egress + sync | **all** |
|---|---|---|---|---|---|---|---|---|
| **whole frame** | 252.0 | 262.2 | 260.1 | 252.5 | 236.1 | **173.1** | ⚠ **339.3** | **163.0** |
| speed-up vs serial mean 256.2 | 1.02 | 0.98 | 0.99 | 1.01 | 1.09 | **1.48** | 0.76 | **1.57** |
| fps | 8.9 | 8.7 | 8.9 | 8.7 | 9.6 | 12.3 | 6.5 | **12.2** |
| BrainTickSystem | 95.9 | 102.2 | 97.7 | 96.2 | 98.3 | **42.2** | 132.0 | **42.6** |
| StressSystem | 16.2 | 15.7 | 15.7 | 15.9 | 7.2 | 7.4 | 18.8 | 7.2 |
| CgfThreatEvaluationSystem | 7.3 | 6.6 | 7.1 | 6.6 | 2.4 | 2.5 | 7.0 | 2.2 |
| CalloutSystem | 15.7 | 14.9 | 15.5 | 13.3 | 13.6 | 14.1 | 14.5 | 11.5 |
| WeaponDispatcherSystem | 18.7 | 20.9 | 20.9 | 19.1 | 17.3 | 16.6 | 31.2 | 14.6 |
| SquadCoordinationSystem | 5.5 | 6.1 | 5.2 | 5.2 | 5.7 | 5.8 | 7.0 | 5.6 |
| RecorderTickSystem | 18.9 | 20.2 | 19.3 | 20.1 | 19.6 | 18.8 | 24.0 | 18.7 |
| CycloneEgressSystem | 31.6 | 32.8 | 33.6 | 33.3 | 32.0 | 29.7 | 35.8 | 25.5 |
| CycloneNetworkIngressSystem | 14.0 | 14.7 | 17.2 | 15.0 | 12.6 | 8.8 | 26.0 | 3.6 |
| EgressPipelineLaunchSystem | — | — | — | — | — | — | 5.5 | 4.0 |
| garbage MB/frame | 2.39 | 2.45 | 2.51 | 2.45 | 2.45 | 2.23 | 2.33 | 2.36 |
| **SimHost** ms / fps | 96.1 / 21.1 | 100.0 / 21.0 | 106.1 / 20.3 | 97.1 / 21.0 | 109.3 / 21.2 | 92.9 / 20.5 | **51.3 / 39.5** | **43.8 / 42.6** |

## 3. Part 1 — `--poc4` (the one worker pool), the thread curve, the noise

| `--poc4` | serial | **all, pool at budget** | all, ONE thread |
|---|---|---|---|
| CGF whole frame | 260.8 | **173.3** (1.48×, 12.3 fps) | 254.6 (8.8 fps) |
| BrainTickSystem | 99.8 | 42.4 | 91.8 |
| CycloneNetworkIngressSystem | 16.7 | 8.1 | 21.6 |
| garbage MB/frame | 2.48 | 2.15 | 2.36 |
| **CGF OS threads** | 27 | **32** | 28 |
| SimHost work ms / threads | 104.1 / 32 | 48.6 / 33 | 42.2 / 30 |

⚠ The handoff asks "one worker pool vs two". `--poc4` measures the pool at its budget vs at one thread, not vs a two-pool build — this branch has no two-pool switch. The thread counts above are what the rig reports.

**Thread curve** (`HROT_BRAIN_PRESCORE=1`, Throughput, unrecorded):

| | serial | 1 | 2 | 4 | 8 |
|---|---|---|---|---|---|
| whole frame | 233.2 | 222.4 | 192.6 | 160.3 | **150.1 (1.55×)** |
| BrainTickSystem | 98.5 | 89.6 | 68.8 | 44.8 | 41.1 |
| StressSystem | 16.9 | 16.9 | 14.1 | 8.3 | 6.6 |
| CalloutSystem | 15.6 | 15.3 | 16.1 | 17.4 | 17.5 |
| CycloneNetworkIngressSystem | 9.8 | 10.2 | 6.9 | 7.6 | 3.9 |
| garbage KB/frame | 825 | 797 | 750 | 597 | 503 |

POC 2 on this machine: brain 101 → 51 ms, whole 236 → 164. The brain still flattens after 4 threads (7 pinned logical CPUs ≈ 3.5 cores).

**Noise** — recorded serial whole frame: **252.0** (poc3) · **260.8** (poc4) · **255.8** (repeat) ⇒ spread **8.8 ms (±1.7 %)**. A whole-frame difference under ~10 ms is not a result.

### 3a. T-5 alone — repeated, it is real

| T-5 alone (`FDP_EGRESS_OFFFRAME=1 HROT_PARALLEL_SYNC=1`), recorded | run 1 (`--poc3`) | run 2 (repeat) | serial (`--poc3`) |
|---|---|---|---|
| CGF whole frame / fps | **339.3 / 6.5** | **337.2 / 6.0** | 252.0 / 8.9 |
| BrainTickSystem | 132.0 | 119.1 | 95.9 |
| WeaponDispatcherSystem | 31.2 | 41.6 | 18.7 |
| CycloneNetworkIngressSystem | 26.0 | 22.7 | 14.0 |
| egress by translator, total | 40.0 ms | 40.4 ms | 28.1 ms |
| SimHost | 51.3 ms / 39.5 fps | (line not captured) | 96.1 / 21.1 |

⇒ **T-5 on a SERIAL frame costs CGF ~85 ms**, spread over serial systems that T-5 does not touch (brain, weapons, ingress). With the parallel systems
and the pre-score also on (`rec-all`, 163 ms) the cost is not visible. ⚠ Not explained. A guess, NOT measured: the off-frame egress work and
the serial frame compete for CGF's 7 pinned logical CPUs. The next step would be one unpinned T-5 pair, or T-5 with `HROT_PARALLEL_SYNC` off.

## 4. Part 2 — the threaded editor, OFF vs `HROT_THREADED_KERNEL=3`

Phases from the session log: load → play 120 s (2-D) → pause → step ×5 → 3-D view on → play ~20 s → pause → 3-D off. `[FrameStats]` every ~2 s, values below are the typical steady window.

| drawing frame (ms) | OFF mean / p95 | `=3` mean / p95 | `=3` split (update · draw · ui) |
|---|---|---|---|
| ⭐ **playing, 2-D** | **~97 / ~105** (update 83 · draw 7.5 · ui 6.3) | **15.6 / 17.7** | 3.0 · 7.6 · 4.8 |
| paused, 2-D | ~25 / ~26 (update 14.5) | 15.1 / 17.5 | 2.9 · 7.3 · 4.6 |
| paused, 3-D | ~46 / ~47 (draw 25) | 32.6 / 36.8 | 2.8 · 24.6 · 4.7 |
| playing, 3-D | ~130 / ~138 (update 98 · draw 26) | 32.7 / 37.0 | 2.8 · 24.6 · 4.9 |

`[X-4F]` drawing frames with the world, playing 2-D: mean **16.0–16.9**, p95 **17.4–20.7 ms**. Whole-run figure printed by the script: world frames mean 17.2 / p95 39.9, map-only 4.1 / 20.2 (it includes the 3-D phase).

| simulation on its thread (`[X-4]` + `[POC2]`, per 5 s window) | ticks | tick period | tables / sync | rows + map / sync |
|---|---|---|---|---|
| ⭐ playing, 2-D (20 windows) | 20–26 | **~210 ms** | **~4.5 ms** (3.8–5.5) | **~0.65 ms** |
| paused, 2-D | 40–47 | 100–130 ms | ~1.1 ms | ~0.55 ms |
| playing, 3-D | 22–23 | ~225 ms | ~4.6 ms | ~0.66 ms |
| before load (empty) | 578–650 | 8 ms | 0.8 ms | 0.15 ms |

Per-window costs are derived from the cumulative means the log prints (rounding ±0.5 ms on tables). Totals: 2 734 syncs, guard off, **0 worker faults, 0 exceptions, 0 ERROR lines** in both modes. Every session step returned `ok`. Sim time at pause: OFF 120.49 s, `=3` 120.00 s.

⚠ **Save was not exercised** — `threaded-poc2-measure.py` has no save step. The handoff's "pause and save work" is only half covered here (pause ✅).

## 5. What looked wrong or limited the measurement

| | |
|---|---|
| ⚠ **the threaded node ticks at ~4.8 Hz playing, every ~115 ms paused** | OFF ticks once per drawing frame (~10 Hz playing, update 83 ms; paused update 14.5 ms). The worker's tick period is 2× OFF's playing frame, and paused it is 8× OFF's paused update. Sim time still keeps real time. Either the worker paces itself, or each tick on the worker costs far more than inline. **Not explained** — the log's "node frame mean" is the tick period (ticks × mean ≈ 5 s per window), not the work inside a tick, so it can't separate the two |
| ⚠ **`rec-t5` 339 / 337 ms** | reproduced; see §3a |
| ⚠ **network ingress falls with threads** | 14 → 3.6 ms with everything on, 9.8 → 3.9 across the thread curve, though ingress is not a parallelised system. Seen in POC 2 too. Not explained |
| ⚠ **`threaded-poc2-measure.py` FrameStats regex** | `[0-9.]+` misses this machine's decimal commas (`15,6`) — the script printed no `[FrameStats]` rows; read from the logs by hand. Same as the POC 2 run. Fix is one line, not done (measure only) |
| ⚠ budgets MISS (frame, garbage) | in every Part 1 run, as before; expected at 1 000 men |
| ⚠ sampled movers moved 1–4 of 12 | in every column, serial included — same as before, not a regression |
| ⚠ the background chain was killed by Claude Code under memory pressure | **after** it printed `ALL DONE` — every run completed; no data lost |

---
_ui session on the Windows machine, 2026-11-12_
