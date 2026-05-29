# Hrot.BTree.Editor

**Project file**: `Hrot/Subsystems/AI/Hrot.BTree.Editor/Hrot.BTree.Editor.csproj`
**Project folder**: `Hrot/Subsystems/AI/Hrot.BTree.Editor/`
**Target framework**: net8.0
**Date**: 2026-05-30

---

## README Validation

**Status: Missing**

No `README.md` was found anywhere inside the `Hrot.BTree.Editor` project folder.
All architectural context in this document was derived by reading source code directly.

---

## Executive Overview

`Hrot.BTree.Editor` is the visual authoring tool for Behavior Trees (BTrees) used
by HROT AI agents. It sits between two worlds:

- **Authoring side**: a NodeEditor-based canvas where designers compose Behavior
  Trees from typed nodes (composites, leaves, decorators). Changes made in the canvas
  are reflected immediately in an editor-side data model (`BehaviorTreeAsset`).

- **Runtime side**: the `Fbt.Kernel` (FastBTree) which executes trees at simulation
  time. The editor reads `BehaviorTreeBlob` structures emitted by the kernel and
  projects them back into the editable model.

### Authoring Workflow

1. The designer opens a `.cs` source file that contains `[BTreeDefinition]`-annotated
   static methods.
2. `BTreeAssetContributor` scans the assembly (on load or hot-reload), invokes each
   definition method to obtain a `BehaviorTreeBlob`, and feeds it through
   `BehaviorTreeAssetProjector` to produce a `BehaviorTreeAsset`.
3. The canvas renders the asset via `NodeEditor.Core`, backed by `BTreeNodeCatalog`,
   `BTreeTypeSystem`, `BTreeLinkValidator`, and `BTreeCommandSink`.
4. Every structural or property change is routed through `BTreeCommandSink`, which
   mutates the `BehaviorTreeAsset` and marks it dirty.
5. When the designer saves, `BTreeFluentEmitter` serializes the asset back to a C#
   source file containing three methods: `CreateBuilder()`, `Build()`, and `Layout()`.
6. The project is compiled. `BTreeAssetContributor.LoadFrom()` is called again on the
   new assembly, completing the round-trip.

### What the Editor Produces

- A C# static class with a fluent `BTreeBuilder<BB, Ctx>` call chain that defines the
  tree structure (`CreateBuilder()`).
- A `[BTreeDefinition]` thunk (`Build()`) that the Fbt kernel uses to compile the blob.
- A `[BTreeLayout]` method (`Layout()`) that stores canvas positions so the layout
  survives recompilation.

### Debug / Live Mode

When a simulation is running, the editor connects a `BTreeDebugSession` to the kernel
adapter. Visual overlays (`BTreeRuntimeOverlayRenderer`, `HeatmapOverlayRenderer`,
`SubtreeBoundaryRenderer`, `BTreeBreakpointGutterRenderer`) draw on the canvas to
show the currently executing node, call-stack ancestry, breakpoints, and execution
frequency heatmaps.

---

## Architecture

### Layer Map

The project is organized into nine sub-namespaces that form a clear dependency
hierarchy:

```
+-----------------------------------------------------------+
|                    Hrot.BTree.Editor                      |
|                                                           |
|  +----------+   +---------+   +--------+   +---------+   |
|  |  Catalog |   |  Model  |   | Layout |   |  Emit   |   |
|  +----+-----+   +----+----+   +---+----+   +----+----+   |
|       |              |            |              |        |
|  +----v-----+   +----v----+   +---v----+   +----v----+   |
|  |   Host   |<--| Asset-  |   | Auto   |   | Fluent  |   |
|  | Services |   |Projector|   |Layout  |   |Emitter  |   |
|  +----------+   +---------+   +--------+   +---------+   |
|                                                           |
|  +------------+   +-----------+   +---------+            |
|  | Validation |   |  Debug    |   |Inspector|            |
|  +------------+   +-----------+   +---------+            |
|                                                           |
|  +-----------+   +-----------+   +------------+          |
|  | Renderers |   | Blackboard|   | HotReload  |          |
|  +-----------+   +-----------+   +------------+          |
+-----------------------------------------------------------+
        |                   |                  |
        v                   v                  v
+---------------+  +------------------+  +----------+
| NodeEditor    |  | Fbt.Kernel       |  | AiShared |
| (graph canvas)|  | (BTree runtime)  |  |(shared   |
+---------------+  +------------------+  | infra)   |
                                         +----------+
```

### Data Flow: Authoring Round-Trip

```
  Assembly (compiled .cs)
        |
        | BTreeAssetContributor.LoadFrom(assembly)
        |   - scans for [BTreeDefinition] methods
        |   - invokes method -> BehaviorTreeBlob
        |   - also finds matching [BTreeLayout]
        v
  BehaviorTreeAssetProjector.Project(blob, meta, layout, ...)
        |
        | DFS walk of blob.Nodes[]
        | - decorators -> BTreeEditorPills
        | - composites/leaves -> BTreeEditorNodes
        | - apply saved layout or BTreeAutoLayout
        v
  BehaviorTreeAsset  <------  Canvas (NodeEditor.Core)
        |                          |
        | BTreeCommandSink.Apply() |  user edits
        |   - AddNode              |
        |   - AddLink (parent-child)
        |   - SetNodeProperty
        |   - AddAttachment (pill)
        |
        | dirty -> Save
        v
  BTreeFluentEmitter.Emit(asset)
        |
        | writes CreateBuilder(), Build(), Layout()
        v
  .cs source file  -->  MSBuild  -->  Assembly (next cycle)
```

### Debug Session Data Flow

