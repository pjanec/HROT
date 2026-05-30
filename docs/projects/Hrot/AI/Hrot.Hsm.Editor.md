# Hrot.Hsm.Editor

**Project file**: `Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj`
**Project folder**: `Hrot/Subsystems/AI/Hrot.Hsm.Editor/`
**Target framework**: net8.0
**Date**: 2026-05-30

---

## README Validation

**Status: Missing**

No `README.md` was found anywhere inside the `Hrot.Hsm.Editor` project folder.
All architectural context in this document was derived by reading source code directly.

---

## Executive Overview

`Hrot.Hsm.Editor` is the visual authoring tool for Hierarchical State Machines (HSMs)
used by HROT AI agents. It is a structural sibling of `Hrot.BTree.Editor` and shares
the same pattern language via `Hrot.Editor.AiShared`.

The editor bridges two worlds:

- **Authoring side**: a NodeEditor-based canvas where designers compose HSMs from
  typed state nodes (Simple, Composite, Parallel, Final, History, DeepHistory),
  connect transitions between them, and configure event/action/guard bindings
  through an inspector panel.

- **Runtime side**: the `Fhsm.Kernel` (FastHSM) which compiles `HsmDefinitionBlob`
  structures via `HsmBuilder` and executes them against entity instances in simulation.

### Authoring Workflow

1. The designer opens a `.cs` source file containing `[HsmDefinition]`-annotated
   static methods.
2. `HsmAssetContributor` reflects the assembly (on load or hot-reload), invokes each
   definition method to obtain an `HsmDefinitionBlob`, and feeds it through
   `HsmAssetProjector` to produce an `HsmAsset`.
3. The canvas renders the asset via `NodeEditor.Core`, backed by `HsmNodeCatalog`,
   `HsmTypeSystem`, `HsmLinkValidator`, `HsmCommandSink`, and seven custom renderers.
4. Every structural or property change is routed through `HsmCommandSink`, which
   mutates the `HsmAsset` and marks it dirty.
5. When the designer saves, `HsmFluentEmitter` serializes the asset back to a C#
   source file containing three methods: `CreateBuilder()`, `Compile()`, and
   `Layout()`.
6. The project is compiled. `HsmAssetContributor.LoadFrom()` is called again on the
   new assembly, completing the round-trip.

### What the Editor Produces

- A C# static class with a fluent `HsmBuilder` call chain that defines the machine
  structure, events, actions, guards, and transitions (`CreateBuilder()`).
- A `[HsmDefinition]` thunk (`Compile()`) calling `CreateBuilder().Build().Compile()`
  that the Fhsm kernel uses to compile the blob.
- A `[HsmLayout]` method (`Layout()`) that stores per-state canvas positions, sizes,
  colors, comments, and transition waypoints so layout survives recompilation.

### Relationship to Hrot.BTree.Editor

`Hrot.Hsm.Editor` mirrors the architecture of `Hrot.BTree.Editor` but models a
different formalism:

| Aspect                 | BTree.Editor              | Hsm.Editor                         |
|------------------------|---------------------------|------------------------------------|
| Runtime kernel         | Fbt.Kernel                | Fhsm.Kernel                        |
| Asset type             | BehaviorTreeAsset         | HsmAsset                           |
| Structural units       | Tree nodes (BTreeNode)    | States + Transitions (StateNode,   |
|                        |                           | TransitionNode, RegionNode)        |
| Node kinds             | Composite / Leaf /        | Simple / Composite / Parallel /    |
|                        | Decorator                 | Final / History / DeepHistory      |
| Emitter output method  | `Build()`                 | `Compile()`                        |
| Layout attribute       | `[BTreeLayout]`           | `[HsmLayout]`                      |
| Debug session          | BTreeDebugSession         | HsmDebugSession                    |
| Unique concepts        | Blackboard, subtrees      | Orthogonal regions, event queue,   |
|                        |                           | sync groups, deferred events       |

### Debug / Live Mode

When a simulation is running, the editor connects an `HsmDebugSession` to the kernel
adapter. Six custom canvas renderers draw on the canvas:

- `HsmRuntimeOverlayRenderer` - teal glow on active leaf states and their ancestors.
- `HsmHeatmapRenderer` - blue-to-red fill behind states by visit frequency.
- `HsmBreakpointGutterRenderer` - red dot gutter markers on states with breakpoints.
- `HsmHistoryGlyphsRenderer` - H / H* / F glyphs on pseudo-states.
- `HsmInitialArrowRenderer` - gold LCA outline when a transition is selected.
- `HsmTransitionLabelRenderer` - Event[Guard]/Action labels at transition midpoints.

A seventh renderer, `HsmRegionConflictsRenderer`, draws yellow warning lines between
states in parallel regions that write to the same command lane (an `OutputLaneConflict`
diagnostic).

---

## Architecture

### Layer Map

The project is organized into ten sub-namespaces that form a clear dependency
hierarchy:

```
+-------------------------------------------------------------------+
|                       Hrot.Hsm.Editor                            |
|                                                                   |
|  +---------+  +-------+  +--------+  +------+  +----------+     |
|  | Catalog |  | Model |  | Layout |  | Emit |  | HotReload|     |
|  +----+----+  +---+---+  +---+----+  +--+---+  +----+-----+     |
|       |           |          |          |            |           |
|  +----v----+  +---v------+  +v-------+  +v--------+  |          |
|  | Asset   |  | Asset    |  | Auto   |  | Fluent  |  |          |
|  |Contrib  |  |Projector |  | Layout |  |Emitter  |  |          |
|  +---------+  +----------+  +--------+  +---------+  |          |
|                                                                   |
|  +-------+  +-----------+  +-----------+  +----------+          |
|  |  Host |  |  Debug    |  | Inspector |  | Renderers|          |
|  +-------+  +-----------+  +-----------+  +----------+          |
|                                                                   |
|  +------------+  +----------+  +-----+                           |
|  | Validation |  | Windows  |  | ... |                           |
|  +------------+  +----------+  +-----+                           |
+-------------------------------------------------------------------+
        |                   |                     |
        v                   v                     v
+---------------+  +-------------------+  +------------------+
| NodeEditor    |  | Fhsm.Kernel       |  | AiShared         |
| (graph canvas)|  | (HSM runtime)     |  | (shared infra)   |
+---------------+  +-------------------+  +------------------+
```

