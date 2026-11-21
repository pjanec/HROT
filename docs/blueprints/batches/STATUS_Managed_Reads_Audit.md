<!--STATUS
state: LIVE — done (exp/managed-reads); not merged into any lane
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

⭐ **FLIP — done on this branch: a managed read no longer stamps the chunk version (the default); `HROT_MANAGED_READ_STAMP=1` restores the old
stamping read for one release.** ⛔ **But it does NOT unlock the egress visit gate on the CGF** — POC 3's premise was incomplete.

| question | answer | basis |
|---|---|---|
| does production mutate a RECORDED managed component in place after reading it? | ⭐ **no** — audit + detector agree | M-1: 26 sites, all classified; M-2: 0 hits on a recorded type over ~3 300 frames, ~1.76 M reads, CGF + SimHost, 1 000 men |
| what does mutate in place? | two NOT-recorded (`NoReplay`) bookkeeping types, `EgressPublicationState` / `OutgoingGrantsPending` — **kept** (replacing them whole allocates per publish, R-2084; nothing reads their version) | M-1 rows 1-4; M-2 hits tagged `[NoReplay]` |
| what was fixed? | `ReferenceInvoker` (replication attribute patch, no production caller) re-sets the instance — 0 B | M-1 row 12 |
| rails with the flip ON | **unchanged vs the stamping read on the same binary**: Fdp.Core 1 319 / 6 (stamping 1 320 / 5 — the extra is `EventBusTests.Publish_MultiThreaded_AllEventsRecorded`, 3/3 green alone, an event-bus thread test); Fdp.Toolkits 4 949 / 4 both; Hrot.Network.NED 196 / 0 both | M-4 |
| does a recording replay identically? | ⭐ **yes** — the same replayed-state hash in both modes, every frame equal to the live world | `ManagedReadsReplayEquivalenceTests`, red-proved |
| what does the flip save? | in a world that READS its managed components every frame: **recorded delta bytes −84 %** (2.06 MB → 0.33 MB over 120 frames). On the 1 000-man rig: **no measurable change** (CGF 321.8 → 320.6 KB compressed per delta frame) — its managed chunks are dirty from real writes anyway | M-4 |
| the egress gate on the CGF | ⛔ **still 0 skipped** (3.81 M visits; 4 859 of 4 943 full scans forced by `DescriptorOwnership`) — the tables it watches are WRITTEN every frame: `OwnershipIngressSystem` ~5 `DescriptorOwnership` sets/frame (`OwnershipApplier.Apply` sets a new record for every `OwnershipUpdate`, a repeat included), `BrainTickSystem` ~4 `PartMetadata`/frame, `NetworkAuthority` via command-buffer playback | M-4 stamp trace ⇒ filed **CE-3442** |
| the threaded editor | ⭐ unblocked on this point: a read on the main thread no longer writes the chunk version | the PureReads path, 0 B (`PureReadsTests`) |

⭐ **Must stay:** the two `NoReplay` in-place types (document, do not convert); the diagnostic switch for one release; the detector and the stamp
tracer as instruments (off by default, one `static readonly bool` each).
⚠ **Not done:** the editor feature rails and a scenario-regression subset were not run under the detector (the rig covered the CGF + SimHost
systems); `ParamUsage` shares its rewritten object with earlier snapshots — a different hazard, filed **CE-3443**.

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


## M-2 — the runtime detector (`HROT_MANAGED_MUTATION_CHECK=1`)

