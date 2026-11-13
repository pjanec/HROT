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
| M-4 `CE-4483` | ⭐ measured + the serial work outside the systems cut: the ranked tables (§6), the profile (M-4b), the registry lock read lock-free (M-D6), the module snapshots recycled and synced incrementally (M-D7), the callouts' no-echo check from a grid (M-D8) | `f92849a9a` … (this commit) |
| M-3 `CE-4482` | `UtilityPrescore` retired: the class, its two `UtilityScorer` hooks and helper, the `BrainTickSystem` call, `HROT_BRAIN_PRESCORE`, its two rails (Q4 / T3 — "pre-score == serial score", now covered by the brain phase's own determinism rails); Parallel Simulation §14 / §15 marked SUPERSEDED | (this commit) |

## 2. GATES

| gate | command | result |
|---|---|---|
| M-6 rails | Core `PerEntityRule|EntityCensus` · Toolkits `AimAndFire|WeaponInteractionDispatcher|Weapon|Combat` serial AND `HROT_PARALLEL_SIM=1` · SimHost `ActionDispatchModule|AirAttackNodes|CgfLogicPack|WeaponSystem|Station` serial AND parallel | 7 / 0 (+ M6 rail red-proved: the owner test dropped ⇒ red) · 574 / 0 both · 68 / 1 both (`CE454`, pre-existing) |
| M-5 FriendNear | SimHost `Stress|UnderFire|Callout|ParallelBrains|FrameEntityGrid` serial · parallel · Toolkits `FrameEntityGrid` | 49 / 0 · 48 / 1 (`CE2261_BC5`, pre-existing — §5) · 1 / 0 |
| M-4 rails | `Fdp.ModuleHost.Tests` (whole) · `Fdp.Core.Tests` (whole) · SimHost `Callout|Eqs|ParallelBrains|Sensor` · Toolkits `Replay|Keyframe|Snapshot` | 266 / 0 · 1 360 / 3 ⚠ pre-existing (source-policy scanners naming files this batch never touched: `BridgeRoads.cs`, `DeployProcessorSystem.cs`, `TerrainContentHash.cs` …) · 69 / 0 · 401 / 0. ⭐ `SnapshotRecycleTests` 6 — red-proved: the force dropped, the epoch check dropped, the invalidation dropped, the lock-free path dropped ⇒ each its rail red. `CalloutsTests.M4_TheNoEcho…` green |
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
| M-D6 | `NeedsClone` under the registry lock — make the lookup lock-free how? | ⭐ a `bool[MAX_COMPONENT_TYPES]` written ONLY under the lock (registration, `SetNeedsClone`, `Clear`), read with `Volatile.Read`; the dictionary stays the authority for an out-of-range id — the CE-2389 pattern the per-T id already uses | `ComponentType.cs` |
| M-D7 | stop the snapshots' clear-and-copy-everything — how? | ⭐ the replica pattern (`DoubleBufferProvider`, the staging buffers): a recycled snapshot keeps its content and the next sync copies only the chunks the source changed. ⚠ Two traps, each a rail red-proved: ① a background module's write into its view carries the version the view synced at — the same an unchanged live chunk may carry ⇒ the recycle marks every chunk stamped since that sync never-equal; ② a source REPLACED rather than advanced (a load's clear, a playback frame, a rewind into it) repeats version numbers with other content ⇒ a new `ContentEpoch` (bumped by `Clear`, `SetGlobalVersion`, `ResetGlobalVersion`, a sync INTO the world) and a full copy when it, the source or a non-decreasing version differ. 📌 Measured on the way: the first two forms (force every chunk; force native, clear managed) saved little — the cost was copying every chunk, not the decommit. A new `RecycleForSync`, ⛔ not `SoftClear` (that one EMPTIES a world: the scenario file service, playback); used only by the two pools whose next use is always a sync. ⚠ The price, measured: the pool now KEEPS its memory — 221 MB across 15 pooled worlds (3 of them used) at 1 000 men, where a cleared one held ≈ 0 | `EntityRepository.Sync.cs`, `SnapshotPool`, `OnDemandProvider` |
| M-D8 | the callouts' serial say-pass — parallelise it? | ⭐ no: it is order-dependent BY DESIGN (AQ105 E — who spoke first in the 10 m / 2 s window; N-11's serial follow-up). Its cost was the algorithm: the no-echo check walked every speaker per call ⇒ the CE-2411 per-frame grid (`FrameEntityGrid`, as `UnderFire.FriendNear`), radius + `MovedSlackMetres`, each candidate re-read live with the exact 3-D rule | `Callouts.cs` |
| M-D2 | does any host keep SERIAL brains and want parallel scoring (M-3's precondition)? | ⭐ no — measured in code: the pre-score needs `HROT_PARALLEL_SIM=1`, which itself makes the brains a parallel phase (`BrainTickSystem.Parallel`), so it ran only under the measuring switch `HROT_SERIAL_BRAINS=1`; by design R-4214 ① puts the per-entity brain rule in both modes | `BrainTickSystem.cs`, R-4214 |

## 4. BUGS FIXED

| bug | rail |
|---|---|

## 5. OPEN

| item | why | what it needs |
|---|---|---|
| ⚠⚠ `SquadManeuverTests` (228) with the brains a PARALLEL phase (`HROT_PARALLEL_SIM=1`): **78 red** (serial: 228 / 0) | 📐 measured: the BC5 room drills among them are red at `38f1d1191` (before this batch) and green at `ffc802ab2` (before the overnight brain phase); green with `HROT_SERIAL_BRAINS=1`; the SAME at 1 and 4 threads (deterministic); the survey (`HROT_READ_CHECK=2`) finds no undeclared access. ⚠ The harness drives `BrainTickSystem` ALONE — no kernel, no `Tick()`, no Simulation dispatch — so these are serial-order rails; 📌 tried: taking the tick-start view before each brain run (as the kernel does) ⇒ 94 red, WORSE ⇒ reverted. ⇒ open question: are the squad plays per-entity-safe, or does the per-entity rule's one-tick lag on cross-entity effects (R-4212) change what a squad DOES? The rails cannot answer it in this harness | a squad harness that runs the production order (kernel phases, ticks) in both modes, and the drills compared on it; the live gates (M-7: the stepped gate, continuous play, `--mode all`) in the meantime |

## 6. NUMBERS (before / after)

| what | before | after | how |
|---|---|---|---|
| ⭐ M-4 the tick, 1 000-man demo-city, mode 3 + `HROT_PARALLEL_SIM=1`, PLAYING (Throughput by M-2), 4-core VM | **Debug** 1 thread / 4 threads: tick 2 247 / 1 452 ms · **Release**: 192 / **222** ms ⚠ (worse at 4) | Release 1 / 2 / 4: **182 / 205 / 214** ms; BrainTick 44 / 38 / 26; the sim group 116 / 111 / 106; ⚠ the module-snapshot pool keeps 222 MB | `threaded-scale-measure.py --threads 1,2,4` (`HROT_PARALLEL_SIM=1`) |

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

### M-4b — the sim thread by `dotnet-trace` (Release, 4 threads, playing, 20 s ≈ 90 ticks; ms in the 20 s)

| where | ms | per tick | what it is | ⭐ done |
|---|---|---|---|---|
| `ComponentTypeRegistry.NeedsClone` (under every managed table's `SyncDirtyChunks`) | **2 258** (+ 813 on the UI thread) | ≈ 25 | the global registry LOCK, taken per table per sync — the sim's module-snapshot syncs and staging publish against the UI's pull; ⚠ grows with the threads that contend | ✅ read lock-free (M-D6) |
| `OnDemandProvider.AcquireView` / `HarvestEntry → Clear` (module snapshots) | 2 977 / 778 → **1 786 / 58** | ≈ 33 / 9 → 20 / 1 | every release CLEARED the snapshot and every acquire copied every chunk of every table again (decommit, recommit, every managed reference chunk) | ✅ recycled + incremental (M-D7) |
| `StagingWorlds.Publish` | 1 708 → **1 499** | ≈ 19 → 17 | the UI's copy (the sampler put part of its copy under the lock frame) | ✅ by M-D6 |
| `HrotNodeContext.StepTimeline` (the live timeline's lanes) | 767 | ≈ 8 | serial on the sim thread | ranked (next) |
| brains · callouts · weapon dispatcher · stress · squads | 2 852 · 1 344 · 1 297 · 1 182 · 1 041 | | callouts: 986 of 1 344 is the SERIAL say-pass after the phase, of which `SaidNearby` walked every speaker per call (548) | ✅ `SaidNearby` from a grid (M-D8) |
| `EqsSolverSystem` | 9 221 — on .NET pool threads, NOT the sim thread | | background module ≈ ½ core continuous + its window on the pool | — |

⭐⭐ **Why 4 threads were slower than 1 on the 4-core VM:** ① the registry lock above — more threads syncing, more contention; ② the machine is
oversubscribed: sim thread + 3 pool workers + the UI thread + EQS on the .NET pool = 6 busy threads on 4 cores (the pool's default budget is
cores − 2 for this reason; `HROT_SIM_THREADS=4` overrides it).

⭐⭐ **M-4 verdict.** The serial work OUTSIDE the systems is what the profile named, and the fixes cut it ≈ 2.3 s in 20 s (≈ 25 ms a tick):
the snapshots now sync incrementally, the registry lock is gone. ⚠ **The tick still gets slower with threads on this VM while every system gets
faster** — the sim thread is CPU-bound (it waits on the pool only ≈ 1.3 s in 20 s), and on 4 cores the threads that help the systems (the pool
workers, EQS's window on them, the UI thread) take cores from the serial sim thread. ⇒ the next serial wins are the systems themselves (M-5) and
the live timeline's scan (≈ 9 ms a tick); the thread-count question on a real machine is Windows F-4 (`HANDOFF_…_Windows_Findings`, CE-4504).

⭐⭐ **The Release reading changes the plan:** every kernel system together is ~120 ms of a **222 ms** Release tick at 4 threads, and the tick got
WORSE with threads (192 → 222) while the systems shrank (group 141 → 101). ⇒ the largest serial cost in Release is OUTSIDE the systems — being
profiled (`dotnet-trace`, M-4b). The Debug ranking (weapon dispatcher 276 ms) is Debug overhead: 11 ms in Release.

## NEXT (the resume point)
M-1 … M-9 in order; started at the merge of `coordinator` (`ffc802ab2`) into `exp/threaded-build`.