### Data Flow: Authoring Round-Trip

```
[.cs source file]
       |
       | Assembly.LoadFrom() / hot reload
       v
+-------------------+       +-------------------+
| HsmAssetContributor|      | HsmDefinitionBlob  |
| .LoadFrom(asm)     |----->| (kernel compiled   |
|                    |      |  binary)            |
+-------------------+       +-------------------+
       |                              |
       |  HsmAssetProjector.Project() |
       |<-----------------------------+
       |  + HsmEditorLayout (if any)
       v
+-------------------+
|     HsmAsset      |  <-- editor model (mutable)
|  AllStates        |
|  AllTransitions   |
|  AllRegions       |
|  AllGlobalTrans.  |
|  AllEvents        |
+-------------------+
       |            ^
       | canvas     | HsmCommandSink.Apply()
       v            | (user edits)
+-------------------+
|  HsmGraphModel    |  <-- IGraphModel adapter
| (NodeEditor view) |
+-------------------+
       |
       | Save
       v
+-------------------+
| HsmFluentEmitter  |
| .Emit(asset)      |
+-------------------+
       |
       v
[.cs source file regenerated]
       |  dotnet build
       v
[new assembly -> back to top]
```

### Debug Data Flow

```
+-------------------+        +--------------------+
|  Fhsm Kernel      |        |  HsmDebugSession   |
|  (simulation)     |        |  (IHsmDebugSession)|
|                   | push   |                    |
| kernel adapter    |------->| RecordTrace()      |
| (future Slice 3+) |        | RaiseBreakpointHit |
|                   |        |                    |
+-------------------+        +----+---------------+
                                  |
                    +-------------+-----------+
                    |             |            |
              event routing  heatmap       snapshot
                    |         counts         (future)
                    v             v
         +-------------------+  +---------------------+
         | Typed event subs  |  | HsmHeatmapRenderer  |
         | OnStateEntered    |  | (entry counts)      |
         | OnTransitionFired |  +---------------------+
         | OnGuardEvaluated  |
         | OnRegionConflict  |  +---------------------+
         | etc.              |  | HsmRuntimeOverlay   |
         +-------------------+  | Renderer (active    |
                                 | configuration glow) |
                                 +---------------------+
```

### State Model Hierarchy

```
HsmAsset
  |
  +-- RootState (StateNode, synthetic, FlatIndex=0xFFFF)
  |     |
  |     +-- StateNode (top-level, IsInitial/etc.)
  |     |     |
  |     |     +-- StateNode (child, nested)
  |     |     |     ...
  |     |     +-- StateNode (Final/History pseudo-state)
  |     |
  |     +-- StateNode (Parallel composite)
  |           |
  |           +-- RegionNode  [0]
  |           |     InitialChild -> StateNode
  |           +-- RegionNode  [1]
  |                 InitialChild -> StateNode
  |
  +-- AllTransitions   (TransitionNode list, flat)
  +-- AllGlobalTransitions (GlobalTransitionNode list)
  +-- AllEvents        (EventDefinition list, sorted by EventId)
  +-- AllRegions       (RegionNode list, flat)
```

---

## Source Structure

All types live under the root namespace `Hrot.Hsm.Editor`.

### Catalog

| File | Class | Description |
|------|-------|-------------|
| `Catalog/HsmAssetContributor.cs` | `HsmAssetContributor` | Scans an assembly for `[HsmDefinition]` methods, invokes them, projects each `HsmDefinitionBlob` into an `HsmAsset`. Implements `IAssetCatalogContributor`. Call `LoadFrom(assembly)` after each hot-reload. |

### Model

| File | Class/Type | Description |
|------|------------|-------------|
| `Model/HsmAsset.cs` | `HsmAsset` | Central mutable editor model. Holds the state hierarchy, transitions, events, regions, layout state, and identity maps. |
| `Model/HsmAsset.cs` | `StateNode` | Represents one state. Implements `IContainerNodeModel`. Carries all kernel flags, action names, layout fields, and derived pin IDs. |
| `Model/HsmAsset.cs` | `TransitionNode` | Represents one transition (source -> target, event/guard/action, kind, priority, sync group). |
| `Model/HsmAsset.cs` | `GlobalTransitionNode` | Represents a machine-level (global) transition applied regardless of active state. |
| `Model/HsmAsset.cs` | `RegionNode` | Represents one orthogonal region within a parallel state. |
| `Model/HsmAsset.cs` | `EventDefinition` | Represents one event declared in the machine. |
| `Model/HsmAsset.cs` | `TransitionKind` | Enum: External, Internal, Local. |
| `Model/HsmAssetProjector.cs` | `HsmAssetProjector` | Static factory: `Project(blob, metadata, layout, ...)` -> `HsmAsset`. Applies layout positions/IDs if provided; runs auto-layout otherwise. |
| `Model/HsmGraphModel.cs` | `HsmGraphModel` | Adapts `HsmAsset` to NodeEditor's `IGraphModel`. Exposes states as nodes, transitions as links. |
| `Model/HsmAttachment.cs` | `HsmAttachment` (internal) | Internal implementation of `IAttachmentModel` for HSM state nodes. Created by `HsmCommandSink` on `GraphCommand.AddAttachment`. Stores category, glyph, label, tooltip, stack index, and mutable `AttachmentState`. |
| `Model/HsmPinModel.cs` | `HsmPinModel` | Hidden pin model (internal). One output pin + one input pin per state, both invisible on canvas. |
| `Model/HsmTransitionLink.cs` | `HsmTransitionLink` | Adapts `TransitionNode` to NodeEditor's `ILinkModel`. Solid for External, Dashed for Internal. |

### Host

