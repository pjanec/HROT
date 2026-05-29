# Hrot.ReplayBrowser

**Project path**: `Hrot/Subsystems/Hrot.ReplayBrowser/Hrot.ReplayBrowser.csproj`
**Namespace root**: `Hrot.ReplayBrowser`
**Assembly**: `Hrot.ReplayBrowser`
**Target framework**: `net8.0`
**Date**: 2026-05-30

---

## README Validation

**Status: Missing**

No `README.md` exists in the project folder. This document serves as the primary
architectural reference.

---

## Executive Overview

`Hrot.ReplayBrowser` is a standalone HROT subsystem that lets engineers and testers
browse, inspect, search, and export recorded simulation sessions captured by the FDP
flight-recorder infrastructure (`.fdp` binary files).

It is activated by passing `-m replaybrowser` on the command line.  The runner's
subsystem discovery mechanism instantiates it via the `INetworkFactory` constructor.
Unlike the live `SimHost` or `IG` subsystems the replay browser operates in a fully
**offline, sandboxed** mode: it never connects to a DDS network and never mutates live
simulation state.

### Key Capabilities

| Capability | Description |
|---|---|
| Recording playback | Frame-accurate seek, step forward/backward, continuous play at configurable rate |
| Entity inspection | Per-entity component viewer bound to the current replay frame |
| Frame diff | Side-by-side component diff between consecutive frames for a selected entity |
| Event browser | Structured view of all diagnostic events captured in the recording |
| Replay search | Flexible predicate engine: component, event, lifecycle, spatial, structural, compound |
| JSON export | Stream entire recording (or a filtered window) to JSON in absolute, incremental, or changelog format |
| Gizmo overlays | Full SimHost / AI / ScenarioEditor gizmo set rendered over a `MapCanvas` |
| Causality jump | Click an event source in the event browser and jump to the frame + entity that produced it |
| "Seek to change" | Find the nearest frame (forward or backward) where a selected entity's state differs |
| Back/forward history | Browser-style navigation history for both frame position and entity selection |
| Headless mode | All of the above context and search logic runs without a GPU window for unit tests |
| **Federated multi-node loading** | Open a set of per-node `.fdp` recordings from one distributed exercise; validated by matching `ExerciseId` from the `.meta.json` sidecar |
| **Merged View (Frankenstein)** | Synthesise a mathematically correct merged `EntityRepository` from authority-filtered ECS slices across all loaded nodes; correctness-first offline diagnostic |
| **Per-node time offsets** | Dial independent wall-clock tick offsets per node to compensate for capture skew; "causality may not hold" indicator when any offset is non-zero |
| **Paradox-safe entity handles** | Cross-node relational references that miss due to offset desync resolve to `Entity.Null` rather than crashing the deserializer |

---

## Architecture

### Layer Overview

The subsystem sits at the top of a four-layer stack.  The federation tier was
added in the Frankenstein feature set (phases P1–P5) and lives entirely within
`Fdp.Toolkits`.

```
+-----------------------------------------------------------------------+
|                    Hrot.ReplayBrowser (this assembly)                 |
|                    ReplayBrowserSubsystem                             |
|  gizmos  panels  diff  search  export  causality  nav-history         |
|  FederationPanel  mode-switch  transient-master rebuild               |
+---------------------------+-------------------------------------------+
|  Fdp.Presentation         |  Fdp.Toolkits                            |
|  (ImGui windows/panels,   |  (ReplayBrowserContext, PlaybackHistory,  |
|   MapCanvas, WindowMgr,   |   EntitySelectionHistory, DiffService,    |
|   FederationPanel)        |   RecordingExportService, SearchService)  |
+---------------------------+-------------------------------------------+
|                   Federation tier (Fdp.Toolkits)                      |
|  FederatedReplayManager  FederatedGuidResolver  NetworkIdGuid         |
|  TransientMasterBuilder  RepositoryPriming                            |
+-----------------------------------------------------------------------+
|                       Fdp.Core                                        |
|  (EntityRepository, FdpEventBus, PlaybackController, .fdp format,    |
|   RecordingMetadata with ExerciseId/NodeId)                           |
+-----------------------------------------------------------------------+
```

### Subsystem Lifecycle

The runner calls `ISubsystem` methods in the following strict order once per frame:

```
Initialize(config)
   |
   +-- allocate FederatedReplayManager (initially null until first LoadGroup call)
   +-- allocate history trackers
   +-- (non-headless) build MapCanvas, panels, gizmo stack
   +-- WireDelegates()
       +-- wire OnLoadGroup -> FederatedReplayManager.LoadGroup(paths)
       +-- wire IsMergedViewQuery -> () => _viewMode == ViewMode.Merged

Update(deltaTime)         <-- every frame
   |
   +-- drain _pendingChangeSeekFrame (async seek-to-change result)
   +-- advance playback accumulator (if IsPlaying AND SingleNode mode)
   +-- reactive diff: re-compute when frame or entity selection changes
   +-- update SearchPanel.CurrentFilePath (null in Merged View)
   +-- tick gizmo systems (selection, action dispatch, data-driven, stateless)
   +-- SwapBuffers on interaction bus

DrawWorld()               <-- inside Raylib.BeginDrawing(), before ImGui
   |
   +-- MapCanvas.Draw()  (2-D map + all registered layers including gizmo layer)

DrawUI()                  <-- inside rlImGui.Begin()
   |
   +-- gizmoLayer context menu & struct inspector
   +-- main menu bar gizmo contributions

Shutdown()
   |
   +-- FederatedReplayManager.Dispose()  (cascades to all owned ReplayBrowserContext instances)
   +-- transient master EntityRepository.Dispose()
```

### Playback State Machine

The timeline panel owns two boolean flags that drive the playback state transitions
inside `Update`. `IsPlaying` and `PlaybackRate` are set by the user via the transport
controls.

```
            +-------------+
 [Initial]  |   Stopped   |<-----------------------------------------+
            +------+------+                                           |
                   |  user presses Play                               |
                   v                                                   |
            +------+------+   accumulator >= frameTime               |
            |   Playing   |---> StepForward() each tick              |
            +------+------+   until StepForward returns false         |
                   |  reached end of recording                         |
                   +------------------------------------------------->+
                   |  user presses Stop / Pause                        |
                   +------------------------------------------------->+

Seek (any state):
   user drags slider / clicks event / presses step buttons
       --> ReplayBrowserContext.SeekToFrame(targetFrame)
       --> PlaybackHistoryTracker.PushWaypoint(before, after)
```

### Component Diff Engine

The reactive diff engine in `Update` re-runs whenever the current frame index or the
selected entity changes. It temporarily seeks back one frame, serializes the entity,
steps forward, serializes again, then calls `ComponentDiffService.ComputeEntityDiff`.

```
+------------------+      SeekToFrame(n-1)     +--------------------+
|  Update() loop   |-------------------------->|  ReplayBrowserCtx  |
|                  |      Serialize entity      |  SandboxRepo       |
|  lastDiff != now |<--------------------------|  PlaybackController |
+--------+---------+      StepForward()         +--------------------+
         |                Serialize entity
         |                ComputeEntityDiff()
         v
+--------+---------+
|  ComponentDiff-  |
|  Panel.Current-  |
|  Diffs = result  |
+------------------+
```

### Seek-to-Change (Async Background Scan)

When the user clicks "Find Next Change" / "Find Previous Change" in the diff panel,
`SeekToNextChangeAsync` is launched on the thread-pool. It opens an **isolated**
`ReplayBrowserContext` (does not touch the GUI context) and scans frames sequentially
until it finds the first frame where `IsActualIncludedDiff` returns true.