`FDP/Engine/Fdp.Core/ManagedMutationCheck.cs`. Every managed object a READ hands out (the shim's `GetRO`, `GetManagedComponentRO`,
`TryGetComponent` — so the editor's reflection reads too) is fingerprinted with the table's version for that entity and the system reading it
(the scheduler names it). At the kernel frame's end: the same object still in its slot, the version unmoved, the fingerprint changed ⇒ a hit,
written to `HROT_MANAGED_MUTATION_OUT/managed-mutations-<pid>.txt`. Managed SINGLETONS are not hooked (they never stamped). Rails
`ManagedMutationCheckTests` (4) — red-proved: the fingerprint comparison off ⇒ the in-place rail fails; the value-hash of a boxed struct off ⇒
the nested-struct rail fails.

| run (demo-city, 1 000 men, 50 vehicles, 10 aircraft; Release) | CGF | SimHost |
|---|---|---|
| stamping reads | 2 097 frames, 972 568 reads: `EgressPublicationState` ×2 363, `OutgoingGrantsPending` ×262 — both `[NoReplay]` | 1 109 frames, 781 152 reads: **0** |
| non-stamping (default) | 2 087 frames, 987 183 reads: `EgressPublicationState` ×2 363 | 1 163 frames, 786 333 reads: **0** |

⇒ ⭐ every hit is in M-1's table. 📌 Two DETECTOR bugs found on the way, both fixed with a rail: a struct past the depth limit was hashed by the
identity of a fresh box (false `RoutePlan` / `SubordinateOrders` hits), and "not recorded" tested ANY `Transient` bit (`NoPreview` alone is
recorded).

📌 **Rig trap, cost ~30 min:** on Linux `scripts/cluster-node.sh` runs the **Debug** runner unless `HROT_RUNNER_DLL` is set, and its `stop-all` is
`pkill -f 'ClusterRunner[.]dll'` — ⛔ a shell whose COMMAND LINE contains that path kills itself. Set it inside a wrapper script.

## M-3 — fixes

| site | fix | per-frame allocation |
|---|---|---|
| `JsonAttributeCompiler.cs` `ReferenceInvoker<T>` | re-set the same instance (`SetManagedComponent`) after the setter | 0 B |
| `EgressPublicationState`, `OutgoingGrantsPending` | ⭐ **kept** — `NoReplay`, node-local bookkeeping; no reader of their version | replacing whole = a new dictionary per publish (R-2084) |
| `ParamUsage` | none here — recorded either way (it calls `SetComponent`); the snapshot sharing is **CE-3443** | — |

## M-4 — the flip, the rails, the measurements

`ThreadedReads.PureReads = HROT_MANAGED_READ_STAMP != "1"` — the default; the legacy read is the diagnostic switch.

| gate (same Release binary, each run twice) | non-stamping (default) | stamping (`HROT_MANAGED_READ_STAMP=1`) |
|---|---|---|
| `dotnet test FDP/Engine/Fdp.Core.Tests -c Release --no-build` | 1 319 passed / 6 failed / 22 skipped | 1 320 / 5 / 22 |
| `dotnet test FDP/Toolkits/Fdp.Toolkits.Tests -c Release --no-build` | 4 949 / 4 / 1 | 4 949 / 4 / 1 |
| `dotnet test Hrot/Network/Hrot.Network.NED.Tests -c Release --no-build` | 196 / 0 | 196 / 0 |

The common reds name files this branch does not touch (`BitMask512` paranoid throws, `DeployProcessorSystem.cs:97` wall clock,
`BridgeRoads.cs` / `TerrainContentHash.cs` JSON options, `TakeCheckpointIntent`, the S0 id/type rails, `BinaryMergerTests`) ⇒ pre-existing.
⚠ Their pre-existence was shown by the identical result under the stamping read, not by a run at the base sha.

| measurement | stamping | non-stamping |
|---|---|---|
| ⭐ replay equivalence rail — replayed-state hash (120 frames) | `0296B83E…E61D` | `0296B83E…E61D` — **equal** |
| replay equivalence rail — recorded delta bytes | 2 063 835 | **326 427 (−84 %)** |
| rig CGF recorder — compressed / uncompressed per delta frame | 321 795 / 5 409 060 | 320 607 / 5 302 590 |
| rig SimHost recorder — compressed / uncompressed per delta frame | 177 040 / 2 453 528 | 183 823 / 2 485 770 |
| rig CGF egress gate — skipped / visited | 0 / 3 788 328 | **0 / 3 806 127** |
| …full scans: `DescriptorOwnership` · `PartMetadata` · `NetworkAuthority` | 4 845 · 3 809 · 3 277 of 4 929 | 4 859 · 3 851 · 3 305 of 4 943 |

**Who writes them** (`HROT_STAMP_TRACE=DescriptorOwnership,NetworkAuthority,PartMetadata`, `FDP/Engine/Fdp.Core/StampTrace.cs`), CGF, 2 162 frames:

| stamps | type | writer |
|---|---|---|
| 11 776 | `DescriptorOwnership` | `OwnershipIngressSystem` (→ `OwnershipApplier.Apply`) |
| 8 610 | `PartMetadata` | `BrainTickSystem` |
| 4 290 | `PartMetadata` | `NetworkSpawningSystem` |
| 4 166 | `DescriptorOwnership` | `CycloneEgressSystem` |
| 2 891 | `NetworkAuthority` | command-buffer playback |
| 1 871 | `NetworkAuthority` | `NetworkSpawningSystem` |

⇒ the gate cannot skip on the CGF while any of its three "authority" tables is written every frame; the remedy (skip a no-op ownership Set,
or a per-entity rather than per-table authority signal) is **CE-3442**.