| File | Class | Description |
|------|-------|-------------|
| `Host/HsmEditorHostServices.cs` | `HsmEditorHostServices` | Implements `IEditorHostServices`. Aggregates all per-asset services for one NodeEditor canvas instance. Provides viewport-reset signaling. |
| `Host/HsmCommandSink.cs` | `HsmCommandSink` (internal) | Implements `IGraphCommandSink`. Dispatches graph commands (add/remove state, link/transition, region, attachment, etc.) to per-command handlers. Marks the asset dirty after each successful command. All handlers are fully implemented including `GraphCommand.AddAttachment`, `GraphCommand.RemoveAttachments`, `GraphCommand.ChangeParent`, and all parallel-region mutations. |
| `Host/HsmNodeCatalog.cs` | `HsmNodeCatalog` (internal) | Implements `INodeCatalog`. Static catalog of six state kind entries (Simple, Composite, Parallel, Final, History, DeepHistory). |
| `Host/HsmTypeSystem.cs` | `HsmTypeSystem` (internal) | Implements `ITypeSystem`. HSM states have no typed pins; all type queries return negative answers. |
| `Host/HsmLinkValidator.cs` | `HsmLinkValidator` (internal) | Implements `ILinkValidator`. Validates that a new transition is legal: pins must resolve to known states; Final states cannot be sources; History pseudo-states cannot be targets. |
| `Host/HsmKinds.cs` | `HsmKinds` (internal static) | String constants for the six state kind IDs used by the catalog and `StateNode.Kind`. |
| `Host/HsmTraceLaneProvider.cs` | `HsmTraceLaneProvider` | Implements `ITraceLaneProvider`. Declares the six HSM trace lane descriptors (States, Events, Actions, Guards, Timers, Conflicts). |
| `Host/HsmTransitionSnapHelper.cs` | `HsmTransitionSnapHelper` (public static) | Snap-to-state helper for drag-to-create-transition gestures. Finds the nearest valid snap target within a configurable canvas radius. |

### Emit

| File | Class | Description |
|------|-------|-------------|
| `Emit/HsmFluentEmitter.cs` | `HsmFluentEmitter` | Implements `IFluentCSharpEmitter<HsmAsset>`. Serializes an `HsmAsset` back to a `.cs` file with `CreateBuilder()`, `Compile()`, and `Layout()` methods. |

### Layout

| File | Class | Description |
|------|-------|-------------|
| `Layout/HsmAutoLayout.cs` | `HsmAutoLayout` (public static) | Grid-based initial layout. Lays top-level states left-to-right; places composite children in a three-column grid. Runs only when no layout data is present. |

### Validation

| File | Class/Type | Description |
|------|------------|-------------|
| `Validation/HsmValidator.cs` | `HsmValidator` | Validates an `HsmAsset` and returns a list of `HsmDiagnostic`. Enforces structural rules: initial children, history placement, final state constraints, state depth, dangling event references, output lane conflicts, and cross-region blackboard conflicts. |
| `Validation/HsmAssetValidator.cs` | `HsmAssetValidator` | Adapts `HsmValidator` to `IAssetValidator` so diagnostics appear in the cross-asset `DiagnosticsWindow`. |
| `Validation/HsmDiagnostic.cs` | `HsmDiagnostic`, `HsmDiagnosticSeverity` | Diagnostic record type (Code, Severity, Message, TargetStableIds). |
| `Validation/HsmDiagnosticCode.cs` | `HsmDiagnosticCode` | Enum of 15 diagnostic codes covering structural, referential, performance, and blackboard-conflict problems. |
| `Validation/HsmOutputLaneMaskInferrer.cs` | `HsmOutputLaneMaskInferrer` | Reflects assemblies for `[HsmAction]` attributes to build an FQN->CommandLane map. Computes and applies `OutputLaneMask` to all states for conflict detection. |

### Debug

| File | Class/Type | Description |
|------|------------|-------------|
| `Debug/IHsmDebugSession.cs` | `IHsmDebugSession` | HSM-specific debug session interface. Extends `IAiDebugSession` with snapshot access, trace history ring, heatmap counters, and eight typed event surfaces. |
| `Debug/HsmDebugSession.cs` | `HsmDebugSession` | Production implementation. Maintains a 200-record ring buffer. Routes trace records to typed events. Tracks heatmap entry counts. Step-control methods are no-ops until kernel wiring (Slice 3+). |
| `Debug/HsmDebugTypes.cs` | `HsmInstanceSnapshot` | Immutable snapshot of one HSM instance: active leaf IDs, event queue, timer slots, history slots, phase, micro-step, generation, flags. |
| `Debug/HsmDebugTypes.cs` | `HsmTraceRecord` (abstract) | Base record for all kernel trace events in the ring buffer. |
| `Debug/HsmDebugTypes.cs` | `HsmStateEntered` | Trace record: state entered by Self entity. |
| `Debug/HsmDebugTypes.cs` | `HsmStateExited` | Trace record: state exited by Self entity. |
| `Debug/HsmDebugTypes.cs` | `HsmTransitionFired` | Trace record: transition fired (source/target IDs, event, guard result, sync group). |
| `Debug/HsmDebugTypes.cs` | `HsmEventQueued` | Trace record: event enqueued onto an instance. |
| `Debug/HsmDebugTypes.cs` | `HsmRegionConflict` | Trace record: conflicting command-lane usage detected between two parallel regions. |
| `Debug/HsmDebugTypes.cs` | `HsmGuardEvaluated` | Trace record: guard function evaluated with its boolean result. |
| `Debug/HsmDebugTypes.cs` | `HsmTimerEvent` | Trace record: timer slot fired or was set. |
| `Debug/HsmDebugTypes.cs` | `HsmEventQueueEntry` | Entry in a snapshot event queue (EventId, name, priority, queue position). |
| `Debug/HsmDebugTypes.cs` | `HsmTimerSlot` | Timer slot state in a snapshot (owner StableId, remaining ticks). |
| `Debug/HsmDebugTypes.cs` | `HsmHistorySlot` | History slot state in a snapshot (shallow/deep, owner, recorded child). |
| `Debug/HsmDebugTypes.cs` | `HsmBreakpointHit` | Separate notification raised when a breakpoint fires. |
| `Debug/HsmBreakpointMenuPopulator.cs` | `HsmBreakpointMenuPopulator` | Static helper that populates the right-click context menu for an HSM state node with Universal Breakpoints items. Synthesises `TraceBufferScanPredicateDto` and `CompoundPredicateDto` conditions over `HsmTraceWorkingMemory1024` and registers them via `IDataBreakpointManager.AddBreakpoint`. Menu items: "Break on Enter" (StateEnter opcode), "Break on Exit" (StateExit opcode), "Break on Guard Evaluated" (GuardEvaluated opcode), "Add Conditional Data Breakpoint..." (compound Enter + user-configured Branch B). The `SourceElementId` of each registered breakpoint is set to `state.StableId` so `HsmBreakpointGutterRenderer` can draw the gutter dot. |