```
Main thread                             Background Task
    |                                       |
    | _seekToChangeTask =                   |
    | SeekToNextChangeAsync(entity, dir)    |
    |-------------------------------------->|
    |                                       | new ReplayBrowserContext (isolated)
    |                                       | SeekToFrame(startFrame)
    |                                       | loop StepForward()
    |                                       |   SerializeEntity -> baseline
    |                                       |   SerializeEntity -> current
    |                                       |   ComputeTreeDiff()
    |                                       |   if has non-excluded modified node
    |                                       |     return foundFrame
    |                                       |
    |<------ _pendingChangeSeekFrame = -----|
    |        foundFrame (volatile int)      |
    |                                       |
    | next Update() drains pending frame    |
    | SeekToFrame(pendingFrame)             |
```

The result is handed back to the main thread via a `volatile int` field
(`_pendingChangeSeekFrame`) rather than a `Task` continuation to avoid UI thread
marshaling complexity inside a Raylib game loop.

### Federated Replay and Merged View (Frankenstein)

The "Frankenstein" feature set lets an operator open the per-node `.fdp` recordings
from one distributed exercise together, align them on a shared wall-clock tick, dial
independent per-node offsets, and inspect either a single node or a **mathematically
correct merged ECS snapshot** synthesised from all loaded contexts.

This is an **offline post-mortem diagnostic only**.  Scrub stutter in Merged View is
intentional and accepted by design (see §6.2.1 of the design document).

#### View modes

| Mode | Description |
|---|---|
| **Single-Node** | `RepositoryAdapter` bound to one selected node's `SandboxRepo`. All real-time controls (Play, Search, Seek-to-Change) are fully available. |
| **Merged** | `RepositoryAdapter` bound to a synthesised transient `EntityRepository`. Play button, Search panel, and Seek-to-Change arrows are disabled. Diff panel works via two transient-master rebuilds per step. |

The `FederationPanel` ImGui panel provides the mode radio toggle, the base wall-tick
numeric input, per-node offset rows with a causality-warning glyph, and the
Local-Entities Provider dropdown (visible in Merged View only).

#### Multi-file load and validation

`ReplayTimelinePanel.LoadFdpAsync` triggers a multi-file open dialog.  On confirm,
`FederatedReplayManager.LoadGroup(string[] paths)` is called which:

1. Reads each file's `.meta.json` sidecar via `MetadataSerializer.Deserialize`.
2. Rejects the batch if any `ExerciseId == Guid.Empty` ("unknown exercise").
3. Rejects the batch if not all `ExerciseId` values are identical ("exercise mismatch").
4. Rejects the batch if any two files share the same `NodeId` ("duplicate NodeId N").
5. On success, instantiates one `ReplayBrowserContext` per file keyed by `NodeId`.

Rejections propagate as a `LoadGroupException` and surface in the UI as a modal with
the specific reason.  Any already-created contexts are disposed before the exception
propagates.

#### Frankenstein synthesis pipeline

`TransientMasterBuilder.Build(manager)` produces a fresh `EntityRepository` per
operator action:

```
FederatedReplayManager
  |
  +-- [Step 2] Correlate entities across all nodes by NetworkIdentity.Value
  |   -> Dictionary<long, List<(nodeId, entity)>>
  |
  +-- [Step 3] Pre-allocate global entities in transient repo
  |   -> NetworkIdGuid.From(netVal).ToString("N") as DOM key
  |   -> transientRepo.CreateEntity() per unique NetworkIdentity
  |
  +-- [Step 3b] Pre-allocate local entities from LocalEntitiesProvider node
  |   -> synthetic MD5-based Guid key per (nodeId, index, generation)
  |
  +-- [Step 5] Consensus extraction per global entity
  |   Primary-owner node first, then ascending NodeId
  |   presenceMask AND authorityMask AND ~alreadyClaimed = extract mask
  |   ScenarioSerializer.SerializeEntity(repo, entity, resolver, extract)
  |
  +-- [Step 5b] Full-mask extraction of local-only entities from provider
  |
  +-- [Step 6] ScenarioSerializer.DeserializeWith(transientRepo, dom, resolver, preAllocated)
       Skips subsystem-type filter; passes FederatedGuidResolver everywhere
       Entity.Null on any unresolvable cross-node reference (paradox handling)
```

**Cost:** O(entities x components) with JSON in the loop.  Real cluster snapshots
may take hundreds of milliseconds per rebuild.  Continuous playback is forbidden in
Merged View because of this cost.

#### Local-Entities Provider

Entities without `NetworkIdentity` (local-only: visual effects, camera anchors,
debug markers) cannot be correlated cross-node. `FederatedReplayManager` designates
one node as the **Local-Entities Provider** (defaults to the lowest-numbered loaded
NodeId, typically the Brain/CGF). Only its local-only entities appear in the merged
view. The operator can change the provider via the `FederationPanel` dropdown, which
triggers a full rebuild.

#### Paradox handling

When per-node offsets cause a fragment to reference an entity absent from all
loaded contexts at the offset time, `FederatedGuidResolver.Resolve(string)` returns
`Entity.Null` without throwing. The `EntityInspectorPanel` renders such fields in
a warning colour with a tooltip describing the cause.

#### Mode-switch policies

| Feature | Single-Node | Merged View |
|---|---|---|
| **Play button** | Enabled | Disabled (tooltip shown) |
| **Replay Search** | Full `CurrentFilePath` bound | `CurrentFilePath = null`; status text shown |
| **Seek to Prev/Next Change** | Enabled | Disabled (tooltip shown) |
| **Component Diff (passive)** | Two-seek via context | Two transient-master rebuilds per diff |

### Gizmo System Integration

The subsystem initializes the full HROT gizmo stack (same as ScenarioEditor), including:

- Data-driven gizmos registered per component type (SimHost, AI, Common, Presentation, ScenarioEditor)
- Stateless global gizmos (rubber-band selection, spatial search bounds overlay)
- Layer control gizmo for toggling visibility categories
- Context-menu gizmo actions wired to the interaction bus

The `DebugGizmoLayer` is added to `MapCanvas` at z-order slot 31.

### Navigation History

Two independent history stacks provide browser-style back/forward navigation:

```
EntitySelectionHistory          PlaybackHistoryTracker
(entity back/fwd)               (frame + entity back/fwd)
     |                               |
     | PushSelection(e)              | PushWaypoint(frame, entity)
     | GoBack() / GoForward()        | GoBack() / GoForward()
     v                               v
  InspectorState.SelectedEntity   context.SeekToFrame(frame)
                                   + entity restore
```

Both trackers suppress duplicate entries and truncate forward history on a new push,
matching standard browser back/forward semantics.

---

## Source Structure

### Project folder

```
Hrot/Subsystems/Hrot.ReplayBrowser/
+-- Hrot.ReplayBrowser.csproj
+-- ReplayBrowserSubsystem.cs

FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Federation/
+-- FederatedReplayManager.cs
+-- FederatedGuidResolver.cs
+-- NetworkIdGuid.cs
+-- RepositoryPriming.cs
+-- TransientMasterBuilder.cs
```

### Namespaces

| Namespace | Assembly | Notes |
|---|---|---|
| `Hrot.ReplayBrowser` | `Hrot.ReplayBrowser` | This project (single class) |
| `Fdp.Toolkit.ReplayBrowser` | `Fdp.Toolkits` | Headless services, context, histories |
| `Fdp.Toolkit.ReplayBrowser.Diff` | `Fdp.Toolkits` | Diff tree model and service |
| `Fdp.Toolkit.ReplayBrowser.Search` | `Fdp.Toolkits` | Predicate DTOs, search service |
| `Fdp.Toolkit.ReplayBrowser.Federation` | `Fdp.Toolkits` | Federated multi-node replay: manager, resolver, synthesis builder, priming helper |
| `Fdp.Presentation.Panels.ReplayBrowser` | `Fdp.Presentation` | ImGui panel classes (including `FederationPanel`) |
| `Fdp.Presentation.Windows.ReplayBrowser` | `Fdp.Presentation` | WindowManager-aware wrappers |
| `Fdp.Core.FlightRecorder` | `Fdp.Core` | Binary .fdp format, PlaybackController |

