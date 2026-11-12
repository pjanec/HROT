<!--STATUS
state: LIVE — the overnight experiment; VERDICT at the top
updated: 2026-11-12
current-answer: the J-4 VERDICT at the top; the sections below hold the evidence
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — OWNS this (§13 the job-system lean, §16 POC 4 as built)
  - HANDOFF_Coordinator_To_Backend_Parallel_Sim_POC_4.md — the handoff (dispatched at f3376866a)
  - STATUS_Parallel_Sim_POC_3.md (on exp/parallel-sim-3) — the base this branch builds on
  - parallel-sim-4/J3_Access_Analysis.md — J-3's full table and the conflict lists
  - STATUS_Threaded_Analysis_Instruments.md (on exp/threaded-analysis) — A-1, the access tracer J-3 ran
-->

# STATUS — POC 4: ONE worker pool, and is a dependency scheduler worth it? (branch `exp/parallel-sim-4`)

**Branch:** `exp/parallel-sim-4` from `exp/parallel-sim-3` with `coordinator@f3376866a` merged (`61fddcfd7`). **Machine:** the cloud VM, 4 cores
(the pool = 2 threads; ±15 % run-to-run noise — trends only). Nothing merged into a lane; every opt-in OFF by default.

## ⭐ J-4 VERDICT — **GO on the one pool (J-1) and the two loops on it (J-2); ⛔ NO-GO on a dependency scheduler (J-3)**