```
  Fbt Kernel (simulation thread)
        |
        | kernel adapter (future Slice 3)
        |   RecordNodeExecuted()
        |   RecordAsyncEvent()
        |   RaiseBreakpointHit()
        v
  BTreeDebugSession
    - ring buffer: _nodeHistory   (max 200)
    - ring buffer: _asyncHistory  (max 200)
    - aggregate:   _aggregateCounters
    - IsPaused / PausedAt / PausedOnEntity
        |
        |  per-frame poll during canvas render
        v
  Custom Renderers (ICustomCanvasRenderer):
    +----------------------------+  pass: AfterNodes
    | BTreeRuntimeOverlayRenderer|  gold outline on running node,
    |                            |  dim gold on stack ancestry,
    |                            |  OK/X/~ glyphs on recent execs
    +----------------------------+
    +----------------------------+  pass: BeforeContent
    | HeatmapOverlayRenderer     |  blue->red fill by exec freq
    +----------------------------+
    +----------------------------+  pass: BeforeContent
    | SubtreeBoundaryRenderer    |  dashed rect around subtree AABB
    +----------------------------+
    +----------------------------+  pass: AfterNodes
    | BTreeBreakpointGutterRenderer|  red dot in node gutter
    +----------------------------+
    +----------------------------+  pass: AfterWires
    | ObserverGuardBadgeRenderer |  "OBSERVES" badge on guard links
    +----------------------------+
```

### Node Type Taxonomy

```
  NodeType (from Fbt.Kernel)
  |
  +-- Composites (have children, ordered)
  |     Sequence        bt.composite.sequence
  |     Selector        bt.composite.selector
  |     ObserverSelector bt.composite.observerSelector
  |     Parallel        bt.composite.parallel
  |     Root            bt.composite.root
  |
  +-- Leaves (no children; collapsed to single canvas node)
  |     Action          bt.leaf.action
  |     Condition       bt.leaf.condition
  |     Wait            bt.leaf.wait
  |     Subtree         bt.leaf.subtree
  |
  +-- Decorators (collapsed to pill badges on host node)
        Inverter         bt.decorator.inverter
        Repeater         bt.decorator.repeater
        Cooldown         bt.decorator.cooldown
        ForceSuccess     bt.decorator.forceSuccess
        ForceFailure     bt.decorator.forceFailure
        UntilSuccess     bt.decorator.untilSuccess
        UntilFailure     bt.decorator.untilFailure
```

### Pin Direction Convention

NodeEditor uses a "reversed" wiring convention for BTrees:

- In BTree execution, the parent ticks its children.
- In the canvas, **children own the output pin** and connect to the **parent's input pin**.
- This enables NodeEditor's standard many-to-one exec rule: many child output pins
  connect to one parent input pin.
- `BTreeLinkValidator` enforces this: leaf nodes cannot be link targets (cannot have
  child output pins connecting to them), and cycles are rejected.

### Hot Reload Classification

`BTreeQuickReloadHasher` delegates to `HotReloadClassifier` (from `AiShared`) to
classify a reload as one of three tiers by comparing the `BehaviorTreeBlob` hash
fields:

| Tier       | Condition                                  | Editor Action                  |
|------------|--------------------------------------------|--------------------------------|
| `Cosmetic` | Both hashes same                           | No-op                          |
| `Soft`     | StructureHash same, ParamHash differs      | Update param values only       |
| `Hard`     | StructureHash differs                      | Full re-projection             |

---

## Source Structure

All 34 source files span nine sub-namespaces under `Hrot.BTree.Editor`.

### Hrot.BTree.Editor.Model

Core editor-side data model. Mutable; consumed by all other layers.

| File | Class / Type | Purpose |
|------|-------------|---------|
| `BehaviorTreeAsset.cs` | `BehaviorTreeAsset` | Top-level editor model for one BTree asset. Owns node + pill lists, lookup tables, canvas state, and `BehaviorTreeBlob`. Implements `IEditableAsset`, `IBlackboardManagedAsset`, and `IBTreeSyncableAsset`. |
| `BehaviorTreeAsset.cs` | `BTreeEditorNode` | Mutable editor node. Holds `NodeType`, canvas `Position`, `DisplayLabel`, `Comment`, `ChildVisualIds`, typed payloads, `IsBreakpoint`. |
| `BehaviorTreeAsset.cs` | `BTreeEditorPill` | Decorator collapsed into an attachment badge on the host node. Holds `DecoratorType`, `IntParam`, `FloatParam`, `StackIndex`. |
| `BehaviorTreeAsset.cs` | `BTreeActionPayload` | Payload for Action leaves: `MethodFqn`, `ExpressionTargetField`, `DelegateShape`. |
| `BehaviorTreeAsset.cs` | `BTreeConditionPayload` | Payload for Condition leaves: same shape as Action. |
| `BehaviorTreeAsset.cs` | `BTreeWaitPayload` | Payload for Wait leaves: `Duration` in seconds. |
| `BehaviorTreeAsset.cs` | `BTreeSubtreePayload` | Payload for Subtree leaves: `SubtreeAssetId`, `SubtreeName`, `IsResolved`. |
| `BehaviorTreeAsset.cs` | `BTreeActionDelegateShape` | Enum: `ThreeParamReusable` or `FourParamFull`. |
| `BehaviorTreeAssetProjector.cs` | `BehaviorTreeAssetProjector` | Internal static class. DFS walk of `BehaviorTreeBlob.Nodes[]`. Maps decorators to pills, non-decorators to nodes. Resolves `NodeDebugMetadata` for visual IDs and labels. Calls `BTreeAutoLayout` when no saved layout exists. |
| `BTreeSubtreeResolver.cs` | `BTreeSubtreeResolver` | Static. Walks all Subtree nodes and resolves `SubtreeName` against `IAssetCatalog`. Updates `IsResolved` / `SubtreeAssetId` in-place. |

### Hrot.BTree.Editor.Host

Adapters that bridge the editor model to `NodeEditor.Core` interfaces.