### Files and Classes

#### `Hrot.ReplayBrowser` (this assembly)

| File | Class | Description |
|---|---|---|
| `ReplayBrowserSubsystem.cs` | `ReplayBrowserSubsystem` | Top-level subsystem; implements `ISubsystem` + `IWindowRegistrar` |
| `ReplayBrowserSubsystem.cs` | `NullRecordingExportService` (private nested) | Stub used when export service not yet available |
| `ReplayBrowserSubsystem.cs` | `NullFileDialogService` (private nested) | Stub used when file dialog not yet available |
| `ReplayBrowserSubsystem.cs` | `ReplaySpatialPickerContext` (private nested) | Bridges `GlobalGizmoManager` to `ISpatialPickerContext` for the search panel |
| `ReplaySpatialBoundsGizmo` (private nested) | | Stateless gizmo that draws the active spatial search bounds as a dashed green box |

#### `Fdp.Toolkits` - ReplayBrowser Federation layer

| File | Class | Description |
|---|---|---|
| `Federation/FederatedReplayManager.cs` | `FederatedReplayManager` | Coordinates multi-node recording load, time-synchronised seeking, and per-node offsets; owns all `ReplayBrowserContext` lifetimes |
| `Federation/FederatedReplayManager.cs` | `LoadGroupException` | Thrown by `FederatedReplayManager.LoadGroup` when group validation fails |
| `Federation/FederatedGuidResolver.cs` | `FederatedGuidResolver` | `IGuidResolver` with hot-swappable save/load maps; returns `Entity.Null` on miss instead of throwing |
| `Federation/NetworkIdGuid.cs` | `NetworkIdGuid` | Packs a `long` `NetworkIdentity.Value` into the first 8 bytes of a `Guid` for use as a stable DOM entity key; round-trips via `ToLong` |
| `Federation/TransientMasterBuilder.cs` | `TransientMasterBuilder` | Builds a fresh transient `EntityRepository` from the current playback state of all nodes in a `FederatedReplayManager`; implements the consensus-mask synthesis algorithm |
| `Federation/RepositoryPriming.cs` | `RepositoryPriming` | Shared static helper: reflects all loaded assemblies and registers `[ComponentId]`-annotated types and `[EventId]`-annotated events on a repository and optional bus |

#### `Fdp.Toolkits` - ReplayBrowser layer

| File | Class | Description |
|---|---|---|
| `ReplayBrowserContext.cs` | `ReplayBrowserContext` | Headless sandbox: owns `EntityRepository`, `FdpEventBus`, `IDiagnosticEventHistoryService`, `PlaybackController` |
| `PlaybackHistoryTracker.cs` | `PlaybackHistoryTracker` | Back/forward stack of `NavigationWaypoint` (frame + entity) |
| `PlaybackHistoryTracker.cs` | `NavigationWaypoint` | Readonly record struct with `FrameIndex` and `SelectedEntity` |
| `EntitySelectionHistory.cs` | `EntitySelectionHistory` | Back/forward stack of selected `Entity` handles |
| `RecordingExportService.cs` | `RecordingExportService` | Streams .fdp to JSON; supports absolute, incremental, and changelog modes |
| `IRecordingExportService.cs` | `IRecordingExportService` | Interface for recording export |
| `JsonExportOptions.cs` | `JsonExportOptions` | Export configuration (window, format, entity filter, epsilon) |
| `Diff/ComponentDiffService.cs` | `ComponentDiffService` | Recursive JSON tree diff with epsilon tolerance for numerics |
| `Diff/DiffNode.cs` | `DiffNode`, `DiffObject`, `DiffValue` | Diff tree model |
| `Diff/IComponentDiffService.cs` | `IComponentDiffService` | Interface for diff computation |
| `Search/RecordingSearchService.cs` | `RecordingSearchService` | Headless frame-step scan engine; each call isolated |
| `Search/IRecordingSearchService.cs` | `IRecordingSearchService` | Interface for search |
| `Search/SearchPredicateDto.cs` | `SearchPredicateDto` (abstract) | Polymorphic JSON-serializable predicate base |
| `Search/SearchPredicateDto.cs` | `CompoundPredicateDto` | Logical AND/OR composition |
| `Search/SearchPredicateDto.cs` | `PropertyMatchDto` | Component field match |
| `Search/SearchPredicateDto.cs` | `NumericPredicateDto` | Numeric range sub-predicate |
| `Search/SearchPredicateDto.cs` | `StringPredicateDto` | String contains/startsWith/exact sub-predicate |
| `Search/SearchPredicateDto.cs` | `TransientEventPredicateDto` | Event type + field match |
| `Search/SearchPredicateDto.cs` | `LifecyclePredicateDto` | Entity birth/death range scan |
| `Search/SearchPredicateDto.cs` | `SpatialBoundingPredicateDto` | 2-D AABB spatial filter |
| `Search/SearchPredicateDto.cs` | `StructuralPredicateDto` | Component presence/absence |
| `Search/SearchPredicateDto.cs` | `BehaviorParamPredicateDto` | AI behavior parameter match |
| `Search/PredicateCompiler.cs` | `PredicateCompiler` | Compiles `SearchPredicateDto` into executable `Func<EntityRepository, Entity, bool>` |
| `Search/EventScannerCompiler.cs` | `EventScannerCompiler` | Compiles `TransientEventPredicateDto` into event-stream scanners |
| `Search/TargetEntityFilter.cs` | `TargetEntityFilter` | Optional entity filter applied before the main predicate |

#### `Fdp.Presentation` - ReplayBrowser panels and windows

| File | Class | Description |
|---|---|---|
| `Panels/ReplayBrowser/ReplayTimelinePanel.cs` | `ReplayTimelinePanel` | Transport controls, frame slider, metadata, multi-file loader (via `OnLoadGroup` delegate), export expander |
| `Panels/ReplayBrowser/FederationPanel.cs` | `FederationPanel` | ImGui panel for federated replay controls: mode toggle (Single-Node / Merged), base wall-tick input, per-node offset rows, causality banner, Local-Entities Provider dropdown |
| `Panels/ReplayBrowser/ReplaySearchPanel.cs` | `ReplaySearchPanel` | Multi-mode search UI; disabled (`CurrentFilePath = null`) while Merged View is active |
| `Panels/ReplayBrowser/ComponentDiffPanel.cs` | `ComponentDiffPanel` | Renders `DiffNode` tree with old/new value columns; Seek-to-Change buttons disabled in Merged View |
| `Panels/EventBrowserPanel.cs` | `EventBrowserPanel` | Structured event log with causality-jump links |
| `Panels/EntityInspectorPanel.cs` | `EntityInspectorPanel` | Component inspector; renders `Entity`-typed fields in warning colour when value is `Entity.Null` and `InspectorState.IsMergedView` is true |
| `Windows/ReplayBrowser/ReplayTimelineWindow.cs` | `ReplayTimelineWindow` | `PerspectiveBound` window wrapping the timeline panel |
| `Windows/ReplayBrowser/FdpEntityInspectorWindow.cs` | `FdpEntityInspectorWindow` | `PerspectiveBound` window wrapping the entity inspector panel |
| `Windows/ReplayBrowser/ComponentDiffWindow.cs` | `ComponentDiffWindow` | `PerspectiveBound` window wrapping the diff panel |
| `Windows/ReplayBrowser/FdpEventBrowserWindow.cs` | `FdpEventBrowserWindow` | `PerspectiveBound` window wrapping the event browser panel |
| `Windows/ReplayBrowser/ReplaySearchWindow.cs` | `ReplaySearchWindow` | `PerspectiveBound` window wrapping the search panel |

#### `Fdp.Core` - FlightRecorder