| item | result (VM) | verdict |
|---|---|---|
| ⭐ **J-1 one pool, two lanes** | `Fdp.ModuleHost.Threading.WorkerPool` DELETED; `FdpWorkerPool` carries a FRAME lane (`Run`: parallel systems, replica sync) and a BACKGROUND lane (`TryRun`: EQS windows, the off-frame egress scan). A worker checks the frame lane before EVERY background item. Rail: a 60 × 20 ms background window running, a 48-batch frame run still gets **32 of 48 batches on workers, 65 ms** (≈ 3 threads' worth); red-proved (background-first ⇒ red). Every rail of both old pools green on the one pool | ⭐ **GO** |
| ⭐ **J-2 the loops on the pool** | `EntityQuery.ForEachParallel` and `RaycastSolverSystem` off `Parallel.ForEach/For`: a pooled work object, **0 B per call when warm** (rail, red-proved), raycasts identical on 1 and 4 threads (rail, red-proved). The kernel's per-frame `new List<Task>` + `ToArray` → a reused field | ⭐ **GO** |
| J-2 fair A/B, everything on, recorded (base `61fddcfd7` = two pools + `Parallel.For`, vs HEAD; interleaved twice) | CGF frame **221 / 197 → 199 / 159 ms**; ⭐ **garbage 0.98 / 1.01 → 0.76 / 0.59 MB per frame**; SimHost threads **35 / 37 → 31 / 31**; CGF threads 30 → 30 / 31 (the VM's pools are 1 worker each) | the garbage drop is outside the noise; the frame trend is favourable but inside the VM's ±15 % — ⭐ the Windows `--poc4` is the number |
| J-2 the background modules' / recorder's `Task.Run` | ⛔ **NOT moved — premise failed (R-106)**: they hand work ACROSS frames (a `SlowBackground` module ticks up to 100 ms; a hung one is abandoned as a zombie, CE-3066). On a pool worker one such item would block the frame lane for its whole length, and a zombie would hold a worker for good — the very thing J-1's rail forbids. ⇒ needs a third kind of job (below) | **CHANGE** |
| ⭐ **J-3 measure before a dependency scheduler** | from the A-1 trace of the 1 000-man CGF, every system's read / write set: the Simulation phase is **149.8 ms serial, critical path 144.2 ms**; the whole frame **158.1 → 149.4 ms** at best on 6 cores (**−8.7 ms, 5.5 %**); even with every structural change deferred and managed reads not stamping, **−14.4 ms (9 %)** | ⛔ **NO-GO** — the heavy systems form ONE chain of real data dependencies |

**J-3, the chain that bounds it** (VM ms; every edge a component both touch, from the trace):
StressSystem 15.9 → CgfThreatEvaluation 6.2 (`TargetMemory`) → **BrainTick 78.3** (`TargetMemory`, `StanceIntent`, `BehaviorState`) →
Callout 13.7 (`BehaviorState`, `CalloutState`) → WeaponDispatcher 16.5 (`WeaponState`) → SquadCoordination 5.0 (`WeaponState`, `PartMetadata`).
On Windows (POC 2's `rec-serial`) the same six are **168 of 266 ms** — the same chain, so the same answer. ⇒ the cores are better spent INSIDE the
chain's systems (POC 2's brain pre-score bought −48 ms on Windows; a dependency scheduler's best is −9 to −14 ms on the VM).
**What would change it:** a system that stops writing a shared component (e.g. Brain → WeaponDispatcher meet only on `WeaponState` /
`BehaviorState`; splitting those into per-system outputs merged at the end) — or a second heavy system that is disjoint from the brain, which
the trace shows none of today.

**What was hard:** a fair before / after. The first "before" binary predated the coordinator merge, and the default config (no parallel code
at all) moved 59 → 78 ms in BrainTick — a base difference, not the pool. Re-measured against the merge commit, interleaved. And J-2's raycast
rail first passed its own mutation — a slot left unwritten kept the serial run's hit; it now asserts each slot carries its own ray id.

**Cost to finish (rough days):** the one pool + J-2 into a lane ½ (railed, small) · a THIRD job kind for cross-frame work — a capped queue
(at most N workers for long jobs, the rest always free for the frame lane; a completion handle instead of `Task`) — then the background
modules and the recorder's per-frame `Task.Run` (1 `Task` per frame today) onto it: 2 · the A-1 tracer's reader does not follow onto the
pool's own threads (it flows by `AsyncLocal`, which dedicated threads do not inherit) — pass the reader into a batch if it is ever needed
there: ¼.

⭐ **The Windows command** (8 cores, every node hidden): `git checkout exp/parallel-sim-4 && dotnet build Hrot/Runner/Hrot.ClusterRunner/Hrot.ClusterRunner.csproj -c Release && python scripts/parallel-sim-measure.py --pin --poc4`
— recorded: serial · everything on (the pool at its budget) · everything on with ONE thread; prints every node's frame and OS thread count
(the new `threads` field of `/diagnostics/architecture`). For the before / after on Windows, run the same command on `exp/parallel-sim-3`
(no `threads` there — Task Manager's thread column).

| item | state | evidence |
|---|---|---|
| J-1 one pool, two lanes | ✅ built `e7800031e` | §J-1 |
| J-2 ForEachParallel + raycasts on the pool, kernel wait list | ✅ built `c3dd550aa` | §J-2 |
| J-2 background modules / recorder | ⛔ stopped (R-106) — needs a cross-frame job kind | verdict |
| J-3 access sets, critical path, ideal | ✅ measured `724ce06ae` | [J3_Access_Analysis.md](parallel-sim-4/J3_Access_Analysis.md) |
| J-4 verdict, design §16, `--poc4` | ✅ | this file |

## J-1 — one pool, two lanes

| claim | code / evidence |
|---|---|
| the duplicate is gone: one class, `Fdp.Core.Jobs.FdpWorkerPool` | `Fdp.ModuleHost/Threading/WorkerPool.cs` deleted; `EgressPipeline` and `EqsSolverSystem` call `FdpWorkerPool.Shared.TryRun` |
| frame first: a worker drains the frame lane, takes ONE background item, looks again | `FdpWorkerPool.WorkerLoop` |
| one budget by construction (cores − 2, `HROT_SIM_THREADS`) | `FdpWorkerPool.Budget()` |
| 0 B per run; the generation-tagged cursors; a late worker never takes a newer run's batch | `_frameCursor` / `_bgCursor` (gen << 32 \| next) |
| a background window never roots its caller | `_bgWork = s_idle` after every window |
| ⭐ rail: a background window delays a frame run by at most one item | `ParallelEntitiesTests.J1_ABackgroundWindow_DelaysAFrameRun_ByAtMostOneItem` — 65 ms, 32 / 48 on workers; 🔴 red-proof (`while (TryBackground())` first) ⇒ red |
| rail: every background index once, a fault rethrown, a second window refused | `J1_TheBackgroundLane_RunsEveryIndexOnce_RethrowsAFault_AndRefusesASecondWindow` |

## J-2 — the loops on the pool

| claim | evidence |
|---|---|
| `ForEachParallel`: batches = the same entity ranges, run on the pool; a per-thread reused work object | `EntityQuery.RangeWork`; rail `ChunkIterationTests.J2_ForEachParallel_RunsOnThePool_EveryEntityOnce_AndAllocatesNothingWhenWarm` (0 B over 20 calls); 🔴 red-proof (`Rent` always new) ⇒ red |
| raycasts: one cast per batch; request / hit buffers kept (grown, never shrunk) | `RaycastSolverSystem` implements `IBatchWork`; rail `RaycastSolverSystemTests.J2_TheCasts_RunOnThePool_AndHitTheSameAsOneThread` (1 vs 4 threads, each slot its own ray id); 🔴 red-proof (cast 0 reads request 1) ⇒ red — ⚠ the first version of this rail PASSED its mutation (an unwritten slot kept the serial run's hit); it now asserts every slot carries its own ray id |
| the kernel's frame-synced wait list reused; faults still thrown together after every run is waited | `ModuleHostKernel._tasksToWait` |
| threads per node measurable everywhere | `/diagnostics/architecture` `process.threads`; `scale-rig.py` `threads` |
| ⚠ `TurretsOf` still builds a `Dictionary` per raycast tick when turrets exist | not this item — noted |

## Gates (HEAD, every opt-in OFF unless named)

| gate | result |
|---|---|
| Fdp.Core.Tests (all) | 1317 pass; 4 red: the 3 source-scan rails (JSON options ×2, wall clock — files POC 4 never touched; red at the base) + `CE2418_ASteadyScopeAllocatesNothing` (green alone — process-wide flake) |
| Fdp.Core query + parallel rails | ✅ 23 / 23 |
| Hrot.SimHost.Tests: EQS + Stress + Callouts (serial-vs-parallel) | ✅ 80 / 80 |
| Fdp.Network.Cyclone.Tests: egress + off-frame, `FDP_EGRESS_OFFFRAME=0` | ✅ 14 / 14 · ⚠ with `=1` one red: the test's ON-frame rig is then off-frame too (an environment artefact of a process-wide switch, not the pool) |
| Fdp.Toolkits.Tests: StarterPack (pre-score) + threat parallel + EQS | ✅ 184 / 184 |
| Fdp.Toolkits.Tests: raycast + physics + car kinematics | ✅ 185 / 185 |
| Fdp.ModuleHost.Tests: the frame-synced / policy / convoy classes | ✅ 74 / 74 |
| red-proofs | J-1 ✅ red · J-2 query ✅ red · J-2 raycast ✅ red (after tightening) |
| `DeterminismRails` · scenario subset (`bc-house-defended`, `ld-sentry-surprise`, `cc-platoon-bound`) | ✅ **5 / 5** · ✅ **3 / 3 PASS** |
