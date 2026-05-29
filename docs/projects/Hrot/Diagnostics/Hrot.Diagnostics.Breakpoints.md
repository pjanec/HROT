# Hrot.Diagnostics.Breakpoints

**Project file:** `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/Hrot.Diagnostics.Breakpoints.csproj`
**Target framework:** net8.0
**Date:** 2026-05-30

---

## README Validation

**Status: Missing**

No `README.md` exists in the project folder. This document serves as the authoritative
architectural reference for the assembly.

---

## Executive Overview

`Hrot.Diagnostics.Breakpoints` is the **Universal Breakpoints** diagnostic substrate for
the HROT/FDP simulation engine. It transforms the engine's debugging surface from the
Slice 1 narrow execution-flow pauses (Blueprint node probes) into a **single data-driven
diagnostic substrate** that can halt the simulation on any combination of:

- Arbitrary ECS **component-data conditions** (e.g. `Health.Current < 10`).
- Transient **FdpEventBus payload** constraints (e.g. `HitEvent.Damage > 50`).
- **BTree and HSM lifecycle opcodes** (Enter / Exit / Abort / Transition / Guard) via
  trace-buffer ring scans.
- Dynamic-partition **Blueprint variable conditions** across tiered `BlueprintBlackboard*`
  components.
- **Structural archetype mutations** (component added/removed on an entity).
- **Spatial bounding-box** transitions (entity enters or exits a 2D axis-aligned box).
- **Entity-lifecycle** transitions (birth/death events matching an identifier substring).
- **External-hit tags** for Blueprint node activation (Slice 1 probe path, routed via
  `OnExternalHit`).

All breakpoint kinds are expressed as a single polymorphic `SearchPredicateDto` tree
(shared with the Replay Browser), JIT-compiled once by `IPredicateCompiler` /
`IEventScannerCompiler` into zero-allocation delegates, and evaluated against live ECS
chunk memory. The simulation halts via **soft-pause** semantics: the kernel finishes
any in-flight phase work, then keeps the OS thread spinning so the editor UI stays
responsive.

---

## Architecture

### System-Level Overview

```
+----------------------------------------------------------+
|  Editor / Graph UI  (per perspective)                    |
|  +- DataBreakpointManagerWindow  (predicate builder)     |
|  +- BTree / HSM / Blueprint context menus                |
|  +- Watch panel (persists watches.json)                  |
+-----------------------------+----------------------------+
                              | SearchPredicateDto (JSON-serializable)
                              v
+----------------------------------------------------------+
|  IDataBreakpointManager  (one per subsystem)             |
|  +- Breakpoint registry  (Breakpoint records + DTOs)     |
|  +- Reference-counted gate (mounts/unmounts snapshot)    |
|  +- JIT compile via IPredicateCompiler / EventScanner    |
|  +- Hot-reload rebind (OnHotReloadCompleted / Begin)     |
|  +- Triple-buffer orchestrator                           |
|  +- Deferred-mutation queue (P4)                         |
+-----------------------------+----------------------------+
                              | compiled Func<EntityRepository,Entity,bool>
                              v
+----------------------------------------------------------+
|  DataBreakpointSystem  (PostSimulation, after Recorder)  |
|  +- QueryDelta over dirty entity chunks                  |
|  +- FdpEventBus event-scanner loop                       |
|                                                          |
|  DebugSnapshotProvider  (BeforeSync)                     |
|  +- _preTickSnapshot.SyncFrom(live) when gate is on      |
+-----------------------------+----------------------------+
                              | RequestPause / RequestStepOneTick
                              v
+----------------------------------------------------------+
|  IEngineDebugTimeController                              |
|  (was IBlueprintTimeController; lives in Blueprints.Core)|
|  +- MasterSyncTimeControllerAdapter                      |
+----------------------------------------------------------+
```

### Triple-Buffer Snapshot Model

Three `EntityRepository` instances cooperate to enable non-destructive pause and step:

| Repository | Lifetime | Owner | Populated by |
|---|---|---|---|
| `_liveRepo` | Always | `ModuleHostKernel` | Engine simulation |
| `_preTickSnapshot` | Allocated at init | `DebugSnapshotProvider` | `SyncFrom(_liveRepo)` at BeforeSync each tick while the gate is on |
| `_postTickSnapshot` | Allocated at init | `DataBreakpointManager` | `SyncFrom(_liveRepo)` exactly when a predicate fires |

When a predicate fires:
1. `_postTickSnapshot.SyncFrom(_liveRepo)` — capture post-tick state.
2. `_liveRepo.SyncFrom(_preTickSnapshot)` — rewind live world to start-of-tick.
3. `_timeController.RequestPause()` — halt the clock on the next frame boundary.

The editor UI reads `_preTickSnapshot` (via `IActiveViewProvider.ActiveView`) while paused,
so the inspector shows the state that triggered the breakpoint without mutating live memory.

On **Step**:
```
_liveRepo.SyncFrom(_postTickSnapshot)   // restore tick-N end state
DrainPendingMutations(_liveRepo)        // apply any staged edits
_timeController.RequestStepOneTick()    // advance one tick
```

On **Continue**, the same drain + restore runs, then `_timeController.RequestResume()`.

### Gate Reference Counting

`_activeBreakpointCount` tracks enabled breakpoints:

- **0 to 1 transition**: `_snapshotProvider.SetEnabled(true)` — snapshot begins next tick.
- **1 to 0 transition**: `_snapshotProvider.SetEnabled(false)` — snapshot stops next tick.

When dormant (count == 0), `DebugSnapshotProvider.Execute` returns in a single branch
with zero allocation. The `DataBreakpointSystem.Execute` early-outs on
`!_manager.HasMountedDelegates`.

---

## Key Types

### `Breakpoint` (record)

```csharp
public sealed record Breakpoint
{
    public required BreakpointId Id { get; init; }
    public SearchPredicateDto?   Condition { get; init; }
    public Entity?               FilterEntity { get; init; }
    public int                   HitCount { get; init; }
    public int                   OccurrenceThreshold { get; init; } = 1;
    public bool                  Enabled { get; init; }
    public string                DisplayName { get; init; } = string.Empty;
    public Guid?                 SourceElementId { get; init; }
    public bool                  IsWatch { get; init; }
    public bool                  IsBroken { get; init; }
}
```

One record type covers every breakpoint kind — the polymorphic `Condition` field absorbs
all variation. `SourceElementId` is set by graph-editor context menus to the node's
`VisualId`; gutter renderers use it to draw the red dot without querying the Slice 1
debug session.

### `BreakpointId`

Opaque auto-incremented `int` wrapper. `BreakpointId.Invalid` (zero) is the sentinel for
unassigned identifiers.

### `IDataBreakpointManager`

Per-subsystem orchestrator. Full API surface:

