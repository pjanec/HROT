<!--STATUS
state: LIVE — in progress
updated: 2026-11-12
current-answer: P-8 the verdict (top, when written) · then the items
related-designs:
  - ../../DESIGN_Threaded_Simulation_Presentation.md — OWNS this: §14 the target shape this POC builds · §13.8 the instruments' answer · §12 the world inversion
  - HANDOFF_Coordinator_To_Backend_Threaded_POC_2.md — the dispatch (at a89010765)
  - STATUS_Threaded_Analysis_Instruments.md — A-1…A-5, the evidence (masks, costs, A-4's per-item table)
-->

# STATUS — POC 2 of the threaded editor: the target shape of §14

**Where:** branch [`exp/threaded-poc-2`](https://github.com/pjanec/HROT/tree/exp/threaded-poc-2), from `exp/threaded-analysis` (`253b0f00b`) with
`coordinator` merged (`5f0761334`; the one conflict, the design's §13.8 vs §14, resolved by keeping both). ⛔ Nothing goes to a lane.
Switch: **`HROT_THREADED_KERNEL=2`** (default OFF ⇒ the editor is today's).

## P-8 — the verdict

⏳ not yet.

## The items

| # | item | state | sha | note |
|---|---|---|---|---|
| P-1 | reads are reads | ✅ built + rails (worker, Opus-reviewed) | next commit | §P-1 below |
| P-2 | the 3 unsafe statics | ✅ 2 fixed + rails; 1 premise failed | next commit | §P-1 below |
| P-3 | the UI work leaves the tick | | | |
| P-4 | two worlds | ⏳ built, not yet run | | §P-4 below |
| P-5 | the S→A sync | ⏳ built, not yet run | | §P-4 below |
| P-6 | the gizmo frame published; picking | | | |
| P-7 | UI writes as commands; the guard | ⏳ the guard built | | §P-7 below |

## P-1 / P-2 — reads are reads; the unsafe statics *(Sonnet worker, Opus review, `2026-11-12`)*

**Switch:** `Fdp.Core.ThreadedReads.PureReads` = `HROT_THREADED_KERNEL=2` or `HROT_PURE_READS=1` (a `static readonly bool`).

| change | where |
|---|---|
| a managed read stamps nothing and allocates nothing: `ManagedComponentTable.GetRefRO` (a shared null slot for an unallocated chunk), `EntityRepository.GetManagedComponentRefRO`, `UnsafeShim.ManagedAccessor.GetRO` takes it when the switch is ON | `ManagedComponentTable.cs`, `EntityRepository.cs`, `Internal/UnsafeShim.cs` |
| `TryGetComponent` reads through the RO path when ON (it took the RW path and stamped) | `EntityRepository.TryGetComponentPure` |
| `GetSingletonUnmanagedRO<T>` — a read-only ref; **50 read-only call sites in 42 files migrated** (always on: a strict read) | the writers left: `EqsModule.cs:130,165` · `EqsResultUpdateSystem.cs:90` · `EqsSolverSystem.cs:828` · `NavigationSolverComponentRegistry.cs:99,105` · `AccurateLineOfSightTest.cs:85` |
| `UtilityInputNames` → a `Lazy` (`NewTable()` shared with the rail); `UtilityDecisionCatalog.Shared` → `Interlocked.CompareExchange` | always on (thread-safety, no behaviour change) |
| ⛔ `RunModeChoice.PresetCache` — **A-2's premise FAILED**: every access (`RunModeChoice.cs:571,574,583`) is already under `lock (PresetCache)` | no change |

**Rails:** `PureReadsTests` (9, `FDP/Engine/Fdp.Core.Tests/`) — the pure paths leave every chunk version unchanged, with the legacy path's
counter-proof, and allocate 0 B in a steady loop; `UtilityStaticsRaceTests` (2, `Fdp.Toolkits.Tests/Utility/`) race two threads. **Red-proved:**
the pure paths made to stamp / allocate ⇒ 4 rails red; `Shared` back to `??=` ⇒ red; the lazy table built `PublicationOnly` ⇒ red.

⚠ **Hidden dependencies the switch exposes** — managed objects mutated IN PLACE after a `GetComponent`, recorded today only because the read
stamps (with the switch ON their changes stop reaching the recorder / egress): `SmartEgressSystem.cs:76,114,115,127,128` (`EgressPublicationState`),
`OwnershipRecomputeSystem.cs:110,115`, `PartialOwnerReclaimSystem.cs:91,98` (descriptor sets). They break CLAUDE.md's "managed components are
immutable, replaced whole" — each needs a `SetManagedComponent` of a new value (the cost to finish, not done here).

**Gates** (base reds named in `STATUS_Threaded_Analysis_Instruments.md` §A-1 gates):

| command | result |
|---|---|
| `dotnet test FDP/Engine/Fdp.Core.Tests -c Release --no-build` (switch OFF) | 1318 passed · 6 failed · 22 skipped — the 5 base reds + `AsyncRecorderTests.ConcurrentAccess_MultipleThreads_ThreadSafety`, flaky: 4/7 reruns red with the change, 2/4 with the original line restored |
| the same with `HROT_PURE_READS=1` | 1319 passed · 5 failed (the base reds) — no new red with the switch ON |
| `Fdp.Toolkits.Tests --filter …Utility` | 169 passed |
| `Fdp.ModuleHost.Tests` (OFF) | 257 passed · 1 failed: `KernelFrameTimingsTests.ASteadyFrameOfParts_Allocates0B…`, green alone (OFF and ON) — a suite-order flake, not checked at base |
| `dotnet build Hrot/Runner/Hrot.ClusterRunner -c Release --no-restore` | 0 errors (Opus, after its own fixes) |
| `Fdp.Examples.Showcase` | ⚠ outside `HROT.sln`, does not compile (121 errors, missing `Fdp.Kernel` types) — its 8 migrated sites were REVERTED, not shipped unproven |

## P-4 / P-5 — two worlds and the sync *(Opus, `2026-11-12`)*

**What was built** (all behind `HROT_THREADED_KERNEL=2`):

| piece | where | what |
|---|---|---|
| the S clone | `EntityRepository.CloneForSimulation` (`FDP/Engine/Fdp.Core/EntityRepository.TwoWorlds.cs`) | every table, the entity index, both clocks, and EVERY singleton in tables S owns (unmanaged by value, managed as the same object) |
| the S→A sync | `EntityRepository.SyncPresentationFrom` | the entity index and the TABLE-masked tables from S; the bits A had OUTSIDE the mask are put back on every entity still alive with the same generation (A's UI state survives); singletons copied except the ones A rebuilds |
| the row copy | `EntityRepository.SyncRowsFrom` + `IComponentTable.CopyRowFrom` | every component of the selected entities, whatever the mask (the inspector a ROW reader) |
| the kernel on S | `ModuleHostKernel.RetargetLiveWorld` | the kernel ticks S; every automatic provider re-made over S |
| the editor | `EditorSubsystem.TwoWorldsStep` / `EngageTwoWorlds` / `DisengageTwoWorlds` / `SyncPresentationWorld` | engaged while a PREVIEW runs; `NetworkEntityMap`: S keeps the one the systems captured, A gets its own rebuilt after each sync |
| the lock | `ThreadedKernelGate` (`TwoWorlds`, `Engaged`, `LockWholeTick`) | ⭐ two variants measured: the worker holds the lock for the sync only (default), or for the whole tick (`HROT_TWO_WORLDS_LOCK_TICK=1`) |
| the masks | [presentation-masks.json](threaded-poc-2/presentation-masks.json) (`scripts/access-trace-masks.py` from the A-1 trace) | `HROT_TWO_WORLDS_MASKS=<file>`, `HROT_TWO_WORLDS_MASK=ui-no-inspector` (default) · `map` · `ui-all`; no file ⇒ every type |

**Decisions — claim table:**

| decision | code — how it is | design — how it was meant |
|---|---|---|
| ⭐ S is a CLONE the kernel is pointed at, not a second composed node | ✅ the editor composes the presentation INSIDE the node bootstrap (`RegisterHostSystems`, `EditorSubsystem.cs` ~2100: `_world = ctx.World`); ✅ the kernel holds one world field (`ModuleHostKernel.cs:48`), used in ~30 places | §12.4: "build the node on S and the presentation on A … the main build item" — ⭐ deferred: the retarget gives the same two worlds without recomposing, and the systems it misses are exactly the guard's list (the cost to finish) |
| engaged only while a PREVIEW runs | ✅ loads are 2PC transactions handled in the node frame (`IEditorLogic.LoadScenarioByName`, the edit-load handler); edits and saves write A in Edit | §14 picture ①: the editor's playing mode is the threaded one; Edit stays single-threaded in this POC |
| ⚠ `SelectionState` comes FROM S until P-3 | ✅ it is UI state rewritten every frame from `SelectionStore` (`SelectionState.cs:34-36`) — ⛔ but by `SelectionRequestSystem` / `EcsSelectionState.Sync`, which run INSIDE the tick, i.e. on S (A-4 per-item table). ⭐ First written as "never from S"; corrected before any run: A would have kept a stale selection | §14.1 "UI-only state is written directly in A" — true once P-3 moves the selection systems out |
| events forwarded both ways at the sync | ✅ the UI publishes requests on A's world bus (`SelectionChangeRequest`, orders, edits — A-4); S's systems read S's bus; `FdpEventBus.PublishCurrentOf` copies a bus's readable events into another's pending (`FdpEventBus.cs`, CE-3316) | §14.1 "logs and events forwarded" — the baseline of the command path (one tick late) |
| `NetworkEntityMap`: A gets its OWN, rebuilt | ✅ A-3: rebuilt and kept 0.76 ms, 0 B; ✅ two plain dictionaries changed in place every tick (§13.1) | §14.1 row 3 "REBUILT (`NetworkEntityMap`)" |
| managed singletons: the same object in both worlds | ✅ the clone copies the slot (`CopySingletonsFrom`) | §14.1 row 4 — ⚠ an object the simulation changes IN PLACE (not swapped) is a race this POC does not fix; §13.1 names them |

## P-7 — the guard *(Opus, `2026-11-12`)*

`Fdp.Core.WorldGuard` (`HROT_WORLD_GUARD=1` record · `=throw`): rides the A-1 tracer's write hooks without its counting. It records
**S written off the sim thread** (a UI write that should be a command), **A written by the sim thread** outside the sync (a system that captured
A instead of using the view the kernel passes), and either world's **bus published from the wrong thread** — once per (violation, kind, type,
reader), with the first stack, to `HROT_WORLD_GUARD_OUT`.