| File | Class | Description |
|---|---|---|
| `FlightRecorder/PlaybackController.cs` | `PlaybackController` | Manages `.fdp` file stream; builds frame index on open; exposes `SeekToFrame`, `StepForward`, and wall-clock-tick seek (`SeekToWallClockTicks`) |
| `FlightRecorder/RecordingGlobalHeader.cs` | `RecordingGlobalHeader` | Binary header: magic[6] + FormatVersion(u32) + Timestamp(i64) = 18 bytes |
| `FlightRecorder/RecorderSystem.cs` | `RecorderSystem` | Frame recording (write path) |
| `FlightRecorder/PlaybackSystem.cs` | `PlaybackSystem` | Frame deserialization (read path) |
| `FlightRecorder/AsyncRecorder.cs` | `AsyncRecorder` | Non-blocking recording writer; writes `RecordingMetadata` (including `ExerciseId` and `NodeId`) to the `.meta.json` sidecar on `Dispose` |
| `FlightRecorder/SchemaValidator.cs` | `SchemaValidator` | Validates recorded schema against current assemblies on open |
| `FlightRecorder/Metadata/RecordingMetadata.cs` | `RecordingMetadata` | Recording sidecar model; includes `ExerciseId` (shared across all nodes in one exercise) and `NodeId` (which distributed node produced this file) |

---

## Public API Reference

### `ReplayBrowserSubsystem`

**Namespace**: `Hrot.ReplayBrowser`
**Implements**: `ISubsystem`, `IWindowRegistrar`

```csharp
public sealed class ReplayBrowserSubsystem : ISubsystem, IWindowRegistrar
```

#### Constructors

| Constructor | Description |
|---|---|
| `ReplayBrowserSubsystem(INetworkFactory networkFactory)` | Primary constructor used by subsystem discovery. The `networkFactory` parameter is accepted but unused; the subsystem is offline. |
| `ReplayBrowserSubsystem()` | Parameterless constructor for unit tests. |

#### ISubsystem members

| Member | Type | Description |
|---|---|---|
| `Name` | `string` (readonly) | Returns `"ReplayBrowser"` |
| `TitleBarColor` | `Vector4` (readonly) | `(0.2, 0.6, 0.8, 1.0)` - steel blue |
| `Initialize(SubsystemConfig config)` | `void` | Allocates manager slot, history trackers, and (non-headless) the full UI stack including `FederationPanel` |
| `Update(float deltaTime)` | `void` | Advances playback (Single-Node only), runs reactive diff, updates search panel path, ticks gizmo systems |
| `DrawWorld()` | `void` | Calls `MapCanvas.Draw()` (no-op when headless) |
| `DrawUI()` | `void` | Renders gizmo context menus and main-menu contributions (no-op when headless) |
| `Shutdown()` | `void` | Disposes `FederatedReplayManager` (cascades to all contexts) and any allocated transient master |

#### IWindowRegistrar members

| Member | Description |
|---|---|
| `RegisterWindows(WindowManager windowManager)` | Registers all 5 replay-browser windows into the `WindowManager` under the `"ReplayBrowser"` perspective |

#### Internal test seams

These members are `internal` and visible to `Hrot.ReplayBrowser.Tests`.

| Member | Description |
|---|---|
| `Manager` | Returns the active `FederatedReplayManager` (or `null` before first load) |
| `ActiveRepo` | Returns the current active `EntityRepository` (single-node sandbox repo or transient master) |
| `ViewMode` | Returns the current `ViewMode` (`SingleNode` or `Merged`) |
| `TransientBuildOverride` | Test seam: when set, replaces the `TransientMasterBuilder.Build` call; allows tests to inject a controlled repo or count builds |
| `RegisterWindowsCore(WindowManager, ReplayTimelinePanel, EntityInspectorPanel, ComponentDiffPanel, EventBrowserPanel, ReplaySearchPanel)` | Registers 5 windows using caller-supplied panel instances; skips headless guard |
| `WireDelegatesForTest(EntitySelectionHistory, PlaybackHistoryTracker, InspectorState, ComponentDiffPanel, EventBrowserPanel)` | Wires all delegate chains using injected dependencies; returns `(seekIntent, selectIntent, matchIntent)` |
| `LoadFdpGroupForTest(string[] paths, TransientMasterBuilder builder)` | Loads a federated group bypassing the headless guard; used for integration tests |
| `LoadFdpViaManager(string path)` | Loads a single `.fdp` through a fresh `FederatedReplayManager`; replaces `_manager`, wires `OnTimeChanged` |
| `SetViewMode(ViewMode mode)` | Switches view mode, applies all panel gates, and triggers an immediate rebind |
| `ExecuteCausalityJump(int eventFrame, Entity target)` | Seeks to `eventFrame + 1` and selects `target`; pushes history on both stacks |
| `ExecuteCausalityJump(Entity target)` | Compatibility overload; uses the primary node's `CurrentFrame` as origin |

---

### `ReplayBrowserContext`

**Namespace**: `Fdp.Toolkit.ReplayBrowser`

```csharp
public sealed class ReplayBrowserContext : IDisposable
```

#### Properties

| Property | Type | Description |
|---|---|---|
| `SandboxRepo` | `EntityRepository` | Isolated ECS repository for replay state |
| `SandboxBus` | `FdpEventBus` | Isolated event bus replayed alongside ECS data |
| `HistoryService` | `IDiagnosticEventHistoryService` | Accumulated event history for the event browser panel |
| `Playback` | `PlaybackController?` | Active playback controller; `null` before `LoadRecording` |
| `CurrentFdpPath` | `string?` | Path of the currently loaded `.fdp` file |
| `CurrentFrame` | `int` | Current frame index; `-1` when no recording is loaded |

#### Methods

| Method | Description |
|---|---|
| `LoadRecording(string fdpPath)` | Opens an `.fdp` file; builds frame index; seeks to frame 0 |
| `SeekToFrame(int frameIndex, bool suppressHistory = false)` | Randomly seeks to a frame; optionally skips history update |
| `StepForward(bool suppressHistory = false)` | Advances one frame; returns `false` at end |
| `StepBackward(bool suppressHistory = false)` | Rewinds one frame; returns `false` at start |
| `Dispose()` | Disposes `PlaybackController` and `EntityRepository`; double-dispose safe |

---

### `FederatedReplayManager`

**Namespace**: `Fdp.Toolkit.ReplayBrowser.Federation`

```csharp
public sealed class FederatedReplayManager : IDisposable
```

Coordinates loading and time-synchronised seeking of a multi-node federated replay group.
Created via the static `LoadGroup` factory after validating all per-node `.meta.json` sidecars.
Owns the lifetime of all `ReplayBrowserContext` instances it creates.

#### Properties

| Property | Type | Description |
|---|---|---|
| `Contexts` | `IReadOnlyDictionary<int, ReplayBrowserContext>` | Per-node contexts keyed by `NodeId` |
| `ExerciseId` | `Guid` | Distributed exercise identifier shared by all loaded nodes |
| `BaseWallTicks` | `long` | Base wall-clock tick origin applied to every node seek |
| `NodeOffsets` | `IReadOnlyDictionary<int, long>` | Per-node wall-clock tick offsets applied on top of `BaseWallTicks` |
| `LocalEntitiesProviderNodeId` | `int` | NodeId considered the canonical source of local (non-networked) entities; defaults to the lowest loaded NodeId |
| `OnTimeChanged` | `event Action?` | Fired after every seek or local-entities-provider change |

#### Methods