| Method / Property | Description |
|---|---|
| `Add(Breakpoint)` | Register a fully-constructed breakpoint; returns its `BreakpointId`. |
| `AddBreakpoint(dto, ...)` | Convenience factory; constructs a `Breakpoint` and calls `Add`. |
| `Remove(id)` | Remove by id; gate count is decremented if the breakpoint was enabled. |
| `SetEnabled(id, bool)` | Toggle enabled state; adjusts gate count. |
| `UpdateCondition(id, dto)` | Replace the predicate; triggers recompile. |
| `MarkAsWatch(id, bool)` | Flag a breakpoint as a watch entry. |
| `SaveWatches(path)` / `LoadWatches(path)` | Persist/restore watch-flagged breakpoints as JSON. |
| `OnHotReloadCompleted()` | Drop cached delegates and recompile all DTOs. |
| `OnHotReloadBegin()` | If paused, force-continue; flush pending mutations. |
| `StageMutation(entity, type, value)` | Enqueue a deferred component mutation (P4). |
| `OnHit(bp, entity)` | Called by `DataBreakpointSystem`; performs triple-buffer rewind + pause. |
| `RequestStep()` | Restore post-tick snapshot, drain mutations, advance one tick. |
| `RequestContinue()` | Restore post-tick snapshot, drain mutations, resume. |
| `OnExternalHit(tag, entity)` | Resolves external-hit tag predicates (Blueprint probe path). |
| `AllBreakpoints` | Read-only snapshot of all registered breakpoints. |
| `IsPaused` | True while simulation is held at a breakpoint. |
| `PausedTick` | Tick index at which the current pause was triggered. |
| `HasMountedDelegates` | True when at least one compiled predicate is mounted. |
| `MountedComponentPredicates` | Enumerable of `(Breakpoint, CompiledComponentPredicate)`. |
| `MountedEventScanners` | Enumerable of `(Breakpoint, CompiledEventScanner)`. |
| `ActiveView` | Returns `_preTickSnapshot` while paused, `_liveRepo` otherwise. |
| `PendingMutationsCount` | Number of staged mutations awaiting drain. |
| `OnBreakpointHit` | Event raised when a breakpoint fires (after rewind). |
| `OnPauseStateChanged` | Event raised when `IsPaused` changes. |
| `OnBreakpointListChanged` | Event raised when the registry changes. |

### `DataBreakpointManager`

Concrete implementation of `IDataBreakpointManager`. Also implements:
- `IActiveViewProvider` — exposes `ActiveView` for inspector adapters.
- `IMutationInterceptor` — intercepts `StructEdit` commits while paused.

Internal structures:
- `_componentPredicates`: `Dictionary<BreakpointId, CompiledComponentPredicate>`.
- `_eventScanners`: `Dictionary<BreakpointId, CompiledEventScanner>`.
- `_structuralTrackers`: per-id tracker sets for structural predicates.
- `_spatialTrackers`: per-id bounding-box + position accessor for spatial predicates.
- `_lifecycleTrackers`: per-id known-alive sets for lifecycle predicates.
- `_externalHitPredicates`: tag-keyed dictionary for Blueprint external-hit route.
- `_pendingMutations`: `Queue<PendingDebugMutation>` drained on Step/Continue.

### `DebugSnapshotProvider`

`IEcsModuleSystem` scheduled in `SystemPhase.BeforeSync`. Holds a reference to
`_preTickSnapshot` (owned by the manager). Gate is a `volatile int` flipped via
`Interlocked.Exchange`. When `_isEnabled == 0`, `Execute` returns in one branch
with zero allocation and zero `SyncFrom` calls.

### `DataBreakpointSystem`

`IEcsModuleSystem` scheduled in `SystemPhase.PostSimulation`, after
`RecorderTickSystem` (via `[UpdateAfter(typeof(RecorderTickSystem))]`). This ordering
guarantees the flight recorder captures the natural tick-N state before the rewind is
applied. Iterates `MountedComponentPredicates` (using `QueryDelta` to skip clean chunks)
and `MountedEventScanners` (scanning the live `FdpEventBus`); signals `_manager.OnHit`
on each confirmed match.

### `PendingDebugMutation`

Immutable `readonly struct` queued when the operator edits a component via `StructEdit`
while the simulation is paused:

| Field | Description |
|---|---|
| `Target` | Entity whose component is to be mutated. |
| `ComponentTypeId` | Resolved via `ComponentTypeRegistry`. |
| `IsManaged` | True for managed-reference components (classes). |
| `Payload` | Boxed struct or managed class reference. |
| `SizeBytes` | `Marshal.SizeOf` of the component type (0 for managed). |

Drained into the ECB at the N+1 tick boundary (Step or Continue).

### Predicate Types Supported

