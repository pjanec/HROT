<!--STATUS
state: LIVE — in progress
updated: 2026-11-13
current-answer: the whole file (NEXT is the resume point)
related-designs:
  - HANDOFF_Coordinator_To_Backend_Mode_And_Serial_Systems.md — the work M-1 … M-9 (CE-4480 … CE-4488), scope frozen at ffc802ab2
  - NOTE_Backend_To_Coordinator_Parallel_Switches_And_Throughput.md — the findings it answers (D1–D5)
  - STATUS_Overnight_Threaded_Build.md — the overnight build this continues (D-1 … D-43, the gate commands)
  - ../../DESIGN_Parallel_Simulation.md — §10–§11 the pre-score, §18 the per-entity rule
-->

# STATUS — the mode set by what is running, the pre-score retired, the serial systems converted

Branch `exp/threaded-build`. Rulings R-4218 / R-4219 (R-4207, R-4215).

## 1. DONE

| item | what | commit |
|---|---|---|
| M-1 `CE-4480` | ⭐ the batch size does NOT change the result: the stepped 1 000-man run with the adaptive batch `max(32, n/(threads×4))` gives the same world at 1 / 2 / 4 / 8 threads AND the same world as the fixed 32 batch (every type equal but `EntityGuid`, minted at random per run). By construction: contiguous batches merged in batch order = entity order | (measurement) |
| M-2 `CE-4481` | ⭐ the mode is set by what is running: every kernel VOTES at its tick boundary (its clock stepped ⇒ Deterministic — stepping, Run-to, filming and the regression all step it), a harness may PIN Deterministic (`SimulationModeSelector.RequireDeterministic`, held by `HeadlessTestExecutor`); the process value = Deterministic when any vote or pin, else Throughput — ONE value for the process, `--mode all` included. `HROT_SIM_MODE` removed (code + `parallel-sim-measure.py`); `ParallelEntities.Mode`'s comment true | (this commit) |
| M-3 `CE-4482` | `UtilityPrescore` retired: the class, its two `UtilityScorer` hooks and helper, the `BrainTickSystem` call, `HROT_BRAIN_PRESCORE`, its two rails (Q4 / T3 — "pre-score == serial score", now covered by the brain phase's own determinism rails); Parallel Simulation §14 / §15 marked SUPERSEDED | (this commit) |

## 2. GATES

| gate | command | result |
|---|---|---|
| M-1 stepped gate, adaptive vs fixed batch | `SAVE=… HROT_SIM_MODE=throughput python3 scratchpad/stepped-hash.py 1,2,4,8` (the old switch, on the binary before M-2) vs `stepped-hash.py 1` (fixed 32) | STATE EQUAL across all five runs, type by type |
| M-2 rails | `dotnet test FDP/Engine/Fdp.ModuleHost.Tests --no-build --filter "…SimulationModeFromWhatRuns|…PausedAndStepped|…ModuleHostKernelTime"` | 24 / 0; red-proved (the kernel's `Vote` dropped ⇒ both R4218 rails red) |
| M-3 suites | Toolkits `Utility|Behavior|Squad` · SimHost `ParallelBrains|Utility|Brain|Sop` | 974 / 0 · 114 / 0 |

## 3. DECISIONS

| # | question | decision | where |
|---|---|---|---|
| M-D1 | the handoff points the pre-score's record at Parallel Simulation §10–§11 | ⭐ it is §14 (POC 2) and §15 (POC 3) — §10–§11 never mention it; those are the sections marked superseded | design |
| M-D3 | how does the kernel know "stepped / Run-to / filming / regression"? | ⭐ all four STEP the clock: Run-to is the ACK-gated stepped run (`RunToController`), the director and the verify run advance by `StepBy` (`DirectorRunner.cs:730/986`) ⇒ the clock's `TimeMode.Deterministic` (`_steppedFrame`, already computed for CE-3292) is the one signal — no per-feature flag | `ModuleHostKernel` |
| M-D4 | "one value for the process" when kernels disagree for a tick (`--mode all`, slaves switching on the master's event) | ⭐ any Deterministic vote wins — the safe direction; a harness pin likewise | `SimulationModeSelector.Vote` |
| M-D5 | "the tests ⇒ Deterministic" | ⭐ an explicit counted pin (`RequireDeterministic`), held by the headless test harness; rails that run `ParallelEntities` without a kernel set the mode directly (unchanged) — ⚠ a test kernel in continuous play votes Throughput, which M-1 shows is the same result | `HeadlessTestExecutor` |
| M-D2 | does any host keep SERIAL brains and want parallel scoring (M-3's precondition)? | ⭐ no — measured in code: the pre-score needs `HROT_PARALLEL_SIM=1`, which itself makes the brains a parallel phase (`BrainTickSystem.Parallel`), so it ran only under the measuring switch `HROT_SERIAL_BRAINS=1`; by design R-4214 ① puts the per-entity brain rule in both modes | `BrainTickSystem.cs`, R-4214 |

## 4. BUGS FIXED

| bug | rail |
|---|---|

## 5. OPEN

| item | why | what it needs |
|---|---|---|

## 6. NUMBERS (before / after)

| what | before | after | how |
|---|---|---|---|
| ⭐ M-4 the tick, 1 000-man demo-city, mode 3 + `HROT_PARALLEL_SIM=1`, PLAYING (Throughput by M-2), 4-core VM | **Debug** 1 thread / 4 threads: tick 2 247 / 1 452 ms · **Release**: 192 / **222** ms ⚠ (worse at 4) | — | `threaded-scale-measure.py --threads 1,4` (`HROT_PARALLEL_SIM=1`) |

### M-4 — what is serial in the tick (ms per call, recent average; 4 threads)

| system | Debug 1 → 4 | Release 1 → 4 | per-entity? | writes on OTHER entities |
|---|---|---|---|---|
| `EqsSolverSystem` (background module, its own window on the pool) | 700 → 522 | 342 → 167 | its window (`105705de9`) | — (sensor results on its own carriers) |
| `TogglableSimulationGroup` (73 members) | 1 067 → 917 | 141 → 101 | — | — |
| · `BrainTickSystem` | 461 → 186 | 56 → 26 | ✅ phase | by command |
| · `WeaponDispatcherSystem` | 220 → **276** | 9.9 → 11.1 | ⛔ serial | an operated station's mount `WeaponState`, a turret part's `WeaponTask` (exclusive per seat) |
| · `StressSystem` | 64 → 61 | 17.1 → 11.2 | ✅ phase (gains little) | edges / breaks deferred |
| · `CalloutSystem` | 52 → 50 | 12.6 → 12.9 | ✅ phase (no gain) | follow-ups |
| · `SquadCoordinationSystem` | 47 → 36 | 9.7 → 11.0 | ✅ per level | members (owned) |
| · `CgfThreatEvaluationSystem` | 21 → 14 | 7.6 → 6.1 | ✅ phase | — |
| · the rest (≈ 66 small) | ≈ 120 | ≈ 18 | mostly serial | — |
| `TogglableInputGroup` (31: ray casts, fire processing, ingress) | 171 → 179 | 5.3 → 5.4 | ray casts on the pool | `FireProcessingSystem` writes the struck |
| `TogglablePostSimulationGroup` (movement, ballistics) | 39 → 42 | 8.6 → 8.1 | movement ✅ phase | — |

⭐⭐ **The Release reading changes the plan:** every kernel system together is ~120 ms of a **222 ms** Release tick at 4 threads, and the tick got
WORSE with threads (192 → 222) while the systems shrank (group 141 → 101). ⇒ the largest serial cost in Release is OUTSIDE the systems — being
profiled (`dotnet-trace`, M-4b). The Debug ranking (weapon dispatcher 276 ms) is Debug overhead: 11 ms in Release.

## NEXT (the resume point)
M-1 … M-9 in order; started at the merge of `coordinator` (`ffc802ab2`) into `exp/threaded-build`.
