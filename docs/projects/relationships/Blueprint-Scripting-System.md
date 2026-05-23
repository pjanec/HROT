# Blueprint Scripting System

**Date**: 2026-05-23
**Scope**: How `Hrot.Blueprints.Core`, `Hrot.Blueprints.Compiler`, `Hrot.Blueprints.Editor`,
`Hrot.Blueprints.Generators`, `NodeEdit` (NodeEditor.Core + NodeEditor.UI), and
`StructEdit` (StructEdit.Core + StructEdit.Reflection) fit together as the visual
scripting system for HROT AI behavior authoring.

---

## Table of Contents

1. [Blueprint System Overview](#1-blueprint-system-overview)
2. [The Four-Project Blueprint Stack](#2-the-four-project-blueprint-stack)
3. [Node Editor Integration (NodeEdit)](#3-node-editor-integration-nodeedit)
4. [Property Editor Integration (StructEdit)](#4-property-editor-integration-structedit)
5. [The Compilation Pipeline](#5-the-compilation-pipeline)
6. [Blueprint Runtime Execution](#6-blueprint-runtime-execution)
7. [End-to-End Authoring Workflow](#7-end-to-end-authoring-workflow)
8. [Data Flow Diagrams](#8-data-flow-diagrams)
9. [Code Examples](#9-code-examples)
10. [Best Practices and Anti-patterns](#10-best-practices-and-anti-patterns)
11. [Links to Individual Project Docs](#11-links-to-individual-project-docs)

---

## 1. Blueprint System Overview

### What Blueprints Are in HROT

Blueprints are data-driven behavior scripts for AI entities.  An author creates a
Blueprint in the visual editor; the compiler turns it into C# source code; Roslyn
compiles that source into an assembly; the runtime loads the assembly and invokes the
generated code every time the entity needs to act.

The data representation is a `.bp.json` file.  A `BlueprintAsset` contains:

- A header (schema version, subsystem type).
- A stable `AssetId` (GUID) and a human-readable `Name`.
- A `BlueprintDispatchKind` that determines which runtime contract the generated class
  must satisfy.
- One or more `Graph` objects, each a directed dataflow/control-flow graph.
- Field declarations: `Parameters`, `WorkingState` (AiPrimitive); `Variables`,
  `EventDispatchers`, `CustomEvents`, `CallablePeers` (Instance).
- Editor layout metadata (viewport position, zoom, node canvas positions).

### The Three Dispatch Kinds

#### Library

A pure collection of reusable static functions.  No persistent state, no engine events,
no variables.  Libraries are referenced by AiPrimitive and Instance Blueprints via
`FunctionCallNode`.  The generated class is a `public static class` with one `static`
method per `Function` graph.

Constraints enforced by the validator (Stage 2):
- Must not declare member variables or custom events.
- Must not contain Event graphs.

#### AiPrimitive

A single-behaviour unit that implements one action or one condition.  It carries:
- `Parameters` -- read-only per-instance configuration (unmanaged structs only).
- `WorkingState` -- mutable per-instance working memory (unmanaged structs only).
- `Primitive.Intent` -- `Action` or `Condition`.
- `Primitive.Hostings` -- the set of runtime slots that can host this primitive
  (`BTreeAction`, `BTreeCondition`, `HsmAction`, `HsmGuard`, `BlueprintCall`).

The generated class exposes `TickCore(ref Params, ref WorkingState, Entity, EntityRepository, float)`
plus one unsafe thunk per hosting so the BTree / HSM schedulers can call the primitive
without knowing its concrete type.

#### Instance

A full-featured stateful actor Blueprint.  It carries:
- `Variables` -- persistent mutable state (unmanaged, laid out in a sequential `State`
  struct that starts with a `BlueprintLatentCursor` for coroutine bookmarking).
- `EventDispatchers` and `CustomEvents`.
- `CallablePeers` -- GUIDs of sibling Instance Blueprints whose functions may be called.
- Both `Function` and `Event` graphs.

The generated class exposes `Tick(ref State, ...)` and one event-handler method per
`EventEntry` graph.

### How Blueprints Differ from BTree / HSM

| Dimension          | Blueprint                         | BTree / HSM                    |
|--------------------|-----------------------------------|--------------------------------|
| Authoring surface  | Visual dataflow graph (.bp.json)  | XML / attribute decorators     |
| Compilation        | Multi-stage compiler + Roslyn     | Direct C# attributes / builder |
| State model        | Sequential struct, cursor-based   | BTreeState / HSM stack         |
| Latent execution   | WaitForChannel / WaitForEvent     | Yield primitives               |
| Extensibility      | AiPrimitive Blueprints in BTree   | C# leaf nodes                  |
| Debug              | DebugProbe + DebugMapIndex        | Per-library tracing            |

Blueprints and BTree/HSM are complementary.  An AiPrimitive Blueprint can be wired
directly as a BTree leaf (`BTreeAction` / `BTreeCondition` thunk) or as an HSM
activity (`HsmAction` / `HsmGuard` thunk), enabling visual scripting inside a
tree-structured control policy.

---

## 2. The Four-Project Blueprint Stack

```
+================================================+
|  Hrot.Blueprints.Editor                        |
|  Visual authoring UI: GraphEditorWindow,       |
|  InspectorWindow, AssetBrowserWindow,          |
|  QuickReloadService, FullRebuildService        |
+================================================+
         |                     |
         | compiles via        | edits via
         v                     v
+========================+   +=======================+
|  Hrot.Blueprints       |   |  Hrot.Blueprints      |
|  .Compiler             |   |  .Generators          |
|  8-stage pipeline      |   |  Roslyn               |
|  IBlueprintCompiler    |   |  IIncrementalGenerator|
+========================+   +=======================+
         |                           |
         | produces IrAsset /        | emits .g.cs files
         | GeneratedSource           | into the build
         v                           v
+================================================+
|  Hrot.Blueprints.Core                          |
|  Asset schema: BlueprintAsset, Node subtypes,  |
|  Pin, Link, Graph                              |
|  Runtime: DebugProbe, IBlueprintDebugSession,  |
|  DebugMapIndex, InMemoryRoslynCompiler         |
+================================================+
         |
         | asset data model consumed by
         v
+================================================+
|  NodeEdit (NodeEditor.Core + NodeEditor.UI)    |
|  Generic node-graph widget; Blueprint Editor   |
|  maps BlueprintAsset onto IGraphModel          |
+================================================+
         |
         | selected-node property editing via
         v
+================================================+
|  StructEdit (StructEdit.Core + .Reflection)    |
|  Reflection-driven property editor;            |
|  Blueprint Editor defines DrawerRegistry for   |
|  custom type drawers                           |
+================================================+
```

### Project Dependency Summary

```
Hrot.Blueprints.Core
  (no Hrot dependencies)

Hrot.Blueprints.Compiler
  --> Hrot.Blueprints.Core   (asset schema + compiler contracts)

Hrot.Blueprints.Generators
  --> Hrot.Blueprints.Compiler   (drives the full pipeline)
  --> Hrot.Blueprints.Core       (asset schema)
  --> Microsoft.CodeAnalysis     (IIncrementalGenerator)

Hrot.Blueprints.Editor
  --> Hrot.Blueprints.Core       (asset schema + debug interfaces)
  --> Hrot.Blueprints.Compiler   (IBlueprintCompiler, CompileOptions)
  --> NodeEditor.Core            (IGraphModel, IGraphCommandSink)
  --> NodeEditor.UI              (canvas widget, panels)
  --> StructEdit.Core            (IEditSession, DrawerRegistry pattern)
  --> ImGuiNET                   (host UI)
  --> Fdp.Toolkit.Blueprints     (runtime registry + hot-reload coordinator)
```

---

## 3. Node Editor Integration (NodeEdit)

### Project Layout

NodeEdit is an external dependency at `FDP/ExtDeps/NodeEdit/src/`.

```
NodeEditor.Primitives   -- ID wrappers, enums, RectF, IdGenerator
NodeEditor.Core         -- Host interfaces, GraphView, commands, spatial index
NodeEditor.UI           -- ImGui canvas renderers, panels, picker, find bar
NodeEditor.Demo         -- 13 runnable demo scenarios (not used by Blueprint Editor)
```

`NodeEditor.Core` has **no dependency on ImGui**.  All rendering is in `NodeEditor.UI`.
The Blueprint Editor implements the 11 host interfaces in `NodeEditor.Core.Interfaces/`
and hands a configured `GraphView` to the canvas renderer.

### Host Interfaces

| Interface                  | Blueprint Editor responsibility                              |
|----------------------------|--------------------------------------------------------------|
| `IGraphModel`              | Wraps a `BlueprintAsset` + one `Graph`; exposes nodes/links  |
| `INodeModel`               | Wraps one `Node`; maps Kind, Title, Position, Pins           |
| `IPinModel`                | Wraps one `Pin`; maps Label, Direction, Kind (Exec/Data)     |
| `ILinkModel`               | Wraps one `Link`                                             |
| `IGraphCommandSink`        | Translates graph commands into asset mutations + dirty mark  |
| `INodeCatalog`             | Provides the palette of node types the user can place        |
| `ITypeSystem`              | Maps `BlueprintTypeRef` to display colour and compatibility  |
| `IDebugSession`            | Bridges `IBlueprintDebugSession` to NodeEdit breakpoints     |
| `IDetailsViewProvider`     | Opens InspectorWindow when a node is selected               |
| `IEditorHostServices`      | File dialogs, clipboard, preferences                        |
| `IPinDefaultValueEditorRegistry` | Routes inline editors for pin default values          |

### Graph Model Mapping

```
BlueprintAsset                    NodeEditor model
--------------                    ----------------
BlueprintAsset.Graphs[i]    -->   IGraphModel (one tab per graph)
  Graph.Nodes[j]            -->   INodeModel
    Node.Pins[k]            -->   IPinModel (Direction: In/Out, Kind: Exec/Data)
  Graph.Links[l]            -->   ILinkModel
```

- `IsExec = true` pins become `PinKind.Exec` (white diamond, exec flow).
- `IsExec = false` pins become `PinKind.Data` (typed circle, coloured by type).
- `PinDirection.Input` / `PinDirection.Output` maps directly.
- `Node.EditorMetadata.{X,Y}` maps to `INodeModel.Position`.

### Node Categories and Colours

The Blueprint Editor assigns `NodeCategory` based on the concrete `Node` subtype:

| Node subtype           | Category         | Header colour      |
|------------------------|------------------|--------------------|
| `EventEntryNode`       | Event            | Red                |
| `FunctionCallNode`     | Function         | Blue (impure) / Purple (pure) |
| `BranchNode`           | Flow control     | Grey               |
| `SequenceNode`         | Flow control     | Grey               |
| `GetVariableNode`      | Variable         | Dark green         |
| `SetVariableNode`      | Variable         | Dark green         |
| `LiteralNode`          | Literal          | Olive              |
| `ChannelCommandNode`   | Command          | Orange             |
| `WaitForChannelNode`   | Latent           | Cyan               |
| `WaitForEventNode`     | Latent           | Cyan               |
| `CallPeerBlueprintNode`| Peer             | Teal               |

### Command Pattern

All mutations flow through `IGraphCommandSink.Apply(GraphCommand)`.  The Blueprint
Editor implements this sink and translates each command into mutations on the in-memory
`BlueprintAsset`, then calls `DirtyTracker.MarkDirty(assetId)`.

The undo/redo history (`CommandHistory`) is owned by `GraphEditorWindow` and replays
or reverses these commands.  The model's `IGraphModel.Changed` event is raised after
each mutation; the canvas redraws on the next ImGui frame.

### Debug Integration

`IDebugSession` connects NodeEdit's node overlay system (green execution glow,
breakpoint badges) to `IBlueprintDebugSession`.

```
IBlueprintProbeSink.OnNodeEnter(entity, nodeIdString)
  --> IBlueprintDebugSession routes by nodeIdString
  --> DebugMapIndex.TryResolveNode(nodeIdString)  => NodeMapEntry (GraphId, SourceLine)
  --> IDebugSession adapter sets CurrentlyExecutingNode
  --> Canvas redraws: highlighted node flashes green
```

---

## 4. Property Editor Integration (StructEdit)

### StructEdit.Core Concepts

`StructEdit.Core` provides a session-based property-editing protocol:

- `IComponentEditService.Open(object, Type)` returns an `IEditSession`.
- The session builds an `EditDocument`, which is a tree of `EditNode` descriptors.
- Each `EditNode` carries a name, JSON path, CLR type, and optional `IValueBinding`.
- Callers render the tree using their own UI (ImGui in the Blueprint Editor) and commit
  or cancel the session.

### Blueprint Editor's Use of StructEdit

The Blueprint Editor does **not** instantiate `IComponentEditService` directly for its
primary inspector; instead it follows the same pattern with its own lightweight
`DrawerRegistry` and `IStructEditDrawer<T>`:

```
DrawerRegistry
  Register<float>(new FloatDrawer())
  Register<int>(new IntDrawer())
  Register<bool>(new BoolDrawer())
  Register<string>(new StringDrawer())
  ... custom Blueprint-specific drawers ...

InspectorWindow.DrawUI()
  for each selected node's writable pin default:
    drawerRegistry.TryGet<T>(out var drawer)
    drawer.Draw(label, ref value, ctx)
```

`IStructEditDrawer<T>` is a single-method interface:

```csharp
public interface IStructEditDrawer<T>
{
    // Returns true if the value was modified.
    bool Draw(string label, ref T value, DrawContext ctx);
}
```

`DrawContext` carries `IsReadOnly`, an `IdPrefix` for ImGui stability, and an optional
`TypeRegistry` reference for Blueprint-type-aware drawers.

### Reflection-based Property Discovery

`StructEdit.Reflection` builds `EditDocument` trees by walking CLR type metadata.
The `ComponentEditServiceBuilder` registers:
- `IBufferViewProvider` instances that know how to read/write fields.
- `ICustomFieldEditor` overrides for types with special editors.
- `ICustomComponentEditor` overrides for types needing a completely custom tree.
- `IComponentValidator` for post-edit validation.

Memory classification (`DefaultComponentMemoryClassifier`) determines the buffer
strategy:

| CLR type characteristic          | Buffer type               |
|----------------------------------|---------------------------|
| `unmanaged` blittable struct     | `NativeStructEditBuffer`  |
| Reference type (class)           | `ManagedObjectEditBuffer` |
| Non-blittable struct             | `BoxedStructEditBuffer`   |

The Blueprint Inspector uses this system when editing `ParameterDecl.DefaultValueJson`
values: it deserializes the JSON into the target CLR type, opens an `IEditSession`,
renders the tree with type-specific drawers, then on commit serializes back to JSON.

---

## 5. The Compilation Pipeline

The compiler lives in `Hrot.Blueprints.Compiler`.  `BlueprintCompiler.Compile` drives
8 ordered stages.  Stages 1-7 are pure transformations; Stage 8 is optional in-process
Roslyn finalization used by QuickReload.

```
  .bp.json
     |
     v Stage 1 -- Parse
  BlueprintAsset (raw)
     |
     v Stage 2 -- Validate (14 validators)
  BlueprintAsset (validated)
     |
     v Stage 3 -- Normalize
  BlueprintAsset (implicit casts inserted, orphans pruned)
     |
     v Stage 4 -- TypeResolve
  TypedAsset (pin/field types resolved to IrTypeRef)
     |
     v Stage 5 -- Schedule
  IrAsset (nodes -> IrBlock / IrStatement / IrOperation)
     |
     v Stage 6 -- Lower
  IrAsset (dispatch-specific synthesis, field layout, structure hash, debug probes)
     |
     v Stage 7 -- Emit
  GeneratedSource (C# string) + DebugMap
     |
     v Stage 8 -- RoslynFinalize (QuickReload path only)
  PE bytes + PDB bytes
```

### Stage 1: Parse

`Stage1_Parse.Run(string json, DiagnosticSink)` deserializes the JSON to
`BlueprintAsset` using `BlueprintJsonServices` (System.Text.Json with polymorphic
`Node` discriminators).  Emits `BP0001` / `BP0002` / `BP0010` / `BP0011` on failure.

The Roslyn source generator skips Stage 1 and deserializes inline before calling the
compiler, because `AdditionalTextsProvider` already supplies the text.

### Stage 2: Validate

Runs 14 `IValidator` implementations in sequence:

| Validator                    | Checks                                                          |
|------------------------------|-----------------------------------------------------------------|
| `V_AssetStructure`           | Non-empty AssetId, non-empty Name                               |
| `V_DispatchKindCompatibility`| Library/AiPrimitive/Instance field/event constraints            |
| `V_NodeStructure`            | Each node has required pins; no duplicate pin IDs               |
| `V_LinkStructure`            | Links reference existing nodes/pins; no self-loops              |
| `V_GraphStructure`           | Each graph has exactly one entry node; no unreachable islands   |
| `V_VariablesAndState`        | Unique IDs; non-empty names                                     |
| `V_AiPrimitiveIntent`        | Action hostings compatible with intent                          |
| `V_LatentRules`              | Latent nodes not used in pure (non-latent) graphs               |
| `V_ChannelCommandReferences` | ChannelType and ActionId resolve in BuiltInChannelCommandCatalog|
| `V_EventGraphReferences`     | EventTypeId in EventEntryNode resolves in BuiltInEngineEventCatalog|
| `V_WaitNodeReferences`       | Wait node event/channel types resolve                           |
| `V_PeerReferences`           | CallPeerBlueprintNode.PeerBlueprintId in CallablePeers list     |
| `V_TypeReferences`           | All TypeId strings appear in ITypeRegistry                      |
| `V_DeterminismOrdering`      | Determinism-annotated graphs are topologically consistent       |

Any error from any validator causes the pipeline to short-circuit via
`FailResult(sink, asset)`.

### Stage 3: Normalize

Three passes over the asset:

1. **MaterializeDefaultPinLiterals** -- reserved for future use; no-op in Slice 1.
2. **InsertImplicitCasts** -- for each data link where `fromType != toType` and
   `ITypeRegistry.TryGetCoercion` succeeds, inserts a synthesised `CastNode` between
   source and destination and rewires the links.  Emits `BP2002` warning per insertion.
3. **EliminateOrphanNodes** -- removes nodes with no incoming exec edge and no
   outgoing connections to the rest of the graph.

### Stage 4: TypeResolve

Resolves every `BlueprintTypeRef` to an `IrTypeRef` (containing `FullName`,
`IsUnmanaged`, `SizeBytes`, `IsEntityHandle`).

- Variable/parameter/state fields are resolved first.
- Unmanaged constraints on `Variables` and `WorkingState` are verified (BP1503).
- A two-pass algorithm propagates wildcard types for `ArrayMakeNode` / `ArrayGetNode`.
- Link type compatibility is verified after resolution.

Returns a `TypedAsset` pairing the `BlueprintAsset` with two dictionaries:
`PinTypes: Dictionary<Guid, IrTypeRef>` and `FieldTypes: Dictionary<Guid, IrTypeRef>`.

### Stage 5: Schedule

`GraphScheduler` converts each `Graph` into an `IrGraph` consisting of `IrBlock`
objects, each containing a list of `IrStatement` (wrapping `IrOperation` and an
`IrValue` assignment destination) and an `IrTerminator`.

Control flow terminators:
- `IrTerm_Goto` -- unconditional jump
- `IrTerm_Branch` -- conditional `if(cond) goto T else goto F`
- `rTerm_Return` / `IrTerm_ReturnStatus` -- function return
- `IrTerm_Suspend` -- latent: encode resume-point index and optional deadline
- `IrTerm_FallThrough` -- fall to next block (eliminated in lowering)

Operations include ECS reads (`IrOp_GetComponent`, `IrOp_HasComponent`), ECS writes
(`IrOp_AddComponent`, `IrOp_RemoveComponent`), pure math/logic calls (`IrOp_PureCall`),
Blueprint cross-calls (`IrOp_LibraryCall`, `IrOp_PeerCall`, `IrOp_AiPrimitiveCall`),
and latent wait primitives (`IrOp_WaitForChannel`, `IrOp_WaitForEvent`).

Stage 5 also builds the `IrField` lists (with types but not yet with byte offsets).

### Stage 6: Lower

Lowering is dispatch-specific:

- **LibraryLowering** -- validates no latent operations; no state synthesis.
- **AiPrimitiveLowering** -- synthesises `__phase` field in `WorkingState` for
  latent-capable primitives; rewrites suspend terminators into phase-switch dispatch.
- **InstanceLowering** -- synthesises `__cursor` bookmarking via
  `BlueprintLatentCursor`; rewrites `WaitForChannel` / `WaitForEvent` nodes into
  suspend / poll pairs.

After dispatch-specific lowering, `FieldLayout.ComputeFieldLayouts` assigns sequential
byte offsets and sizes to all fields.  `StructureHashComputation.Compute` then hashes
the final field layout to produce `StructureHash` (used for hot-reload compatibility
checks).  Finally, `DebugProbeInsertion.Apply` injects `DebugProbe.NodeEnter` and
`DebugProbe.PinValueChanged` calls into each block (debug mode only).

### Stage 7: Emit

`CSharpEmitter` builds the generated source string via `StringBuilder`.  It dispatches
to one of three emitters:

| Dispatch kind  | Emitter            | Generated shape                                    |
|----------------|--------------------|----------------------------------------------------|
| Library        | `LibraryEmitter`   | `public static class Name_IdHex_Bp { ... }`        |
| AiPrimitive    | `AiPrimitiveEmitter` | + `Params` struct + `WorkingState` struct + unsafe thunks |
| Instance       | `InstanceEmitter`  | + `State` struct (with cursor) + event methods + thunks |

Every generated file begins with:

```
// <auto-generated />
// Asset: <Name> (<AssetId>)
// BlueprintId: 0x<hex>
// StructureHash: 0x<hex>
```

All files close with a `[BlueprintRegistrar]`-annotated static class that registers
the Blueprint with `BlueprintRegistryStaging` and, for BTree-hosted primitives, with
`BehaviorRegistry`.

### Stage 8: RoslynFinalize (QuickReload Only)

`InMemoryRoslynCompiler.Compile` invokes the Roslyn `CSharpCompilation` API with the
generated source and the current runtime's metadata references, producing:
- `Pe` bytes loaded into a new collectible `AssemblyLoadContext`.
- `Pdb` bytes for source-level debugging (when `EmitPdbWithEmbeddedSource = true`).

### Roslyn Source Generator Role

`BlueprintIncrementalGenerator` is an `IIncrementalGenerator` registered in
`Hrot.Blueprints.Generators`.  It hooks into the Roslyn build pipeline:

1. **Provider 1** -- collects all `AdditionalTexts` with `.bp.json` extension.
2. **Provider 2** -- does a lightweight `BlueprintSignatureParser.Parse` for each file
   to build a sibling catalog without triggering full compiles.
3. **Provider 3** -- collects all signatures into an `ImmutableArray<BlueprintSignature>`.
4. **Provider 4** -- for each file, runs the full `BlueprintCompiler.Compile` with the
   sibling catalog; registers the result via `spc.AddSource(fileName, source)`.

Roslyn incremental caching means the generator only re-compiles assets whose text has
changed.  This is the normal-path compilation (Release mode, no embedded PDB).  The
resulting `.g.cs` files enter the same `dotnet build` that compiles the rest of the
HROT project.

---

## 6. Blueprint Runtime Execution

### Dispatch Kinds Explained with Examples

**Library Blueprint** (`BlueprintDispatchKind.Library`)

A library contains reusable pure or impure functions that have no per-instance state.
Example: a math utility library `VectorUtils` with a `ClampAngle` function graph.
The emitter produces a `static class VectorUtils_AABBCCDD_Bp` with a static
`ClampAngle(float angle, float min, float max)` method.  Other Blueprints call it via
`FunctionCallNode { TargetTypeId = "VectorUtils_AABBCCDD_Bp", MethodName = "ClampAngle" }`.

**AiPrimitive Blueprint** (`BlueprintDispatchKind.AiPrimitive`)

An AiPrimitive encapsulates a single behaviour that a BTree or HSM can host.
Example: `ApproachTarget` with `Intent = Action`, `Hostings = [BTreeAction, HsmAction]`.
The emitter produces:

```csharp
public static class ApproachTarget_12345678_Bp
{
    public struct Params { public float AcceptanceRadius; }
    public struct WorkingState { public int __phase; public float __deadline; }

    public static NodeStatus TickCore(
        ref Params p, ref WorkingState ws,
        Entity self, EntityRepository world, float time) { ... }

    public static unsafe Fbt.NodeStatus BTreeTick(
        ref BrainBlackboard bb, ref BTreeState state,
        ref BTreeContext ctx, int paramIndex) { ... }
    // ...
}
```

**Instance Blueprint** (`BlueprintDispatchKind.Instance`)

An Instance Blueprint is a long-lived stateful actor script.  Example: `GuardRoutine`
with variables `PatrolPoint`, `AlertLevel`, event graphs for `OnEnemySighted` and
`OnDamageTaken`, and a Tick function.  The emitter produces:

```csharp
public static class GuardRoutine_87654321_Bp
{
    public struct State
    {
        public BlueprintLatentCursor Cursor;  // first 16 bytes
        public Vector3 PatrolPoint;
        public float AlertLevel;
    }
    public static unsafe void OnEnemySighted(ref State s, Entity self,
        EntityRepository world, EnemySightedEvent evt) { ... }
    public static unsafe NodeStatus Tick(ref State s, Entity self,
        EntityRepository world, float time) { ... }
    public static unsafe NodeStatus TickThunk(...) { ... }
}
```

### Memory Model (State Structs)

All per-instance state is stored in an `unmanaged` sequential struct.  The runtime
allocates a flat byte array (sized via `StateSize = Unsafe.SizeOf<State>()`) per live
entity.  No GC pressure from state access.

For AiPrimitive:
- `Params` -- configured once at spawn; read-only at tick time.
- `WorkingState` -- read/written every tick; contains `__phase` for latent dispatch.

For Instance:
- `State` starts with a `BlueprintLatentCursor` (16 bytes) that stores the current
  resume-point index for coroutine-style latent execution.

### Unsafe Thunks

The generated thunk methods cast raw blackboard/state pointers to typed struct
pointers using `Unsafe.As<byte, Params>` and call `TickCore` without boxing or virtual
dispatch.  This keeps per-entity AI tick cost close to a direct static call.

### Debug Probes and Hot-Reload

**Debug Probes** are injected by `DebugProbeInsertion` (Stage 6, debug mode only):

```csharp
global::Hrot.Blueprints.Core.Debug.DebugProbe.NodeEnter(self, "3f2504e0-...");
global::Hrot.Blueprints.Core.Debug.DebugProbe.PinValueChanged(self, "pin-guid", value);
```

`DebugProbe.Sink` is a static field; when null all calls are no-ops (release builds
leave probes out entirely).  When a debug session is attached, `Sink` is set to the
`IBlueprintDebugSession` instance, which routes events to the NodeEdit `IDebugSession`
adapter for visual overlay.

**Hot-Reload** has two paths:

| Path             | Trigger                          | Mechanism                              |
|------------------|----------------------------------|----------------------------------------|
| Quick Reload     | "Quick Reload" button in editor  | `QuickReloadService` -- in-memory Roslyn compile, new collectible ALC, AiHotReloadCoordinator atomic swap |
| Full Rebuild     | "Full Rebuild" button or file watcher | `FullRebuildService` -- `dotnet build`, DLL replaced on disk, coordinator picks up new debug maps |

The `StructureHash` (a hash of the field layout) is checked on hot-reload.  A mismatch
means the state struct layout changed; the coordinator resets per-entity state instead
of migrating it.

---

## 7. End-to-End Authoring Workflow

### Step-by-Step from "Open Editor" to "Blueprint Executing at Runtime"

```
Step 1  Author opens HROT editor application.
        BlueprintEditorModule.OnEditorActivated() registers all window menu entries.

Step 2  Author opens the Asset Browser (AssetBrowserWindow).
        FileSystemAssetCatalog scans the assets directory for *.bp.json files.

Step 3  Author double-clicks a Blueprint asset.
        EditorSelectionStore.SelectedAsset = <asset>
        EditorSelectionStore.OnSelectionChanged fires.
        GraphEditorWindow.OnSelectionChanged() calls OpenAsset(asset).

Step 4  GraphEditorWindow.DrawUI() renders the node canvas each frame.
        Blueprint Editor's IGraphModel adapter maps the BlueprintAsset onto NodeEdit's
        view model.  NodeEditor.UI renders the canvas with ImGui.

Step 5  Author places a new node from the picker panel.
        NodeEditor.UI fires an IGraphCommandSink.Apply(AddNodeCommand).
        Blueprint Editor's sink mutates BlueprintAsset, calls DirtyTracker.MarkDirty.

Step 6  Author connects two pins.
        IGraphCommandSink.Apply(AddLinkCommand).
        Link inserted into Graph.Links; canvas redraws.

Step 7  Author selects a node to edit its default pin values.
        InspectorWindow.DrawUI() calls DrawerRegistry.TryGet<T> per field.
        Custom drawers render ImGui widgets.
        On value change, VariableDecl.DefaultValueJson is updated; asset marked dirty.

Step 8  Author clicks "Save".
        GraphEditorWindow.DrawUI() calls DirtyTracker.MarkClean.
        FileSystemAssetCatalog writes BlueprintAsset as JSON to disk.

Step 9  Author clicks "Quick Reload".
        QuickReloadService.TriggerAsync(asset) is called.
        Stage 1-7 compile pipeline runs in-process.
        Stage 8 (RoslynFinalize) produces PE bytes.
        A new collectible ALC loads the PE.
        AiHotReloadCoordinator performs an atomic swap.
        Editor output console logs "Quick reload completed in Xms".

Step 10 Runtime entity ticks.
        Scheduler finds entity's Blueprint ID; looks up generated class.
        Generated TickCore / TickThunk called with ref State or ref Params+WorkingState.
        DebugProbe.NodeEnter fires for each executed node.
        If debug session attached: NodeEdit canvas highlights executing node.
```

### Sequence Diagram 1: Quick Reload

```
Author           GraphEditorWindow    QuickReloadService   BlueprintCompiler    AiHotReloadCoordinator
  |                     |                    |                     |                      |
  |--[click QuickReload]->                   |                     |                      |
  |                     |--TriggerAsync(asset)->                   |                      |
  |                     |                    |--BuildSiblingSignatures()                  |
  |                     |                    |--Compile(asset, options)->                 |
  |                     |                    |                     |--Stage1..Stage7       |
  |                     |                    |          <--CompileResult(GeneratedSource)--|
  |                     |                    |--RoslynCompiler.Compile(source)             |
  |                     |                    |  <---(PE bytes, PDB bytes)                 |
  |                     |                    |--new CollectibleALC.LoadFrom(peBytes)       |
  |                     |                    |--RegisterDebugMap(debugMap)                |
  |                     |                    |--coordinator.ApplyHotReload(assetId, asm)->|
  |                     |                    |                               <--success---|
  |                     |       <--QuickReloadResult(Succeeded=true, DurationMs=X)--------|
  |<--LogInfo("completed")--                 |                                            |
```

### Sequence Diagram 2: Full Rebuild Path

```
Author          FullRebuildService    dotnet build     BlueprintIncrementalGenerator    Runtime
  |                   |                   |                       |                       |
  |--[click FullRebuild]->               |                        |                      |
  |                   |--TriggerAsync()   |                        |                      |
  |                   |--Process.Start("dotnet build")->           |                      |
  |                   |                   |--Roslyn picks up .bp.json AdditionalTexts     |
  |                   |                   |                       |--Parse .bp.json       |
  |                   |                   |                       |--Compile(Stage1..7)   |
  |                   |                   |                       |--spc.AddSource(*.g.cs)|
  |                   |                   |--[emits Hrot.AI.Behaviors.Generated.dll]-------->
  |                   |<--exit code 0-----|                        |                      |
  |                   |--PendingDrainAfterBuild = true             |                      |
  |<--LogInfo("Full rebuild completed")   |                        |                      |
  |                   |                   |                        | [next sim frame]     |
  |                   |                   |                        |     AiHotReloadCoordinator.DrainPendingReload()
  |                   |                   |                        |                     ---> swap DLL
```

---

## 8. Data Flow Diagrams

### Diagram 1: Asset File to Generated Assembly

```
+---------------------+
|  Author's .bp.json  |
+---------------------+
          |
          | System.Text.Json (polymorphic Node discriminators)
          v
+---------------------+
|  BlueprintAsset     |  <-- in-memory asset object tree
|  (schema types)     |
+---------------------+
          |
          | BlueprintCompiler (Stages 2-7)
          v
+---------------------+     +---------------------+
|  IrAsset            |     |  DebugMap            |
|  (IR: blocks, ops,  |---->|  (NodeId -> line     |
|   fields, hashes)   |     |   range mapping)     |
+---------------------+     +---------------------+
          |
          | CSharpEmitter (Stage 7)
          v
+---------------------+
|  GeneratedSource    |  <-- C# string
|  (*.g.cs)           |
+---------------------+
          |
     +----+----+
     |         |
     | Source  | Quick Reload
     | Gen     | path
     v         v
  dotnet    InMemoryRoslynCompiler
  build         |
     |          v
     v    +---------------------+
  .dll   |  PE bytes + PDB      |
  file   |  (in-memory)         |
         +---------------------+
                  |
                  v
         CollectibleAssemblyLoadContext
                  |
                  v
         Generated static class methods
         (TickCore / BTreeTick / Tick)
```

### Diagram 2: Editor Data Flow (Per-Frame)

```
+--------------------+     EditorSelectionStore     +---------------------+
|  AssetBrowserWindow|---(SelectedAsset changed)---->|  GraphEditorWindow  |
+--------------------+                              +---------------------+
                                                              |
                                         IGraphModel adapter wraps BlueprintAsset
                                                              |
                                                              v
                                                  +---------------------+
                                                  |  NodeEditor.UI      |
                                                  |  Canvas renderer    |
                                                  |  (ImGui)            |
                                                  +---------------------+
                                                              |
                                                 IGraphCommandSink.Apply(cmd)
                                                              |
                                                              v
                                                  +---------------------+
                                                  |  DirtyTracker       |
                                                  |  MarkDirty(assetId) |
                                                  +---------------------+
                                                              |
                                                    on Save or Quick Reload
                                                              |
                                                              v
                                                  +---------------------+
                                                  |  FileSystemAsset    |
                                                  |  Catalog (JSON)     |
                                                  +---------------------+
```

### Diagram 3: Debug Session Data Flow

```
   Runtime entity tick
         |
         v
   TickCore / BTreeTick (generated code, debug build)
         |
         | DebugProbe.NodeEnter(self, nodeIdString)
         | DebugProbe.PinValueChanged(self, pinId, value)
         v
   IBlueprintProbeSink (DebugProbe.Sink)
         |
         v
   IBlueprintDebugSession (BlueprintDebugSession)
         |
         +---> DebugMapIndex.TryResolveNode(nodeIdString)
         |                    => NodeMapEntry { GraphId, SourceLine }
         |
         +---> IDebugSession adapter (NodeEdit integration)
                       |
                       v
               GraphView overlay refresh
               (green execution glow, breakpoint badge)
                       |
                       v
               InspectorWindow watches panel updated
```

---

## 9. Code Examples

### Example 1: Defining a Blueprint in JSON (AiPrimitive)

```json
{
  "header": { "subsystemType": "Hrot.Blueprints", "schemaVersion": "1.0" },
  "assetId": "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
  "name": "ApproachTarget",
  "dispatch": "AiPrimitive",
  "primitive": {
    "intent": "Action",
    "hostings": [ "BTreeAction", "HsmAction" ]
  },
  "parameters": [
    {
      "id": "a1b2c3d4-0000-0000-0000-000000000001",
      "name": "AcceptanceRadius",
      "type": { "typeId": "System.Single" },
      "defaultValueJson": "1.5"
    }
  ],
  "workingState": [
    {
      "id": "a1b2c3d4-0000-0000-0000-000000000002",
      "name": "Phase",
      "type": { "typeId": "System.Int32" },
      "defaultValueJson": "0"
    }
  ],
  "graphs": [
    {
      "id": "b2c3d4e5-0000-0000-0000-000000000001",
      "name": "Main",
      "kind": "Function",
      "inputs": [],
      "outputs": [],
      "nodes": [
        {
          "kind": "EventEntry",
          "id": "c3d4e5f6-0000-0000-0000-000000000001",
          "eventTypeId": "Hrot.AI.Events.AiPrimitiveTickEvent",
          "pins": [
            { "id": "d4e5f6a7-0001-0000-0000-000000000001", "name": "Out", "direction": "Out", "isExec": true }
          ],
          "editorMetadata": { "x": 100, "y": 150 }
        },
        {
          "kind": "Return",
          "id": "c3d4e5f6-0000-0000-0000-000000000002",
          "status": "Running",
          "pins": [
            { "id": "d4e5f6a7-0002-0000-0000-000000000001", "name": "In", "direction": "In", "isExec": true }
          ],
          "editorMetadata": { "x": 400, "y": 150 }
        }
      ],
      "links": [
        {
          "fromNodeId": "c3d4e5f6-0000-0000-0000-000000000001",
          "fromPinId":  "d4e5f6a7-0001-0000-0000-000000000001",
          "toNodeId":   "c3d4e5f6-0000-0000-0000-000000000002",
          "toPinId":    "d4e5f6a7-0002-0000-0000-000000000001"
        }
      ]
    }
  ]
}
```

### Example 2: Typical Generated C# Output (AiPrimitive)

```csharp
// <auto-generated />
// Asset: ApproachTarget (3f2504e0-4f89-11d3-9a0c-0305e82c3301)
// BlueprintId: 0x3F250400
// StructureHash: 0xA1B2C3D4E5F60001

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Blueprints;

namespace Hrot.AI.Behaviors.Generated;

public static class ApproachTarget_3F250400_Bp
{
    public const int BlueprintId = unchecked((int)0x3F250400);
    public const ulong StructureHash = 11600276611571376129UL;

    [StructLayout(LayoutKind.Sequential)]
    public struct Params
    {
        public float AcceptanceRadius;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WorkingState
    {
        public int Phase;
        public int __phase;      // synthesized by AiPrimitiveLowering
    }

    private static unsafe void InitDefaultWorkingState(WorkingState* dst)
    {
        *dst = default;
    }

    public static global::Hrot.Blueprints.Core.Assets.NodeStatus TickCore(
        ref Params p,
        ref WorkingState ws,
        global::Fdp.Core.Entity self,
        global::Fdp.Core.EntityRepository world,
        float time)
    {
        // block_entry:
        global::Hrot.Blueprints.Core.Debug.DebugProbe.NodeEnter(
            self, "c3d4e5f6-0000-0000-0000-000000000001");
        // ... generated body ...
        return global::Hrot.Blueprints.Core.Assets.NodeStatus.Running;
    }

    public static unsafe global::Fbt.NodeStatus BTreeTick(
        ref global::Fdp.Toolkit.Behavior.Components.BrainBlackboard bb,
        ref global::Fbt.BehaviorTreeState state,
        ref global::Fdp.Toolkit.Behavior.BTreeContext ctx,
        int paramIndex)
    {
        ref var p  = ref global::System.Runtime.CompilerServices.Unsafe
            .As<byte, Params>(ref bb.ParamsBuffer[paramIndex]);
        ref var ws = ref global::System.Runtime.CompilerServices.Unsafe
            .As<byte, WorkingState>(ref bb.WorkingStateBuffer[paramIndex]);
        var result = TickCore(ref p, ref ws, ctx.Self, ctx.World, ctx.Time);
        return result == global::Hrot.Blueprints.Core.Assets.NodeStatus.Success
            ? global::Fbt.NodeStatus.Success
            : result == global::Hrot.Blueprints.Core.Assets.NodeStatus.Failure
                ? global::Fbt.NodeStatus.Failure
                : global::Fbt.NodeStatus.Running;
    }
}

[global::Fdp.Toolkit.Blueprints.Attributes.BlueprintRegistrar]
public static class BlueprintRegistrar_ApproachTarget_3F250400_Bp
{
    public static void Register(
        global::Fdp.Toolkit.Blueprints.BlueprintRegistryStaging staging,
        global::Fdp.Toolkit.Behavior.BehaviorRegistry behReg)
    {
        staging.Register(ApproachTarget_3F250400_Bp.BlueprintId, typeof(ApproachTarget_3F250400_Bp));
        behReg.RegisterBlueprintAction(ApproachTarget_3F250400_Bp.BlueprintId,
            ApproachTarget_3F250400_Bp.BTreeTick);
    }
}
```

### Example 3: Compiling and Loading a Blueprint Programmatically

```csharp
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Roslyn;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;

// --- Step 1: Load and parse the asset ---
string json = File.ReadAllText("ApproachTarget.bp.json");
BlueprintAsset? asset = BlueprintJsonServices.Deserialize(json);
if (asset is null) throw new Exception("Failed to deserialize Blueprint asset.");

// --- Step 2: Compile with the full pipeline (Stages 1-7) ---
var compiler = new BlueprintCompiler();
var options = new CompileOptions(
    Mode:              CompilerMode.Debug,
    NodeRegistry:      BuiltInNodeRegistry.Instance,
    TypeRegistry:      StaticTypeRegistry.Instance,
    EngineEvents:      BuiltInEngineEventCatalog.Instance,
    ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
    WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
    SiblingSignatures: Array.Empty<BlueprintSignature>(),
    EmitPdbWithEmbeddedSource: true);

CompileResult result = compiler.Compile(asset, options);

foreach (var diag in result.Diagnostics)
    Console.WriteLine($"[{diag.Severity}][{diag.Code}] {diag.Message}");

if (!result.Succeeded)
    throw new Exception("Blueprint compilation failed.");

// --- Step 3: Roslyn-compile the generated C# to a PE (Stage 8) ---
var refs = MetadataReferenceResolver.ForRuntimeAssemblies(
    AppDomain.CurrentDomain.GetAssemblies());
var roslynCompiler = new InMemoryRoslynCompiler(refs);
var roslynSink = new DiagnosticSink();
string assemblyName = $"BpPatch_{result.BlueprintId:X8}";

var (peBytes, pdbBytes) = roslynCompiler.Compile(
    result.GeneratedSource!,
    result.GeneratedFileName ?? "dynamic.cs",
    assemblyName,
    roslynSink);

if (roslynSink.HasErrors)
    throw new Exception("Roslyn compilation failed.");

// --- Step 4: Load into a collectible ALC ---
var alc = new AssemblyLoadContext($"Blueprint_{result.BlueprintId:X8}", isCollectible: true);
Assembly asm;
using (var ms = new System.IO.MemoryStream(peBytes))
    asm = alc.LoadFromStream(ms);

// --- Step 5: Invoke the generated registrar to register with the runtime ---
// Convention: registrar class name follows BlueprintRegistrar_<name>_<id>_Bp pattern.
foreach (var type in asm.GetTypes())
{
    if (type.GetCustomAttribute<
        Fdp.Toolkit.Blueprints.Attributes.BlueprintRegistrarAttribute>() is not null)
    {
        var registerMethod = type.GetMethod("Register",
            BindingFlags.Public | BindingFlags.Static);
        registerMethod?.Invoke(null, new object[] { staging, behReg });
    }
}

// To unload: alc.Unload();  (only safe after draining all active ticks)
```

---

## 10. Best Practices and Anti-patterns

### Best Practices

**Asset organisation**

- Use one `.bp.json` per logical behaviour unit.  Avoid monolithic Blueprints with
  dozens of graphs; split responsibilities across Library + AiPrimitive or Library +
  Instance pairings.
- Keep `Name` stable; it is used to generate the class name and appears in log output.
  Renaming a Blueprint requires a full rebuild.

**State types**

- Keep all `Variables`, `Parameters`, and `WorkingState` types `unmanaged`.  Managed
  types (strings, arrays) are rejected by Stage 4 (BP1503) for state fields.
- Minimise `WorkingState` size for AiPrimitive Blueprints.  Each live entity allocates
  `sizeof(WorkingState)` bytes; 16-32 bytes is typical, 256+ bytes is a red flag.

**Latent nodes**

- Prefer `WaitForChannel` over `WaitForEvent` when the event source is within the same
  aggregate entity; channel commands guarantee ordering and bounded latency.
- Avoid deeply nested latent sequences in Library graphs -- latent nodes are only valid
  in AiPrimitive and Instance graphs.

**Compilation**

- Run `BlueprintCompiler.Validate` in CI on every commit that touches `.bp.json` files.
  Validation-only mode skips Stages 3-8 and is fast.
- Set `SiblingSignatures` when compiling assets that call peer Blueprints.  Omitting
  sibling signatures causes Stage 5 peer-call type resolution to fall back to
  `UnknownType`, and generated code may not compile.

**Hot-reload**

- Use Quick Reload during authoring for fast iteration; it does not require a full
  `dotnet build` but does require the HROT application to be running.
- Monitor `StructureHash` mismatches in the output console.  A mismatch means per-entity
  working state will be reset on next tick, which may cause brief visible AI glitches.

**NodeEdit integration**

- Implement `IGraphCommandSink` to write mutations back to `BlueprintAsset` atomically.
  Do not mutate `BlueprintAsset` from outside the sink; the NodeEdit undo stack depends
  on commands being the sole mutation path.
- Set `INodeModel.SizeOverride` for container nodes only; let the canvas auto-size
  regular nodes to avoid stale layout bugs after pin changes.

**StructEdit drawers**

- Register custom `IStructEditDrawer<T>` for domain types (e.g. `BlueprintTypeRef`,
  `AiPrimitiveHosting`) so the Inspector shows meaningful controls instead of raw
  string editing.
- Return `true` from `Draw` only when the value was actually changed; spurious dirty
  marks cause unnecessary Quick Reload offers.

### Anti-patterns

**Do not** hand-edit generated `.g.cs` files.  They are overwritten on every build by
the source generator and on every Quick Reload.

**Do not** store managed references (strings, arrays, objects) in `Variables` or
`WorkingState`.  They fail the unmanaged constraint check (BP1503) and the sequential
struct layout assumption is violated.

**Do not** reuse the same `AssetId` for two different Blueprints.  The `BlueprintId`
hash is derived from `AssetId`; collisions cause the runtime to map two assets to the
same generated class name.

**Do not** skip `SiblingSignatures` when calling `BlueprintCompiler.Compile` for
assets that use `CallPeerBlueprintNode`.  The peer-call type resolution relies on the
sibling catalog.  Omitting it produces `UnknownType` for peer output pins, which
propagates as `?` in generated code and fails Roslyn compilation.

**Do not** call `IGraphCommandSink.Apply` from a background thread while the ImGui
frame is rendering.  All command application must be synchronised to the UI thread.

**Do not** hold a reference to a `WorkingState` pointer across an `await` or a
`Task.Run` boundary.  The GC may relocate managed wrappers; the unmanaged buffer must
be pinned for the entire duration of any async operation touching it.

**Do not** attach `DebugProbe.Sink` in release builds.  The sink is `null` by default
and all probe calls become no-ops; assigning a sink in release mode reintroduces
allocation and virtual dispatch on every executed node.

**Do not** unload a collectible ALC while entities are still ticking Blueprint code
loaded from it.  Drain all active ticks via `AiHotReloadCoordinator` before calling
`alc.Unload()`.

---

## 11. Links to Individual Project Docs

### Blueprint System Projects

| Project | Path |
|---------|------|
| Hrot.Blueprints.Core | [Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/](../../../Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/) |
| Hrot.Blueprints.Compiler | [Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/](../../../Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/) |
| Hrot.Blueprints.Editor | [Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/](../../../Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/) |
| Hrot.Blueprints.Generators | [Hrot/Subsystems/Blueprints/Hrot.Blueprints.Generators/](../../../Hrot/Subsystems/Blueprints/Hrot.Blueprints.Generators/) |
| Hrot.Blueprints.Tests | [Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/](../../../Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/) |

### Supporting Libraries

| Library | Path | Notes |
|---------|------|-------|
| NodeEditor.Core | [FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/](../../../FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/) | Host interfaces, GraphView, commands |
| NodeEditor.UI | [FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/](../../../FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/) | ImGui canvas, panels, picker, find bar |
| NodeEditor.Primitives | [FDP/ExtDeps/NodeEdit/src/NodeEditor.Primitives/](../../../FDP/ExtDeps/NodeEdit/src/NodeEditor.Primitives/) | ID wrappers, enums, geometry |
| NodeEdit README | [FDP/ExtDeps/NodeEdit/README.md](../../../FDP/ExtDeps/NodeEdit/README.md) | Architecture and task tracker |
| StructEdit.Core | [FDP/ExtDeps/StructEdit/src/StructEdit.Core/](../../../FDP/ExtDeps/StructEdit/src/StructEdit.Core/) | Session, EditDocument, bindings |
| StructEdit.Reflection | [FDP/ExtDeps/StructEdit/src/StructEdit.Reflection/](../../../FDP/ExtDeps/StructEdit/src/StructEdit.Reflection/) | Reflection-based document builder |

### Related Architecture Documents

| Document | Description |
|----------|-------------|
| [HROT Architecture](../../HROT%20architecture.md) | Top-level HROT system overview |
| [AI Dev Guide](../../AI_DEV_GUIDE.md) | Developer guide for AI subsystems |
| [Project Checklist](../../00-PROJECT-CHECKLIST.md) | Current implementation status |

---

*End of document.*