| DTO | Discriminator | Description |
|---|---|---|
| `CompoundPredicateDto` | `Compound` | AND / OR tree. |
| `PropertyMatchDto` | `PropertyMatch` | ECS component field threshold. |
| `NumericPredicateDto` | `Numeric` | Scalar numeric range. |
| `StringPredicateDto` | `String` | Substring match. |
| `TransientEventPredicateDto` | `TransientEvent` | `FdpEventBus` payload scan. |
| `LifecyclePredicateDto` | `Lifecycle` | Entity birth / death. |
| `SpatialBoundingPredicateDto` | `SpatialBounding` | 2D bounding-box entry/exit. |
| `StructuralPredicateDto` | `Structural` | Archetype mutation + authority filter. |
| `BehaviorParamPredicateDto` | `BehaviorParam` | Typed projection over `BrainBlackboard`. |
| `TraceBufferScanPredicateDto` | `TraceBufferScan` | Ring-buffer scan over BTree/HSM trace components. |
| `BlueprintVariablePredicateDto` | `BlueprintVariable` | Dynamic-partition Blueprint variable. |
| `ExternalHitTagPredicateDto` | `ExternalHitTag` | Blueprint probe external-hit routing. |

`TraceBufferScanPredicateDto`, `BlueprintVariablePredicateDto`, and
`ExternalHitTagPredicateDto` are new DTO types introduced by this subsystem.

### `PredicateBuilderState`

Pure-logic state for the Predicate Builder panel within the manager window. Tracks a
`PredicateMode` enum and the corresponding root DTO. `SwitchMode` discards the current
DTO and creates a blank replacement. `Apply` calls `UpdateCondition` on the manager.
Unit-testable without an ImGui context.

### `PredicateMode` (enum)

```
Component | Event | Lifecycle | Spatial | Structural
Compound | BehaviorParam | BlueprintVariable | TraceBufferScan
```

### `BreakpointConditionSummarizer`

Static helper. Converts any `SearchPredicateDto` to a short human-readable string for the
"Condition Summary" column in the manager window grid (e.g. `"Component: Health [0, 10]"`,
`"Trace[0x01]"`, `"Blueprint: 3fa2e1b8..."`).

### `BreakpointJsonClipboard`

Static helper. Serializes / deserializes `SearchPredicateDto` to/from JSON using
`System.Text.Json` with the polymorphic `[JsonDerivedType]` attributes. Powers the
"Copy to Clipboard" / "Paste from Clipboard" toolbar buttons in the manager window.

### `TemporalStatusBannerPanel` / `TemporalStatusBannerState`

Small overlay panel rendered when `IsPaused == true`. Shows the paused tick index and
the count of pending mutations. ImGuiNET is not referenced by this project; callers
supply an `Action<string>` delegate for the actual text rendering, making the logic
unit-testable without an ImGui context.

### `WatchPersistence`

Static helper. Serializes watch-flagged breakpoints to a JSON file (`watches.json`).
Loaded back on editor startup; entries that fail recompilation are marked `IsBroken`.

### `IBreakpointNotifier`

Thin one-method interface (`Notify(string)`). Implemented by subsystems to forward
toast notifications to the editor's indicator surface
(`IEditorIndicators.Notify`).

---

## Phase Summary

| Phase | Status | Deliverables |
|---|---|---|
| P0 — Foundation rename | Done | `IEngineDebugTimeController` in `Blueprints.Core.Debug`; `IBlueprintTimeController` deprecated alias |
| P1 — Snapshot orchestration | Done | `DebugSnapshotProvider`, `IDataBreakpointManager` skeleton, triple-buffer pause primitives |
| P2 — Universal substrate | Done | `DataBreakpointSystem` (component + event paths), structural/spatial/lifecycle scanners |
| P3 — Virtual snapshot UI swap | Done | `IEntityStatefulGizmo` signature change, inspector adapter view repointing, temporal status banner |
| P4 — Deferred mutation | Done | `PendingDebugMutation`, `StageMutation` API, ECB drain on Step/Continue |
| P5 — Trace-buffer integration | Done | Compiler extension for trace-buffer scans; BTree / HSM breakpoints end-to-end |
| P6 — Blueprint variable integration | Done | `BlueprintVariablePredicateDto`, slot-table-aware IL emission |
| P7 — Graph-editor synthesis | Done | BTree context menu, HSM context menu, Blueprint context menu |