| File | Class / Type | Purpose |
|------|-------------|---------|
| `BTreeEditorHostServices.cs` | `BTreeEditorHostServices` | Implements `IEditorHostServices`. Aggregates all NodeEditor service interfaces: catalog, type system, link validator, command sink, pickers, clipboard, icons, diagnostics, debug session, input, theme, custom renderers. Supports runtime attachment/detachment of the debug session and viewport-reset signaling. |
| `BTreeNodeCatalog.cs` | `BTreeNodeCatalog` | Implements `INodeCatalog`. Provides static palette entries for all 16 node kinds (5 composites, 4 leaves, 7 decorator pills). Composites have both input and output exec pins; leaves have output only; decorators have no pins (palette action: AttachToSelected). |
| `BTreeTypeSystem.cs` | `BTreeTypeSystem` | Implements `ITypeSystem`. BTree has a single edge type (`bt.exec`). All pins are exec-typed. Pins are white triangles. No data-flow types. |
| `BTreeLinkValidator.cs` | `BTreeLinkValidator` | Implements `ILinkValidator`. Enforces: leaf nodes cannot receive child connections; duplicate parent edge is forbidden; cycles are detected by walking the ancestor chain. |
| `BTreeCommandSink.cs` | `BTreeCommandSink` | Implements `IGraphCommandSink`. Translates `GraphCommand` discriminated union records (MoveNodes, AddNode, RemoveNodes, AddLink, RemoveLinks, SetNodeProperty, AddAttachment, RemoveAttachments, SetAttachmentProperty, ReorderAttachments, Batch) into mutations on `BehaviorTreeAsset`. |
| `BTreeKinds.cs` | `BTreeKinds` | Static string constants for all 16 node kind IDs. Helper: `IsLeaf(NodeKindKey)`, `IsDecorator(NodeKindKey)`, `KindIdToNodeType(string)`. |
| `BTreeTraceLaneProvider.cs` | `BTreeTraceLaneProvider` | Implements `ITraceLaneProvider`. Declares four timeline swim-lanes: `bt.nodes` (NodeStatus), `bt.stack` (Stack), `bt.async` (Async), `bt.errors` (Errors). |

### Hrot.BTree.Editor.Validation

Structural correctness checking. Independent of NodeEditor.

| File | Class / Type | Purpose |
|------|-------------|---------|
| `BTreeValidator.cs` | `BTreeValidator` | Main validator. Runs seven rule groups: empty composites, unbound leaf methods, invalid pill params, max static depth (8), cycle detection, orphaned nodes. Returns `IReadOnlyList<BTreeDiagnostic>`. |
| `BTreeDiagnostic.cs` | `BTreeDiagnostic` | Immutable record: `VisualId`, `Severity`, `Code`, `Message`. `Guid.Empty` VisualId = tree-level issue. |
| `BTreeDiagnostic.cs` | `BTreeDiagnosticSeverity` | Enum: `Info`, `Warning`, `Error`. |
| `BTreeDiagnostic.cs` | `BTreeDiagnosticCode` | Enum: 12 diagnostic codes including `EmptyComposite`, `UnboundActionMethod`, `CycleDetected`, `OrphanedNode`, etc. |
| `BTreeAssetValidator.cs` | `BTreeAssetValidator` | Adapts `BTreeValidator` to `IAssetValidator` (from AiShared). Maps `BTreeDiagnostic` to `AssetDiagnostic` for the shared DiagnosticsWindow. |

### Hrot.BTree.Editor.Emit

C# source code generation.

| File | Class / Type | Purpose |
|------|-------------|---------|
| `BTreeFluentEmitter.cs` | `BTreeFluentEmitter` | Implements `IFluentCSharpEmitter<BehaviorTreeAsset>`. Generates a static C# class with `CreateBuilder()` (fluent `BTreeBuilder<BB,Ctx>` chain), `Build()` (`[BTreeDefinition]` thunk), and `Layout()` (`[BTreeLayout]` canvas positions). Collects and sorts `using` directives from blackboard/context type names and action/condition FQNs. |

### Hrot.BTree.Editor.Debug

Runtime debug session and event types.

| File | Class / Type | Purpose |
|------|-------------|---------|
| `IBTreeDebugSession.cs` | `IBTreeDebugSession` | Extends `IAiDebugSession`. Adds: `GetCurrentStateSnapshot()`, `GetRecentNodeHistory(int)`, `GetRecentAsyncHistory(int)`, `HeatmapModeActive`, `GetAggregateCounters(Guid)`, `ResetAggregateCounters()`, and five events (`OnBreakpointHit`, `OnNodeExecuted`, `OnAsyncIssued`, `OnAsyncResolved`, `OnAsyncAborted`). |
| `BTreeDebugSession.cs` | `BTreeDebugSession` | Production implementation of `IBTreeDebugSession`. Extends `AiDebugSessionBase`. Maintains two ring buffers (max 200 entries each): `_nodeHistory` and `_asyncHistory`. Tracks per-node aggregate counters when `HeatmapModeActive`. Exposes `RecordNodeExecuted()`, `RecordAsyncEvent()`, `RaiseBreakpointHit()` for the future kernel adapter. Step controls are no-ops until kernel wiring (Slice 3+). |
| `BTreeBreakpointMenuPopulator.cs` | `BTreeBreakpointMenuPopulator` | Static helper that populates the right-click context menu for a BTree canvas node with Universal Breakpoints items. Called by the canvas right-click handler; synthesises `TraceBufferScanPredicateDto` and `CompoundPredicateDto` conditions and registers them via `IDataBreakpointManager.AddBreakpoint`. Menu items: "Break on Activation (Enter)" (NodeEvaluated + Running status), "Break on Completion (Exit)" (Success OR Failure compound), "Break on Abort", "Add Conditional Data Breakpoint..." (opens predicate editor). The `SourceElementId` of each registered breakpoint is set to `node.VisualId` so `BTreeBreakpointGutterRenderer` can draw the gutter dot without querying the Slice 1 session. |
| `BTreeDebugTypes.cs` | `BehaviorTreeStateSnapshot` | Immutable record: running node index + VisualId, stack pointer, node-index stack, VisualId stack, local registers, async handles, tree version. |
| `BTreeDebugTypes.cs` | `BTreeNodeExecuted` | Immutable record: entity, asset ID, node VisualId, `NodeStatus`, sim time, tick. |
| `BTreeDebugTypes.cs` | `BTreeAsyncEvent` | Immutable record: entity, asset ID, node VisualId, request ID, tree version, `BTreeAsyncPhase`, sim time. |
| `BTreeDebugTypes.cs` | `BTreeBreakpointHit` | Immutable record: `Breakpoint`, entity, optional `NodeStatus` at hit, sim time. |
| `BTreeDebugTypes.cs` | `BTreeAsyncPhase` | Enum: `Issued`, `Resolved`, `Aborted`. |

### Hrot.BTree.Editor.Blackboard

Blackboard schema reflection and display.

