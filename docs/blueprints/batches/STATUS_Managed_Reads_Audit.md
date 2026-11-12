<!--STATUS
state: LIVE — in progress (exp/managed-reads)
updated: 2026-11-12
current-answer: VERDICT (top), then M-1 … M-5
stale-below: nothing yet
related-designs:
  - ../../DESIGN_Threaded_Simulation_Presentation.md — §13.8 / §14.1 "reads are reads" (A1-2: a managed read stamps the chunk version); this file's verdict folds into §14.1
  - ../../DESIGN_Parallel_Simulation.md — §13 (on exp/parallel-sim-3: the egress visit gate skips 0 because reads stamp); this file's verdict folds into §13
  - ../../DESIGN_Egress_Change_Detection.md — §5 the visit gate whose skip rate M-4 measures
  - HANDOFF_Coordinator_To_Backend_Managed_Reads_Audit.md — the dispatch (CE-4445, at 91d32e914)
  - HANDOFF_Coordinator_To_Backend_Parallel_Sim_POC_4.md — runs in parallel; ⚠ this branch touches Fdp.Core (EntityRepository, UnsafeShim) and Fdp.ModuleHost (SystemScheduler, ModuleHostKernel) — see "Shared files"
-->

# STATUS — managed component reads stop stamping (CE-4445, R-4210)

## VERDICT

_(written at M-5)_

## Shared files (POC 4 runs in parallel)

⚠ This branch edits `Fdp.Core` and `Fdp.ModuleHost`, but not their threading:

| file | what |
|---|---|
| `FDP/Engine/Fdp.Core/ThreadedReads.cs` | the one switch (`PureReads`) |
| `FDP/Engine/Fdp.Core/ManagedComponentTable.cs`, `EntityRepository.cs`, `Internal/UnsafeShim.cs` | the read path that does not stamp; the detector's read hooks |
| `FDP/Engine/Fdp.Core/ManagedMutationCheck.cs` | new: the runtime detector |
| `FDP/Engine/Fdp.ModuleHost/Scheduling/SystemScheduler.cs` | names the running system for the detector (only with `HROT_MANAGED_MUTATION_CHECK=1`) |
| `FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs` | calls the detector's end-of-frame check (only with the switch on) |

## The unified implementation — one read path

Two implementations existed. ⭐ **POC 2's P-1 (`exp/threaded-poc-2` `f98bf9b60`) was kept**; `exp/parallel-sim-3` `754d52ce2` was not:

| | POC 2 P-1 (kept) | parallel-sim-3 (dropped) |
|---|---|---|
| a read of an entity whose chunk does not exist | a shared null slot, **no allocation** | allocated the chunk |
| `TryGetComponent` | covered (`TryGetComponentPure`) | still stamped |
| the switch | `static readonly` (folded by the JIT) | read per call |

Committed `224d37a07` with POC 2's tracer lines removed. Rails: `PureReadsTests` (9).

## M-1 — the static audit

⭐ **Tool:** `Hrot/Tools/Hrot.Tools.ManagedMutationAudit` (`hrot-mutation-audit`, outside the solution). It opens `HROT.sln` in an `MSBuildWorkspace`,
finds every managed READ (`GetComponent` · `GetComponentRO` · `GetManagedComponentRO` · `GetManagedComponent` · `TryGetComponent` ·
`TryGetManagedComponent` · `GetManagedComponentRefRO` · `GetSingletonManaged` · `GetSingleton`, with a reference-type argument, on a repository /
view / world / shim), follows the object into the local it lands in (`var x =`, `out var x`, one alias level), and reports every place that
mutates it: **A** a field / property / element set, `++`/`--`, passed by `ref`, a collection `Add` / `Remove` / `Clear` / indexer set; **B** a call
of an instance method of the component (it may mutate — each one read by hand).

```
dotnet build Hrot/Tools/Hrot.Tools.ManagedMutationAudit -c Release
dotnet Hrot/Tools/Hrot.Tools.ManagedMutationAudit/bin/Release/net8.0/hrot-mutation-audit.dll HROT.sln <out.md>
```

⚠ Why not `Hrot.Tools.ReachAnalysis` (exp/threaded-analysis): it answers *"what does each system write"* per system; the question here is
*"is the object a READ returned mutated afterwards"* — it needs the read's result followed to its uses, which reach does not do.

### The table — every in-place mutation of a managed object obtained by a read
