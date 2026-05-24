# Universal Breakpoints — Onboarding

Welcome. This workstream builds the **Universal Breakpoint** diagnostic substrate for the HROT/FDP engine — a single data-driven breakpoint surface that subsumes Slice 1's narrow Blueprint-node pauses into a polymorphic predicate engine covering ECS data, transient events, BTree/HSM execution, Blueprint variables, structural mutations, spatial bounding boxes, and entity lifecycle.

---

## 1. What this slice ships

A new debugging layer that lets developers halt the simulation on arbitrary conditions over the live ECS — not just Blueprint node execution. Built almost entirely by wiring together engine primitives that already exist (predicate compiler, event scanner compiler, `EntityRepository.SyncFrom` + `.QueryDelta`, `EntityCommandBuffer`, soft-pause adapter). Only one new ECS-level abstraction (a manager + two `IEcsModuleSystem` siblings) is added per subsystem.

Read these in order:

1. **[DESIGN.md](./DESIGN.md)** — full architecture, phase plan, success conditions, project-dependency check, and a coverage matrix showing every final idea from the design talk and where it lands.
2. **[TASK-DETAIL.md](./TASK-DETAIL.md)** — per-task work + unit-test specifications. Tasks are grouped by phase (P0…P9 + INT).
3. **[TASK-TRACKER.md](./TASK-TRACKER.md)** — checkbox tracker; updated as work lands.
4. **[DEBT-TRACKER.md](./DEBT-TRACKER.md)** — empty starting tracker for tech debt accumulated through the workstream.

---

## 2. Source talk (the why)

The design crystallised over a multi-day architectural conversation captured in:

- **[design-talk.md](./design-talk.md)** — the full conversation; long but essential for understanding the trade-offs (especially the "destructive SyncFrom" → "Virtual Snapshot" pivot, deferred mutation, the multi-node drift trade-off, and the AAA-style BTree/HSM mapping).
- **[soft-pause.md](./soft-pause.md)** — short primer on the Slice 1 soft-pause mechanism that this design re-uses.
- **[universal-breakpoints-idea.md](./universal-breakpoints-idea.md)** — earlier sketch that the design talk refined; useful as a delta reference.

Background docs you'll need:

- **[Blueprint_Subsystem_Slice2_Candidates.md](../blueprints-1/Blueprint_Subsystem_Slice2_Candidates.md)** §5 (Theme D — Universal Breakpoints).
- **[Blueprint_Subsystem_Architecture_v1.2.md](../blueprints-1/Blueprint_Subsystem_Architecture_v1.2.md)** for the Blueprint runtime and partition allocator.
- **[HROT architecture.md](../../docs/HROT%20architecture.md)** for engine-wide context (ECS, soft pause, time controllers, recorder).

---

## 3. Where the touched code lives

This workstream spans a handful of engine and subsystem projects; do not be intimidated by the breadth — most changes are additions, not edits.

| Area | Path | What we change |
|---|---|---|
| Time-controller interface (Slice 1) | [Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintTimeController.cs](../../Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintTimeController.cs) | Rename → `IEngineDebugTimeController` (P0) |
| Time-controller adapter | [Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/MasterSyncTimeControllerAdapter.cs](../../Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/MasterSyncTimeControllerAdapter.cs) | Re-target to the new interface; no behavioural change |
| Blueprint debug session (Slice 1) | [Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs](../../Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintDebugSession.cs) | Wire `OnExternalHit` to the new manager (P7) |
| Predicate DTO hierarchy | [FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs](../../FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs) | Add `BlueprintVariablePredicateDto` + `[JsonDerivedType]` entry (P6) |
| Predicate compiler | [FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/IPredicateCompiler.cs](../../FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/IPredicateCompiler.cs) and concrete impl | Extend with trace-buffer scan branch + blueprint-variable IL path (P5, P6) |
| BTree trace buffer | [FDP/Toolkits/Fdp.Toolkits/Behavior/Diagnostics/BTreeTraceWorkingMemory1024.cs](../../FDP/Toolkits/Fdp.Toolkits/Behavior/Diagnostics/BTreeTraceWorkingMemory1024.cs) | Read-only consumer; no edits |
| HSM trace buffer | sibling `HsmTraceWorkingMemory1024.cs` | Read-only consumer; no edits |
| Blueprint cursor | [FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintLatentCursor.cs](../../FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintLatentCursor.cs) | No edits (verified shape; we do **not** add `NodeIdAtEntry` — Blueprint node BPs stay on the probe path) |
| ECB API | [FDP/Engine/Fdp.Core/Abstractions/IEntityCommandBuffer.cs](../../FDP/Engine/Fdp.Core/Abstractions/IEntityCommandBuffer.cs) | `SetComponentRaw` / `SetManagedComponentRaw` already present; just consume |
| ECS view | `FDP/Engine/Fdp.Core/Abstractions/ISimulationView.cs` & friends | Read-only consumer |
| Gizmo interface | [FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IStatefulGizmo.cs](../../FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/IStatefulGizmo.cs) | **Signature change**: add `ISimulationView view` to `UpdateAndDraw` (P3) |
| Gizmo managers | `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs`, `BehaviorGizmoManagerSystem.cs` | Pass active view from `IDataBreakpointManager` into each gizmo per frame (P3) |
| Inspector adapter / panels | `Hrot.Presentation` (entity inspector panel host) | Read view from manager when paused (P3) |
| Graph editors | BTree / HSM / Blueprint canvas projects in `Hrot.Presentation` / `Hrot.Blueprints.Editor` | Add context menu entries that synthesise predicate DTOs (P7) |
| New code (this slice) | recommended new folder `Hrot.Diagnostics.Breakpoints/` (or under `Hrot.Blueprints.Core.Debug` if you prefer to keep it nearby) | `IDataBreakpointManager`, `DataBreakpointManager`, `DataBreakpointSystem`, `DebugSnapshotProvider`, `PendingDebugMutation` |
| Manager UI window | `Hrot.Presentation` | New `WindowScope.PerspectiveBound` window (P8) |