| File | Class / Type | Purpose |
|------|-------------|---------|
| `BlackboardField.cs` | `BlackboardField` | Immutable record: `Name`, `FieldType`, `Kind`. |
| `BlackboardFieldKind.cs` | `BlackboardFieldKind` | Enum: `Bool`, `Numeric`, `Vector`, `Enum`, `Struct`, `Other`. |
| `BlackboardSchema.cs` | `BlackboardSchema` | Holds `StructType` and `IReadOnlyList<BlackboardField>`. |
| `BlackboardSchemaBuilder.cs` | `BlackboardSchemaBuilder` | Static. Reflects public instance fields of a struct type via `BindingFlags.Public | BindingFlags.Instance`. Classifies each field into `BlackboardFieldKind`. |
| `LiveBlackboardPanel.cs` | `LiveBlackboardPanel` | ImGui panel. Three-column table (Field / Type / Value). Reads live values from `IBTreeDebugSession.GetCurrentStateSnapshot()` when a session is attached; shows "--" placeholder until Slice 3 wires actual values. |

### Hrot.BTree.Editor.Catalog

Assembly scanning and asset registration.

| File | Class / Type | Purpose |
|------|-------------|---------|
| `BTreeAssetContributor.cs` | `BTreeAssetContributor` | Implements `IAssetCatalogContributor`. `LoadFrom(Assembly)` scans all types for `[BTreeDefinition]` static parameterless methods returning `BehaviorTreeBlob`. For each: derives `AssetId` via `AssetIdHasher.FromName()`, finds an optional matching `[BTreeLayout]`, projects through `BehaviorTreeAssetProjector`. Fires `ContributorChanged` after each load. |

### Hrot.BTree.Editor.HotReload

| File | Class / Type | Purpose |
|------|-------------|---------|
| `BTreeQuickReloadHasher.cs` | `BTreeQuickReloadHasher` | Static. Delegates to `HotReloadClassifier.Classify(structureHash, nextStructureHash, paramHash, nextParamHash)` to return a `HotReloadTier`. |

### Hrot.BTree.Editor.Layout

Automatic canvas layout.

| File | Class / Type | Purpose |
|------|-------------|---------|
| `BTreeAutoLayout.cs` | `BTreeAutoLayout` | Static. Reingold-Tilford tidy-tree algorithm. Root at (0, 0), children grow downward. Constants: horizontal sibling gap = 40 px, vertical gap = 80 px (+ 23 px per pill row on child), node width = 160 px. Two-pass: bottom-up `FirstPass` computes preliminary X + modifier; top-down `SecondPass` finalizes absolute positions. Writes `BTreeEditorNode.Position` in-place. Only reachable nodes are positioned. |

### Hrot.BTree.Editor.Inspector

StructEdit facets and runtime inspector pane.

| File | Class / Type | Purpose |
|------|-------------|---------|
| `BTreeFacets.cs` | `BTreeActionFacet` | StructEdit struct for Action nodes: `MethodFqn` (with `[BehaviorHashPicker]`), `ExpressionTargetField` (with `[BlackboardFieldPicker]`), `Comment`, `IsBreakpoint`, read-only `VisualId` / `LastResult` / `TickCount`. |
| `BTreeFacets.cs` | `BTreeConditionFacet` | Same shape as `BTreeActionFacet`. |
| `BTreeFacets.cs` | `BTreeWaitFacet` | `Duration` (`[EditRange(0,600)]`, `[EditUnit("seconds")]`), `Comment`, `IsBreakpoint`, `VisualId`. |
| `BTreeFacets.cs` | `BTreeSequenceFacet` | `Comment`, `IsBreakpoint`, read-only `VisualId` + `ChildCount`. |
| `BTreeFacets.cs` | `BTreeSelectorFacet` | Same as `BTreeSequenceFacet`. |
| `BTreeFacets.cs` | `BTreeObserverSelectorFacet` | Same as `BTreeSequenceFacet`. |
| `BTreeFacets.cs` | `BTreeRepeaterFacet` | `Count` (`[EditRange(1,9999)]`), `Comment`, `VisualId`. |
| `BTreeFacets.cs` | `BTreeCooldownFacet` | `Duration` (`[EditUnit("seconds")]`), `Comment`, `VisualId`. |
| `BTreeRuntimeInspectorPane.cs` | `BTreeRuntimeInspectorPane` | Implements `IRuntimeInspectorPane`. Displays `BehaviorTreeStateSnapshot` data: running node, stack depth, tree version, stack frame labels, local registers, async handles. |
| `BlackboardFieldPickerAttribute.cs` | `BlackboardFieldPickerAttribute` | Marker attribute for StructEdit: renders field as a blackboard field picker dropdown. |
| `BehaviorHashPickerAttribute.cs` | `BehaviorHashPickerAttribute` | Marker attribute for StructEdit: renders field as a behavior method picker populated from the `BehaviorRegistry`. |

### Hrot.BTree.Editor.Renderers

Custom `ICustomCanvasRenderer` implementations drawn by the NodeEditor canvas.

| File | Class / Type | Render Pass | Purpose |
|------|-------------|-------------|---------|
| `BTreeRuntimeOverlayRenderer.cs` | `BTreeRuntimeOverlayRenderer` | `AfterNodes` | Gold outline on the running node; dim gold on stack ancestry (brightness proportional to depth); OK/X/~ status glyphs on recently executed nodes (last 50). |
| `HeatmapOverlayRenderer.cs` | `HeatmapOverlayRenderer` | `BeforeContent` | Fills node backgrounds blue->green->yellow->red according to normalized aggregate execution frequency. Active only when `HeatmapModeActive` is true and a session is attached. |
| `SubtreeBoundaryRenderer.cs` | `SubtreeBoundaryRenderer` | `BeforeContent` | Draws a dashed blue rectangle around the AABB of the subtree root when the stack pointer is > 0 (paused inside a subtree). |
| `BTreeBreakpointGutterRenderer.cs` | `BTreeBreakpointGutterRenderer` | `AfterNodes` | Draws a red filled circle in the top-left gutter of each node that has an active, enabled breakpoint. |
| `ObserverGuardBadgeRenderer.cs` | `ObserverGuardBadgeRenderer` | `AfterWires` | Draws "OBSERVES" badges at 30% along links from `ObserverSelector` to their `Condition` guard children. Hidden at low zoom. |

---

## Public API Reference

### BehaviorTreeAsset (Model)

