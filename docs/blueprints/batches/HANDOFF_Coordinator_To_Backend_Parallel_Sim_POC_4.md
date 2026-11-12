<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — OWNS this: ⭐ §13 a job system? (four ways to use the cores, the lean) · §11–§12 the aggregate and the timer · §14 (on exp/parallel-sim-3) POC 3 as built
  - STATUS_Parallel_Sim_POC_3.md (on exp/parallel-sim-3) and STATUS_Ui_Windows_POC_2.md (on exp/parallel-sim-2) — the baseline
  - STATUS_Threaded_Analysis_Instruments.md (on exp/threaded-analysis) — A-1, the access tracer whose per-system read / write sets J-3 uses
-->

# HANDOFF — coordinator → backend: an OVERNIGHT EXPERIMENT — POC 4 of the simulation's throughput: ONE pool, and is a dependency scheduler worth it?

**For:** the simulation-throughput backend session. **From:** coordinator. **Dispatched at `<stamp>`**, scope frozen there. ⭐ **Still an experiment.**
🔒 User, `2026-11-12`: *"yes, queue both for the simulation lane"* — on §13's lean: one pool with two priorities, and measure before a dependency scheduler.
⛔⛔ **Autonomous — never stop for a question** (R-4156). Decide, record with a claim table, continue; a failed premise stops THAT item (R-106).

## Where to work

⭐ A NEW branch **`exp/parallel-sim-4`** from **`exp/parallel-sim-3`**, with **`coordinator`** merged in. Status file
`docs/blueprints/batches/STATUS_Parallel_Sim_POC_4.md` ON THAT BRANCH. ⛔ Nothing merges into a lane; ⛔ do not push to `backend`. Everything
opt-in, default OFF. Extend `parallel-sim-measure.py` with `--poc4` so ONE command on the 8-core Windows machine measures it (every node HIDDEN).

## The situation (§13)

Four independent ways use the cores, each sizing itself to the machine: `WorkerPool` (`Fdp.ModuleHost/Threading/WorkerPool.cs` — EQS windows,
off-frame egress), `FdpWorkerPool` (`Fdp.Core/Jobs/`, these branches only — the parallel systems; ⛔ a DUPLICATE of the first), the .NET thread
pool (`Task.Run` — background modules `ModuleHostKernel.cs:738`, the recorder's I/O `AsyncRecorder.cs:224`), `Parallel.ForEach`
(`EntityQuery.cs:444` — allocates — and `RaycastSolverSystem`); plus 8 dedicated `new Thread` sites. Windows POC 1 already saw oversubscription
("8 threads" on 7 pinned CPUs). Systems declare NO component access.

## The items

| # | item | done when |
|---|---|---|
| **J-1** | ⭐ **ONE pool, two priorities** — merge `FdpWorkerPool` into `WorkerPool` (keep the per-batch ordered merge, 0 B, the generation-tagged cursor); a FRAME lane (the parallel systems, the replica sync's parallel copy) that always goes first, and a BACKGROUND lane (EQS windows, the off-frame egress scan, background modules, the recorder) that yields to it; one budget (cores − 2) by construction | the duplicate deleted; every existing rail (serial-vs-parallel, determinism, P-0's 0 B, EQS, egress) green on the one pool; a rail that a background job never delays a frame batch beyond one work item |
| **J-2** | move `EntityQuery.ForEachParallel` / the raycast solver off `Parallel.ForEach`, and the background modules' `Task.Run`, onto the pool | 0 B per steady run where it allocated; the thread count of the CGF process at 1 000 men before / after (`dotnet-counters` or `Process.Threads`) |
| **J-3** | ⭐ **MEASURE before building a dependency scheduler**: from the access tracer (`FDP/Engine/Fdp.Core/AccessTrace.cs` + `scripts/access-trace-*.py`, on `exp/threaded-analysis` — run it on the 1 000-man CGF node, headless), each system's component READ / WRITE set per phase; compute which systems are pairwise disjoint, the CRITICAL PATH of each phase (the longest chain that must stay ordered), and the frame time if every disjoint group ran side by side on the Windows per-system ms | a table: phase · systems · the critical path ms · the ideal ms with side-by-side groups · the gain over today; ⭐ a recommendation — build declared-access scheduling or not — with what would change it |
| **J-4** | ⭐ **the verdict** — the CGF frame before / after J-1 + J-2 (VM; the Windows command), J-3's recommendation, the cost to finish | top of the status file; folded into Parallel Simulation §13 (as-built) |

## Rules

- **Correctness first**: every rail that ran on either pool runs on the one pool; Deterministic results unchanged at 1 / 2 / 4 / 8 threads.
- **0 B per steady tick** (R-2084). Run anything slow in the background; never poll. ⛔ No screenshots or recordings in git.

**Done when:** J-4 is written — however far J-1…J-3 got.

---
_coordinator lane, 2026-11-12_