| Method | Description |
|---|---|
| `static LoadGroup(string[] paths)` | Factory: validates sidecars, instantiates contexts, returns a manager; throws `LoadGroupException` on validation failure |
| `SetBaseWallTicks(long ticks)` | Sets base tick origin and calls `SeekAll`; fires `OnTimeChanged` |
| `SetNodeOffset(int nodeId, long offsetTicks)` | Sets the per-node offset and calls `SeekAll`; fires `OnTimeChanged` |
| `SetLocalEntitiesProvider(int nodeId)` | Changes provider node; fires `OnTimeChanged` without seeking |
| `SeekAll()` | Seeks every context to `BaseWallTicks + NodeOffset[nodeId]`; fires `OnTimeChanged` |
| `StepForwardAll()` | Advances every context one frame; updates `BaseWallTicks` from the provider node; fires `OnTimeChanged` |
| `StepBackwardAll()` | Rewinds every context one frame; updates `BaseWallTicks`; fires `OnTimeChanged` |
| `Dispose()` | Disposes all owned `ReplayBrowserContext` instances; double-dispose safe |

---

### `LoadGroupException`

**Namespace**: `Fdp.Toolkit.ReplayBrowser.Federation`

```csharp
public sealed class LoadGroupException : Exception
```

Thrown by `FederatedReplayManager.LoadGroup` when group validation fails.
Possible `Message` values:
- `"unknown exercise"` — a sidecar has `ExerciseId == Guid.Empty`
- `"exercise mismatch"` — sidecars carry different `ExerciseId` values
- `"duplicate NodeId {N}"` — two sidecars carry the same `NodeId`

---

### `FederatedGuidResolver`

**Namespace**: `Fdp.Toolkit.ReplayBrowser.Federation`
**Implements**: `IGuidResolver` (`Fdp.Toolkit.Scenario`)

```csharp
public sealed class FederatedGuidResolver : IGuidResolver
```

Custom `IGuidResolver` with hot-swappable save and load maps.
Unlike the engine's internal `LoadResolver`, `Resolve(string)` returns
`Entity.Null` on a cache miss rather than throwing.

| Method | Description |
|---|---|
| `SetSaveMap(Dictionary<Entity, string> map)` | Replaces the save-phase map |
| `SetLoadMap(Dictionary<string, Entity> map)` | Replaces the load-phase map |
| `string Resolve(Entity entity)` | Returns the pre-computed GUID string, or `"null"` if unmapped |
| `Entity Resolve(string guidStr)` | Returns the mapped entity, or `Entity.Null` if unmapped (never throws) |

---

### `NetworkIdGuid`

**Namespace**: `Fdp.Toolkit.ReplayBrowser.Federation`

```csharp
public static class NetworkIdGuid
```

Encodes a `long` `NetworkIdentity.Value` as a deterministic `Guid` suitable
for use as a JSON entity key in the merged-view DOM.
Packs the 8 bytes of the `long` into the first 8 bytes of the `Guid`; remaining
bytes are zero.  The resulting string is always parseable by `Guid.TryParse`.

| Method | Description |
|---|---|
| `static Guid From(long value)` | Packs `value` into a `Guid` |
| `static long ToLong(Guid g)` | Extracts the `long` packed by `From`; round-trips: `ToLong(From(v)) == v` |

---

### `TransientMasterBuilder`

**Namespace**: `Fdp.Toolkit.ReplayBrowser.Federation`

```csharp
public sealed class TransientMasterBuilder
```

Builds a transient merged `EntityRepository` from the current playback state of all
nodes in a `FederatedReplayManager`. The builder is stateless beyond the injected
serializer and may be called multiple times.

```csharp
public TransientMasterBuilder(ScenarioSerializer serializer)
```

| Method | Description |
|---|---|
| `EntityRepository Build(FederatedReplayManager manager)` | Synthesises and returns a fresh `EntityRepository`; caller is responsible for disposing it |

The `Build` algorithm:
1. Allocates a fresh `EntityRepository` and primes it via `RepositoryPriming`.
2. Correlates entities across all nodes by `NetworkIdentity.Value`.
3. Pre-allocates one entity per unique global ID using `NetworkIdGuid.From` as key.
4. Pre-allocates local-only entities from the `LocalEntitiesProviderNodeId` node using a synthetic MD5-derived Guid key (`MakeSyntheticKey`).
5. Runs consensus-mask extraction per global entity: `presenceMask AND authorityMask AND NOT alreadyClaimed`.
6. Extracts local-only entities from the provider using the full presence mask (no authority filter).
7. Calls `ScenarioSerializer.DeserializeWith(transientRepo, dom, resolver, preAllocated)`.

---

### `RepositoryPriming`

**Namespace**: `Fdp.Toolkit.ReplayBrowser.Federation`

```csharp
public static class RepositoryPriming
```

Shared static helper that reflects all loaded (non-system) assemblies and registers
every `[ComponentId]`-annotated type and (optionally) every `[EventId]`-annotated
struct on the target repository and event bus.

| Method | Description |
|---|---|
| `static void RegisterDiscoveredComponents(EntityRepository repo, FdpEventBus? bus = null)` | Discovers and registers all component and event types via reflection |

---

### `FederationPanel`

**Namespace**: `Fdp.Presentation.Panels.ReplayBrowser`

```csharp
public sealed class FederationPanel
```

ImGui panel for per-node replay federation controls (DESIGN §8.2).
Constructed over a `FederatedReplayManager` instance and re-created whenever a new
group is loaded.

| Member | Description |
|---|---|
| `ViewMode ActiveMode` | Current view mode (`SingleNode` or `Merged`) |
| `event Action<ViewMode>? OnViewModeChanged` | Fires when the operator changes the mode toggle |
| `bool HasNonZeroOffset` | True when any node in the manager has a non-zero offset |
| `void SetMode(ViewMode mode)` | Programmatically switches mode and fires `OnViewModeChanged` |
| `void SetNodeOffset(int nodeId, long offsetTicks)` | Forwards to `FederatedReplayManager.SetNodeOffset` |
| `void SetBaseWallTicks(long ticks)` | Forwards to `FederatedReplayManager.SetBaseWallTicks` |
| `void SetLocalEntitiesProvider(int nodeId)` | Forwards to `FederatedReplayManager.SetLocalEntitiesProvider` |
| `void DrawContent()` | Renders the panel contents via ImGui |

---

### `ViewMode`

**Namespace**: `Fdp.Presentation.Panels.ReplayBrowser`

```csharp
public enum ViewMode { SingleNode, Merged }
```

Controls whether `ReplayBrowserSubsystem._activeRepo` is bound to a single node's
`SandboxRepo` (`SingleNode`) or a synthesised transient master (`Merged`).

---

### `PlaybackHistoryTracker`

**Namespace**: `Fdp.Toolkit.ReplayBrowser`

```csharp
public sealed class PlaybackHistoryTracker
```

| Member | Description |
|---|---|
| `event Action<NavigationWaypoint>? OnWaypointRequested` | Fires when `GoBack`/`GoForward` activates a waypoint |
| `bool CanGoBack` | True when the cursor is past the first history entry |
| `bool CanGoForward` | True when there is a later entry in the history stack |
| `void PushWaypoint(int frameIndex, Entity selectedEntity)` | Pushes a waypoint; suppressed during navigation; truncates forward history on diverge |
| `void PushWaypoint(NavigationWaypoint waypoint)` | Overload taking the record struct |
| `void GoBack()` | Activates the previous waypoint and fires `OnWaypointRequested` |
| `void GoForward()` | Activates the next waypoint and fires `OnWaypointRequested` |
| `void Clear()` | Resets the entire history stack |

---

### `EntitySelectionHistory`

**Namespace**: `Fdp.Toolkit.ReplayBrowser`

```csharp
public sealed class EntitySelectionHistory
```

| Member | Description |
|---|---|
| `event Action<Entity>? OnSelectionChanged` | Fires when the active selection changes |
| `bool CanGoBack` | True when the cursor is past the first selection |
| `bool CanGoForward` | True when there is a later selection in the history |
| `void PushSelection(Entity entity)` | Pushes a new entity selection; suppressed during navigation |
| `void GoBack()` | Activates the previous entity selection |
| `void GoForward()` | Activates the next entity selection |

---

### `ComponentDiffService`

**Namespace**: `Fdp.Toolkit.ReplayBrowser.Diff`

