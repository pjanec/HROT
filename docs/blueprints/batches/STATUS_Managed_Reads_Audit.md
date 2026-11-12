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

📐 Run `2026-11-12` on a clean tree: 90 projects, **0 compile errors, 406 managed reads tracked, 26 sites** (13 direct, 13 method calls), 87 s.
Classified by hand:

| # | site | type (policy) | mutation | verdict |
|---|---|---|---|---|
| 1 | `SmartEgressSystem.cs:114, 115, 128` | `EgressPublicationState` (Transient · NodeLocal) | `LastPublishedTickMap[k] = tick`, `DirtyDescriptors.Add/Remove` | ⭐ **real, harmless** — see "Transient bookkeeping" |
| 2 | `SmartEgressUtil.cs:156, 157, 181, 199` | `EgressPublicationState` | same maps; `LastPublishedTickMap.Clear()` | ⭐ same |
| 3 | `EqsSensorConfigEgressTranslator.cs:241` | `EgressPublicationState` | `LastPublishedTickMap.Remove` | ⭐ same |
| 4 | `OwnershipRecomputeSystem.cs:115` · `PartialOwnerReclaimSystem.cs:99` | `OutgoingGrantsPending` (Transient) | `Descriptors.Remove` | ⭐ same |
| 5 | `EgressHold.cs:50, 61, 62` | `EgressHold` — a managed **singleton** | `Reason = null`, `Lapsed++` | out of scope: a singleton read (`storage[0]`) never stamped |
| 6 | `WorldKeyframes.cs:189`, `EcsRecordReplayController.cs:247`, `ReplayBrowserSubsystem.cs:932-933` | `NetworkEntityMap` (singleton) | `RebuildFromWorld`, `Clear` | out of scope (singleton, a service) |
| 7 | `VehicleCommandSystem.cs:219` · `TerrainResidency.cs:799` · `DoorLeaf3D.cs:55` · `WorldInfoReport.cs:85` | `RoadNetworkHolder` · `IGeographicTransform` · `TerrainWorld` · `INavmeshProvider` (singletons) | `Borrow`, `SetOrigin`, queries | out of scope (singleton services) |
| 8 | `OwnershipRecomputeSystem.cs:322` | `DescriptorOwnership` | `WithOwner(...)` | ✅ false positive — returns a copy (`this with { … }` over new collections) |
| 9 | `DeferredTakeOwnershipIngressTranslator.cs:138` | `PendingAuthorityGrants` | `WithGrant(...)` | ✅ false positive — a copy |
| 10 | `RouteWaypointGizmo.cs:340` · `PersonalRouteAuthoringSystem.cs:114` | `RoutePlan` | `WithWaypoints(...)` | ✅ false positive — a copy |
| 11 | `MacroInstanceTranslator.cs:37` | `VariantForms` | `MacroListWith(...)` | ✅ false positive — parses into a new list |

**Found outside the tool** (the graph inventory of read methods — `search_graph name_pattern "^(Try)?Get(Managed)?(Component|Singleton)[A-Za-z]*$"
label Method`, 130 nodes, 2 pages — named read paths the tool's set lacked):

| # | site | verdict |
|---|---|---|
| 12 | ⭐ `JsonAttributeCompiler.cs:58-59` `ReferenceInvoker<T>` — the replication attribute patch: `context.GetManagedComponent<T>()` then the setter mutates it in place | ⭐ **real**. No production caller (`RegisterReferencePath` is used only by `EntityAttributeSchemaTests`); R1's `ReplaceInvoker` is the live path. **Fixed (M-3):** re-sets the same instance (`SetManagedComponent`), so the version moves; 0 B. Missed by the tool: `T` is a type parameter |
| 13 | `ParamUsage.cs:100-115` (named by the handoff) — a non-generic `GetComponent(e, t)`, rewritten in place (SR-160 "mutable DTO state by design") | ✅ **recorded today and after the flip** — it calls `SetComponent` after the walk. ⚠ Separate hazard, not a stamp question: the object is shared with every earlier snapshot. Missed by the tool: non-generic read |
| 14 | `UnitDebugSettingsMemory.cs:43-48` (named by the handoff) | out of scope — a singleton store |
| 15 | `ComponentReflector.GetComponent` · `RepositoryAdapter` / `SimulationViewAdapter.GetComponent` (editor inspector, non-generic) | they call the generic reads by reflection ⇒ the runtime detector (M-2) sees them |

**Transient bookkeeping (rows 1-4).** Both types are `Transient` ⇒ never recorded, snapshotted or replicated; a grep for
`HasComponentChanged` / a version read on either type finds nothing ⇒ **no consumer reads their chunk version**, so the flip changes nothing
for them. Replacing them whole would allocate a new dictionary per publish on the egress hot path (R-2084) ⇒ ⭐ **kept as mutable node-local
bookkeeping**, and the detector reports such hits tagged `[Transient]` rather than as defects.

**Coverage, stated:** one method at a time; one alias level; generic `T` reads and non-generic reads are not followed (rows 12, 13 were found
by the graph + grep instead); `check_index_coverage` is not in the CLI, so the graph inventory's completeness could not be checked. ⇒ the
runtime detector (M-2) is the second net.