---

## Dependencies

```
Hrot.Diagnostics.Breakpoints
  -> Fdp.Core                       (Entity, EntityRepository, FdpEventBus, ComponentTypeRegistry)
  -> Fdp.ModuleHost                 (IEcsModuleSystem, SystemPhase, ISimulationView, IPredicateCompiler,
                                     IEventScannerCompiler, RecorderTickSystem)
  -> Fdp.Toolkits                   (SearchPredicateDto hierarchy, IPredicateCompiler, IEventScannerCompiler,
                                     BTreeTraceWorkingMemory1024, HsmTraceWorkingMemory1024)
  -> Hrot.Blueprints.Core           (IEngineDebugTimeController)
```

The test project additionally references:
- `Hrot.Presentation` (DataBreakpointManagerPanel, DataBreakpointManagerWindow)
- `Hrot.Blueprints.Editor` (BlueprintBreakpointMenuPopulator)
- `Hrot.BTree.Editor` (BTreeBreakpointMenuPopulator)
- `Hrot.Hsm.Editor` (HsmBreakpointMenuPopulator)

---

## Integration Points

### Time Control

`IEngineDebugTimeController` (defined in `Hrot.Blueprints.Core.Debug`) is the clock
surface. `MasterSyncTimeControllerAdapter` is the concrete implementation. The breakpoint
manager holds a reference and calls `RequestPause()` / `RequestResume()` /
`RequestStepOneTick()`. The Slice 1 `IBlueprintTimeController` is a deprecated alias that
inherits the new interface.

### Editor UI

`DataBreakpointManagerPanel` (in `Hrot.Presentation`) consumes `IDataBreakpointManager`
to render the predicate builder, breakpoint grid, toolbar, and temporal status banner.
It is hosted in `DataBreakpointManagerWindow` (a `ManagedWindow` with
`WindowScope.PerspectiveBound`, so each subsystem perspective has its own isolated
manager window).

### Graph Editors — Context Menus

Each graph editor contributes a static populator that synthesises the appropriate
`SearchPredicateDto` and calls `IDataBreakpointManager.AddBreakpoint`:

| Populator | Graph | Predicate synthesised |
|---|---|---|
| `BTreeBreakpointMenuPopulator` (Hrot.BTree.Editor) | BTree nodes | `TraceBufferScanPredicateDto` (NodeEvaluated) or `CompoundPredicateDto[Or]` for compound cases |
| `HsmBreakpointMenuPopulator` (Hrot.Hsm.Editor) | HSM states | `TraceBufferScanPredicateDto` (StateEnter / StateExit / GuardEvaluated) |
| `BlueprintBreakpointMenuPopulator` (Hrot.Blueprints.Editor) | Blueprint nodes | `ExternalHitTagPredicateDto` + `BlueprintVariablePredicateDto` compound |

### Graph Editors — Gutter Renderers

`BTreeBreakpointGutterRenderer` and `HsmBreakpointGutterRenderer` are `ICustomCanvasRenderer`
implementations that draw a red filled circle at the left gutter of each node/state that has
an active (enabled) breakpoint. They check `Breakpoint.SourceElementId` against
`node.VisualId` (or `state.StableId`) without querying the Slice 1 debug session.

### StructEdit Mutation Interception

`DataBreakpointManager` implements `IMutationInterceptor`. When `IsPaused == true`, the
`StructEdit` commit pipeline routes component writes to `StageMutation` instead of applying
them directly to the live repository. Staged mutations are flushed at N+1 tick boundary.

### Hot Reload Resilience

`OnHotReloadBegin` is called when a hot-reload cycle starts: if currently paused, the
manager forces a `RequestContinue`, flushes pending mutations, and notifies the user via
`IBreakpointNotifier`. `OnHotReloadCompleted` drops all cached compiled delegates and
recompiles from retained DTOs; entries that fail are marked `IsBroken`.

---