```csharp
public sealed class ComponentDiffService : IComponentDiffService
```

| Method | Description |
|---|---|
| `DiffNode? ComputeDiff(string name, JsonNode? oldNode, JsonNode? newNode, double epsilonTolerance)` | Recursively computes a `DiffNode` tree; applies epsilon tolerance for `Number` leaves |
| `IReadOnlyList<DiffNode> ComputeEntityDiff(Entity, EntityRepository, ScenarioSerializer, Action applyStepFunc)` | Serializes the entity before and after `applyStepFunc`, then calls `ComputeDiff` per component |
| `IReadOnlyList<DiffNode> ComputeTreeDiff(JsonNode? before, JsonNode? after, double epsilon)` | Public overload operating on arbitrary JSON trees |

---

### `DiffNode` model

**Namespace**: `Fdp.Toolkit.ReplayBrowser.Diff`

| Class | Properties | Description |
|---|---|---|
| `DiffNode` (abstract) | `Name`, `IsModified` | Base class for all diff tree nodes |
| `DiffObject : DiffNode` | `Children: List<DiffNode>` | Object/component group; `IsModified` is true if any descendant is modified |
| `DiffValue : DiffNode` | `OldValue`, `NewValue`, `ValueType` | Leaf value; `IsModified` set at construction |

---

### `RecordingExportService`

**Namespace**: `Fdp.Toolkit.ReplayBrowser`

```csharp
public sealed class RecordingExportService : IRecordingExportService
```

| Method | Description |
|---|---|
| `void ExportToJson(string inputFdpPath, string outputJsonPath, JsonExportOptions options)` | Streams the recording to a JSON file using `Utf8JsonWriter` directly to avoid buffering |

The `JsonExportOptions` class controls the export:

| Option | Type | Default | Description |
|---|---|---|---|
| `WindowMode` | `ExportWindowMode` | `FullFile` | `FullFile`, `ByFrame`, or `ByTime` |
| `FormatMode` | `ExportFormatMode` | `Incremental` | `Incremental`, `AbsoluteState`, or `Changelog` |
| `StartFrame / EndFrame` | `int` | `0 / MaxValue` | Frame range (ByFrame mode) |
| `StartTimeSec / EndTimeSec` | `float` | `0 / Infinity` | Time range (ByTime mode) |
| `FilterBySelection` | `bool` | `false` | Restrict to `TargetEntities` list |
| `IncludeEntities / IncludeEvents` | `bool` | both `true` | What to include in output |
| `Minified` | `bool` | `false` | Compact vs pretty JSON |
| `EpsilonTolerance` | `double` | `0.001` | Suppress numeric changes smaller than this in Changelog mode |

---

### `IRecordingSearchService` / `RecordingSearchService`

**Namespace**: `Fdp.Toolkit.ReplayBrowser.Search`

```csharp
public sealed class RecordingSearchService : IRecordingSearchService
```

| Method | Description |
|---|---|
| `IReadOnlyList<SearchResultDto> ExecuteSearch(string fdpPath, SearchPredicateDto root, TargetEntityFilter? entityFilter, CancellationToken ct)` | Frame-step scan over entire recording; returns matching `(frame, entity, matchContext)` list |
| `IReadOnlyList<LifecycleSearchResultDto> ExecuteLifecycleSearch(string fdpPath, LifecyclePredicateDto criteria, TargetEntityFilter? entityFilter, CancellationToken ct)` | Detects entity birth and death frames matching name/id criteria |

Search modes dispatched by root predicate type:

| Root predicate type | Dispatch path |
|---|---|
| `TransientEventPredicateDto` | `RunEventScan` - scans event streams, not ECS components |
| `LifecyclePredicateDto` | `ExecuteLifecycleSearch` - detects entity birth/death |
| All others | `RunFrameStepScan` - frame-step loop with `PredicateCompiler` |

---

### `SearchPredicateDto` hierarchy

**Namespace**: `Fdp.Toolkit.ReplayBrowser.Search`

```
SearchPredicateDto (abstract)
+-- CompoundPredicateDto         (Operator: And|Or; Conditions: List<SearchPredicateDto>)
+-- PropertyMatchDto             (ComponentType, PropertyPath, Operator, Predicate)
+-- TransientEventPredicateDto   (EventType, PropertyPath, Operator, TargetValue)
+-- LifecyclePredicateDto        (IdentifierType, TargetValue, NameComponentType, NamePropertyPath)
+-- SpatialBoundingPredicateDto  (Bounds: BoundingBox2D)
+-- StructuralPredicateDto       (required/excluded component types)
+-- BehaviorParamPredicateDto    (AI behavior parameter path + value match)
+-- SearchPredicateValueDto (abstract)
    +-- NumericPredicateDto      (MinValue, MaxValue)
    +-- StringPredicateDto       (Substring, StartsWith, ExactMatch)
    +-- EnumPredicateDto<TEnum>  (AllowedValues)
```

All types are JSON-serializable via `System.Text.Json` polymorphic attributes using
the `"$type"` discriminator property.

---

### `NavigationWaypoint`

**Namespace**: `Fdp.Toolkit.ReplayBrowser`

```csharp
public readonly record struct NavigationWaypoint(int FrameIndex, Entity SelectedEntity)
```

Immutable value type carried by both history trackers.

---

## Dependencies

### NuGet Packages

| Package | Version | Purpose |
|---|---|---|
| `Raylib-cs` | 7.0.2 | 2-D/3-D rendering window and input (used by `MapCanvas`, gizmo layers) |
| `rlImGui-cs` | 3.2.0 | ImGui integration with Raylib render loop |

### Project References

| Project | Purpose |
|---|---|
| `Fdp.Core` | EntityRepository, FdpEventBus, PlaybackController, FlightRecorder format, SimTransform, RecordingMetadata (ExerciseId/NodeId) |
| `Fdp.Presentation` | MapCanvas, WindowManager, IWindowRegistrar, all ImGui panels and windows, FederationPanel |
| `Fdp.Toolkits` | ReplayBrowserContext, histories, diff, export, search, BehaviorRegistry, Vis2D, Scenario, Federation (FederatedReplayManager, TransientMasterBuilder, etc.) |
| `Hrot.Core` | INetworkFactory (accepted by constructor; unused at runtime) |
| `Hrot.CGF` | CgfBehaviorSetup.LoadFromAiAssembly for AI behavior renderer wiring |
| `Hrot.SimHost` | HrotScenarioSerializerFactory, Gizmo registrars, SimHost gizmo definitions |

### Transitive Dependencies (selected)

| Component | Path |
|---|---|
| `ImGuiNET` | via `rlImGui-cs` |
| `GizmoMap.Presentation` | via `Fdp.Toolkits` or `Fdp.Presentation` |
| `StructEdit.Reflection` | via `Fdp.Presentation` (ComponentEditService) |
| `Hrot.Common` | via `Hrot.SimHost` (GlobalActionRegistry, InteractionEventRegistry, LayerControlGizmo) |
| `Hrot.AI.Behaviors` | via `Hrot.CGF` (AI behavior gizmo registrar) |
| `Hrot.IG` | via `Hrot.SimHost` (SelectionState, MapOverlayStyle components) |
| `Hrot.ScenarioEditor` | via `Hrot.SimHost` (SelectionInteractionSystem, ScenarioEditor gizmos) |
| `Hrot.Presentation` | via `Hrot.SimHost` (BrainBlackboardRenderer, BTreeVisualizerRenderer, etc.) |

### InternalsVisibleTo

```
Hrot.ReplayBrowser.Tests
```

---

## Usage Examples

### Example 1: Initialize and load a recording in headless mode

Use this pattern in integration or unit tests that verify search / diff logic without
a GPU window.