---

## 4. How to build & test

This is a standard FDP/.NET workspace. From the repo root:

```powershell
# Build everything
dotnet build IOS-IG-SimHost-FDP-2.sln -c Debug

# Run tests for the breakpoint-relevant projects
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj

# Performance baselines (BenchmarkDotNet)
dotnet run -c Release --project FDP/Engine/Fdp.Core.Benchmarks -- --filter '*QueryDelta*'
```

Per-task test fixtures and expected assertions are listed in [TASK-DETAIL.md](./TASK-DETAIL.md). Each task explicitly says which test names to create.

For interactive verification, the HROT Editor runs in a single-node `OfflineNetworkFactory` topology where Brain + Muscle + IG share one `EntityRepository` — this is the **intended debugging environment** for universal breakpoints. See [DESIGN.md §11.2](./DESIGN.md#112-multi-node-consequences-single-node-is-supported-workflow) for why multi-node debugging is explicitly out of scope.

---

## 5. Critical constraints to keep in mind

1. **Zero-cost dormant state.** If you cannot enforce that the snapshot provider and breakpoint system both have a single-branch fast-path returning in < 50 ns when no breakpoints are armed, the change is wrong. Reference-counted gating in the manager is the only acceptable mechanism.
2. **Never resimulate a tick to step forward.** The forward-snapshot pattern (`_postTickSnapshot.SyncFrom` at hit time, restore on clean step) is the architectural advance that makes this safe. Any code path that calls `EventAccumulator.InjectEvents` to replay a frame is wrong.
3. **Live mutations are deferred to N+1 via ECB.** Never write boxed inspector edits to the rewound live repo. Stage them; drain them via `IEntityCommandBuffer.SetComponentRaw` / `SetManagedComponentRaw` after the `_postTickSnapshot` restore.
4. **Recorder is invariant.** The breakpoint subsystem must impose zero awareness on `AsyncRecorder` / `RecorderTickSystem`. The recorder runs first in PostSimulation (capturing the natural tick-N state); the manager rewinds afterward; the `.fdp` file remains linear.
5. **Subsystem-isolated.** Manager + system + snapshot provider are per-subsystem instances. No cross-subsystem singletons. Window scope is `PerspectiveBound`, mirroring `FdpEntityInspectorWindow`.

---

## 6. Behaviour expectations for new contributors

Before authoring code:

- Read **[DEV-GUIDE.md](../.guides/DEV-GUIDE.md)** end to end. It defines the conventions this workstream follows for batch instructions, reports, debt tracking, and code review.
- Read this whole `breakpoints-1/` folder — the design talk in particular contains many decisions that the DESIGN.md doc only summarises (e.g., why we accept multi-node drift, why we drop `MultiplexingProbeSink`, why we keep Slice 1's Blueprint probe path).

When picking a task:

- Tasks in [TASK-DETAIL.md](./TASK-DETAIL.md) are written so a developer can pick one up cold given the design doc. Each ID's "Success conditions" section is the contract — the task is not done until those test names exist and pass.
- Tech debt discovered mid-task gets entered into [DEBT-TRACKER.md](./DEBT-TRACKER.md) per the project's standard format. P1 debt is a corrective task in the next batch.

If something in the codebase contradicts the design, the codebase wins — file a debt entry and update the design (the design talk's claims have been verified against v228, but the codebase moves).
