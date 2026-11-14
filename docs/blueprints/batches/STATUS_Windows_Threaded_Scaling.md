<!--STATUS
state: LIVE — measured, done (rerun after CE-4484 / CE-4485 still owed, §7)
updated: 2026-11-13
current-answer: §0 (the report), then §1–§6 per measurement
related-designs:
  - HANDOFF_Coordinator_To_Windows_Threaded_Scaling_Measure.md (on `coordinator`, dispatched at 488a9f15a) — the handoff this answers (CE-4489 … CE-4494)
  - STATUS_Overnight_Threaded_Build.md (on exp/threaded-build) — the 4-core Debug VM numbers this replaces with real cores; N-14 the EcoQoS opt-out
  - HANDOFF_Coordinator_To_Backend_Mode_And_Serial_Systems.md — the VM lane; §3's ranking feeds its M-4 / M-5
  - ../../DESIGN_Parallel_Simulation.md (on `coordinator`) — §19 Windows scheduling: §4 here updates its "half speed" verdict. ⚠ not edited here (measure only)
  - STATUS_Ui_Windows_Throttling.md (on exp/perf) — the earlier throttling numbers §4 compares against
-->

# STATUS — the threaded build on real cores, Windows, Release, 1 … 12 threads (CE-4489 … CE-4494)

Measure only — no HROT code changed. Built `exp/threaded-build` @ **`f9c51b183`** (Release, 0 errors). Drivers are scratch wrappers over the
branch's own scripts (`threaded-scale-measure.py`, `scale-rig.py`); they add process CPU, the clock, the throttling state and a
decimal-comma parse (below). Raw outputs stay outside the repo.

## 0. Report

| | |
|---|---|
| **machine** | AMD Ryzen 7 6800H — **8 physical / 16 logical, no P/E split** (all Zen 3+ cores), base 3.2 GHz · 31.2 GB · Windows 11 Home **build 26200** · laptop on **AC**, battery 100 % · power plan **Balanced** |
| **scenario** | `scale-rig-demo-city-1000-2s` regenerated with the handoff's own command (`scale-rig.py --terrain demo-city --men 1000`, no vehicles / aircraft — 1 139 authored entities, ~10 000 live) and used by EVERY measurement below |

### W-1 / W-2 — the editor, playing then paused (`CE-4489` / `CE-4490`)