```csharp
public sealed class BehaviorTreeAsset : IEditableAsset, IBlackboardManagedAsset, IBTreeSyncableAsset
{
    // IEditableAsset
    public Guid   AssetId       { get; }
    public string Name          { get; set; }
    public AssetKind Kind       { get; }             // AssetKind.BTree
    public string SourceFilePath{ get; }
    public bool   IsDirty       { get; }
    public bool   IsEditorOwned { get; }
    public event Action? Changed;

    // Kernel data
    public string BlackboardTypeName { get; }
    public string ContextTypeName    { get; }
    public string TargetNamespace    { get; set; }
    public BehaviorTreeBlob Blob     { get; }

    // Collections
    public IReadOnlyList<BTreeEditorNode> Nodes { get; }
    public IReadOnlyList<BTreeEditorPill> Pills { get; }

    // Canvas state
    public Vector2 CanvasPanOffset { get; set; }
    public float   CanvasZoomLevel { get; set; }

    // Blackboard (IBlackboardManagedAsset)
    public string BlackboardTypeName { get; }
    public bool   IsBlackboardEditorManaged { get; }
    public IReadOnlyList<BlackboardVariableEntry> BlackboardVariables { get; }
    public BlackboardLoadState LoadState { get; }
    public string? LoadDiagnosticMessage { get; }
    // ... AddVariable, RemoveVariable, RenameVariable, GetAliasesFor, etc.

    // Subtree sync (IBTreeSyncableAsset)
    public SubtreeNodeInfo?                   GetSubtreeNodeInfo(Guid nodeVisualId);
    public IReadOnlyList<SubtreeSyncBinding>  GetSyncBindings(Guid nodeVisualId);
    public void SetSyncBinding(Guid nodeVisualId, SubtreeSyncBinding binding);
    public void ClearSyncBindings(Guid nodeVisualId);
    public IReadOnlyList<BlackboardVariableEntry> GetVariablesOfType(string typeName);

    // Lookup
    public BTreeEditorNode? FindNode(Guid visualId);
    public int              FindBlobIndex(Guid visualId);
    public BTreeEditorPill? FindPill(Guid visualId);

    // Mutation
    public void MarkDirty();
    public void ClearDirty();
}
```

### BTreeEditorNode (Model)

```csharp
public sealed class BTreeEditorNode
{
    public Guid     VisualId;
    public NodeType KernelType;
    public int      KernelBlobIndex;
    public Vector2  Position;
    public string   DisplayLabel;
    public string?  Comment;
    public List<Guid> ChildVisualIds;

    public BTreeActionPayload?    Action;
    public BTreeConditionPayload? Condition;
    public BTreeWaitPayload?      Wait;
    public BTreeSubtreePayload?   Subtree;

    public bool IsBreakpoint;
    public bool IsLeaf      { get; }   // Action | Condition | Wait | Subtree
    public bool IsDecorator { get; }   // Inverter | Repeater | Cooldown | ...
}
```

### BTreeEditorPill (Model)

```csharp
public sealed class BTreeEditorPill
{
    public Guid     VisualId;
    public Guid     HostNodeVisualId;
    public NodeType DecoratorType;
    public int?     IntParam;
    public float?   FloatParam;
    public string?  Comment;
    public int      StackIndex;   // 0 = top of stack
}
```

### BTreeValidator (Validation)

```csharp
public sealed class BTreeValidator
{
    public IReadOnlyList<BTreeDiagnostic> Validate(BehaviorTreeAsset asset);
}
```

**Diagnostic codes emitted:**

| Code | Severity | Condition |
|------|----------|-----------|
| `EmptyComposite` | Warning | Sequence / Selector / ObserverSelector with 0 children |
| `UnboundActionMethod` | Error | Action node with empty `MethodFqn` |
| `UnboundConditionMethod` | Error | Condition node with empty `MethodFqn` |
| `WaitDurationInvalid` | Warning | Wait `Duration` <= 0 |
| `UnresolvedSubtree` | Error | Subtree `IsResolved == false` |
| `RepeaterCountInvalid` | Warning | Repeater `IntParam` <= 0 |
| `StackDepthExceeded` | Warning | Static tree depth > 8 |
| `CycleDetected` | Error | DFS finds a back-edge |
| `OrphanedNode` | Warning | Node not reachable from Root |

**Deferred to Slice 2:** `BlackboardFieldMissing`, `MethodSignatureMismatch`, `DanglingReferenceAfterReload`.

### BTreeAssetValidator (Validation)

```csharp
public sealed class BTreeAssetValidator : IAssetValidator
{
    public BTreeAssetValidator(BTreeValidator inner);
    public AssetKind SupportedKind { get; }   // AssetKind.BTree
    public IReadOnlyList<AssetDiagnostic> Validate(IEditableAsset asset);
}
```

### BTreeFluentEmitter (Emit)

```csharp
public sealed class BTreeFluentEmitter : IFluentCSharpEmitter<BehaviorTreeAsset>
{
    public string Emit(BehaviorTreeAsset asset);
}
```

Produces a complete `.cs` source file as a string. The caller is responsible for
writing it to disk.

### BTreeNodeCatalog (Host)

```csharp
public sealed class BTreeNodeCatalog : INodeCatalog
{
    public BTreeNodeCatalog();
    // INodeCatalog members supplied to NodeEditor (GetEntry, Enumerate, etc.)
}
```

Registers 16 palette entries: 5 composites, 4 leaves, 7 decorator pills.

### BTreeTypeSystem (Host)

```csharp
public sealed class BTreeTypeSystem : ITypeSystem
{
    public static readonly TypeKey ExecKey;  // "bt.exec"
    public bool        TryGetTypeInfo(TypeKey key, out TypeDisplayInfo info);
    public Vector4     GetPinColor(TypeKey key);    // white
    public PinShape    GetPinShape(TypeKey key, ContainerKind container); // Triangle
    public IPinDefaultValueEditor? GetDefaultEditor(TypeKey key);   // null
    public bool        AreCompatible(TypeKey from, TypeKey to);
    public bool        IsImplicitCast(TypeKey from, TypeKey to);    // false
}
```

### BTreeLinkValidator (Host)

```csharp
public sealed class BTreeLinkValidator : ILinkValidator
{
    public BTreeLinkValidator(IGraphModel graph);
    public LinkValidationResult Validate(PinId from, PinId to);
}
```