```csharp
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Toolkit.Runner;
using Hrot.ReplayBrowser;

// 1. Construct the subsystem without a network factory (test constructor).
var subsystem = new ReplayBrowserSubsystem();

// 2. Initialize in headless mode (no Raylib window is created).
subsystem.Initialize(new SubsystemConfig
{
    DomainId      = 0,
    Headless      = true,
    OwnWindow     = false,
    NodeId        = 0,
    SubsystemName = "ReplayBrowser",
});

// 3. Access the context directly via the internal test seam
//    or by wiring delegates.
var context = new ReplayBrowserContext();
context.LoadRecording(@"C:\Recordings\scenario_2026-05-23.fdp");

Console.WriteLine($"Total frames: {context.Playback!.TotalFrames}");
Console.WriteLine($"Current frame: {context.CurrentFrame}");

// 4. Step through frames.
while (context.StepForward())
{
    // process each frame
}

context.Dispose();
subsystem.Shutdown();
```

---

### Example 2: Run a component property search over a recording

Search for all frames in which any entity's `SimTransform.Position.X` is greater
than 500 meters, optionally restricted to a single entity by network ID.

```csharp
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Search;
using Fdp.Toolkit.Behavior;
using Fdp.Presentation.Editing;
using StructEdit.Reflection;

// Build the services (no GPU needed).
var editService = new ComponentEditServiceBuilder().Build();
var predicateCompiler = new PredicateCompiler(editService, new BehaviorRegistry());
var eventScannerCompiler = new EventScannerCompiler(editService);
var searchService = new RecordingSearchService(predicateCompiler, eventScannerCompiler);

// Describe the predicate: SimTransform.Position.X > 500.
var predicate = new PropertyMatchDto
{
    ComponentType = typeof(Fdp.Core.SimTransform),
    PropertyPath  = "Position.X",
    Operator      = SearchOperator.GreaterThan,
    Predicate     = new NumericPredicateDto { MinValue = 500.0, MaxValue = double.MaxValue }
};

// Execute the search (runs on calling thread; consider Task.Run for UI).
var results = searchService.ExecuteSearch(
    fdpPath: @"C:\Recordings\scenario_2026-05-23.fdp",
    root: predicate);

foreach (var result in results)
{
    Console.WriteLine(
        $"Frame {result.Frame}: entity ({result.Entity.Index},{result.Entity.Generation}) "
        + $"matched context: {result.MatchContext}");
}
```

---

### Example 3: Export a recording window to a diff/changelog JSON file

Export frames 100-200 for a specific entity to a JSON changelog where only changed
fields (with epsilon 0.01) are written. Suitable for offline analysis tools.

```csharp
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Core;

// Build the export service.
var exportService = new RecordingExportService();

var options = new JsonExportOptions
{
    WindowMode          = ExportWindowMode.ByFrame,
    FormatMode          = ExportFormatMode.Changelog,
    StartFrame          = 100,
    EndFrame            = 200,
    FilterBySelection   = true,
    TargetEntities      = { new Entity(3, 1) },
    IncludeEntities     = true,
    IncludeEvents       = false,
    Minified            = false,
    EpsilonTolerance    = 0.01,
};

exportService.ExportToJson(
    inputFdpPath:   @"C:\Recordings\scenario_2026-05-23.fdp",
    outputJsonPath: @"C:\Exports\entity3_frames100-200_changelog.json",
    options:        options);

Console.WriteLine("Export complete.");
```

---

### Example 4: Use the diff service to inspect frame-over-frame changes

Compute the component diff for a single entity between frames 42 and 43, then
walk the diff tree to find only modified numeric leaves.

```csharp
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Diff;
using Fdp.Toolkit.Scenario;

using var context = new ReplayBrowserContext();
context.LoadRecording(@"C:\Recordings\scenario_2026-05-23.fdp");

var diffService  = new ComponentDiffService();
var serializer   = /* obtain HrotScenarioSerializerFactory.Build(...) */;

var entity = new Fdp.Core.Entity(5, 1);

// Seek to one frame before the frame of interest.
context.SeekToFrame(42, suppressHistory: true);

// ComputeEntityDiff serializes before, calls applyStep, serializes after.
var diffs = diffService.ComputeEntityDiff(
    entity,
    context.SandboxRepo,
    serializer,
    () => context.StepForward(suppressHistory: true));

// Walk all modified leaf values.
void Visit(DiffNode node, string path)
{
    if (node is DiffValue val && val.IsModified)
        Console.WriteLine($"{path}: {val.OldValue} -> {val.NewValue}");
    if (node is DiffObject obj)
        foreach (var child in obj.Children)
            Visit(child, path == "" ? child.Name : $"{path}.{child.Name}");
}

foreach (var root in diffs)
    Visit(root, root.Name);
```

---

### Example 5: Wire playback history navigation (test seam pattern)

This pattern is used by `Hrot.ReplayBrowser.Tests` to exercise the history
wiring without a live Raylib window.

```csharp
using Fdp.Core;
using Fdp.Presentation.Panels;
using Fdp.Presentation.Panels.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser;
using Hrot.ReplayBrowser;

var subsystem       = new ReplayBrowserSubsystem();
var entityHistory   = new EntitySelectionHistory();
var playbackHistory = new PlaybackHistoryTracker();
var inspectorState  = new InspectorState();
var context         = new ReplayBrowserContext();
var diffPanel       = new ComponentDiffPanel();
var eventPanel      = new EventBrowserPanel(context.HistoryService);

// Wire all delegates using injected stubs (no Raylib needed).
var (seekIntent, selectIntent, matchIntent) = subsystem.WireDelegatesForTest(
    entityHistory, playbackHistory, inspectorState,
    context, diffPanel, eventPanel);

// Simulate user seeking to frame 10 then frame 20.
seekIntent(10);
seekIntent(20);

// Back/forward should work correctly.
Assert.True(playbackHistory.CanGoBack);

int restoredFrame = -1;
playbackHistory.OnWaypointRequested += wp => restoredFrame = wp.FrameIndex;
playbackHistory.GoBack();

// restoredFrame should be 10 (the previous waypoint).
Console.WriteLine($"Restored frame: {restoredFrame}"); // 10

subsystem.Shutdown();
```

---

### Example 6: Load a federated multi-node recording group and inspect the merged view

This pattern exercises the full Frankenstein synthesis pipeline in headless (test) mode
using `TransientBuildOverride` to substitute a controlled repo.

```csharp
using Fdp.Toolkit.ReplayBrowser;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Fdp.Toolkit.Runner;
using Hrot.ReplayBrowser;

// 1. Initialize the subsystem in headless mode.
var subsystem = new ReplayBrowserSubsystem();
subsystem.Initialize(new SubsystemConfig { Headless = true });

// 2. Override the transient builder so tests do not require a real serializer.
var fakeRepo = new EntityRepository();
subsystem.TransientBuildOverride = _ => fakeRepo;

// 3. Load a group of two recordings (per-node .fdp files sharing an ExerciseId).
//    The files must have matching .meta.json sidecars.
var paths = new[]
{
    @"C:\Recordings\exercise_2026-05-30_node0.fdp",
    @"C:\Recordings\exercise_2026-05-30_node1.fdp",
};
subsystem.LoadFdpGroupForTest(paths, new TransientMasterBuilder(/* serializer */));

// 4. Switch to Merged View to trigger synthesis.
subsystem.SetViewMode(ViewMode.Merged);

// The subsystem's ActiveRepo is now the synthesised transient master.
Console.WriteLine($"Merged repo alive: {subsystem.ActiveRepo != null}");

// 5. Inspect manager state.
var manager = subsystem.Manager!;
Console.WriteLine($"Loaded nodes: {string.Join(", ", manager.Contexts.Keys)}");
Console.WriteLine($"Exercise ID: {manager.ExerciseId}");
Console.WriteLine($"Provider node: {manager.LocalEntitiesProviderNodeId}");

// 6. Apply a per-node offset and observe that OnTimeChanged fires.
manager.SetNodeOffset(nodeId: 1, offsetTicks: 100);

// 7. Switch back to single-node view and seek.
subsystem.SetViewMode(ViewMode.SingleNode);
manager.SetBaseWallTicks(1234567890L);

subsystem.Shutdown();
```