| | sim tick ms | UI frame playing ms | UI frame paused ms | BrainTick ms | `TogglableSimulationGroup` ms | EQS ms (background) | CPU cores (% of the 16) | clock % of base | working set MB (peak) | staging MB |
|---|---|---|---|---|---|---|---|---|---|---|
| ⭐ **W-2: every switch OFF** (today's editor) | **144.4** | **146.3** | 36.5 | 28.0 | 81.4 | 16.0 | 2.36 (14.7 %) | 124 | 2 256 (2 558) | 0 |
| W-1: mode 3, **1** thread | 246.2 | 10.5 | 13.2 | 83.0 | 239.3 | 185.7 | 2.57 (16.0 %) | 113 | 2 453 (2 786) | 93.4 |
| W-1: **2** | 121.6 | 8.6 | 12.8 | 27.8 | 90.3 | 49.7 | 3.57 (22.3 %) | 125 | 2 482 (2 989) | 93.3 |
| W-1: **4** | 109.2 | 8.8 | 12.9 | 14.9 | 69.2 | 29.9 | 4.04 (25.3 %) | 124 | 2 331 (2 884) | 93.3 |
| W-1: **6** | 108.4 | 9.0 | 13.4 | 11.8 | 67.6 | 24.0 | 4.25 (26.6 %) | 123 | 2 551 (2 830) | 93.4 |
| ⭐ W-1: **8** | **112.6** | **9.0** | 12.7 | **10.3** | 66.7 | 20.8 | 4.49 (28.0 %) | 123 | 2 392 (2 769) | 93.0 |
| W-1: **12** | 110.7 | 9.6 | 12.8 | 9.8 | 65.6 | 18.2 | 4.85 (30.3 %) | 125 | 2 603 (2 822) | 93.4 |

⭐ **Reading:** the UI frame drops from **146 ms to ~9 ms** — it no longer waits for the tick at any thread count. The sim tick goes **144 → ~110 ms**
(1.3×) and **stops improving at 4 threads**: `TogglableSimulationGroup` (~66 ms, 59 % of the tick) no longer shrinks. BrainTick scales
**8.2×** (84.7 → 10.4 ms, 1 → 8). CPU use rises only from 2.4 to 4.5–4.9 cores of 16.

### W-3 — what is still serial on real cores (`CE-4491`) — ⭐ the conversion order

`TogglableSimulationGroup`'s total equals the sum of its 68 `EditorSim` members to within 1 ms at 2–12 threads, so its members are listed
one by one. Sorted by ms at 8 threads; % = of the 112.6 ms sim tick at 8 threads.

| # | system | where | ms @1 | ms @8 | speed-up | % of tick @8 | |
|---|---|---|---|---|---|---|---|
| 1 | ⭐ **`StressSystem`** | group | 20.9 | **16.4** | 1.27× | **14.6** | ⛔ serial |
| 2 | ⭐ **`CalloutSystem`** | group | 16.1 | **10.3** | 1.56× | **9.1** | ⛔ serial |
| 3 | `BrainTickSystem` | group | 84.7 | 10.4 | 8.15× | 9.2 | ✅ parallel |
| 4 | ⭐ **`CgfThreatEvaluationSystem`** | group | 8.4 | **5.9** | 1.42× | **5.2** | ⛔ serial |
| 5 | ⭐ **`WeaponDispatcherSystem`** | group | 11.9 | **5.7** | 2.07× | **5.1** | ⛔ mostly serial |
| 6 | `SquadCoordinationSystem` | group | 12.9 | 5.4 | 2.38× | 4.8 | partly |
| 7 | ⭐ `StaffComposerSystem` | group | 4.2 | 2.8 | 1.51× | 2.5 | ⛔ serial |
| 8 | `TogglableInputGroup` | Input | 10.9 | 2.9 | 3.79× | 2.6 | — |
| 9 | `TogglablePostSimulationGroup` | PostSimulation | 11.3 | 2.9 | 3.96× | 2.5 | — |
| 10 | `CarKinematicsSystem` | PostSimulation | 6.6 | 1.3 | 5.15× | 1.1 | ✅ parallel |
| 11 | `LocalGridBuilderSystem` | EQS | 1.8 | 1.1 | 1.66× | 1.0 | serial, small |
| 12 | `CycloneEgressSystem` | Export | 1.3 | 1.0 | 1.36× | 0.9 | serial, small |
| — | the ~60 other group members | group | ~13 | ~9 | | ~8 | each < 0.9 ms |
| (bg) | `EqsSolverSystem` | EQS (background module) | 182.6 | 20.8 | 8.78× | not on the tick | ✅ parallel |

⇒ **convert first:** `StressSystem` (16.4 ms), `CalloutSystem` (10.3), `CgfThreatEvaluationSystem` (5.9), `WeaponDispatcherSystem` (5.7),
then `StaffComposerSystem` (2.8). Together **~41 ms of the ~66 ms** the group spends at 8 threads. ⚠ Ranking differs from the VM's
(`TogglableSimulationGroup` ~600 ms / `WeaponDispatcherSystem` ~200 ms, Debug): in Release on real cores `WeaponDispatcherSystem` is only
5.7 ms and `StressSystem` / `CalloutSystem` lead.

### W-4 — separate node processes, `HROT_PARALLEL_SIM=1` (`CE-4492`)

`scale-rig.py --terrain demo-city --men 1000` (defaults: warm-up 20 s, 30 s measured, unpinned, unrecorded, headless), real-time factor
1.0 in every run.

| run | CGF work ms · fps | CGF BrainTick | CGF top systems (ms) | CGF cores | SimHost work ms | SimHost EQS (bg) ms | SimHost cores | CGF / SimHost peak GB | clock % |
|---|---|---|---|---|---|---|---|---|---|
| ⭐ **switch off** | **108.5** · 21.2 | 36.2 | Stress 10.8 · WeaponDispatcher 9.4 · Callout 9.2 · Egress 7.8 + 6.9 | 1.22 | 22.3 | 15.9 | 2.87 | 0.84 / 1.29 | 118 |
| threads **1** | 112.9 · 20.3 | 40.3 | Stress 11.8 · Callout 9.7 · WeaponDispatcher 9.1 · Egress 7.0 + 6.8 | 1.19 | 22.3 | 77.8 | 1.71 | 0.88 / 1.24 | 121 |
| **2** | 81.2 · 27.5 | 19.5 | WeaponDispatcher 8.8 · Callout 8.2 · Stress 7.0 · Egress 6.9 + 6.5 | 1.52 | 22.6 | 44.8 | 1.82 | 0.89 / 1.22 | 124 |
| **4** | 71.1 · 30.7 | 12.3 | WeaponDispatcher 8.2 · Callout 8.1 · Egress 7.2 + 6.7 · Stress 4.8 | 1.76 | 23.3 | 27.8 | 1.96 | 0.88 / 1.27 | 121 |
| ⭐ **8** | **69.0** · 31.2 | **9.1** | Callout 8.7 · WeaponDispatcher 8.4 · Egress 7.7 + 6.9 · Stress 4.0 | 1.93 | 25.3 | 19.5 | 2.28 | 0.90 / 1.28 | 119 |
| **12** | 70.4 · 30.2 | 9.3 | Callout 8.9 · WeaponDispatcher 8.7 · Egress 7.3 + 7.0 · Stress 4.7 | 2.51 | 25.9 | 17.0 | 2.70 | 0.92 / 1.29 | 119 |

⇒ CGF **108.5 → 69.0 ms (1.57×)**, flat from 4 threads. BrainTick 36 → 9 ms. What stays: `CalloutSystem` + `WeaponDispatcherSystem` +
the two `CycloneEgressSystem` passes ≈ **31 ms** at every thread count — serial. SimHost's frame does not depend on the switch (22–26 ms);
its EQS solve shrinks 78 → 17 ms with threads (background).

### W-5 — the EcoQoS opt-out, N-14 (`CE-4493`)

| | |
|---|---|
| ⭐ **the opt-out is in force** | read from outside with `GetProcessInformation(ProcessPowerThrottling)` on every node process, every run: **ControlMask 5 (execution speed + timer resolution), StateMask 0 = never throttle** — orchestrator, CGF, SimHost and the hidden-window editor alike. With `HROT_ECOQOS=1` the same read gives ControlMask 0 (Windows decides) ⇒ the switch works |
| ⚠ **no log line** | the handoff asks to confirm "the opt-out's log line at node start" — **there is none**: `Program.cs:52` calls `WindowsScheduling.OptOutProcess()` and ignores the result. The external read above replaces it |
| ⭐ **clock** | the machine's `% Processor Performance` (`_Total`) while measuring: **118–125 % of 3.2 GHz (≈3.8–4.0 GHz)** in every opted-out run, editor and nodes — the "unthrottled" level of §19 (it was ~80 % throttled) |
| **frame time, opted out vs Windows' default** (`HROT_ECOQOS=1`, same run otherwise) | switch off: CGF **108.5 vs 128.9 ms** (1.19×), SimHost **22.3 vs 30.8 ms** (1.38×) · `PARALLEL_SIM` 8 threads: CGF **69.0 vs 93.3 ms** (1.35×), SimHost **25.3 vs 35.6 ms** (1.41×) |
| ⚠ **verdict** | ✅ windowless nodes now run unthrottled. ⚠ But today Windows' default costs only **1.2–1.4×**, not §19's **~1.9×** (CGF 228 vs 121 ms) — and the machine clock with the default was ~120–125 %, not ~80 %. ⇒ the default's throttling varies from day to day / run to run; the opt-out removes that variable. `% Processor Performance` is machine-wide, not per thread — a per-thread clock was not read |

### W-6 — stepped determinism on Windows (`CE-4494`)

Editor, mode 3, the same 1 000-man rig loaded into Edit, then stepped from paused in chunks of 30 (60 + 60 + 120 steps = 4 s sim time,
`simTime` exactly 1 / 2 / 4 s in every run), `GET /world/hash?byType=1` after each chunk. Two sessions: **1 · 4 · 12 · 1** and, after one
step + an explicit pause, **1 · 1 · 4 · 12**.

| compared | at start | after 60 steps | after 120 | after 240 |
|---|---|---|---|---|
| ⭐ **1 vs 1 (the control)** | EntityGuid (+ Utterance in the 2nd session) | + `NavigationApplied` (+ `NavigationIntent` in the 1st) | + `NavigationIntent` | + `SquadDrillState` |
| 1 vs 4 | the same | the same | the same | the same |
| 1 vs 12 | the same | the same | the same | the same (+ `ReportOutbox` once, 1st session only) |

| | |
|---|---|
| ⭐ **verdict on the thread count** | **no difference attributable to it** — 1 vs 4 and 1 vs 12 differ in exactly the types 1 vs 1 differs in, in both sessions. `SimTransform` (positions) is EQUAL everywhere, entity counts equal (5 648 → 10 145) |
| ⛔ **but the stepped run is NOT repeatable on Windows, even at 1 thread** | `Utterance`, `NavigationApplied`, `NavigationIntent`, `SquadDrillState` differ between two identical 1-thread runs. ⇒ the gate cannot prove thread-count determinism here until that run-to-run difference is explained. Likely suspects (not measured): something wall-clock paced, e.g. the background solvers (pathfinding, EQS: `Manual` phase modules) landing results at different steps on a faster machine |
| ⚠ **not done** | `?rows=<Type>` for the four types (one more session) — the per-row names of what differs are not taken; ⚠ the gate is a rebuild of the VM's uncommitted `stepped-hash.py` from its description (no orders issued), so it may not match the VM's run exactly |
| ⚠ the `tick` field is a frame counter | it advanced ~470 per 60 steps and also while paused — `simTime` is the step clock; nothing wrong, but a trap for reading the hash output |

### Surprising, one line each

- ⚠ **1 thread is slower than the switch off:** tick 246 vs 144 ms, BrainTick 83 vs 28 ms, and the group's total exceeds the sum of its members by **72 ms** (≈0 at every other count). Likely the ONE pool thread is shared with EQS (186 ms at 1 thread) and the brain phase waits for it — not proven.
- ⚠ **the UI frame with every switch off is 146 ms while playing** — the UI waits for each tick; paused it is 36.5 ms vs ~13 ms in mode 3.
- ⚠ **CPU stays low:** at most 4.9 of 16 logical cores in the editor, 2.5 for the CGF node — the serial systems above are what holds it, not the pool.
- ⚠ **`CycloneEgressSystem` costs ~14.6 ms on the CGF node** (two passes, serial, every count) — a candidate beside Callout / WeaponDispatcher.
- ⚠ **`threaded-scale-measure.py`'s `[FrameStats]` regex `[0-9.]+` truncates on a decimal-comma locale** (Czech: `mean 8,9 ms` → 8). Same defect as `threaded-poc2-measure.py`; worked around in the scratch driver, not fixed here.
- ⛔ **the stepped run differs between two identical 1-thread runs** on Windows (`Utterance`, `Navigation*`, `SquadDrillState`) — the VM saw seven equal runs (§W-6).
- ⚠ **the stepped gate is not committed** (`scratchpad/stepped-hash.py` on the VM) — rebuilt from its description for W-6; the handoff's "committed successor" does not exist on the branch.

## 7. Not done

| | |
|---|---|
| ⚠ **the rerun of W-1 / W-3 / W-4 after the conversions** | CE-4484 / CE-4485 were not ticked on `exp/threaded-build` during this session (head `f92849a9a` = M-4's ranking) ⇒ the coordinator dispatches the rerun |
| W-2 at the off variant's thread counts | the off editor ignores `HROT_SIM_THREADS` for the switches; one run at the default pool (cores − 2) as asked |

---
_ui session on the Windows machine, 2026-11-13_