### BTreeEditorHostServices (Host)

```csharp
internal sealed class BTreeEditorHostServices : IEditorHostServices
{
    public BTreeEditorHostServices(
        BTreeNodeCatalog nodeCatalog,
        BTreeTypeSystem typeSystem,
        BTreeLinkValidator linkValidator,
        BTreeCommandSink commandSink,
        IPickerRegistry pickers,
        IClipboard clipboard,
        IIconProvider icons,
        IDiagnosticsSink? diagnostics,
        IInputSource input,
        IEditorTheme theme,
        IDebugSession? debug = null,
        IReadOnlyList<ICustomCanvasRenderer>? customRenderers = null);

    public void SetDebugSession(IDebugSession? session);
    public void RequestViewportReset();
    public bool ViewportResetPending { get; }
    public bool ConsumeViewportReset();
}
```

### BTreeKinds (Host)

```csharp
internal static class BTreeKinds
{
    // 16 string constants, e.g.:
    public const string Sequence         = "bt.composite.sequence";
    public const string Action           = "bt.leaf.action";
    public const string Inverter         = "bt.decorator.inverter";
    // ...

    public static bool      IsLeaf(NodeKindKey key);
    public static bool      IsDecorator(NodeKindKey key);
    public static NodeType  KindIdToNodeType(string kindId);
}
```

### BTreeCommandSink (Host)

```csharp
internal sealed class BTreeCommandSink : IGraphCommandSink
{
    internal BTreeCommandSink(BehaviorTreeAsset asset, IGraphModel graph);
    public GraphCommandResult Apply(GraphCommand command);
}
```

Handles: `MoveNodes`, `AddNode`, `RemoveNodes`, `AddLink`, `RemoveLinks`,
`SetNodeProperty`, `AddAttachment`, `RemoveAttachments`, `SetAttachmentProperty`,
`ReorderAttachments`, `Batch`.

### BTreeTraceLaneProvider (Host)

```csharp
public sealed class BTreeTraceLaneProvider : ITraceLaneProvider
{
    public AssetKind Kind { get; }   // AssetKind.BTree
    public IReadOnlyList<TraceLaneDescriptor> Lanes { get; }
    // bt.nodes, bt.stack, bt.async, bt.errors
}
```

### IBTreeDebugSession (Debug)

```csharp
public interface IBTreeDebugSession : IAiDebugSession
{
    BehaviorTreeStateSnapshot? GetCurrentStateSnapshot();
    IReadOnlyList<BTreeNodeExecuted> GetRecentNodeHistory(int max = 100);
    IReadOnlyList<BTreeAsyncEvent>   GetRecentAsyncHistory(int max = 100);
    bool HeatmapModeActive { get; set; }
    IReadOnlyDictionary<Guid, int>? GetAggregateCounters(Guid assetId);
    void ResetAggregateCounters();

    event Action<BTreeBreakpointHit>? OnBreakpointHit;
    event Action<BTreeNodeExecuted>?  OnNodeExecuted;
    event Action<BTreeAsyncEvent>?    OnAsyncIssued;
    event Action<BTreeAsyncEvent>?    OnAsyncResolved;
    event Action<BTreeAsyncEvent>?    OnAsyncAborted;
}
```

### BTreeDebugSession (Debug)

```csharp
public sealed class BTreeDebugSession : AiDebugSessionBase, IBTreeDebugSession
{
    // Entry points called by the kernel adapter:
    public void RecordNodeExecuted(BTreeNodeExecuted record);
    public void RecordAsyncEvent(BTreeAsyncEvent record);
    public void RaiseBreakpointHit(BTreeBreakpointHit hit);
    // All IAiDebugSession step methods are no-ops until Slice 3.
}
```

### BTreeAutoLayout (Layout)

```csharp
public static class BTreeAutoLayout
{
    // Constants:
    //   HorizontalSpacing = 40f
    //   VerticalSpacing   = 80f  (+ 23f per pill row on child)
    //   NodeWidth         = 160f
    //   PillRowHeight     = 23f
    //   NodeHeaderHeight  = 24f

    public static void Layout(BehaviorTreeAsset asset);
}
```

### BlackboardSchemaBuilder (Blackboard)

```csharp
public static class BlackboardSchemaBuilder
{
    public static BlackboardSchema Build(Type structType);
}
```

### BTreeAssetContributor (Catalog)

```csharp
public sealed class BTreeAssetContributor : IAssetCatalogContributor
{
    public AssetKind Kind { get; }           // AssetKind.BTree
    public event Action? ContributorChanged;
    public IReadOnlyList<IEditableAsset> Enumerate();
    public void LoadFrom(Assembly assembly);
}
```

### BTreeSubtreeResolver (Model)

```csharp
public static class BTreeSubtreeResolver
{
    public static void Resolve(BehaviorTreeAsset asset, IAssetCatalog catalog);
}
```

### BTreeQuickReloadHasher (HotReload)

```csharp
public static class BTreeQuickReloadHasher
{
    public static HotReloadTier Classify(BehaviorTreeBlob previous, BehaviorTreeBlob next);
}
```

### Inspector Attributes

```csharp
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class BlackboardFieldPickerAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class BehaviorHashPickerAttribute : Attribute { }
```

---

## Visual Asset Comparison

`Hrot.BTree.Editor` participates in the Visual Asset Comparison feature via three files
under `Comparison/`:

| File | Purpose |
|------|---------|
| `BTreeComparisonSanitizer` | Sanitizes BTree `.cs` files: parses the layout method body, hoists per-node comments and sync-binding annotations into the builder chain, truncates the layout method, and humanizes cross-asset GUID references. |
| `BTreeComparisonToolbar` | Per-window wrapper that delegates to the shared `ComparisonToolbarAction`; renders "Compare with...", "Paste LLM Response...", and "Exit Comparison" toolbar buttons. |
| `BTreeEditorComparisonServiceCollectionExtensions` | `AddBTreeEditorComparison()` DI extension; registers `BTreeComparisonSanitizer` as a singleton and wires it into `SanitizerRegistry`. |

Call `AddBTreeEditorComparison()` after `AddSharedAiEditor()` in the composition root.

See [Hrot.Editor.AiShared.Comparison.md](../Editor/Hrot.Editor.AiShared.Comparison.md) for the
full comparison feature architecture.

