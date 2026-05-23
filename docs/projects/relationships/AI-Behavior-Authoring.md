# AI Behavior Authoring Flow

**Date**: 2026-05-23
**Scope**: Cross-project architectural relationship document covering the full pipeline from
visual authoring through compilation to runtime execution for AI behaviors in the HROT
simulation system. Projects covered: `Fbt.Kernel`, `Fbt.Compiler`, `Fhsm.Kernel`,
`Fhsm.Compiler`, `Hrot.Editor.AiShared`, `Hrot.BTree.Editor`, `Hrot.Hsm.Editor`,
`Hrot.AI.Behaviors`.

---

## Table of Contents

1. [AI Behavior System Overview](#1-ai-behavior-system-overview)
2. [The Two Runtime Libraries](#2-the-two-runtime-libraries)
3. [The Authoring Pipeline](#3-the-authoring-pipeline)
4. [Shared Editor Infrastructure (Hrot.Editor.AiShared)](#4-shared-editor-infrastructure)
5. [The BTree Editor (Hrot.BTree.Editor)](#5-the-btree-editor)
6. [The HSM Editor (Hrot.Hsm.Editor)](#6-the-hsm-editor)
7. [Runtime Behavior Registration (Hrot.AI.Behaviors)](#7-runtime-behavior-registration)
8. [End-to-End Authoring Workflow](#8-end-to-end-authoring-workflow)
9. [Data Flow Diagrams](#9-data-flow-diagrams)
10. [Code Examples](#10-code-examples)
11. [Best Practices and Anti-patterns](#11-best-practices-and-anti-patterns)
12. [Links to Individual Project Docs](#12-links-to-individual-project-docs)

---

## 1. AI Behavior System Overview

### 1.1 Behavior Trees vs Hierarchical State Machines

HROT supports two complementary AI authoring paradigms. Choosing the right one depends on
the nature of the behavior being modeled.

**Behavior Trees (BTrees)** are best suited for:
- Task-sequencing logic where success/failure drives control flow
- Reactive behaviors that must abort and retry when conditions change
- Hierarchical action selection with clearly defined priorities (Selector) or phases (Sequence)
- Behaviors requiring parameterized, reusable subtrees
- Any logic that operates primarily on a per-tick polling model

**Hierarchical State Machines (HSMs)** are best suited for:
- Lifecycle modeling where an entity has discrete modes (e.g. Idle, Moving, Engaging)
- Event-driven logic where transitions are triggered by external stimuli (radio messages,
  sensor hits, timer expiry)
- Behaviors with clear entry/exit semantics and orthogonal concurrency regions
- Long-duration stateful behaviors where "what state am I in" is more natural than
  "what is my priority order"

In practice, HROT uses both in combination: an HSM governs high-level mode transitions
(tactical state), while a BTree implements the reactive task logic within each mode.

### 1.2 The Authoring-to-Runtime Pipeline

The pipeline has three phases:

```
Phase 1: Authoring
  Visual Editor (NodeEdit canvas) --> edits BehaviorTreeAsset / HsmAsset

Phase 2: Compilation
  BTreeFluentEmitter / HsmFluentEmitter --> emits .cs source
  Fbt.Compiler (TreeCompiler / BTreeBuilder) --> produces BehaviorTreeBlob
  Fhsm.Compiler (HsmFlattener + HsmEmitter) --> produces HsmDefinitionBlob

Phase 3: Runtime
  Interpreter<TBlackboard,TContext> --> ticks BTree per entity
  HsmKernel.UpdateBatch --> updates HSM instances per batch
```

The compiler artifacts (`BehaviorTreeBlob`, `HsmDefinitionBlob`) are immutable and shared
across all entity instances. Per-entity mutable state lives in `BehaviorTreeState` (64 bytes,
one cache line) or `HsmInstance64/128/256` structs.

### 1.3 How Behaviors Drive HROT Simulation Entities

Simulation entities in the HROT Combat Game Framework (CGF) hold a `Brain` component.
`AiBehaviorFactory` maps integer behavior IDs to `BehaviorDefinition` records, each holding
either a compiled `Interpreter<BrainBlackboard, BTreeContext>` (for BTrees) or a
`HsmDefinitionBlob` (for HSMs). The `BrainTier` field distinguishes which execution path
is taken each tick.

Tactical orders flow in via `AssignTacticalIntentEvent`. Tactical mappers (e.g.
`DefendAreaMapper`) translate the intent name + unit type into an `AssignBehaviorEvent`
that carries the behavior ID and JSON parameters. The Brain system looks up the
`BehaviorDefinition`, parses parameters via `ParseParamsDelegate`, and writes them into
the entity's `BrainBlackboard` parameter slots before the next tick.

---

## 2. The Two Runtime Libraries

### 2.1 FastBTree Kernel (Fbt.Kernel)

#### Node Types

`NodeType` (1 byte, fits in `NodeDefinition.Type`) defines every node the interpreter
can execute:

| Category    | Node Types                                                    |
|-------------|---------------------------------------------------------------|
| Composites  | Root, Selector, Sequence, Parallel, ObserverSelector          |
| Leaves      | Action, Condition, Wait                                       |
| Decorators  | Inverter, Repeater, Cooldown, ForceSuccess, ForceFailure,     |
|             | UntilSuccess, UntilFailure                                    |
| Advanced    | Service, Observer, Subtree                                    |

#### Execution Algorithm

The interpreter (`Interpreter<TBlackboard, TContext>`) is a recursive descent evaluator
operating on a flat array of `NodeDefinition` structs stored in depth-first order inside
`BehaviorTreeBlob.Nodes`. Each tick:

1. A hot-reload safety check resets `RunningNodeIndex` and `StackPointer` when the blob
   structure has been replaced and the saved index is now out of bounds.
2. A pause check short-circuits immediately when `BehaviorInstanceFlags.Paused` is set.
3. `ExecuteNode(0, ...)` is called from the root (index 0).
4. Control dispatches on `NodeType` to one of the typed Execute methods.
5. `NodeStatus` (Failure=0, Success=1, Running=2) is propagated up the call stack.
6. When the root returns non-Running, `RunningNodeIndex` is cleared.

The tick signature is:

```csharp
NodeStatus Tick(
    ref TBlackboard blackboard,
    ref BehaviorTreeState state,
    ref TContext context)
```

All three parameters are passed by `ref`; no allocations occur during a tick.

#### Memory Model

**BehaviorTreeBlob** (shared, immutable):
- `NodeDefinition[] Nodes` -- flat bytecode array (8 bytes per node)
- `string[] MethodNames` -- action/condition names indexed by `PayloadIndex`
- `float[] FloatParams` -- timer durations and other float parameters
- `int[] IntParams` -- repeat counts and other integer parameters
- `string[] SubtreeAssetIds` -- cross-tree references for Subtree nodes
- `int StructureHash` -- hash of node types and topology (used for hard reload detection)
- `int ParamHash` -- hash of float/int tables (used for soft reload detection)

**BehaviorTreeState** (per-entity, exactly 64 bytes, one cache line):

```
Offset  Size   Field
------  ----   -----
0       2      RunningNodeIndex (ushort)
2       2      StackPointer (ushort)
4       4      TreeVersion (uint)
8       16     NodeIndexStack[8] (ushort[8] -- subtree call stack)
24      16     LocalRegisters[4] (int[4] -- loop counters, node-local state)
40      24     AsyncHandles[3] (ulong[3] -- packed (TreeVersion<<32)|RequestID)
56      4      InstanceFlags (overlays AsyncHandles[2])
-- 4 pad --
Total:  64 bytes
```

**NodeDefinition** (8 bytes, packed):

```
Offset  Size   Field
------  ----   -----
0       1      Type (NodeType enum)
1       1      ChildCount
2       2      SubtreeOffset (distance to next sibling, for subtree skip)
4       4      PayloadIndex (into MethodNames, FloatParams, IntParams, or SubtreeAssetIds)
```

#### Context Interface (IAIContext)

All BTree action and condition delegates receive a `TContext` implementing `IAIContext`.
This interface provides:
- Time services: `DeltaTime`, `Time`, `FrameCount`
- Batched physics: `RequestRaycast` / `GetRaycastResult` (async handle pattern)
- Batched pathfinding: `RequestPath` / `GetPathResult`
- Parameter lookup: `GetFloatParam(int)`, `GetIntParam(int)`

The batched async pattern means expensive queries (raycasts, path requests) are issued on
one tick and their results consumed on the next, keeping the BTree tick synchronous and
allocation-free.

### 2.2 FastHSM Kernel (Fhsm.Kernel)

#### State and Transition Model

`HsmDefinitionBlob` holds the immutable ROM of a state machine:
- `StateDef[] _states` -- each exactly 32 bytes
- `TransitionDef[] _transitions` -- event-triggered edge definitions
- `RegionDef[] _regions` -- orthogonal concurrency regions
- `GlobalTransitionDef[] _globalTransitions` -- machine-wide transitions
- `LinkerTableEntry[] _actionTable` -- maps action slot IDs to function IDs
- `LinkerTableEntry[] _guardTable` -- maps guard slot IDs to function IDs

`StateDef` (32 bytes) encodes topology and behavior:
- Parent/child/sibling links (ushort indices)
- `OnEntryActionId`, `OnExitActionId`, `ActivityActionId` (ushort slots)
- `HistorySlotIndex`, `TimerSlotIndex`
- `StateFlags` (Initial, History, Shallow/Deep history, etc.)
- `Depth` (0-16), `RegionCount`

`HsmEvent` (24 bytes, fixed layout):
- `EventId` (ushort), `Priority` (EventPriority enum), `Flags`, `Timestamp`
- `Payload[16]` (inline 16-byte blob, or indirect ID for larger payloads)

#### Event Processing

The kernel processes instances in batches via `HsmKernel.UpdateBatch`. Internally
`HsmKernelCore.UpdateBatchCore` iterates each instance, validates it (MachineId
matches blob's StructureHash, not Terminated, not Paused), and calls
`ProcessInstancePhase`. The per-instance header (`InstanceHeader`) is always the
first field of any `HsmInstance64/128/256` struct, enabling the type-erased void*
core to access it without generics.

Three pre-defined instance sizes are provided: `HsmInstance64`, `HsmInstance128`,
`HsmInstance256`. The correct size is chosen based on the number of state slots,
history slots, and timer slots required by the compiled definition.

The Least Common Ancestor (LCA) algorithm is used during transitions: the kernel
computes the `TransitionPath` (exit path + LCA + entry path), fires OnExit actions
from the current state up to the LCA, then fires OnEntry actions from the LCA down
to the target state.

### 2.3 BTrees vs HSMs -- Comparison

| Aspect              | BTree (Fbt.Kernel)                     | HSM (Fhsm.Kernel)                          |
|---------------------|----------------------------------------|--------------------------------------------|
| Execution model     | Tick-driven (called every frame)       | Event-driven + Activity tick               |
| Control flow        | Recursive descent with status return   | Explicit state transitions via LCA         |
| State storage       | 64-byte BehaviorTreeState struct       | 64/128/256-byte HsmInstance struct         |
| Shared blob         | BehaviorTreeBlob (immutable)           | HsmDefinitionBlob (immutable)              |
| Action binding      | ActionRegistry (string->delegate map)  | LinkerTable (ushort->function ID)          |
| Parameterization    | FloatParams[]/IntParams[] in blob      | Event payloads (inline or indirect)        |
| Concurrency         | Parallel node                          | Orthogonal regions                         |
| Hot reload granularity | StructureHash + ParamHash           | StructureHash + ParameterHash              |
| Hard reset trigger  | StructureHash changed                  | StructureHash changed                      |
| Soft reload trigger | ParamHash changed (keep entity state)  | ParameterHash changed (keep entity state)  |
| Best for            | Task sequencing, reactive priorities   | Mode transitions, event-driven lifecycles  |

---

## 3. The Authoring Pipeline

### 3.1 Overview Diagram

```
+------------------------+     emit .cs     +------------------+
|  Hrot.BTree.Editor     |----------------->| BTreeFluentEmitter|
|  (NodeEdit canvas,     |                  | CreateBuilder()  |
|   BehaviorTreeAsset)   |                  | Build()          |
+------------------------+                  | Layout()         |
         |                                  +--------+---------+
         | hot-reload path                           |
         v                                           | C# source
+------------------------+     compile      +--------v---------+
|  Fbt.Compiler          |<-----------------| Fbt.Compiler     |
|  BTreeBuilder<T,T>     |                  | BTreeSchema      |
|  TreeCompiler          |                  | FbtAutoDiscovery |
+--------+---------------+                  +------------------+
         |
         | BehaviorTreeBlob
         v
+------------------------+     register     +------------------+
|  AiBehaviorFactory     |----------------->| BehaviorRegistry |
|  ActionRegistry wiring |                  | (BehaviorDefinition)|
+--------+---------------+                  +------------------+
         |
         | per-entity tick
         v
+------------------------+
|  Interpreter<BB, Ctx>  |
|  Fbt.Kernel            |
|  BehaviorTreeState     |
+------------------------+
```

```
+------------------------+     emit .cs     +------------------+
|  Hrot.Hsm.Editor       |----------------->| HsmFluentEmitter  |
|  (NodeEdit canvas,     |                  | CreateBuilder()  |
|   HsmAsset)            |                  | Compile()        |
+------------------------+                  | Layout()         |
         |                                  +--------+---------+
         | hot-reload path                           |
         v                                           | C# source
+------------------------+     compile      +--------v---------+
|  Fhsm.Compiler         |<-----------------| Fhsm.Compiler    |
|  HsmBuilder            |                  | HsmNormalizer    |
|  HsmFlattener          |                  | HsmFlattener     |
|  HsmEmitter            |                  | HsmEmitter       |
+--------+---------------+                  +------------------+
         |
         | HsmDefinitionBlob
         v
+------------------------+     register     +------------------+
|  AiBehaviorFactory     |----------------->| BehaviorRegistry |
|  HSM registration      |                  | (BehaviorDefinition)|
+--------+---------------+                  +------------------+
         |
         | per-batch tick
         v
+------------------------+
|  HsmKernel.UpdateBatch |
|  Fhsm.Kernel           |
|  HsmInstance64/128/256 |
+------------------------+
```

### 3.2 The BTree Compilation Chain

A `BehaviorTreeAsset` (the editor model) is serialized to C# source by
`BTreeFluentEmitter`. The emitter produces three static methods in a generated class:

- `CreateBuilder()` -- the fluent tree definition using `BTreeBuilder<TBlackboard, TContext>`
- `Build()` -- a `[BTreeDefinition("TreeName")]`-annotated thunk that calls `CreateBuilder().Build()`
- `Layout()` -- a `[BTreeLayout]`-annotated snapshot of canvas node positions

At compile time, the Fbt.SourceGen source generator scans for `[BTreeDefinition]` methods
and emits a `FbtTreeCatalog` class with typed accessors (e.g. `FbtTreeCatalog.GetMoveToLocation()`).
At runtime, `BTreeBuilder.Build()` calls `TreeCompiler.FlattenToBlob()` to produce the
`BehaviorTreeBlob`.

### 3.3 The HSM Compilation Chain

A `HsmAsset` (the editor model) is serialized to C# source by `HsmFluentEmitter`. The
emitter produces:

- `CreateBuilder()` -- fluent `HsmBuilder` definition
- `Compile()` -- a `[HsmDefinition]`-annotated thunk calling `CreateBuilder().Build().Compile()`
- `Layout()` -- `[HsmLayout]` canvas snapshot

At runtime, the compilation pipeline is:
1. `HsmNormalizer.Normalize(graph)` -- resolves initial states, validates transitions
2. `HsmFlattener.Flatten(graph)` -- produces flat ROM arrays (`FlattenedData`)
3. `HsmEmitter.Emit(flatData)` -- produces `HsmDefinitionBlob`
4. `HsmEmitter.BuildMachineMetadata(graph)` -- produces `MachineMetadata` for diagnostics

---

## 4. Shared Editor Infrastructure

`Hrot.Editor.AiShared` provides the infrastructure shared between `Hrot.BTree.Editor` and
`Hrot.Hsm.Editor`. Neither editor reimplements these concerns.

### 4.1 Asset Identity and Catalog

**`AssetKind`** enumerates `Blueprint`, `BTree`, and `Hsm`. Every editor asset implements
**`IEditableAsset`** (from the `Identity` folder), which provides:
- `Guid AssetId` -- stable cross-reload identity
- `string Name` -- human-readable name
- `AssetKind Kind`
- `string SourceFilePath`
- `bool IsDirty`
- `bool IsEditorOwned`

**`AssetCatalog`** / **`IAssetCatalog`** hold all open assets. Contributors (e.g.
`BTreeAssetContributor`) populate the catalog by scanning file system paths and projecting
blobs into editor models.

### 4.2 The Debug Session Model

```
+----------------------------+
|  AiDebugSessionBase        |
|  (abstract)                |
|  - breakpoint list         |
|  - pause/continue state    |
|  - AiTracerCoordinator     |
+---+------------------------+
    |               |
    v               v
BTreeDebugSession  HsmDebugSession
(IBTreeDebugSession) (IHsmDebugSession)
```

`IAiDebugSession` extends `IAiTraceObserver` and adds:
- Breakpoint management: `SetBreakpoint`, `ClearBreakpoint`, `ClearAllBreakpoints`
- Step control: `StepOver`, `StepInto`, `StepOut`, `Continue`, `Pause`
- State: `IsAttached`, `IsPaused`, `PausedAt`, `PausedOnEntity`
- Change notification: `event Action? OnSessionStateChanged`

`AiDebugSessionBase` provides the full breakpoint list management, pause/resume logic,
and `AiTracerCoordinator` wiring so concrete session classes only need to implement
kernel-specific snapshot retrieval and step semantics.

`BTreeDebugSession` adds:
- In-memory ring buffer for `BTreeNodeExecuted` records (last 200 events)
- In-memory ring buffer for `BTreeAsyncEvent` records (async token lifecycle)
- Heatmap mode: aggregate `Dictionary<Guid, int>` counting node execution frequency

`DebugSessionRegistry` / `LiveSessionRegistry` manage session lifetime and
multi-entity session tracking.

### 4.3 Hot-Reload Classification

The `HotReloadClassifier` in `Hrot.Editor.AiShared.HotReload` is the single decision
point for reload tier classification:

```csharp
public static HotReloadTier Classify(
    int previousStructureHash, int newStructureHash,
    int previousParamHash,     int newParamHash)
```

Returns one of three tiers:

| Tier     | Condition                          | Entity impact                              |
|----------|------------------------------------|---------------------------------------------|
| Cosmetic | Neither hash changed               | No impact; layout-only change               |
| Soft     | Only ParamHash changed             | Entity state preserved; param tables patched|
| Hard     | StructureHash changed              | All entity states reset to default          |

Both `BTreeQuickReloadHasher` and `HsmQuickReloadHasher` delegate to this classifier,
providing typed wrappers that extract the appropriate hashes from their respective blob types.

`HotReloadTier.MostImpactful(a, b)` merges multiple coalesced changes by returning the
more severe tier (`Hard > Soft > Cosmetic`).

### 4.4 The Fluent C# Emitter Base

`FluentCSharpEmitterBase` and `IFluentCSharpEmitter<TAsset>` define the emitter contract.
`EmitterOptions` controls formatting preferences. `UsingDirectiveSet` collects `using`
directives and deduplicates them into ordered groups (system namespaces first, then
alphabetically sorted non-system namespaces). The header comment block inserted at the top
of every emitted file includes the source asset GUID to enable round-trip identification.

### 4.5 Validation

`IAssetValidator` is the extension point for asset-specific validation rules. Both editors
register concrete validators that report errors and warnings without throwing exceptions.
Validation results surface in the editor inspector panel and block code generation for
assets with fatal errors.

---

## 5. The BTree Editor

`Hrot.BTree.Editor` is the visual authoring tool for behavior trees. It depends on
`NodeEdit` (the general-purpose graph canvas from `FDP\ExtDeps\NodeEdit`) and
`Hrot.Editor.AiShared`.

### 5.1 The Asset Model

**`BehaviorTreeAsset`** is the authoritative mutable editor model. It holds:
- A list of `BTreeEditorNode` objects (visual nodes with stable `Guid VisualId`)
- A list of `BTreeEditorPill` objects (decorator stacks attached to host nodes)
- Canvas layout state (`CanvasPanOffset`, `CanvasZoomLevel`)
- Identity: `AssetId`, `Name`, `SourceFilePath`, `TargetNamespace`
- Type bindings: `BlackboardTypeName`, `ContextTypeName`
- A reference to the compiled `BehaviorTreeBlob`

**`BTreeEditorNode`** represents one non-decorator node:
- `NodeType Type` -- composite, leaf, or advanced
- `Guid VisualId` -- stable identity through edits and reloads
- `Vector2 Position` -- canvas position
- `List<BTreeEditorPill> Pills` -- ordered decorator stack
- Typed payload (`BTreeActionPayload`, `BTreeConditionPayload`, `BTreeWaitPayload`,
  `BTreeSubtreePayload`) depending on `Type`

**`BTreeEditorPill`** represents one decorator node collapsed into a pill badge:
- `NodeType DecoratorType` -- e.g. Inverter, Cooldown, Repeater
- `int? IntParam`, `float? FloatParam` -- optional parameters
- `int StackIndex` -- ordering within the decorator stack

The pill model flattens the decorator chain that would be separate nodes in the blob into
a compact visual representation, reducing canvas clutter.

### 5.2 Node Catalog

`BTreeNodeCatalog` implements `INodeCatalog` for the NodeEdit canvas. It provides palette
entries for all static node types (Composite, Leaf, Decorator categories). Dynamic
Action/Condition entries are populated at editor startup by scanning assemblies for
`[BTreeAction]` and `[BTreeCondition]` attributes via `BTreeSchemaExporter`.

Each catalog entry carries:
- `NodeKind` string (e.g. `BTreeKinds.Sequence`)
- Display name and tooltip
- Search tags for the palette search box
- Icon path
- Pin signatures (Exec pins, typed data pins)

### 5.3 Graph Serialization Format

The editor persists a `BehaviorTreeAsset` as a C# source file containing the fluent builder
definition (via `BTreeFluentEmitter`). The file is re-parsed into a `BehaviorTreeBlob` at
load time by executing the builder. This approach means the on-disk format is also valid
C# code, making diff/merge and code review straightforward.

The emitted file contains three sections:
```csharp
public static class OrcCombat
{
    public static BTreeBuilder<BB, Ctx> CreateBuilder() { ... }

    [BTreeDefinition("OrcCombat")]
    public static BehaviorTreeBlob Build() => CreateBuilder().Build("OrcCombat");

    [BTreeLayout("OrcCombat")]
    public static BTreeEditorLayout Layout() { ... }
}
```

### 5.4 C# Code Generation (Emit)

`BTreeFluentEmitter.Emit(BehaviorTreeAsset asset)` traverses the asset's node tree in
depth-first order and emits the fluent builder calls. For each node:

1. Composite nodes emit `.Sequence(children => { ... })` or `.Selector(...)` calls.
2. Leaf nodes emit `.Action("MethodFqn", ...)` or `.Condition(...)` calls.
3. Wait nodes emit `.Wait(duration)`.
4. Subtree nodes emit `.Subtree("AssetId")`.
5. Decorator pills are emitted as wrapping calls (e.g. `.Inverter(...)`, `.Cooldown(...)`).
6. Each node call includes the `visualId:` named argument to preserve round-trip identity.

`CollectUsings` scans all method FQNs and extracts namespaces, then passes them through
`UsingDirectiveSet` for deduplication and ordering.

### 5.5 Blackboard Inspector

`Hrot.BTree.Editor.Blackboard` (not shown in detail) provides the inspector panel for
browsing available blackboard DTO types discovered from the loaded assemblies via
`BTreeSchema`. This lets authors pick the correct `ExpressionTargetField` when adding
action nodes that use the three-parameter reusable delegate shape.

### 5.6 Debug Session and Live Visualization

When the simulation is running, `BTreeDebugSession` attaches to the kernel tracer. The
editor renders live overlays on the canvas nodes:

- Nodes currently in `Running` state glow with a highlight color.
- Nodes that returned `Success` or `Failure` last tick show status badges.
- Breakpoints are shown as colored badges on node edges.
- Heatmap mode colors nodes by execution frequency (cool to warm gradient).

The `BTreeTraceLaneProvider` surfaces trace records into the FDP Diagnostics trace lane
system for cross-system timeline correlation.

`BTreeAutoLayout` provides automatic tree layout when loading an asset that has no saved
canvas positions (e.g. first time opening a freshly compiled asset).

---

## 6. The HSM Editor

`Hrot.Hsm.Editor` is the visual authoring tool for hierarchical state machines. It shares
infrastructure with `Hrot.BTree.Editor` via `Hrot.Editor.AiShared`.

### 6.1 The Asset Model

**`HsmAsset`** is the authoritative mutable editor model. It holds:
- `HsmDefinitionBlob Blob` -- the compiled kernel blob (read-only after projection)
- `MachineMetadata Metadata` -- state/event/action name tables for diagnostics
- `StateNode RootState` -- synthetic root of the editor state hierarchy
- `IReadOnlyList<StateNode> AllStates` -- all states including root
- `IReadOnlyList<TransitionNode> AllTransitions` -- all local transitions
- `IReadOnlyList<GlobalTransitionNode> AllGlobalTransitions` -- machine-wide transitions
- `IReadOnlyList<RegionNode> AllRegions` -- orthogonal concurrency regions
- `IReadOnlyList<EventDefinition> AllEvents` -- all registered events with IDs
- Identity bridges: dictionaries from stable `Guid` to state/transition/region nodes
  and from `ushort` flat indices to editor nodes (for live debug overlay)

**`HsmAssetProjector`** reconstructs a `HsmAsset` from a `StateMachineGraph` (produced by
the compiler) and an optional layout snapshot. The projector builds all the identity bridge
dictionaries in one pass so the editor can look up states by both their stable visual GUID
and their runtime flat index.

### 6.2 State and Transition Creation

`HsmGraphModel` exposes the mutable graph operations:
- Add/remove states
- Add/remove transitions (with source state, target state, trigger event, optional guard)
- Add/remove global transitions
- Define events with IDs and payload descriptors

`HsmPinModel` and `HsmTransitionLink` model the visual connection endpoints on the canvas.
`HsmTransitionSnapHelper` provides snapping assistance when dragging transition lines to
state boundaries.

### 6.3 The HSM Node Catalog

`HsmNodeCatalog` provides the state node palette and property inspector integration.
States, regions, and transitions are distinct visual object types on the canvas (unlike
BTrees, which use a single node type hierarchy). The `HsmKinds` constants define the
kind strings used to route property inspector rendering.

### 6.4 Differences from BTree Editor

| Aspect                  | BTree Editor                               | HSM Editor                                    |
|-------------------------|--------------------------------------------|-----------------------------------------------|
| Primary canvas objects  | Nodes (composites, leaves, decorators)     | States and transitions                        |
| Connection model        | Parent-child tree (exec pins)              | Directed graph (event-triggered transitions)  |
| Nesting                 | Subtree reference nodes                    | Hierarchical state containment (parent/child) |
| Serialization           | Fluent builder emitting `.Sequence(...)` etc. | Fluent builder emitting `.State(...).On(...)` |
| Generated methods       | `CreateBuilder()`, `Build()`, `Layout()`   | `CreateBuilder()`, `Compile()`, `Layout()`    |
| Event palette           | None (conditions check blackboard)         | `HsmEventsWindow` for defining event registry |
| Global transitions      | Not applicable                             | `HsmGlobalsStrip` for machine-wide transitions|
| Debug visualization     | Node execution status overlay              | Active state highlight, transition fire log   |

### 6.5 Event-Driven vs Tick-Driven Comparison

The HSM kernel's `ActivityActionId` is the tick-driven path (equivalent to BTree's
per-frame evaluation). The event-driven path is distinct:

- Events are enqueued via `HsmEventQueue` from external game systems (combat hit
  detection, unit orders, timer expiry) at any time during the frame.
- The kernel drains the event queue during `UpdateBatch` before executing activity
  actions, ensuring transitions triggered by events take effect within the same frame.
- The timer subsystem fires a synthetic `TimerEventId = 0xFFFE` when a state's timer
  slot expires.

This means HSM authors write transitions as "when event X fires, go to state Y" rather
than "each tick, check condition X and abort to state Y".

### 6.6 C# Code Generation (Emit)

`HsmFluentEmitter.Emit(HsmAsset asset)` traverses the asset and emits:

```csharp
public static class InfantryCombat
{
    public static HsmBuilder CreateBuilder()
    {
        var b = new HsmBuilder("InfantryCombat");
        b.Event("Engaged",    eventId: 1);
        b.Event("Suppressed", eventId: 2);
        b.Event("Withdraw",   eventId: 3);
        b.State("Idle")
            .Initial()
            .OnEntry("Actions.EnterIdleStance")
            .On("Engaged", "Attacking");
        b.State("Attacking")
            .OnEntry("Actions.AcquireTarget")
            .Activity("Actions.FireAtTarget")
            .On("Suppressed", "TakeCover")
            .On("Withdraw", "Retreating");
        b.State("TakeCover")
            .OnEntry("Actions.FindCover")
            .On("Engaged", "Attacking");
        b.State("Retreating")
            .OnEntry("Actions.BeginRetreat");
        return b;
    }

    [HsmDefinition("InfantryCombat")]
    public static HsmDefinitionBlob Compile() =>
        HsmEmitter.Emit(HsmFlattener.Flatten(CreateBuilder().Build()));

    [HsmLayout("InfantryCombat")]
    public static HsmEditorLayout Layout() { ... }
}
```

---

## 7. Runtime Behavior Registration

### 7.1 AiBehaviorFactory and Two-Phase Hot Reload

`AiBehaviorFactory` is the single source of truth for all CGF behavior registrations.
It is annotated with `[BlueprintRegistrar]` for attribute-driven discovery.

The design uses a two-phase pattern to avoid stalling the 60 Hz UI loop during hot reload:

**Phase 1 (background thread)** -- `BuildRegistrationAction(geoTransform, entityMap)`:
1. Creates a fresh `ActionRegistry<BrainBlackboard, BTreeContext>`.
2. Calls `FbtActionRegistrar.RegisterAll(actionRegistry)` to bind all `[BTreeAction]`-annotated
   delegates by name.
3. Compiles all `BehaviorTreeBlob` instances by calling the generated `FbtTreeCatalog`
   accessors (e.g. `FbtTreeCatalog.GetMoveToLocation()`). This is the CPU-intensive step.
4. Builds the `HsmDefinitionBlob` for HSM behaviors via the compiler pipeline.
5. Returns a lightweight `Action<BehaviorRegistry>` lambda that captures all compiled blobs.

**Phase 2 (main thread)** -- execute the returned lambda:
1. Calls `registry.Register(id, name, definition)` for each behavior.
2. Each `BehaviorDefinition` carries exactly one of:
   - `BTreeInterpreter` -- a fully constructed `Interpreter<BrainBlackboard, BTreeContext>`
   - `HsmDefinition` + `HsmMetadata` -- a compiled blob plus name tables

The `FbtAssemblyHotReloader.DrainPendingCallbacks()` method invokes the staged lambda
on the main thread after the background compilation completes.

### 7.2 BehaviorDefinition Structure

```csharp
public class BehaviorDefinition
{
    public string Name;
    public int BrainTier;           // BrainTierBTree or BrainTierHsm
    public ParseParamsDelegate ParseParams;
    public Type ParamsDtoType;

    // BTree path
    public Interpreter<BrainBlackboard, BTreeContext> BTreeInterpreter;

    // HSM path
    public HsmDefinitionBlob HsmDefinition;
    public MachineMetadata HsmMetadata;
}
```

Stable integer IDs (e.g. `MoveTo_BT = 3001`) are defined as constants inside
`AiBehaviorFactory` and mirror `CgfBehaviorIds` in `Hrot.CGF`. These IDs must never
change once published because they may be serialized in replay files and scenario
definitions.

### 7.3 The Tactical Intent Mapper

The mapper layer (`ITacticalOrderMapper` in `Fdp.Toolkit.Behavior.TacticalOrderMapper`)
decouples the abstract tactical vocabulary from concrete behavior IDs.

`DefendAreaMapper` exemplifies the pattern:
- `TargetIntentId` returns the intent name it handles (`"DefendArea"`)
- `TryMap` receives the entity, the repository, and the raw JSON parameters
- It reads the `TkbIdentity` component to determine unit type
- It maps unit type to behavior name (`MilitaryApc` -> `"ConvoyEscort"`, etc.)
- It emits an `AssignBehaviorEvent` with the resolved behavior name and forwarded JSON

This means tactical AI (doctrines, command logic) works in terms of intents, not behavior
IDs. The mapper table can be extended without modifying doctrines.

### 7.4 Assignment to Entities

When the Brain system processes an `AssignBehaviorEvent`:
1. It looks up the behavior by name in the `BehaviorRegistry`.
2. If `ParseParams` is non-null, it deserializes `JsonParams` into the appropriate DTO
   struct and writes it into the entity's blackboard parameter area.
3. It stores the behavior ID in the entity's Brain component.
4. On the next tick, the execution loop selects the `BTreeInterpreter` or `HsmDefinition`
   based on `BrainTier` and ticks the entity through the appropriate runtime.

---

## 8. End-to-End Authoring Workflow

### 8.1 Step-by-Step: Creating a New BTree

1. **Create asset**: In the editor, invoke "New BTree Asset". The editor mints a new
   `Guid AssetId` and creates a `BehaviorTreeAsset` with a single Root node.

2. **Set blackboard and context types**: In the asset inspector, specify the C# type
   names for `TBlackboard` (e.g. `BrainBlackboard`) and `TContext` (e.g. `BTreeContext`).
   This determines which action catalog is shown in the palette.

3. **Build the tree**: Drag nodes from the BTree palette onto the canvas. Connect children
   to composites using the Exec pin system. Add decorator pills by right-clicking a node
   and selecting a decorator type from the context menu.

4. **Assign action nodes**: For each Action or Condition leaf, open the inspector and
   select the method from the catalog (populated by `BTreeSchemaExporter` scanning the
   loaded assemblies). The method FQN and delegate shape are stored in `BTreeActionPayload`.

5. **Save**: The editor calls `BTreeFluentEmitter.Emit(asset)` and writes the result to
   the configured `.cs` source file. The emitter produces deterministic output so VCS diffs
   are minimal.

6. **Compile**: The project containing the emitted file is rebuilt. The Fbt.SourceGen
   source generator detects `[BTreeDefinition]` methods and emits `FbtTreeCatalog`
   accessors.

7. **Register**: At startup (or on hot reload), `AiBehaviorFactory.BuildRegistrationAction`
   calls `FbtTreeCatalog.GetMyTree()`, which executes `CreateBuilder().Build()`, producing
   a `BehaviorTreeBlob`. An `Interpreter` is constructed and registered.

8. **Assign to entity**: A doctrine or command system emits an `AssignBehaviorEvent` with
   the behavior name. The Brain system looks up the `BehaviorDefinition` and stores it.

9. **Execute**: Each simulation tick, `Interpreter.Tick(ref blackboard, ref state, ref ctx)`
   is called for the entity. The tree evaluates and the entity acts.

### 8.2 Sequence Diagram: BTree Hot Reload

```
Editor          BTreeFluentEmitter   Compiler     FbtAssemblyHotReloader  AiBehaviorFactory  Brain
  |                    |                |                  |                      |              |
  | save(.cs)          |                |                  |                      |              |
  |------------------>>|                |                  |                      |              |
  |             emit C# source          |                  |                      |              |
  |                    |                |                  |                      |              |
  |                    | write file     |                  |                      |              |
  |                    |--------------->|                  |                      |              |
  |                    |           build ALC assembly      |                      |              |
  |                    |                |                  |                      |              |
  |                    |                | new assembly     |                      |              |
  |                    |                |----------------->|                      |              |
  |                    |                |            detect [BlueprintRegistrar]  |              |
  |                    |                |                  |                      |              |
  |                    |                |                  | BuildRegistrationAction (bg thread) |
  |                    |                |                  |------------------->>|              |
  |                    |                |                  |         compile blobs (CPU)         |
  |                    |                |                  |                      |              |
  |                    |                |                  | staged Action<BehaviorRegistry>     |
  |                    |                |                  |<<--------------------|              |
  |                    |                |                  |                      |              |
  |                    |                |    DrainPendingCallbacks (main thread)  |              |
  |                    |                |                  |--registry.Register-->|              |
  |                    |                |                  |                      |              |
  |                    |                |        BTreeHotReloadManager.TryReload  |              |
  |                    |                |                  |                      |              |
  |                    |         HardReset or SoftReload decision                 |              |
  |                    |                |                  |                      |              |
  |                    |                |                  |  reset entity states if Hard        |
  |                    |                |                  |----------------------------->>|     |
```

### 8.3 Sequence Diagram: Tactical Order to BTree Execution

```
Doctrine      TacticalOrderMapper   Brain System        BehaviorRegistry    Interpreter
  |                  |                   |                     |                  |
  | AssignTacticalIntentEvent            |                     |                  |
  |                  |                   |                     |                  |
  | TryMap(intent, entity)               |                     |                  |
  |----------------->|                   |                     |                  |
  |           map intent + unit type -> behavior name          |                  |
  |                  |                   |                     |                  |
  |                  | AssignBehaviorEvent("ConvoyEscort", json)|                  |
  |                  |------------------>|                     |                  |
  |                  |           registry.Lookup("ConvoyEscort")|                  |
  |                  |                   |-------------------->|                  |
  |                  |                   |    BehaviorDefinition                  |
  |                  |                   |<--------------------|                  |
  |                  |                   |                     |                  |
  |                  |        ParseParams(json) -> blackboard  |                  |
  |                  |                   |                     |                  |
  |                  |        store behaviorId in Brain        |                  |
  |                  |                   |                     |                  |
  |                  |        -- next frame tick --            |                  |
  |                  |                   |                     |                  |
  |                  |        Interpreter.Tick(bb, state, ctx) |                  |
  |                  |                   |------------------------------------------->|
  |                  |                   |                     |            NodeStatus |
  |                  |                   |<-------------------------------------------|
```

### 8.4 Sequence Diagram: HSM Event-Driven Transition

```
Game System    HsmEventQueue     HsmKernel          HsmInstance       StateDef (ROM)
    |               |                |                   |                  |
    | Enqueue(HsmEvent{EventId=2})   |                   |                  |
    |-------------->|                |                   |                  |
    |               | (buffered)     |                   |                  |
    |               |                |                   |                  |
    | -- UpdateBatch tick --         |                   |                  |
    |               |                |                   |                  |
    |               | drain queue    |                   |                  |
    |               |--------------->|                   |                  |
    |               |         validate instance          |                  |
    |               |                |------------------>|                  |
    |               |         find matching transition   |                  |
    |               |                |-------------------------------------------->|
    |               |                |       TransitionDef (eventId, target, guard) |
    |               |                |<--------------------------------------------|
    |               |         compute LCA path           |                  |
    |               |         fire OnExit actions up to LCA                 |
    |               |                |------------------>|                  |
    |               |         update CurrentStateIndex   |                  |
    |               |                |------------------>|                  |
    |               |         fire OnEntry actions down to target           |
    |               |                |------------------>|                  |
```

---

## 9. Data Flow Diagrams

### 9.1 Complete Authoring Data Flow

```
+--------------------+    .cs emit    +-------------------------+
|  BTreeEditorNode   |--------------->| BTreeFluentEmitter      |
|  BTreeEditorPill   |                | - CreateBuilder() body  |
|  BehaviorTreeAsset |                | - Build() thunk         |
+--------------------+                | - Layout() body         |
                                      +----------+--------------+
                                                 |
                                         write to disk
                                                 |
                                      +----------v--------------+
                                      |  .cs source file        |
                                      |  (project source tree)  |
                                      +----------+--------------+
                                                 |
                                          C# compiler + SourceGen
                                                 |
                                      +----------v--------------+
                                      |  FbtTreeCatalog         |
                                      |  Get<TreeName>()        |
                                      +----------+--------------+
                                                 |
                                       BTreeBuilder.Build()
                                       TreeCompiler.FlattenToBlob()
                                                 |
                                      +----------v--------------+
                                      |  BehaviorTreeBlob       |
                                      |  (immutable, shared)    |
                                      +----------+--------------+
                                                 |
                               AiBehaviorFactory.BuildRegistrationAction()
                                                 |
                                      +----------v--------------+
                                      |  ActionRegistry         |
                                      |  Interpreter<BB, Ctx>   |
                                      +----------+--------------+
                                                 |
                                        registry.Register()
                                                 |
                                      +----------v--------------+
                                      |  BehaviorDefinition     |
                                      |  (in BehaviorRegistry)  |
                                      +----------+--------------+
                                                 |
                                  per-entity tick (60 Hz)
                                                 |
                                      +----------v--------------+
                                      |  Interpreter.Tick()     |
                                      |  BehaviorTreeState      |
                                      |  BrainBlackboard        |
                                      +-------------------------+
```

### 9.2 Hot Reload Data Flow

```
+----------------------+
|  Editor saves .cs    |
+----------+-----------+
           |
           | file system watch
           v
+----------+-----------+
|  FbtAssemblyHotReloader|
|  new AssemblyLoadContext|
+----------+-----------+
           |
           | background thread
           v
+----------+-----------+      +------------------------+
| AiBehaviorFactory    |      | ActionRegistry         |
| BuildRegistrationAction----->| FbtActionRegistrar.RegisterAll |
|  (compile CPU work)  |      +------------------------+
+----------+-----------+
           |
           | staged Action<BehaviorRegistry>
           v
+----------+-----------+
| main thread drain    |
| registry.Register()  |
+----------+-----------+
           |
           v
+----------+-----------+
| BTreeHotReloadManager|
| TryReload()          |
| -- compare hashes -- |
+----------+-----------+
           |
    +------+------+
    |             |
    v             v
HardReset      SoftReload
(entity        (entity state
 states reset)  preserved)
```

### 9.3 HSM Compilation Data Flow

```
+-----------------------+
|  HsmBuilder           |
|  (fluent API)         |
|  .State() .On() etc.  |
+----------+------------+
           |
           | Build() -> StateMachineGraph
           v
+----------+------------+
|  HsmNormalizer        |
|  - resolve initials   |
|  - validate topology  |
+----------+------------+
           |
           | Normalize(graph)
           v
+----------+------------+
|  HsmFlattener         |
|  - build action table |
|  - build guard table  |
|  - flatten states     |
|  - flatten transitions|
|  - flatten regions    |
+----------+------------+
           |
           | FlattenedData
           v
+----------+------------+
|  HsmEmitter           |
|  - compute hashes     |
|  - build linker tables|
|  - emit definition    |
+----------+------------+
           |
           | HsmDefinitionBlob   MachineMetadata
           v                           v
+----------+------------+  +-----------+----------+
|  HsmKernel.UpdateBatch|  |  HsmDebugSession     |
|  (runtime execution)  |  |  (symbolication)     |
+-----------------------+  +----------------------+
```

---

## 10. Code Examples

### 10.1 Creating a BTree Programmatically

```csharp
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Hrot.AI.Behaviors.Brains;

// Build a simple "patrol and attack" behavior tree using the fluent API.
// TBlackboard = BrainBlackboard, TContext = BTreeContext.
var builder = new BTreeBuilder<BrainBlackboard, BTreeContext>();

builder.Selector(sel =>
{
    // Branch 1: engage if target is in range
    sel.Sequence(seq =>
    {
        seq.Condition("HasTarget",     visualId: new Guid("..."));
        seq.Condition("TargetInRange", visualId: new Guid("..."));
        seq.Action("AimAndFire",       visualId: new Guid("..."));
    }, visualId: new Guid("..."));

    // Branch 2: patrol otherwise
    sel.Sequence(seq =>
    {
        seq.Action("MoveToWaypoint", visualId: new Guid("..."));
        seq.Wait(2.0f,               visualId: new Guid("..."));
    }, visualId: new Guid("..."));
}, visualId: new Guid("..."));

// Produce the immutable blob.
BehaviorTreeBlob blob = builder.Build("OrcPatrolAndAttack");

// Wire up action delegates (normally done once via FbtActionRegistrar).
var registry = new ActionRegistry<BrainBlackboard, BTreeContext>();
registry.Register("HasTarget",     CgfNodes.HasTarget);
registry.Register("TargetInRange", CgfNodes.TargetInRange);
registry.Register("AimAndFire",    CgfNodes.AimAndFire);
registry.Register("MoveToWaypoint", CgfNodes.MoveToWaypoint);

// Create interpreter shared by all entities with this behavior.
var interpreter = new Interpreter<BrainBlackboard, BTreeContext>(blob, registry);

// Per-entity state (one per entity, allocated in a component pool).
var state = new BehaviorTreeState();
var blackboard = new BrainBlackboard();
var context = new BTreeContext(deltaTime: 0.016f);

// Tick once per frame.
NodeStatus result = interpreter.Tick(ref blackboard, ref state, ref context);
```

### 10.2 Creating an HSM Programmatically

```csharp
using Fhsm.Compiler;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

// Define event IDs for this machine.
const ushort Evt_Engaged    = 1;
const ushort Evt_Suppressed = 2;
const ushort Evt_Withdraw   = 3;

// Build the state machine using the fluent API.
var builder = new HsmBuilder("InfantryCombat");

builder.Event("Engaged",    Evt_Engaged);
builder.Event("Suppressed", Evt_Suppressed);
builder.Event("Withdraw",   Evt_Withdraw);

builder.RegisterAction("Actions.EnterIdleStance");
builder.RegisterAction("Actions.AcquireTarget");
builder.RegisterAction("Actions.FireAtTarget");
builder.RegisterAction("Actions.FindCover");
builder.RegisterAction("Actions.BeginRetreat");

builder.State("Idle")
    .Initial()
    .OnEntry("Actions.EnterIdleStance")
    .On("Engaged", "Attacking");

builder.State("Attacking")
    .OnEntry("Actions.AcquireTarget")
    .Activity("Actions.FireAtTarget")
    .On("Suppressed", "TakeCover")
    .On("Withdraw",   "Retreating");

builder.State("TakeCover")
    .OnEntry("Actions.FindCover")
    .On("Engaged", "Attacking");

builder.State("Retreating")
    .OnEntry("Actions.BeginRetreat");

// Compile to immutable blob.
StateMachineGraph graph = builder.Build();
HsmNormalizer.Normalize(graph);
HsmFlattener.FlattenedData flat = HsmFlattener.Flatten(graph);
HsmDefinitionBlob blob = HsmEmitter.Emit(flat);
MachineMetadata metadata = HsmEmitter.BuildMachineMetadata(graph);

// Per-entity instance (stored in component pool; 64 bytes for this simple machine).
HsmInstance64 instance = default;

// Enqueue an event from a game system.
var queue = new HsmEventQueue();
queue.Enqueue(new HsmEvent { EventId = Evt_Engaged, Priority = EventPriority.Normal });

// Tick the machine (drains event queue and runs activity actions).
var context = new MyHsmContext { DeltaTime = 0.016f };
HsmKernel.Update(blob, ref instance, context, deltaTime: 0.016f);
```

### 10.3 Assigning a Behavior to an Entity at Runtime

```csharp
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Events;
using Hrot.AI.Behaviors;

// Option A: Direct assignment via AssignBehaviorEvent (preferred for game code).
// The Brain system processes this event on the next tick and looks up the definition
// by name in the BehaviorRegistry.
var assignEvent = new AssignBehaviorEvent
{
    Entity       = tankEntity,
    BehaviorName = "MoveToLocation",
    JsonParams   = """{"lat":47.123,"lon":14.456,"speed":15.0}"""
};
eventBus.Raise(assignEvent);

// Option B: Via tactical intent (preferred for doctrine/command AI).
// The TacticalOrderMapper layer translates the intent to a concrete behavior name.
var intentEvent = new AssignTacticalIntentEvent
{
    Entity   = tankEntity,
    IntentId = "DefendArea",
    JsonParams = """{"centreLat":47.1,"centreLon":14.4,"radiusM":500}"""
};
eventBus.Raise(intentEvent);
// DefendAreaMapper maps MilitaryApc -> "ConvoyEscort" and raises AssignBehaviorEvent.

// Option C: Direct registration query (for diagnostics / editor tooling only).
BehaviorDefinition? def = behaviorRegistry.Lookup("MoveToLocation");
if (def != null)
{
    // Inspect compiled blob without ticking it.
    BehaviorTreeBlob blob = def.BTreeInterpreter.Blob;
    Console.WriteLine($"Tree '{blob.TreeName}' has {blob.Nodes.Length} nodes");
    Console.WriteLine($"StructureHash = 0x{blob.StructureHash:X8}");
}
```

---

## 11. Best Practices and Anti-patterns

### 11.1 Best Practices

**Behavior Tree design:**
- Use `Selector` at the top level with priority-ordered branches. The highest-priority
  behavior (combat) goes leftmost; the fallback (idle) goes rightmost.
- Use `Sequence` to express "do A, then B, then C" only when all steps must succeed.
  If any step is optional, use a `Selector` or `ForceSuccess` decorator instead.
- Keep action nodes focused on a single side effect. Prefer many small action delegates
  over one large delegate that does multiple things, because small delegates compose
  and can be reused across trees.
- Use `Subtree` references for behavior fragments shared across multiple trees (e.g.
  a `PatrolRoute` subtree used by several enemy types).
- Keep the blackboard shallow. Each BTree behavior should use a single DTO struct as
  its `TBlackboard`. Avoid putting the entire world state in the blackboard; use the
  context interface for queries that require external services.
- Use `ObserverSelector` instead of plain `Selector` when high-priority branches must
  abort lower-priority running branches immediately on condition change.

**HSM design:**
- Model only the states that have distinct entry/exit semantics or distinct activity
  behaviors. Do not create a state for every sub-phase; use a BTree for the task
  sequencing within a state.
- Register all events with stable IDs at machine definition time. Never use magic
  ushort literals in transition definitions; always use named constants.
- Use global transitions only for machine-wide emergency overrides (e.g. "unit
  destroyed" transitioning from any state to a terminal `Destroyed` state).
- Use orthogonal regions sparingly. They increase instance size and debugging complexity.
  Consider whether two separate machines (one per concern) is clearer.

**Hot reload:**
- Treat `StructureHash` changes (Hard reloads) as disruptive. During development, batch
  structural changes together and save once rather than saving after every node addition.
- Use `Cosmetic`-tier changes (layout only) for iterating on canvas positioning without
  affecting running entities.
- Always call `BTreeHotReloadManager.TryReload` / `HotReloadManager.TryReload` with a
  valid `hardResetAction` so entity states are properly initialized after a Hard reload.

**Registration:**
- Always perform behavior compilation on a background thread via
  `BuildRegistrationAction`, never directly on the main thread.
- Behavior integer IDs are permanent. Once published in a release build or referenced
  in saved scenarios, they must not be reused for a different behavior. Add new behaviors
  with new IDs; deprecate old ones without removing them.

### 11.2 Anti-patterns

**Anti-pattern: Allocating inside a node delegate.**
BTree node delegates are called at 60 Hz for every entity. Any allocation (LINQ,
closures, boxing) in a delegate will cause GC pressure proportional to entity count.
Use `ref` parameters, stack-allocated structs, and pre-allocated buffers instead.

**Anti-pattern: Querying world state directly from a blackboard field.**
The blackboard is a per-entity parameter store, not a world query interface. Expensive
lookups (finding nearby enemies, raycasts) must go through the `IAIContext` batched
async interfaces so the engine can coalesce them.

**Anti-pattern: Deep BTree subtree nesting.**
`BehaviorTreeState` has an 8-level `NodeIndexStack`. Nesting subtrees more than 7 levels
deep will overflow the stack and produce undefined behavior. If your behavior graph is
that deep, split it into independently registered top-level trees and use the Subtree
node to reference them by ID.

**Anti-pattern: Changing behavior IDs between hot reload cycles.**
`AiBehaviorFactory` defines IDs as constants. Hot-reload loads a new assembly version;
if a constant value changes, entities currently assigned the old ID will behave
incorrectly (they will tick the wrong blob) until the Brain component is reset.

**Anti-pattern: Rebuilding ActionRegistry on every tick.**
The `ActionRegistry` is an O(n) dictionary lookup per action node name binding. Build
it once in `BuildRegistrationAction` (the background compilation phase) and share the
same registry instance across all interpreters for the same blackboard/context type pair.

**Anti-pattern: Using HSM for purely reactive task logic.**
An HSM with 15 states each having a single activity action is really a BTree in disguise.
HSM overhead (LCA computation, entry/exit action chains) is higher than BTree dispatch.
Use a BTree for "what should I do this frame" logic; use an HSM for "what mode am I in".

**Anti-pattern: Skipping HsmNormalizer before HsmFlattener.**
`HsmNormalizer.Normalize(graph)` resolves initial child states, validates transition
targets, and assigns `FlatIndex` values. Calling `HsmFlattener.Flatten` on a non-normalized
graph will produce incorrect `StateDef.InitialChildIndex` values and may silently produce
a machine that never leaves its initial state.

---

## 12. Links to Individual Project Docs

| Project                    | Location                                                                      |
|----------------------------|-------------------------------------------------------------------------------|
| Fbt.Kernel                 | `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/`                                       |
| Fbt.Compiler               | `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/`                                     |
| Fbt.SourceGen              | `FDP/ExtDeps/FastBTree/src/Fbt.SourceGen/`                                    |
| Fhsm.Kernel                | `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/`                                        |
| Fhsm.Compiler              | `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/`                                      |
| Fhsm.SourceGen             | `FDP/ExtDeps/FastHSM/src/Fhsm.SourceGen/`                                     |
| Hrot.Editor.AiShared       | `Hrot/Editor/Hrot.Editor.AiShared/`                                           |
| Hrot.BTree.Editor          | `Hrot/Subsystems/AI/Hrot.BTree.Editor/`                                       |
| Hrot.Hsm.Editor            | `Hrot/Subsystems/AI/Hrot.Hsm.Editor/`                                         |
| Hrot.AI.Behaviors          | `Hrot/Subsystems/Hrot.AI.Behaviors/`                                          |
| Hrot.AI.Doctrines          | `Hrot/Subsystems/Hrot.AI.Doctrines/`                                          |
| FDP Core Framework         | `docs/projects/relationships/FDP-Core-Framework.md`                           |
| Hrot Simulation Pipeline   | `docs/projects/relationships/Hrot-Simulation-Pipeline.md`                     |
| FDP AI Guide               | `docs/AI_DEV_GUIDE.md`                                                        |
| HROT Architecture          | `docs/HROT architecture.md`                                                   |

---

## Appendix A: Key Type Summary

| Type                       | Assembly        | Role                                                    |
|----------------------------|-----------------|---------------------------------------------------------|
| `BehaviorTreeBlob`         | Fbt.Kernel      | Immutable compiled BTree asset, shared across entities  |
| `BehaviorTreeState`        | Fbt.Kernel      | Per-entity mutable BTree execution state (64 bytes)     |
| `NodeDefinition`           | Fbt.Kernel      | Single node in the BTree bytecode (8 bytes)             |
| `NodeType`                 | Fbt.Kernel      | Enum of all node types (byte)                           |
| `NodeStatus`               | Fbt.Kernel      | Failure / Success / Running                             |
| `NodeLogicDelegate<BB,Ctx>`| Fbt.Kernel      | Action/condition delegate signature                     |
| `IAIContext`               | Fbt.Kernel      | Context interface (time, physics, path queries)         |
| `ActionRegistry<BB,Ctx>`   | Fbt.Kernel      | Name -> delegate map for action/condition binding       |
| `Interpreter<BB,Ctx>`      | Fbt.Kernel      | Per-tree BTree executor                                 |
| `BTreeHotReloadManager`    | Fbt.Kernel      | Manages BTree blob hot reload lifecycle                 |
| `ReloadResult` (BTree)     | Fbt.Kernel      | NewTree / NoChange / SoftReload / HardReset             |
| `BTreeBuilder<BB,Ctx>`     | Fbt.Compiler    | Fluent API for constructing BehaviorTreeBlob            |
| `BTreeSchema`              | Fbt.Compiler    | Schema of discovered actions/conditions from assemblies |
| `TreeCompiler`             | Fbt.Kernel      | JSON -> BehaviorTreeBlob compilation                    |
| `BehaviorTreeGraph`        | Fbt.Compiler    | Mutable DOM for the authoring tool                      |
| `HsmDefinitionBlob`        | Fhsm.Kernel     | Immutable compiled HSM definition, shared across entities|
| `HsmInstance64/128/256`    | Fhsm.Kernel     | Per-entity mutable HSM execution state                  |
| `StateDef`                 | Fhsm.Kernel     | Single state ROM entry (32 bytes)                       |
| `HsmEvent`                 | Fhsm.Kernel     | Event struct passed to machines (24 bytes)              |
| `HsmKernel`                | Fhsm.Kernel     | Public batch-update API                                 |
| `HotReloadManager` (HSM)   | Fhsm.Kernel     | Manages HSM blob hot reload lifecycle                   |
| `HsmBuilder`               | Fhsm.Compiler   | Fluent API for constructing StateMachineGraph           |
| `HsmFlattener`             | Fhsm.Compiler   | Graph -> flat ROM arrays (FlattenedData)                |
| `HsmEmitter`               | Fhsm.Compiler   | FlattenedData -> HsmDefinitionBlob                      |
| `HsmNormalizer`            | Fhsm.Compiler   | Validates and resolves graph before flattening          |
| `MachineMetadata`          | Fhsm.Kernel     | Name tables for diagnostic symbolication                |
| `IEditableAsset`           | AiShared        | Common interface for all editor assets                  |
| `AssetCatalog`             | AiShared        | Holds all open editor assets                            |
| `IAiDebugSession`          | AiShared        | Breakpoints, step control, pause/continue               |
| `AiDebugSessionBase`       | AiShared        | Abstract base with common breakpoint and pause logic    |
| `HotReloadClassifier`      | AiShared        | Classifies reload tier from hash deltas                 |
| `HotReloadTier`            | AiShared        | Cosmetic / Soft / Hard                                  |
| `IFluentCSharpEmitter<T>`  | AiShared        | Contract for C# emitters                               |
| `BehaviorTreeAsset`        | BTree.Editor    | Mutable editor model of a behavior tree                 |
| `BTreeEditorNode`          | BTree.Editor    | Visual node in the BTree canvas                         |
| `BTreeEditorPill`          | BTree.Editor    | Decorator pill attached to a canvas node                |
| `BTreeFluentEmitter`       | BTree.Editor    | BehaviorTreeAsset -> C# source code                     |
| `BTreeDebugSession`        | BTree.Editor    | Live debug session with ring buffers and heatmap        |
| `BTreeNodeCatalog`         | BTree.Editor    | NodeEdit palette entries for BTree canvas               |
| `HsmAsset`                 | Hsm.Editor      | Mutable editor model of a state machine                 |
| `HsmFluentEmitter`         | Hsm.Editor      | HsmAsset -> C# source code                             |
| `HsmDebugSession`          | Hsm.Editor      | Live debug session for HSM active state tracking        |
| `AiBehaviorFactory`        | AI.Behaviors    | Registers all behaviors into BehaviorRegistry           |
| `BehaviorDefinition`       | Fdp.Toolkit     | Holds Interpreter or HsmDefinitionBlob + metadata       |
| `DefendAreaMapper`         | AI.Behaviors    | Maps "DefendArea" tactical intent to concrete behavior  |
| `ITacticalOrderMapper`     | Fdp.Toolkit     | Interface for intent -> behavior name mapping           |