## Source Structure

All source files are under `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/`.

| File | Primary Type(s) | Description |
|---|---|---|
| `BreakpointTypes.cs` | `BreakpointId`, `Breakpoint` | Core domain records |
| `IDataBreakpointManager.cs` | `IDataBreakpointManager` | Full manager interface |
| `DataBreakpointManager.cs` | `DataBreakpointManager`, `CompiledComponentPredicate`, `CompiledEventScanner` | Concrete orchestrator |
| `DebugSnapshotProvider.cs` | `DebugSnapshotProvider` | BeforeSync snapshot system |
| `DataBreakpointSystem.cs` | `DataBreakpointSystem` | PostSimulation predicate evaluator |
| `PendingDebugMutation.cs` | `PendingDebugMutation` | Deferred mutation envelope |
| `PredicateBuilderState.cs` | `PredicateBuilderState`, `PredicateMode` | Predicate builder UI state |
| `BreakpointConditionSummarizer.cs` | `BreakpointConditionSummarizer` | DTO-to-string summary helper |
| `BreakpointJsonClipboard.cs` | `BreakpointJsonClipboard` | JSON clipboard serializer |
| `TemporalStatusBannerPanel.cs` | `TemporalStatusBannerPanel` | Pause overlay renderer |
| `TemporalStatusBannerState.cs` | `TemporalStatusBannerState` | Banner state model |
| `WatchPersistence.cs` | `WatchPersistence`, `WatchEntry` | Watch JSON persistence |
| `IBreakpointNotifier.cs` | `IBreakpointNotifier` | Toast notification interface |
| `CompoundPredicateHelper.cs` | `CompoundPredicateHelper` | DTO construction utilities |

### Test Project

`Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/` — xUnit test suite (22 files):

| File | Coverage area |
|---|---|
| `DataBreakpointManagerTests.cs` | Registry, gate reference counting, Add/Remove/SetEnabled |
| `DataBreakpointSystemStatefulTests.cs` | Component predicate evaluation, entity filtering, occurrence threshold |
| `TraceBufferScanTests.cs` | BTree/HSM trace-buffer scan predicates |
| `PendingMutationTests.cs` | StageMutation classification (managed/unmanaged), ECB drain at N+1 |
| `ExternalHitTagTests.cs` | Blueprint external-hit tag routing |
| `BlueprintVariableTests.cs` | BlueprintVariablePredicateDto slot-table evaluation |
| `BTreeContextMenuTests.cs` | BTreeBreakpointMenuPopulator predicate synthesis |
| `HsmContextMenuTests.cs` | HsmBreakpointMenuPopulator predicate synthesis |
| `BlueprintContextMenuTests.cs` | BlueprintBreakpointMenuPopulator predicate synthesis |
| `DataBreakpointGizmoViewTests.cs` | ActiveView switches between preTickSnapshot and liveRepo |
| `DataBreakpointInspectorViewTests.cs` | Inspector reads pre-tick / post-tick values |
| `TemporalStatusBannerTests.cs` | Banner hidden/visible, text content |
| `ManagerWindowTests.cs` | DataBreakpointManagerPanel toolbar actions |
| `PredicateBuilderStateTests.cs` | PredicateBuilderState mode switching, Apply |
| `PredicateBuilderP11T7Tests.cs` | Compound predicate builder edge cases |
| `JsonClipboardTests.cs` | Round-trip serialize/deserialize all DTO types |
| `WatchPersistenceTests.cs` | Save/load watch entries, IsBroken on schema mismatch |
| `HotReloadResilienceTests.cs` | Delegate recompile on hot-reload, force-continue while paused |
| `AllocationOptimizationTests.cs` | Zero-allocation fast paths (gate off, no delegates) |
| `ReentrancyTests.cs` | OnHit during OnHit guard |
| `IntegrationTests.cs` | End-to-end: property-match breakpoint fires, rewind, step, ECB drain |
| `P11CorrectnessTests.cs` | Phase 11 correctness regression suite |
