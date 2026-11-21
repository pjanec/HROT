<!--STATUS
state: LIVE — built; Part 1 done (CE-4456)
updated: 2026-11-12
current-answer: the whole file
stale-below: nothing
related-designs:
  - ../../DESIGN_Threaded_Work_Inventory.md — §7 the critical review (P-1: one common base) this branch answers
  - HANDOFF_Coordinator_To_Backend_Integration_Branch.md — CE-4456, this work
  - STATUS_Memory_And_Contact.md — Part 2, measured on this branch
-->

# STATUS — `exp/integration`: the one base (CE-4456 Part 1)

**Branch:** `exp/integration` — ⛔ experimental; never merged into the merged branches or any lane.

## The merges, in order

| # | merged | at | conflicts | resolution |
|---|---|---|---|---|
| 0 | the base: **`exp/perf`** | `bd06a6d35` | — | (merge-ready, CE-4454 P-3) |
| 1 | **`exp/threaded-poc-4`** | `251c68538` | none | — |
| 2 | **`exp/read-check`** | `3efe823e5` | `AccessTrace.cs` | `On` = `HROT_ACCESS_TRACE` ‖ `WorldGuard.On` (POC 2) ‖ `EntityCensus.On` (census) · `Aggregate` keeps POC 2's meaning (counting only with `HROT_ACCESS_TRACE`) · ONE `ReaderName` (both sides added one) |
| | | | `EntityRepository.cs` | the census's entity-carrying records (`RecordE`, a superset of `Record`) + the read-check hook in `GetManagedComponentRefRO` |
| | | | `DESIGN_Threaded_Simulation_Presentation.md` | POC 2–4's as-built §14 kept; the linked CE-4436 line |
| 3 | **`exp/measure-2`** (its current head; K-5 not landed yet — re-merge when it does) | `bdd9fd8d5` | `AccessTrace.cs` · `EntityRepository.cs` · both designs | measure-2 already carries poc-4 + the census and resolved `AccessTrace` the same way ⇒ HEAD kept, the read-check hook kept; the designs keep both additions (Parallel Simulation §18 as measured + §19; Threaded §14.5–14.7) |
| 4 | **`coordinator`** | `75e399786` | none | Parallel Simulation §18 renumbered after the merges: **18.1a** the census detail · **18.2** the declared last-tick copy (coordinator's) · **18.3** the read-check cost — §18.2 now points at its measurement |
| 5 | **`exp/measure-2` again** (its K-5 verdict) | `07be20f0e` | `DESIGN_Parallel_Simulation.md` | both kept: measure-2's heavier-combat section numbered **§18.4**; its one code change (the threaded editor drains the staging remap inside the worker's locked sync) builds clean |

⭐ **One read path, one pool** (checked after the merges): `ThreadedReads.PureReads` is the only read-mode switch (`ThreadedReads.cs:18`),
`FdpWorkerPool` the only worker pool (`Jobs/FdpWorkerPool.cs:23`) — graph `search_graph` `.*Pool$` (Class) + `git grep "class \w*(WorkerPool|JobPool|JobSystem)"`.

## The build and the rails

`dotnet restore HROT.sln` + `dotnet build HROT.sln --no-restore` — **0 errors** (208 warnings). Then each touched project's OWN rails, `--no-build`:

| rails | result |
|---|---|
| `Fdp.Core.Tests`: `TwoWorldsTests`, `PureReadsTests`, `ParallelEntitiesTests`, `ParallelSyncTests`, `EntityCensusTests`, `AccessTraceOffTests`, `ManagedMutationCheckTests`, `ManagedReadsReplayEquivalenceTests`, `ContentVersioningTests` | ⛔ first run: 5 red — **a merge defect, fixed** (below) ⇒ ✅ **30 / 0 / 1 skip** |
| `Fdp.Diagnostics.Contracts.Tests` `PublishedGizmoFrameTests` (in no solution — restored and built on its own) | ✅ 2 / 0 |
| `Fdp.Toolkits.Tests` `QueryTimingsTests` (measure-2's off-by-default rail) | ✅ 5 / 0 |
| the read-check rail (`HROT_READ_CHECK=1 …ReadCheckBench.dll check`) | ✅ 7 / 7 |
| `Hrot.SystemTests` `DeterminismRails` | ✅ 5 / 0 |
| `Hrot.ClusterRunner.Tests` the census rails (`EverySystemForgetsTheLastWorldTests`, `EverySystemStateFieldIsClassifiedTests`) | 3 red — ✅ **pre-existing**: every listed field is declared on `exp/perf` already (none on an added line of the merge diff; spot-checked six), the three world-boundary systems (`RuleEditSystem`, `RuleLeafSampler`, `MacroExpansionSystem`) exist on `exp/perf` |

### ⛔ The merge defect — test component ids collided across the branches

Each branch picked free `[ComponentId]`s for its test components; merged, four collided and the registry threw on registration:

| id | the two | fixed |
|---|---|---|
| 494 | `TwoWorldsTests.TwService` (POC 2) · `ManagedMutationCheckTests.MmcRoute` (CE-4445) | `TwService` → 496 |
| 503 · 504 · 505 | `AccessTraceOffTests` (census) · `ParallelEntitiesTests.Input/Counter`, `ContentVersioningTests.Cv` (exp/perf) | → 497 · 498 · 499 |

⚠ First attempt used 508–513 — ids must stay under 512 (masks and singletons are 512 wide): `TwoWorlds` and `AccessTraceOff` then threw
out-of-range. ⇒ the free ids under 512 in the `Fdp.Core.Tests` process (Fdp.Core's own components + its tests): 471–475, 478, 479, 496–502, 507.

## Not run

By ruling (CE-4454 / CE-4456: test the changed features): the broad suites and the scenario regression.