---

## Dependencies

### ProjectReferences

| Project | Location | Role |
|---------|----------|------|
| `Fbt.Kernel` | `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/` | BTree runtime: `BehaviorTreeBlob`, `NodeType`, `NodeStatus`, `BTreeBuilder<BB,Ctx>`, `[BTreeDefinition]`, `[BTreeLayout]`, `NodeDebugMetadata` |
| `NodeEditor.Core` | `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/` | Graph canvas: `IEditorHostServices`, `INodeCatalog`, `ITypeSystem`, `ILinkValidator`, `IGraphCommandSink`, `ICustomCanvasRenderer`, `GraphCommand`, `PinId`, `NodeId` |
| `Hrot.Editor.AiShared` | `Hrot/Editor/Hrot.Editor.AiShared/` | Shared AI editor infrastructure: `IEditableAsset`, `AssetKind`, `IAssetCatalog`, `IAssetCatalogContributor`, `IAiDebugSession`, `AiDebugSessionBase`, `ITraceLaneProvider`, `IAssetValidator`, `AssetDiagnostic`, `IFluentCSharpEmitter<T>`, `FluentCSharpEmitterBase`, `HotReloadClassifier`, `HotReloadTier`, `AssetIdHasher`, `LayoutDiscovery`, `IRuntimeInspectorPane` |
| `Fdp.Core` | `FDP/Engine/Fdp.Core/` | FDP framework: `Entity`, `Breakpoint` |

### NuGet Packages

No direct NuGet package references are declared in the `.csproj`. All runtime and
UI dependencies are provided transitively through the `ProjectReference` chain.
ImGui.NET is used in `Renderers/`, `Inspector/`, and `Blackboard/` but is referenced
transitively via `NodeEditor.Core` or `Hrot.Editor.AiShared`.

### Build Properties

| Property | Value | Meaning |
|----------|-------|---------|
| `TargetFramework` | `net8.0` | .NET 8 |
| `ImplicitUsings` | `enable` | Global usings active |
| `Nullable` | `enable` | Nullable reference types enforced |
| `TreatWarningsAsErrors` | `true` | Warnings are errors |
| `CycloneDdsDisableCodeGen` | `true` | DDS topic codegen disabled (editor does not publish DDS topics) |

### InternalsVisibleTo

`Hrot.BTree.Editor.Tests` has access to `internal` members. This is used by unit tests
that exercise `BTreeCommandSink`, `BehaviorTreeAssetProjector`, and related internals.

---

## Usage Examples

### Example 1: Loading a BTree Asset from an Assembly

```csharp
// After (re)compiling the user's game assembly, reload all BTree assets.
var contributor = new BTreeAssetContributor();
contributor.ContributorChanged += () =>
{
    foreach (var asset in contributor.Enumerate())
    {
        Console.WriteLine($"Loaded BTree: {asset.Name} ({asset.AssetId})");
    }
};

contributor.LoadFrom(typeof(MyCombatBehavior).Assembly);
// ContributorChanged fires synchronously; asset list is immediately available.
```

### Example 2: Validating a BehaviorTreeAsset and Displaying Diagnostics

```csharp
// Validate a BTree asset after authoring changes.
var validator     = new BTreeValidator();
var assetValidator = new BTreeAssetValidator(validator);

var diagnostics = assetValidator.Validate(btAsset);
foreach (var d in diagnostics)
{
    string icon = d.Severity switch
    {
        AssetDiagnosticSeverity.Error   => "[E]",
        AssetDiagnosticSeverity.Warning => "[W]",
        _                               => "[I]",
    };
    Console.WriteLine($"{icon} {d.Code}: {d.Message}");
}

// Example output:
// [E] UnboundActionMethod: Action node has no bound method.
// [W] EmptyComposite: Sequence has no children.
```

### Example 3: Emitting a BTree Asset to C# Source

```csharp
// Serialize an edited BehaviorTreeAsset back to compilable C# source.
var emitter = new BTreeFluentEmitter();
string sourceCode = emitter.Emit(btAsset);

// Write to disk. The path comes from the asset's SourceFilePath.
File.WriteAllText(btAsset.SourceFilePath, sourceCode, Encoding.UTF8);
btAsset.ClearDirty();

// The emitted file will look similar to:
//
//   // Auto-generated by Hrot.BTree.Editor <guid>
//   using System;
//   using Fbt;
//   using Fbt.Compiler;
//   ...
//   namespace Hrot.AI.Behaviors.Trees;
//
//   public static class PatrolAndAttack
//   {
//       public static BTreeBuilder<CombatBlackboard, AgentContext> CreateBuilder() =>
//           new BTreeBuilder<CombatBlackboard, AgentContext>()
//               .Sequence()
//                   .Action(CombatActions.Scan)
//                   .Selector()
//                       .Condition(CombatConditions.EnemyInRange)
//                       .Action(CombatActions.MoveToLastKnownPosition)
//                   .End()
//               .End();
//
//       [BTreeDefinition("PatrolAndAttack")]
//       public static BehaviorTreeBlob Build() => CreateBuilder().Compile();
//
//       [BTreeLayout("PatrolAndAttack")]
//       public static BTreeEditorLayout Layout() => ...;
//   }
```

### Example 4: Connecting a Debug Session for Live Overlay

```csharp
// Wired by the simulation host when a debug session is available.
var debugSession = new BTreeDebugSession();

// Attach to host services so all renderers receive the session.
hostServices.SetDebugSession(debugSession);
runtimeOverlay.SetSession(debugSession);
heatmapOverlay.SetSession(debugSession);
subtreeBoundary.SetSession(debugSession);
breakpointGutter.SetSession(debugSession);
blackboardPanel.SetSession(debugSession);
runtimeInspectorPane.SetSession(debugSession);

// Enable heatmap mode before a profiling run.
debugSession.HeatmapModeActive = true;
heatmapOverlay.HeatmapModeActive = true;

// The kernel adapter calls these on each tick:
// debugSession.RecordNodeExecuted(new BTreeNodeExecuted(...));
// debugSession.RecordAsyncEvent(new BTreeAsyncEvent(...));
```

### Example 5: Resolving Subtree References After Hot-Reload