### Inspector

| File | Class/Type | Description |
|------|------------|-------------|
| `Inspector/HsmFacets.cs` | `StateFacet` | StructEdit-annotated struct shown when a state is selected: name, actions, flags, deferred events, lane summary, breakpoint, read-only stats. |
| `Inspector/HsmFacets.cs` | `TransitionFacet` | StructEdit-annotated struct shown when a transition is selected: source/target, event, guard, action, priority, kind, sync group, LCA info. |
| `Inspector/HsmFacets.cs` | `RegionFacet` | StructEdit-annotated struct shown when a region is selected: name, priority, initial child, comment, color. |
| `Inspector/HsmFacets.cs` | `EventFacet` | StructEdit-annotated struct shown when an event row is selected: name, ID, payload size, indirect flag, deferred-by summary, transition counts. |
| `Inspector/HsmFacets.cs` | `GlobalTransitionFacet` | StructEdit-annotated struct shown when a global transition chip is selected. |
| `Inspector/HsmFacetMapper.cs` | `HsmFacetMapper` | Maps sub-selection identifiers to facet structs. Computes LCA name and cost for transitions. Provides `FindLca()` and `GetEventFacet()` etc. |
| `Inspector/HsmSubSelections.cs` | `HsmEventSelection` | Selection record for an event row (by EventId). |
| `Inspector/HsmSubSelections.cs` | `HsmGlobalTransitionSelection` | Selection record for a global transition chip (by VisualId). |
| `Inspector/HsmRuntimeInspectorPane.cs` | `HsmRuntimeInspectorPane` | ImGui pane shown in the debug panel. Renders phase, active leaves, event queue, timer slots, and history slots from the live snapshot. |
| `Inspector/HsmActionPickerAttribute.cs` | `HsmActionPickerAttribute` | Marker attribute for StructEdit fields: renders as action FQN picker. |
| `Inspector/HsmEventPickerAttribute.cs` | `HsmEventPickerAttribute` | Marker attribute for StructEdit fields: renders as event picker (from asset's AllEvents). |
| `Inspector/HsmGuardPickerAttribute.cs` | `HsmGuardPickerAttribute` | Marker attribute for StructEdit fields: renders as guard FQN picker. |
| `Inspector/HsmStateSelectorAttribute.cs` | `HsmStateSelectorAttribute` | Marker attribute for StructEdit fields: renders as state name selector (from asset's AllStates). |
| `Inspector/HsmSyncGroupPickerAttribute.cs` | `HsmSyncGroupPickerAttribute` | Marker attribute for StructEdit fields: renders as sync-group picker (from asset's transitions). |

### Renderers

| File | Class | Render Pass | Description |
|------|-------|-------------|-------------|
| `Renderers/HsmRuntimeOverlayRenderer.cs` | `HsmRuntimeOverlayRenderer` | AfterNodes | Teal glow on active leaf states; gold diamond at last-fired transition source. |
| `Renderers/HsmHeatmapRenderer.cs` | `HsmHeatmapRenderer` | BeforeContent | Blue-green-yellow-red fill behind state bodies by visit frequency. Requires `HeatmapModeActive = true`. |
| `Renderers/HsmBreakpointGutterRenderer.cs` | `HsmBreakpointGutterRenderer` | AfterNodes | Small red filled circle in the gutter of states with active breakpoints. Also draws a red affordance dot on transition labels for transitions that have breakpoints set. |
| `Renderers/HsmHistoryGlyphsRenderer.cs` | `HsmHistoryGlyphsRenderer` | AfterNodes | H / H* / F circle glyphs at the center of pseudo-states. |
| `Renderers/HsmInitialArrowRenderer.cs` | `HsmInitialArrowRenderer` | AfterNodes | Gold outline around the LCA composite when a transition is selected. |
| `Renderers/HsmRegionConflictsRenderer.cs` | `HsmRegionConflictsRenderer` | AfterNodes | Yellow line + "!" between conflicting parallel-region states. Driven by `HsmDiagnostic` list from last validation run. |
| `Renderers/HsmTransitionLabelRenderer.cs` | `HsmTransitionLabelRenderer` | AfterWires | Event[Guard]/Action labels at transition midpoints. `FormatLabel()` is a public static utility. |

### HotReload

| File | Class | Description |
|------|-------|-------------|
| `HotReload/HsmQuickReloadHasher.cs` | `HsmQuickReloadHasher` (public static) | Classifies a hot-reload tier by comparing `StructureHash` and `ParameterHash` of two `HsmDefinitionBlob` headers. Delegates to `HotReloadClassifier` from AiShared. |

### Windows

| File | Class | Description |
|------|-------|-------------|
| `Windows/HsmEventsWindow.cs` | `HsmEventsWindow` | ImGui window listing all event declarations. Supports Find References and Rename via `IRefactorService`. |
| `Windows/HsmGlobalsStrip.cs` | `HsmGlobalsStrip` | Strip showing chips for each global transition. Render body is a stub pending later implementation. |

---

## Public API Reference

### HsmAsset

```csharp
public sealed class HsmAsset : IEditableAsset, IBlackboardManagedAsset
{
    // Identity
    public Guid AssetId { get; }
    public string Name { get; set; }
    public AssetKind Kind => AssetKind.Hsm;
    public string SourceFilePath { get; }
    public bool IsDirty { get; internal set; }
    public bool IsEditorOwned { get; }
    public string TargetNamespace { get; }

    // Kernel-side data (read-only after projection)
    public HsmDefinitionBlob Blob { get; }
    public MachineMetadata Metadata { get; }

    // Editor state hierarchy
    public StateNode RootState { get; }
    public IReadOnlyList<StateNode> AllStates { get; }
    public IReadOnlyList<TransitionNode> AllTransitions { get; }
    public IReadOnlyList<GlobalTransitionNode> AllGlobalTransitions { get; }
    public IReadOnlyList<RegionNode> AllRegions { get; }
    public IReadOnlyList<EventDefinition> AllEvents { get; }

    // Canvas layout
    public Vector2 CanvasPanOffset { get; set; }
    public float CanvasZoomLevel { get; set; }

    // Blackboard (IBlackboardManagedAsset)
    public string BlackboardTypeName { get; set; }
    public bool IsBlackboardEditorManaged { get; set; }
    public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables { get; }
    public BlackboardLoadState LoadState { get; }
    public string? LoadDiagnosticMessage { get; }
    // ... AddVariable, RemoveVariable, RenameVariable, etc.

    // Identity bridge lookups
    public StateNode? FindStateByStableId(Guid stableId);
    public TransitionNode? FindTransitionByVisualId(Guid visualId);
    public RegionNode? FindRegionByStableId(Guid stableId);
    public StateNode? FindStateByFlatIndex(ushort flatIndex);
    public TransitionNode? FindTransitionByFlatIndex(ushort flatIndex);
    public EventDefinition? FindEventById(ushort eventId);

    public event Action? Changed;
}
```

### StateNode

```csharp
public sealed class StateNode : IContainerNodeModel
{
    // Primary identity
    public Guid StableId;
    public ushort FlatIndex;
    public string Name;
    public StateNode? Parent;
    public List<StateNode> Children { get; }
    public List<TransitionNode> OutgoingTransitions { get; }
    public List<RegionNode> RegionNodes { get; }

    // State kind flags (from StateDef.Flags)
    public bool IsInitial;
    public bool IsHistory;
    public bool IsDeepHistory;
    public bool IsParallel;
    public bool IsFinal;

    // Action bindings (resolved FQN strings; null = no action)
    public string? OnEntryAction;
    public string? OnExitAction;
    public string? ActivityAction;
    public string? TimerAction;
    public byte OutputLaneMask;
    public List<ushort> DeferredEventIds { get; }
    public int RegionIndex;

    // Layout (persisted in Layout() method)
    public Vector2 Position { get; set; }
    public Vector2? SizeOverride { get; set; }
    public string? Comment;
    public bool IsCollapsed { get; set; }
    public string? ColorOverride;

    // Ephemeral (not persisted)
    public bool IsBreakpoint;

    // Deterministic pin IDs
    public Guid HiddenOutputPinId => DeriveOutputPinId(StableId);
    public Guid HiddenInputPinId  => DeriveInputPinId(StableId);

    internal static Guid DeriveOutputPinId(Guid stableId);
    internal static Guid DeriveInputPinId(Guid stableId);
}
```

### TransitionNode

```csharp
public sealed class TransitionNode
{
    public Guid VisualId;
    public ushort FlatIndex;
    public StateNode Source;
    public StateNode Target;
    public ushort EventId;
    public string? EventName;
    public string? GuardFunction;
    public string? ActionFunction;
    public byte Priority;
    public TransitionKind Kind;   // External | Internal | Local
    public ushort SyncGroupId;
    public List<Vector2> Waypoints { get; }
    public string? Comment;
    public bool IsBreakpoint;
}
```

### HsmAssetContributor

```csharp
public sealed class HsmAssetContributor : IAssetCatalogContributor
{
    public AssetKind Kind => AssetKind.Hsm;
    public event Action? ContributorChanged;
    public IReadOnlyList<IEditableAsset> Enumerate();
    public void LoadFrom(Assembly assembly);
}
```

`LoadFrom()` is the primary entry point. Call it once per assembly load or
hot-reload cycle. It clears the previous asset list, re-reflects, projects,
and fires `ContributorChanged`.

### HsmAssetProjector

```csharp
internal static class HsmAssetProjector
{
    public static HsmAsset Project(
        HsmDefinitionBlob blob,
        MachineMetadata? metadata,
        HsmEditorLayout? layout,
        Guid assetId,
        string machineName,
        string sourceFilePath,
        bool isEditorOwned,
        string assemblyNamespace);
}
```

Pass `null` for `layout` on first import; the projector will call
`HsmAutoLayout.Layout()` automatically. Pass a `HsmEditorLayout` obtained from
`LayoutDiscovery.TryGetLayout<HsmLayoutAttribute, HsmEditorLayout>()` to restore
positions.

### HsmFluentEmitter

```csharp
public sealed class HsmFluentEmitter : IFluentCSharpEmitter<HsmAsset>
{
    public string Emit(HsmAsset asset);
}
```

Returns the full text of a `.cs` file. The caller is responsible for writing it
to disk. The output is deterministic for a given `HsmAsset` content (all
collections sorted by StableId/VisualId Guid).

### HsmValidator

```csharp
public sealed class HsmValidator
{
    public IReadOnlyList<HsmDiagnostic> Validate(HsmAsset asset);
}
```

### HsmAssetValidator

```csharp
public sealed class HsmAssetValidator : IAssetValidator
{
    public AssetKind SupportedKind => AssetKind.Hsm;
    public IReadOnlyList<AssetDiagnostic> Validate(IEditableAsset asset);
}
```

### IHsmDebugSession

```csharp
public interface IHsmDebugSession : IAiDebugSession
{
    HsmInstanceSnapshot? GetCurrentStateSnapshot();
    IReadOnlyList<HsmTraceRecord> GetRecentTraceHistory(int max = 100);
    bool HeatmapModeActive { get; set; }
    IReadOnlyDictionary<Guid, int>? GetStateEntryCounts(Guid assetId);
    void ResetStateEntryCounts();

    event Action<HsmBreakpointHit>? OnBreakpointHit;
    event Action<HsmStateEntered>? OnStateEntered;
    event Action<HsmStateExited>? OnStateExited;
    event Action<HsmTransitionFired>? OnTransitionFired;
    event Action<HsmEventQueued>? OnEventQueued;
    event Action<HsmRegionConflict>? OnRegionConflict;
    event Action<HsmGuardEvaluated>? OnGuardEvaluated;
    event Action<HsmTimerEvent>? OnTimerEvent;
}
```

### HsmDebugSession

```csharp
public sealed class HsmDebugSession : AiDebugSessionBase, IHsmDebugSession
{
    // Called by the kernel adapter (Slice 3+)
    public void RecordTrace(HsmTraceRecord record);
    public void RaiseBreakpointHit(HsmBreakpointHit hit);
}
```

### HsmEditorHostServices

```csharp
internal sealed class HsmEditorHostServices : IEditorHostServices
{
    public HsmEditorHostServices(
        HsmNodeCatalog nodeCatalog, HsmTypeSystem typeSystem,
        HsmLinkValidator linkValidator, HsmCommandSink commandSink,
        IPickerRegistry pickers, IClipboard clipboard, IIconProvider icons,
        IDiagnosticsSink? diagnostics, IInputSource input, IEditorTheme theme,
        IDebugSession? debug = null,
        IReadOnlyList<ICustomCanvasRenderer>? customRenderers = null);

    public void SetDebugSession(IDebugSession? session);
    public void RequestViewportReset();
    public bool ViewportResetPending { get; }
    public bool ConsumeViewportReset();
}
```

### HsmTransitionSnapHelper

```csharp
public static class HsmTransitionSnapHelper
{
    public static StateNode? FindNearestSnapTarget(
        Vector2 canvasPos,
        HsmAsset asset,
        StateNode? excludeSource = null,
        float snapRadiusCanvas = 24f);

    public static bool IsValidTransitionTarget(
        StateNode state,
        HsmAsset asset,
        bool allowFinalTarget = true);
}
```

### HsmFacetMapper

```csharp
public sealed class HsmFacetMapper
{
    public HsmFacetMapper(HsmAsset asset);
    public StateFacet GetStateFacet(Guid stableId);
    public TransitionFacet GetTransitionFacet(Guid visualId);
    public RegionFacet GetRegionFacet(Guid parentStableId, int regionIndex);
    public EventFacet GetEventFacet(ushort eventId);
    public GlobalTransitionFacet GetGlobalTransitionFacet(Guid visualId);
    public StateNode FindLca(StateNode a, StateNode b);
}
```

### HsmTransitionLabelRenderer

```csharp
public sealed class HsmTransitionLabelRenderer : ICustomCanvasRenderer
{
    // Public static utility used by tooling outside the renderer:
    public static string FormatLabel(TransitionNode t);
    // Format: "EventName[GuardShort]/ActionShort [SG:N] (P:N)"
}
```

### HsmOutputLaneMaskInferrer

```csharp
public sealed class HsmOutputLaneMaskInferrer
{
    public static IReadOnlyDictionary<string, CommandLane>
        BuildLaneDictionary(IEnumerable<Assembly> assemblies);

    public static byte ComputeMask(
        StateNode state,
        IReadOnlyDictionary<string, CommandLane> laneMap);

    public static void ApplyToAsset(
        HsmAsset asset,
        IReadOnlyDictionary<string, CommandLane> laneMap);
}
```

### HsmQuickReloadHasher

```csharp
public static class HsmQuickReloadHasher
{
    public static HotReloadTier Classify(
        HsmDefinitionBlob previous,
        HsmDefinitionBlob next);
}
```

Returns `HotReloadTier.Hard`, `HotReloadTier.Soft`, or
`HotReloadTier.Cosmetic` based on `StructureHash`/`ParameterHash` comparison.

### HsmAutoLayout

```csharp
public static class HsmAutoLayout
{
    public static void Layout(HsmAsset asset);
}
```

Writes `Position` and `SizeOverride` directly to each `StateNode`. Only
positions states whose position is zero (first-time layout).

### Diagnostic types

```csharp
public enum HsmDiagnosticCode
{
    CompositeWithoutInitialChild,
    MultipleInitialChildrenInSameParent,
    HistoryOutsideComposite,
    FinalStateWithChildren,
    FinalStateWithOutgoingTransition,
    UnboundAction,
    UnboundGuard,
    OutputLaneConflict,
    CrossRegionBlackboardConflict,
    StateDepthExceeded,
    RegionCountExceedsTier,
    TransitionPriorityCycle,
    EventReferenceDangling,
    ActionSignatureMismatch,
    DanglingReferenceAfterReload,
}

public sealed record HsmDiagnostic(
    HsmDiagnosticCode Code,
    HsmDiagnosticSeverity Severity,
    string Message,
    IReadOnlyList<Guid> TargetStableIds);
```

### Picker and selector attributes

All live in `Hrot.Hsm.Editor.Inspector`. They are marker `Attribute` subclasses
with no constructor parameters.

| Attribute | Applied to | StructEdit renderer |
|-----------|------------|---------------------|
| `HsmActionPickerAttribute` | field/property | Action FQN picker |
| `HsmEventPickerAttribute` | field/property | Event picker (from AllEvents) |
| `HsmGuardPickerAttribute` | field/property | Guard FQN picker |
| `HsmStateSelectorAttribute` | field/property | State name selector |
| `HsmSyncGroupPickerAttribute` | field/property | Sync-group ID picker |

---

## Visual Asset Comparison

`Hrot.Hsm.Editor` participates in the Visual Asset Comparison feature via three files
under `Comparison/`:

| File | Purpose |
|------|---------|
| `HsmComparisonSanitizer` | Sanitizes HSM `.cs` files: parses the layout method body for per-element comments (keyed by `stableId` for states/regions and `visualId` for transitions), hoists comments above the matching builder calls, and truncates the layout method. |
| `HsmComparisonToolbar` | Per-window wrapper that delegates to the shared `ComparisonToolbarAction`. |
| `HsmEditorComparisonServiceCollectionExtensions` | `AddHsmEditorComparison()` DI extension; registers `HsmComparisonSanitizer` and wires it into `SanitizerRegistry`. |

Call `AddHsmEditorComparison()` after `AddSharedAiEditor()` in the composition root.

See [Hrot.Editor.AiShared.Comparison.md](../Editor/Hrot.Editor.AiShared.Comparison.md) for the
full comparison feature architecture.

---

## Dependencies

### ProjectReferences

| Project | Path | Role |
|---------|------|------|
| `Fhsm.Kernel` | `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/` | HSM runtime kernel. Provides `HsmDefinitionBlob`, `MachineMetadata`, `HsmBuilder`, `StateDef`, `TransitionDef`, `RegionDef`, `GlobalTransitionDef`, `StateFlags`, `TransitionFlags`, `EventFlags`, `CommandLane`, and the attribute types `HsmDefinitionAttribute`, `HsmLayoutAttribute`, `HsmActionAttribute`. CycloneDDS code-gen is disabled (`CycloneDdsDisableCodeGen=true`). |
| `NodeEditor.Core` | `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/` | Graph canvas framework. Provides `IGraphModel`, `INodeModel`, `IContainerNodeModel`, `ILinkModel`, `IPinModel`, `IEditorHostServices`, `IGraphCommandSink`, `ILinkValidator`, `INodeCatalog`, `ITypeSystem`, `ICustomCanvasRenderer`, `GraphCommand`, `NodeId`, `LinkId`, `PinId`, `TypeKey`, etc. |
| `Hrot.Editor.AiShared` | `Hrot/Editor/Hrot.Editor.AiShared/` | Shared authoring infrastructure. Provides `IAssetCatalogContributor`, `IEditableAsset`, `AssetKind`, `AssetIdHasher`, `LayoutDiscovery`, `IFluentCSharpEmitter<T>`, `FluentCSharpEmitterBase`, `AiDebugSessionBase`, `IAiDebugSession`, `HotReloadClassifier`, `HotReloadTier`, `IAssetValidator`, `AssetDiagnostic`, `ITraceLaneProvider`, `TraceLaneDescriptor`, `IRuntimeInspectorPane`, `IAssetSubSelection`, `IRefactorService`, `FindResultsWindow`, and layout types `HsmEditorLayout`, `HsmEditorLayoutBuilder`. |

### NuGet packages (inherited via Directory.Build.props)

The project itself declares no direct NuGet package references. It consumes
`ImGuiNET` (via `Fdp.Presentation` transitive reference) for canvas rendering
and `StructEdit.Core` (via `AiShared`) for inspector annotations.

### InternalsVisibleTo

```xml
<InternalsVisibleTo Include="Hrot.Hsm.Editor.Tests" />
```

`HsmCommandSink`, `HsmNodeCatalog`, `HsmTypeSystem`, `HsmLinkValidator`,
`HsmPinModel`, `HsmTransitionLink`, and `HsmAssetProjector` are internal but
accessible to the test project.

---

## Usage Examples

### Example 1: Loading an assembly and projecting an HSM asset

```csharp
// After loading or reloading the behavior assembly:
var contributor = new HsmAssetContributor();
contributor.ContributorChanged += () =>
{
    foreach (var asset in contributor.Enumerate())
    {
        var hsmAsset = (HsmAsset)asset;
        Console.WriteLine($"Loaded: {hsmAsset.Name} ({hsmAsset.AllStates.Count} states)");
    }
};

contributor.LoadFrom(Assembly.LoadFrom("MyAiBehaviors.dll"));
```

### Example 2: Validating an HSM asset and rendering diagnostics

```csharp
var validator = new HsmValidator();
var diagnostics = validator.Validate(hsmAsset);

foreach (var d in diagnostics)
{
    string stateInfo = "";
    if (d.TargetStableIds.Count > 0)
    {
        var state = hsmAsset.FindStateByStableId(d.TargetStableIds[0]);
        stateInfo = state != null ? $" [{state.Name}]" : "";
    }
    Console.WriteLine($"[{d.Severity}] {d.Code}: {d.Message}{stateInfo}");
}

// Feed diagnostics into the canvas renderer for visual conflict markers:
conflictsRenderer.SetDiagnostics(diagnostics);
```

### Example 3: Emitting a modified HSM asset back to C# source

```csharp
// After the user edits state actions via the inspector:
var state = hsmAsset.FindStateByStableId(selectedStateId)!;
state.OnEntryAction = "Hrot.AI.Behaviors.Actions.EnterPatrol";
hsmAsset.MarkDirty(); // normally called by HsmCommandSink

// Serialize and write to disk:
var emitter = new HsmFluentEmitter();
string sourceCode = emitter.Emit(hsmAsset);
File.WriteAllText(hsmAsset.SourceFilePath, sourceCode, System.Text.Encoding.UTF8);

// The produced file contains three methods:
//   CreateBuilder()   - fluent HsmBuilder chain
//   Compile()         - [HsmDefinition] thunk
//   Layout()          - [HsmLayout] canvas positions
```

### Example 4: Attaching a debug session and activating heatmap mode

```csharp
var session = new HsmDebugSession();
hostServices.SetDebugSession(session);
runtimeOverlayRenderer.SetSession(session);
heatmapRenderer.SetSession(session);
heatmapRenderer.HeatmapModeActive = true;
session.HeatmapModeActive = true;

// Later, from the kernel adapter (Slice 3+):
session.RecordTrace(new HsmStateEntered(
    Self: entity,
    AssetId: hsmAsset.AssetId,
    StateStableId: someStateGuid,
    SimulationTime: 12.5f));

// Query entry counts for display:
var counts = session.GetStateEntryCounts(hsmAsset.AssetId);
if (counts is not null)
{
    foreach (var (stableId, count) in counts)
    {
        var state = hsmAsset.FindStateByStableId(stableId);
        Console.WriteLine($"{state?.Name ?? stableId.ToString()}: {count} entries");
    }
}
```

### Example 5: Classifying a hot-reload tier

```csharp
// After hot-reloading the behavior assembly:
HsmDefinitionBlob oldBlob = previousAsset.Blob;
HsmDefinitionBlob newBlob = GetNewBlob(newAssembly);

var tier = HsmQuickReloadHasher.Classify(oldBlob, newBlob);
switch (tier)
{
    case HotReloadTier.Cosmetic:
        // No changes; skip re-projection.
        break;
    case HotReloadTier.Soft:
        // Only parameter values changed; can patch in-place without resetting runtime.
        break;
    case HotReloadTier.Hard:
        // State/transition graph changed; full re-projection and runtime reset required.
        contributor.LoadFrom(newAssembly);
        break;
}
```

### Example 6: Computing output lane masks for parallel conflict detection

```csharp
// Build lane map from action attributes in all loaded assemblies:
var laneMap = HsmOutputLaneMaskInferrer.BuildLaneDictionary(
    new[] { behaviorAssembly, actionLibraryAssembly });

// Apply to asset (writes OutputLaneMask to every StateNode):
HsmOutputLaneMaskInferrer.ApplyToAsset(hsmAsset, laneMap);

// Now validate to detect OutputLaneConflict diagnostics:
var validator = new HsmValidator();
var diagnostics = validator.Validate(hsmAsset);
var conflicts = diagnostics.Where(d => d.Code == HsmDiagnosticCode.OutputLaneConflict);
```

---

## Best Practices

### Authoring

- **Assign explicit AssetId GUIDs** on `[HsmDefinition]` attributes. When no GUID is
  provided, `HsmAssetContributor` falls back to hashing the machine name via
  `AssetIdHasher.FromName()`. Name changes will mint a new ID and lose layout history.

- **Keep the Layout() method**: `HsmFluentEmitter` always emits a `[HsmLayout]` method.
  Do not delete it. Without it, `HsmAssetProjector` runs `HsmAutoLayout` on every
  reload and discards all manual canvas arrangement.

- **Avoid deep nesting**: The validator enforces a maximum state depth of 16 (kernel
  limit). Design machines to stay well below this; typically 4-6 levels is sufficient
  for HROT use cases.

- **Parallel states and lane conflicts**: Assign distinct `[HsmAction(Lane = ...)]`
  attributes to actions that belong to different parallel regions. Run the validator
  after each parallel region addition to catch `OutputLaneConflict` early.

- **Event IDs are stable contracts**: Do not renumber events without using the Rename
  refactor in `HsmEventsWindow`. All transition references use the numeric EventId
  internally, not the string name.

### Comparison with Hrot.BTree.Editor

- Both editors use the same catalog/projection/emit/hot-reload pattern from
  `AiShared`. The primary code-structure difference is that `BTree.Editor` deals
  with a tree (parent pointers, no cycles, single-child composites), while
  `Hsm.Editor` deals with a statechart (orthogonal regions, parallel states,
  explicit transition graph, global transitions).

- `BTreeCommandSink` and `HsmCommandSink` follow the same dispatch pattern. The
  per-command handler stubs are populated incrementally across implementation slices;
  the sink itself does not need to be replaced.

- Both emitters sort output collections by stable identity (StableId/VisualId GUID)
  to produce deterministic diffs. This is important for version-control workflows.
  Do not sort by display name; names can change without an ID change.

- In `BTree.Editor`, the `Blackboard` subsystem is an independent namespace with no
  HSM equivalent. In `Hsm.Editor`, the `Inspector` namespace carries more facet types
  because HSMs have more richly annotated elements (transitions carry EventId, guard,
  action, priority, sync group, and kind).

- The `HsmRuntimeOverlayRenderer` highlights the full active configuration (leaf +
  ancestors), not just the executing node. This is semantically correct for HSMs
  where a composite is simultaneously active whenever any of its descendants is
  active. `BTreeRuntimeOverlayRenderer` highlights the single executing node and its
  call-stack path, which is a different concept.

---

## Related Projects

| Project | Relationship |
|---------|-------------|
| `Hrot.Editor.AiShared` | Direct dependency. Provides all shared editor infrastructure: catalog, emit, debug session base, hot-reload classification, validation interfaces, layout discovery, and layout attribute types. |
| `Fhsm.Kernel` (FastHSM) | Direct dependency. The compiled runtime. Provides the blob binary format, `HsmBuilder` fluent API, `MachineMetadata`, and all kernel data types consumed by the editor. |
| `NodeEditor.Core` (NodeEdit) | Direct dependency. The graph canvas framework. All canvas interaction (rendering, input, commands, selection) passes through its `IEditorHostServices` / `IGraphModel` / `IGraphCommandSink` interfaces. |
| `Hrot.BTree.Editor` | Structural sibling. Shares the same architectural pattern (catalog / model / projector / emitter / host / debug / inspector / renderer / hot-reload). Compare implementations when adding new capabilities. |
| `Hrot.Hsm.Editor.Tests` | Test project (InternalsVisibleTo). Covers `HsmAssetProjector`, `HsmValidator`, `HsmFluentEmitter`, `HsmLinkValidator`, `HsmOutputLaneMaskInferrer`, and `HsmTransitionSnapHelper`. |
| `Hrot.Editor.AiShared.Layout` (within AiShared) | Provides `HsmEditorLayout` and `HsmEditorLayoutBuilder`, the layout serialization types read and written by `HsmAssetProjector` and `HsmFluentEmitter`. |
| `Hrot.Runner` / `Hrot.Engine` | Consumers of the compiled `HsmDefinitionBlob` at simulation time. The editor has no runtime dependency on these; the coupling is via the `.cs` source round-trip only. |
| `StructEdit.Core` | Transitive dependency (via AiShared). Drives the inspector panel rendering for facet structs. The picker attributes in `Hrot.Hsm.Editor.Inspector` are registered as custom StructEdit field renderers by the host application. |