---

### Example 7: Build a transient master directly from a FederatedReplayManager

Use this when you need the merged `EntityRepository` in a headless context without
going through the subsystem.

```csharp
using Fdp.Toolkit.ReplayBrowser.Federation;
using Fdp.Toolkit.Scenario;

// Build a serializer (using any serializer factory available in the project).
ScenarioSerializer serializer = /* HrotScenarioSerializerFactory.Build(...) */;

// Load the federation group.
var manager = FederatedReplayManager.LoadGroup(new[]
{
    @"C:\Recordings\node0.fdp",
    @"C:\Recordings\node1.fdp",
});

// Seek to a wall-clock tick of interest.
manager.SetBaseWallTicks(wallTickFromBreakpoint);

// Synthesise the merged snapshot.
var builder = new TransientMasterBuilder(serializer);
using var mergedRepo = builder.Build(manager);

Console.WriteLine($"Entities in merged view: {mergedRepo.AliveCount}");

// Inspect components on the merged repo exactly as you would a real-time repo.
foreach (var entity in mergedRepo.AliveEntities)
{
    if (mergedRepo.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(entity))
    {
        long netId = mergedRepo.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(entity).Value;
        Console.WriteLine($"  Entity index={entity.Index} NetworkId={netId}");
    }
}

manager.Dispose();
```

---

## Best Practices

### Do not call `SeekToFrame` with `suppressHistory = false` inside a scan loop

`suppressHistory = false` triggers `DiagnosticEventHistoryService.ClearHistory` (or
`RewindHistory`), which is an `O(n)` operation over the captured event log. Always
pass `suppressHistory: true` when scanning programmatically. The main-thread GUI path
calls `SeekToFrame` with the default (`false`) only when the user explicitly navigates.

### Use isolated `ReplayBrowserContext` instances for background work

`SeekToNextChangeAsync` demonstrates the correct pattern: create a `new
ReplayBrowserContext()`, call `LoadRecording`, scan, then dispose. Never share a
`ReplayBrowserContext` between the GUI thread and background tasks.

### Dispose `ReplayBrowserContext` and transient masters after use

`PlaybackController` holds an open `FileStream` and a `BinaryReader`. Failing to
dispose leaves file handles open. Wrap in `using` for any non-GUI context.
`TransientMasterBuilder.Build` allocates a fresh `EntityRepository` on every call;
always dispose the previous one before calling `Build` again.

### Register all component types before scanning

`RecordingSearchService.RegisterAllComponents(repo, playback)` uses reflection to
discover all component types from the recording's schema manifest and register them in
the target `EntityRepository`. Skip this step and `ApplyChunkData` will silently skip
unknown type IDs, producing empty search results.

### Keep predicate DTOs serializable

All `SearchPredicateDto` subclasses use `System.Text.Json` polymorphic serialization
with the `"$type"` discriminator. Ensure that any new predicate type is:
1. Registered in the `[JsonDerivedType(...)]` attribute list on `SearchPredicateDto`
2. Added to the `$type` string registry used by the search panel

### Do not implement `IMapCameraProvider` on the replay subsystem

The test `FND-T10` enforces this. The replay browser intentionally keeps the map
camera independent of other subsystems so that it can zoom to arbitrary historical
positions without affecting a live IG viewport.

### Headless path must be allocation-minimal

All non-headless allocations (MapCanvas, panels, gizmo systems) are guarded by
`if (!_headless)`. Add new non-headless resources only inside those guards. The
headless path is exercised by CI tests without a GPU.

### Never bypass `FederatedReplayManager` to seek individual contexts

`ReplayBrowserSubsystem` must have exactly one path to per-recording state:
`_manager`. Any direct hold on a `ReplayBrowserContext` outside the manager will
desynchronise the merged view — the timeline slider will update only the direct
context, leaving the merged-view rebuild disconnected. Always call
`FederatedReplayManager.SeekAll`, `StepForwardAll`, or `StepBackwardAll` to move
playback.

### Expect severe stutter in Merged View -- it is by design

`TransientMasterBuilder.Build` performs a full JSON round-trip (serialize all
contributing node fragments + deserialize into a fresh `EntityRepository`) on every
operator action. For real cluster snapshots this can take hundreds of milliseconds.
This is the accepted cost of correctness-over-speed. Continuous playback (Play button)
is therefore disabled in Merged View; only step and slider operations are allowed.

### Federated recordings require matching `ExerciseId` in `.meta.json`

Legacy recordings (produced before the Frankenstein feature set) have
`ExerciseId == Guid.Empty`. `FederatedReplayManager.LoadGroup` rejects such files.
If you need to federate legacy recordings, produce new recordings with a
`RecordingConfiguration` that carries a non-empty `ExerciseId` and the correct
`NodeId`, so the recorder stamps both values into the `.meta.json` sidecar.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fdp.Core` | Foundation: ECS, event bus, flight recorder binary format, `PlaybackController` |
| `Fdp.Toolkits` | Provides `ReplayBrowserContext`, all services and history trackers used by this subsystem |
| `Fdp.Presentation` | Provides all ImGui panels and windows; `MapCanvas`; `WindowManager` |
| `Hrot.SimHost` | Sibling subsystem; provides gizmo registrars and `HrotScenarioSerializerFactory` reused here |
| `Hrot.Core` | Provides `INetworkFactory` (accepted by constructor; unused at runtime) |
| `Hrot.CGF` | Provides AI behavior setup (`CgfBehaviorSetup.LoadFromAiAssembly`) |
| `Hrot.ReplayBrowser.Tests` | xUnit test project; exercises headless init, window registration, delegate wiring, causality jump, and navigation history via `internal` test seams |
| `Fdp.Core.Tests` | Tests for `PlaybackController`, frame indexing, and the `.fdp` binary format |
| `Fdp.Toolkits` (ReplayBrowser namespace) | Contains tests for `ReplayBrowserContext`, `ComponentDiffService`, and `RecordingSearchService` |

---

## .fdp File Format Summary

The `.fdp` binary format is written by `RecorderSystem` / `AsyncRecorder` and read by
`PlaybackController`.

```
+--------------------------+
|  RecordingGlobalHeader   |  18 bytes
|  Magic[6] + Version(u32) |  Magic = 0x464450464450 ("FDPFDP")
|  + Timestamp(i64)        |
+--------------------------+
|  Frame 0 outer header    |  FrameOuterHeader (per frame)
|  LZ4-compressed payload  |  entity snapshots + event bus buffers
+--------------------------+
|  Frame 1 outer header    |
|  LZ4-compressed payload  |
+--------------------------+
|  ...                     |
+--------------------------+

Companion file: <recording>.meta.json
  Contains:
  - SchemaManifest: component type names + layout hashes
    Used by SchemaValidator to detect struct layout changes at load time.
  - EventManifest: event type names + layout hashes
  - ExerciseId (Guid): shared by all nodes in one distributed exercise;
    Guid.Empty for legacy recordings produced before federation support.
  - NodeId (int): identifies which distributed node produced this recording;
    0 for legacy recordings.
  - MaxNetworkId (long): highest DIS entity ID observed; used by replay-to-live.
```

`PlaybackController` scans the entire file on open (`BuildFrameIndex`) to build a
`List<FrameMetadata>` with file offsets, enabling O(1) random-access seek. Backward
seek is implemented by re-applying all frames from 0 to `targetFrame - 1`, since
LZ4 frames are delta-compressed and not independently decodable.

`FederatedReplayManager.LoadGroup` uses the `.meta.json` sidecar (specifically
`ExerciseId` and `NodeId`) to validate that a set of files belongs to the same
distributed exercise before instantiating contexts.

---

*Documentation updated 2026-05-30. Original documentation generated 2026-05-23.*