```csharp
// After loading or reloading assets, resolve cross-tree Subtree references.
var catalog = serviceProvider.GetRequiredService<IAssetCatalog>();
foreach (var asset in contributor.Enumerate().OfType<BehaviorTreeAsset>())
{
    BTreeSubtreeResolver.Resolve(asset, catalog);
}

// Nodes whose SubtreeName matches a known BTree asset in the catalog
// will have IsResolved = true and SubtreeAssetId populated.
// Unresolved references emit BTreeDiagnosticCode.UnresolvedSubtree.
```

### Example 6: Classifying a Hot-Reload Tier

```csharp
// Before committing a hot reload, determine whether full re-projection is needed.
BehaviorTreeBlob oldBlob = previousAsset.Blob;
BehaviorTreeBlob newBlob = (BehaviorTreeBlob)definitionMethod.Invoke(null, null)!;

HotReloadTier tier = BTreeQuickReloadHasher.Classify(oldBlob, newBlob);
switch (tier)
{
    case HotReloadTier.Cosmetic:
        // Nothing to do; skip re-projection.
        break;
    case HotReloadTier.Soft:
        // Update leaf params in-place without rebuilding the graph.
        break;
    case HotReloadTier.Hard:
        // Re-project the full blob into the editor model.
        var updated = BehaviorTreeAssetProjector.Project(
            newBlob, newBlob.DebugMetadata, existingLayout,
            asset.AssetId, asset.Name, asset.SourceFilePath,
            asset.IsEditorOwned, asset.BlackboardTypeName, asset.ContextTypeName);
        break;
}
```

---

## Best Practices

### Authoring

- **Always bind Action and Condition methods** before saving. Unbound leaf nodes are
  `Error`-level diagnostics and will prevent the kernel from compiling the tree.

- **Keep static depth below 8**. Trees deeper than 8 levels trigger a `StackDepthExceeded`
  warning. Use `Subtree` nodes to split large trees into smaller, reusable assets.

- **Name Subtree assets before referencing them**. `BTreeSubtreeResolver` matches by
  `SubtreeName`. If the referenced asset is renamed after a Subtree node is created,
  the reference becomes dangling until manually updated.

- **Use comments on nodes**. `BTreeEditorNode.Comment` survives round-trips via
  `NodeDebugMetadata.CustomComment`. Comments appear in both the canvas tooltip and
  the StructEdit inspector panel.

### Code Generation

- **Do not hand-edit emitted files**. `BTreeFluentEmitter` regenerates the entire
  file on each save. Any manual changes will be overwritten.

- **Keep type FQNs stable**. The emitter derives `using` directives from
  `BlackboardTypeName`, `ContextTypeName`, and leaf method FQNs. Renaming types
  without updating the asset will produce uncompilable output.

- **Set `TargetNamespace` on the asset** if the game uses a non-default namespace.
  The emitter uses `"Hrot.AI.Behaviors.Trees"` as the fallback.

### Validation

- **Run `BTreeValidator` before saving**. It catches structural errors (empty
  composites, unbound methods, invalid durations, cycles) that would produce invalid
  kernel blobs.

- **Wire `BTreeAssetValidator` to the shared DiagnosticsWindow** so authors see
  live error badges without triggering a full compilation cycle.

### Debug Session

- **Call `ResetAggregateCounters()` before each heatmap profiling run** to avoid
  mixing counts across sessions.

- **Disable `HeatmapModeActive` when not profiling**. The heatmap renderer iterates
  all visible nodes every frame; leaving it active unnecessarily adds per-frame
  overhead.

- **Attach only one kernel adapter at a time**. `BTreeDebugSession` is not
  thread-safe. All `Record*` calls must come from the same thread (typically the
  simulation thread via a lock on the editor side).

### Layout

- **Avoid calling `BTreeAutoLayout.Layout()` after manual positioning**. The
  projector calls it automatically only when no `[BTreeLayout]` method is found.
  After a user positions nodes and saves, the `Layout()` method is emitted and
  preserves positions on the next load.

---

## Related Projects

| Project | Location | Relationship |
|---------|----------|--------------|
| `Hrot.Editor.AiShared` | `Hrot/Editor/Hrot.Editor.AiShared/` | Direct dependency. Provides all shared AI editor abstractions: `IEditableAsset`, `IAssetCatalog`, `IAiDebugSession`, `AiDebugSessionBase`, `IFluentCSharpEmitter<T>`, `HotReloadClassifier`, trace lane types, `IRuntimeInspectorPane`, `IAssetValidator`. |
| `Hrot.Hsm.Editor` | `Hrot/Subsystems/AI/Hrot.Hsm.Editor/` | Sibling editor for Hierarchical State Machines. Shares the same `AiShared` infrastructure (catalog, hot reload, diagnostics window, trace timeline). |
| `Fbt.Kernel` | `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/` | BTree runtime library. Defines `BehaviorTreeBlob`, `NodeType`, `NodeStatus`, `BTreeBuilder<BB,Ctx>`, `[BTreeDefinition]`, `[BTreeLayout]`, `NodeDebugMetadata`. The editor is the sole authoring tool for blobs consumed by this library. |
| `NodeEditor.Core` | `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/` | Visual graph editing framework. Provides the canvas, graph model, command architecture, and renderer extension points. The BTree editor supplies `BTreeNodeCatalog`, `BTreeTypeSystem`, `BTreeLinkValidator`, and `BTreeCommandSink` as the domain-specific implementation of the NodeEditor host service interfaces. |
| `Fdp.Core` | `FDP/Engine/Fdp.Core/` | FDP framework core. Provides `Entity` (used in debug event records) and `Breakpoint` (used in `BTreeBreakpointHit`). |
| `Hrot.BTree.Editor.Tests` | (test project) | White-box unit tests. Has `InternalsVisibleTo` access. Tests cover `BTreeCommandSink`, `BehaviorTreeAssetProjector`, `BTreeValidator`, `BTreeAutoLayout`, and `BTreeFluentEmitter`. |
| `StructEdit.Core` | (via AiShared) | Reflection-based property editor. `BTreeFacets.cs` structs carry `StructEdit` attributes (`[EditDisplayName]`, `[EditRange]`, `[EditUnit]`, `[EditReadOnly]`) plus the BTree-specific picker attributes (`[BehaviorHashPicker]`, `[BlackboardFieldPicker]`) to drive the inspector panel UI. |
