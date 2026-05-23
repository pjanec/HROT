# Fbt.Kernel

| | |
|---|---|
| **Project path** | `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Fbt.Kernel.csproj` |
| **Namespace root** | `Fbt`, `Fbt.Runtime`, `Fbt.HotReload`, `Fbt.Utilities`, `Fbt.Serialization` |
| **Target framework** | net8.0 |
| **Date** | 2026-05-23 |

---

## README Validation

| Location | Status |
|---|---|
| `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/` | **Missing** — no README.md in this folder |
| `FDP/ExtDeps/FastBTree/src/` | **Missing** — no README.md in the src folder |
| `FDP/ExtDeps/FastBTree/` | **Present and up-to-date** — the root `README.md` accurately describes the overall FastBTree library, its zero-allocation design goals, node types, and quick-start guide. The document aligns with the code examined. The `BehaviorTreeState` 64-byte size, `NodeDefinition` 8-byte size, and available node types all match the implementation. |

---

## Executive Overview

`Fbt.Kernel` is the **runtime execution engine** for the FastBTree behavior-tree library. It provides every primitive needed to represent, execute, trace, and hot-reload behavior trees at runtime, without allocating on the managed heap during normal execution.

The library targets real-time AI systems — specifically HROT unit AI — where thousands of entities may tick their behavior trees every simulation frame. The design choices flow from a single constraint: **zero heap allocations in the hot path**.

Key engineering decisions:

- The entire per-entity execution state fits in **64 bytes** (`BehaviorTreeState`), exactly one CPU cache line. Each entity owns one such struct and nothing else.
- The compiled tree (`BehaviorTreeBlob`) is **shared and immutable** across all entities using the same tree definition. One blob serves N entities.
- The **flat bytecode array** (`NodeDefinition[]`, depth-first order) allows the interpreter to traverse the tree using simple index arithmetic rather than pointer chasing.
- Every `NodeDefinition` is **8 bytes** and cache-aligned. A 20-node tree fits in ~160 bytes of contiguous memory.
- Leaf logic is expressed as **delegates** (`NodeLogicDelegate<TBlackboard, TContext>`), resolved once at interpreter construction time and cached in an array. No dictionary lookups occur at tick time.
- The **generic type parameters** `TBlackboard` and `TContext` are resolved at JIT time, so the interpreter produces specialized machine code per entity type with no boxing.

Performance measurements (README, Intel i7-7700HQ, .NET 10):
- 3-node sequence tick: ~30 ns, 0 bytes allocated
- 21-node complex tree tick: ~100 ns, 0 bytes allocated
- Resume from Running: ~22 ns, 0 bytes allocated

---

## Architecture

### Conceptual Layers

```
+---------------------------------------------------------------+
|                      USER / AI SYSTEM                         |
|  Per-entity: TBlackboard (data), BehaviorTreeState (state)    |
+---------------------------------------------------------------+
           |                           |
           v                           v
+---------------------+   +--------------------------+
|  Interpreter<BB,Ctx>|   |  ITreeRunner<BB,Ctx>     |
|  (hot path, no GC)  |   |  (interface contract)    |
+---------------------+   +--------------------------+
           |
           | reads (immutable, shared)
           v
+-------------------------------------------------------+
|              BehaviorTreeBlob                         |
|  NodeDefinition[]   MethodNames[]                     |
|  FloatParams[]      IntParams[]                       |
|  SubtreeAssetIds[]  DebugMetadata[]?                  |
+-------------------------------------------------------+
           |
           | resolved at construction
           v
+-------------------------------------------------------+
|  NodeLogicDelegate<TBlackboard, TContext>[]            |
|  (cached action/condition function pointers)           |
+-------------------------------------------------------+
```

### Execution Flow Per Tick

```
Interpreter.Tick()
  |
  +-- Hot-reload bounds check (RunningNodeIndex vs Nodes.Length)
  |
  +-- Paused check (BehaviorInstanceFlags.Paused)
  |
  +-- ExecuteNode(0, ...)   <- always starts at root (index 0)
        |
        +-- switch(node.Type)
              |
              +-- Sequence  --> iterate children, skip completed via RunningNodeIndex
              +-- Selector  --> iterate children, skip failed via RunningNodeIndex
              +-- Action    --> call cached delegate, return its NodeStatus
              +-- Condition --> call cached delegate, return its NodeStatus
              +-- Inverter  --> call child, flip Success<->Failure
              +-- Wait      --> check elapsed time via AsyncToken, return Running/Success
              +-- Repeater  --> loop child using LocalRegisters[0] as iteration counter
              +-- Parallel  --> run all children, use LocalRegisters[3] bitfield for child state
              +-- Cooldown  --> check last-exec time via AsyncToken, gate child
              +-- ForceSuccess/ForceFailure --> run child, override result
              +-- UntilSuccess/UntilFailure --> re-run child each tick until condition
              |
              +-- (result != Running) --> clear RunningNodeIndex
```

### Resume Semantics (Resumable Execution)

The interpreter supports resumable execution: a tree that returns `Running` in one tick picks up exactly where it left off in the next tick without re-evaluating previously-completed subtrees.

```
RunningNodeIndex != 0  =>  tree is in Running state
RunningNodeIndex == 0  =>  tree is idle (completed or not started)

Sequence resume: skip children whose subtree range is BEFORE RunningNodeIndex
Selector resume: skip children whose subtree range is BEFORE RunningNodeIndex
Wait resume:     compare ctx.Time against stored start time (AsyncToken in AsyncData)
Repeater resume: iteration counter in LocalRegisters[0]
Parallel resume: child completion bitfield in LocalRegisters[3]
```

### BehaviorTreeState Memory Layout

The struct is exactly 64 bytes, matching one CPU cache line:

```
+---+---+---+---+---+---+---+---+  Offset 0  (8 bytes header)
| RunningNodeIdx| StackPtr  |Version(u32)|
+---+---+---+---+---+---+---+---+
| NodeIndexStack[0..7]           |  Offset 8  (16 bytes, 8x ushort)
+---+---+---+---+---+---+---+---+
| LocalRegisters[0..3]           |  Offset 24 (16 bytes, 4x int)
+---+---+---+---+---+---+---+---+
| AsyncHandles[0..2] (ulong x3)  |  Offset 40 (24 bytes)
+---+---+---+---+---+---+---+---+
                                    Total: 64 bytes
```

Field usage by node type:
- `RunningNodeIndex` - index of the currently-Running leaf or composite
- `LocalRegisters[0]` - Repeater iteration counter
- `LocalRegisters[3]` - Parallel child completion bitfield (bit i = child i succeeded, bit i+16 = child i finished)
- `AsyncHandles[0]` (`AsyncData`) - Wait start time or Cooldown last-exec time (packed via `AsyncToken`)
- `InstanceFlags` overlays `AsyncHandles[2]` (offset 56) — reserved slot, never written by production code

### NodeDefinition Memory Layout

```
+-------+--------+---------------+-------------------+
| Type  | Child  | SubtreeOffset |   PayloadIndex    |
| 1 byte| 1 byte |    2 bytes    |      4 bytes      |
+-------+--------+---------------+-------------------+
   0        1         2   3           4  5  6  7
Total: 8 bytes
```

`SubtreeOffset` encodes the count of nodes in this node's entire subtree (self + all descendants). To jump to the next sibling from index `i`: `nextSibling = i + Nodes[i].SubtreeOffset`.

### Flat Bytecode Layout (Depth-First Order)

A tree `Selector -> [Sequence -> [Cond, Act], Patrol]` is stored as:

```
Index: [0]Selector  [1]Sequence  [2]Condition  [3]Action  [4]Patrol
                     |                                    ^
                     +----SubtreeOffset=3---------------->|
```

This layout means the interpreter only needs a simple index to navigate: no pointer chasing, no recursive object graph traversal.

---

## Source Structure

### Root Level (`Fbt` namespace)

| File | Type | Description |
|---|---|---|
| `NodeType.cs` | `enum NodeType : byte` | All node type codes (Root, Selector, Sequence, Parallel, ObserverSelector, Action, Condition, Wait, Inverter, Repeater, Cooldown, ForceSuccess, ForceFailure, UntilSuccess, UntilFailure, Service, Observer, Subtree) |
| `NodeStatus.cs` | `enum NodeStatus : byte` | Execution result: Failure=0, Success=1, Running=2 |
| `NodeDefinition.cs` | `struct NodeDefinition` | 8-byte bytecode node: Type, ChildCount, SubtreeOffset, PayloadIndex |
| `BehaviorTreeBlob.cs` | `class BehaviorTreeBlob` | Compiled, shared, immutable tree asset |
| `BehaviorTreeState.cs` | `struct BehaviorTreeState` | 64-byte per-entity execution state |
| `BehaviorInstanceFlags.cs` | `enum BehaviorInstanceFlags : byte` | Control flags: None, Paused |
| `BehaviorTreeBuildException.cs` | `class BehaviorTreeBuildException` | Thrown on compilation failure |
| `AsyncToken.cs` | `struct AsyncToken` | Packs async request ID + tree version into a ulong for zombie detection |
| `NodeLogicDelegate.cs` | `delegate NodeLogicDelegate<BB,Ctx>` | Function signature for all action/condition implementations |
| `IAIContext.cs` | `interface IAIContext` | External services: time, raycasts, pathfinding, parameter lookup |
| `ITreeTracer.cs` | `interface ITreeTracer` | Trace events emitted by the kernel: node evaluated, scope push/pop, wait start/complete |
| `NodeDebugMetadata.cs` | `class NodeDebugMetadata` | Per-node debug info: label, source file, line, visual ID |
| `BTreeTraceOpCode.cs` | `enum BTreeTraceOpCode : byte` | Trace event codes used by diagnostic systems |
| `PathResult.cs` | `struct PathResult` | Pathfinding query result: IsReady, Success, PathId, PathLength |
| `RaycastResult.cs` | `struct RaycastResult` | Raycast query result: IsReady, Hit, HitPoint, HitNormal, Distance |
| `SharedAiAttributes.cs` | `class SharedAiConditionAttribute`, `SharedAiActionAttribute`, `SharedAiHeavyActionAttribute` | Attributes for cross-system (BTree + HSM) shared AI methods |

### `Fbt.Runtime` namespace (`Runtime/`)

| File | Type | Description |
|---|---|---|
| `ITreeRunner.cs` | `interface ITreeRunner<BB,Ctx>` | Single-method contract: `Tick(ref BB, ref State, ref Ctx) : NodeStatus` |
| `ActionRegistry.cs` | `class ActionRegistry<BB,Ctx>` | Name-to-delegate dictionary; `Register`, `TryGetAction`, `RegisterCondition`, `TryGetCondition` |
| `Interpreter.cs` | `class Interpreter<BB,Ctx>` | The execution engine; binds delegates at construction, ticks the tree with no GC |

### `Fbt.HotReload` namespace (`HotReload/`)

| File | Type | Description |
|---|---|---|
| `ReloadResult.cs` | `enum ReloadResult` | NewTree, NoChange, SoftReload, HardReset |
| `BTreeHotReloadManager.cs` | `class BTreeHotReloadManager` | Compares structure/param hashes to determine reload type; calls hardResetAction on entity spans |
| `FbtAssemblyHotReloader.cs` | `class FbtAssemblyHotReloader` | Watches a directory for new DLLs, loads them in an AssemblyLoadContext, fires reload events on app thread via DrainPendingCallbacks |

### `Fbt.Utilities` namespace (`Utilities/`)

| File | Type | Description |
|---|---|---|
| `TreeVisualizer.cs` | `static class TreeVisualizer` | Produces an indented text dump of a `BehaviorTreeBlob` for debugging |

### `Fbt.Serialization` namespace (`Serialization/`)

| File | Type | Description |
|---|---|---|
| `JsonTreeData.cs` | `class JsonTreeData`, `class JsonNode` | POCOs for `System.Text.Json` deserialization of the JSON authoring format |
| `BuilderNode.cs` | `class BuilderNode` | Intermediate mutable tree representation used during compilation (not exported) |
| `TreeCompiler.cs` | `static class TreeCompiler` | Converts JSON or `BuilderNode` trees to `BehaviorTreeBlob`; calculates hashes; validates |
| `TreeValidator.cs` | `static class TreeValidator`, `class ValidationResult` | Post-compilation validation: subtree offsets, payload indices, nested Parallel/Repeater detection |
| `BinaryTreeSerializer.cs` | `static class BinaryTreeSerializer` | Save/load `BehaviorTreeBlob` to/from a binary stream with magic-byte header and version check |

### `Fbt` attributes namespace (`Attributes/`)

| File | Type | Description |
|---|---|---|
| `BTreeActionAttribute.cs` | `[AttributeUsage(Method)]` | Marks a static method as an auto-registrable action; consumed by `Fbt.SourceGen` |
| `BTreeConditionAttribute.cs` | `[AttributeUsage(Method)]` | Marks a static method as an auto-registrable condition; consumed by `Fbt.SourceGen` |
| `BTreeDefinitionAttribute.cs` | `[AttributeUsage(Method)]` | Marks a method returning `BTreeBuilder` or `BehaviorTreeBlob` as a named tree catalog entry |
| `FbtRegistrarAttribute.cs` | `[AttributeUsage(Class)]` | Applied by `Fbt.SourceGen` to the emitted registrar class; used by `FbtAutoDiscovery` for reflection scanning |

---

## Public API Reference

### `NodeType` (enum)

```csharp
public enum NodeType : byte
{
    Root = 0,
    Selector = 1,       // Children: execute until one succeeds
    Sequence = 2,       // Children: execute until one fails
    Parallel = 3,       // Children: execute all concurrently
    ObserverSelector = 5, // Selector with abort-on-priority-change
    Action = 10,        // Leaf: perform an action
    Condition = 11,     // Leaf: check a condition
    Wait = 12,          // Leaf: wait for N seconds
    Inverter = 20,      // Decorator: flip child result
    Repeater = 21,      // Decorator: repeat child N times (or forever)
    Cooldown = 22,      // Decorator: limit child execution frequency
    ForceSuccess = 23,  // Decorator: always return Success
    ForceFailure = 24,  // Decorator: always return Failure
    UntilSuccess = 25,  // Decorator: repeat until Success
    UntilFailure = 26,  // Decorator: repeat until Failure
    Service = 30,       // Runs a service periodically alongside child
    Observer = 31,      // Aborts execution if condition changes
    Subtree = 40        // Delegates to an external named tree
}
```

### `NodeStatus` (enum)

```csharp
public enum NodeStatus : byte
{
    Failure = 0,
    Success = 1,
    Running = 2
}
```

### `NodeDefinition` (struct, 8 bytes)

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct NodeDefinition
{
    public NodeType Type;        // 1 byte
    public byte ChildCount;      // 1 byte
    public ushort SubtreeOffset; // 2 bytes: next sibling = self + SubtreeOffset
    public int PayloadIndex;     // 4 bytes: index into MethodNames/FloatParams/IntParams
}
```

### `BehaviorTreeBlob` (class, shared/immutable)

```csharp
public class BehaviorTreeBlob
{
    public string TreeName;
    public int Version;
    public int StructureHash;       // Hash of node topology
    public int ParamHash;           // Hash of float/int parameters
    public NodeDefinition[] Nodes;  // Flat bytecode array (depth-first)
    public string[] MethodNames;    // Action/Condition delegate keys
    public float[] FloatParams;     // Wait durations, Cooldown durations
    public int[] IntParams;         // Repeat counts, Parallel policies
    public string[] SubtreeAssetIds;
    [NonSerialized] public object? CompiledDelegate;
    [NonSerialized] public NodeDebugMetadata[]? DebugMetadata;
}
```

### `BehaviorTreeState` (struct, 64 bytes, per-entity)

```csharp
[StructLayout(LayoutKind.Explicit, Size = 64)]
public unsafe struct BehaviorTreeState
{
    [FieldOffset(0)]  public ushort RunningNodeIndex;
    [FieldOffset(2)]  public ushort StackPointer;
    [FieldOffset(4)]  public uint TreeVersion;
    [FieldOffset(8)]  public fixed ushort NodeIndexStack[8];
    [FieldOffset(24)] public fixed int LocalRegisters[4];
    [FieldOffset(40)] public fixed ulong AsyncHandles[3];
    [FieldOffset(56)] public BehaviorInstanceFlags InstanceFlags;

    public ulong AsyncData { get; set; }          // AsyncHandles[0]
    public ushort CurrentRunningNode { get; set; } // NodeIndexStack[StackPointer]
    public void Reset();
}
```

### `NodeLogicDelegate<TBlackboard, TContext>` (delegate)

```csharp
public delegate NodeStatus NodeLogicDelegate<TBlackboard, TContext>(
    ref TBlackboard blackboard,
    ref BehaviorTreeState state,
    ref TContext context,
    int paramIndex)
    where TBlackboard : struct
    where TContext : struct, IAIContext;
```

### `IAIContext` (interface)

```csharp
public interface IAIContext
{
    float DeltaTime { get; }
    float Time { get; }
    int FrameCount { get; }
    int RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance);
    RaycastResult GetRaycastResult(int requestId);
    int RequestPath(Vector3 from, Vector3 to);
    PathResult GetPathResult(int requestId);
    float GetFloatParam(int index);
    int GetIntParam(int index);
}
```

### `ITreeTracer` (interface)

```csharp
public interface ITreeTracer
{
    void TraceNodeEvaluated(int nodeIndex, NodeStatus status);
    void TraceScopePushed(ushort newStackDepth);
    void TraceScopePopped(ushort newStackDepth);
    void TraceWaitStarted(int nodeIndex, float duration);
    void TraceWaitCompleted(int nodeIndex, float duration);
}
```

### `ITreeRunner<TBlackboard, TContext>` (interface)

```csharp
public interface ITreeRunner<TBlackboard, TContext>
    where TBlackboard : struct
    where TContext : struct, IAIContext
{
    NodeStatus Tick(
        ref TBlackboard blackboard,
        ref BehaviorTreeState state,
        ref TContext context);
}
```

### `Interpreter<TBlackboard, TContext>` (class)

```csharp
public class Interpreter<TBlackboard, TContext> : ITreeRunner<TBlackboard, TContext>
    where TBlackboard : struct
    where TContext : struct, IAIContext, ITreeTracer
{
    public BehaviorTreeBlob Blob { get; }

    public Interpreter(BehaviorTreeBlob blob, ActionRegistry<TBlackboard, TContext> registry);

    public NodeStatus Tick(
        ref TBlackboard blackboard,
        ref BehaviorTreeState state,
        ref TContext context);
}
```

Notes:
- `TContext` must implement both `IAIContext` and `ITreeTracer`.
- Delegates are bound from `registry` at construction time; subsequent registry changes are not reflected.
- The interpreter is **thread-safe for concurrent reads** if each caller uses a different `state` and `blackboard`. The blob and delegate array are read-only after construction.

### `ActionRegistry<TBlackboard, TContext>` (class)

```csharp
public class ActionRegistry<TBlackboard, TContext>
    where TBlackboard : struct
    where TContext : struct, IAIContext
{
    public void Register(string methodName, NodeLogicDelegate<TBlackboard, TContext> action);
    public bool TryGetAction(string methodName, out NodeLogicDelegate<...> action);
    public void RegisterCondition(string key, NodeLogicDelegate<...> condition);
    public bool TryGetCondition(string key, out NodeLogicDelegate<...> condition);
}
```

### `AsyncToken` (struct)

```csharp
public readonly struct AsyncToken
{
    public readonly int RequestID;
    public readonly uint Version;

    public AsyncToken(int requestId, uint version);
    public AsyncToken(ulong packed);

    public ulong Pack();
    public ulong PackedValue { get; }
    public float FloatA { get; }        // Reinterprets RequestID as float

    public static AsyncToken Unpack(ulong packed);
    public static AsyncToken FromFloat(float a, int b);
    public bool IsValid(uint currentTreeVersion);
}
```

### `BTreeHotReloadManager` (class)

```csharp
public class BTreeHotReloadManager
{
    public ReloadResult TryReload<TState>(
        string treeName,
        BehaviorTreeBlob? newBlob,
        Span<TState> liveInstances,
        SpanResetAction<TState>? hardResetAction)
        where TState : unmanaged;

    public BehaviorTreeBlob? GetKnownBlob(string treeName);
}

public delegate void SpanResetAction<TState>(Span<TState> span, int index);
```

### `FbtAssemblyHotReloader` (class)

```csharp
public sealed class FbtAssemblyHotReloader : IDisposable
{
    public delegate IEnumerable<(string treeName, BehaviorTreeBlob blob)>
        AssemblyReloadHandler(Type registrarType, Assembly newAssembly);

    public event Action<string>? OnReloadCompleted;
    public event Action<string, Exception>? OnReloadFailed;
    public WeakReference<AssemblyLoadContext>? PreviousAlcRef { get; }

    public FbtAssemblyHotReloader(string watchDirectory, AssemblyReloadHandler handler);
    public void DrainPendingCallbacks();
    public void Dispose();
}
```

### `TreeCompiler` (static class, in `Fbt.Serialization`)

```csharp
public static class TreeCompiler
{
    public static BehaviorTreeBlob CompileFromJson(string jsonText);
    public static BehaviorTreeBlob FlattenToBlob(BuilderNode root, string treeName);
}
```

### `BinaryTreeSerializer` (static class, in `Fbt.Serialization`)

```csharp
public static class BinaryTreeSerializer
{
    public static void Save(BehaviorTreeBlob blob, string filePath);
    public static void Save(BehaviorTreeBlob blob, Stream stream);
    public static BehaviorTreeBlob Load(string filePath);
    public static BehaviorTreeBlob Load(Stream stream);
}
```

Binary format header: `FBT\0` (4 bytes), version (int32), StructureHash (int32), ParamHash (int32), TreeName (length-prefixed string).

### `TreeValidator` (static class, in `Fbt.Serialization`)

```csharp
public static class TreeValidator
{
    public static ValidationResult Validate(BehaviorTreeBlob blob);
}

public class ValidationResult
{
    public bool IsValid { get; }           // Errors.Count == 0
    public bool HasWarnings { get; }
    public List<string> Errors { get; }
    public List<string> Warnings { get; }
}
```

Validated constraints:
- Every `SubtreeOffset` must be nonzero and must not exceed the node array bounds.
- `PayloadIndex` must be within range for `MethodNames`, `FloatParams`, or `IntParams` depending on node type.
- Nested `Parallel` nodes are detected and reported as warnings (treated as errors by `FlattenToBlob`).
- Nested `Repeater` nodes are detected and reported as warnings (treated as errors by `FlattenToBlob`).
- `Parallel` nodes with more than 16 children receive a warning.

### `TreeVisualizer` (static class, in `Fbt.Utilities`)

```csharp
public static class TreeVisualizer
{
    public static string Visualize(BehaviorTreeBlob blob);
}
```

Produces indented text output showing each node's index, type, method name (if applicable), parameters, child count, and subtree offset.

### Attributes

```csharp
[AttributeUsage(AttributeTargets.Method)]
public sealed class BTreeActionAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public sealed class BTreeConditionAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Method)]
public sealed class BTreeDefinitionAttribute : Attribute
{
    public string TreeName { get; }
    public BTreeDefinitionAttribute(string treeName);
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class FbtRegistrarAttribute : Attribute { }
```

### `SharedAiConditionAttribute` / `SharedAiActionAttribute` / `SharedAiHeavyActionAttribute`

Defined in `Fbt.Kernel` namespace. These attributes mark static methods as shared AI behaviors usable from both BTree and HSM (Hierarchical State Machine) systems. The `Fbt.SourceGen` source generator reads these attributes and emits adapters that project a specific field of a larger blackboard DTO to the required parameter type.

```csharp
// Marks a method that can be used as a condition from both BTree and HSM
[SharedAiCondition(typeof(MyBlackboard), "CombatData")]
static bool IsEnemyVisible(ref CombatData data, Entity self, EntityRepository repo) { ... }
```

---

## Dependencies

`Fbt.Kernel` has **no external package dependencies**. It depends only on the .NET 8 BCL:

| Assembly | Usage |
|---|---|
| `System.Numerics` | `Vector3` in `IAIContext`, `RaycastResult` |
| `System.Text.Json` | JSON deserialization in `TreeCompiler`, `JsonTreeData` |
| `System.Runtime.InteropServices` | `StructLayout`, `FieldOffset` on `NodeDefinition`, `BehaviorTreeState` |
| `System.Runtime.Loader` | `AssemblyLoadContext` in `FbtAssemblyHotReloader` |
| `System.IO` | File and stream operations in `BinaryTreeSerializer` |
| `System.Security.Cryptography` | Hash computation in `TreeCompiler` |

Project-level settings:
- `AllowUnsafeBlocks = true` — required for `fixed` arrays in `BehaviorTreeState`
- `Nullable = enable`
- `TreatWarningsAsErrors = true`

---

## Usage Examples

### Example 1: Define a tree in JSON, compile, and tick

```csharp
using Fbt;
using Fbt.Runtime;
using Fbt.Serialization;

// --- Step 1: Define the tree in JSON ---
string json = """
{
  "TreeName": "GuardAI",
  "Root": {
    "Type": "Selector",
    "Children": [
      {
        "Type": "Sequence",
        "Children": [
          { "Type": "Condition", "Action": "IsEnemyVisible" },
          { "Type": "Action",    "Action": "Attack" }
        ]
      },
      { "Type": "Action", "Action": "Patrol" }
    ]
  }
}
""";

// --- Step 2: Compile JSON to blob ---
BehaviorTreeBlob blob = TreeCompiler.CompileFromJson(json);

// --- Step 3: Register action/condition delegates ---
var registry = new ActionRegistry<GuardBlackboard, GuardContext>();

registry.Register("IsEnemyVisible",
    (ref GuardBlackboard bb, ref BehaviorTreeState st, ref GuardContext ctx, int _) =>
        bb.EnemyInRange ? NodeStatus.Success : NodeStatus.Failure);

registry.Register("Attack",
    (ref GuardBlackboard bb, ref BehaviorTreeState st, ref GuardContext ctx, int _) =>
    {
        ctx.RequestAttack(bb.EnemyId);
        return NodeStatus.Success;
    });

registry.Register("Patrol",
    (ref GuardBlackboard bb, ref BehaviorTreeState st, ref GuardContext ctx, int _) =>
    {
        ctx.MoveToNextWaypoint();
        return NodeStatus.Running; // ongoing
    });

// --- Step 4: Create interpreter (shared for all guards using this tree) ---
var interpreter = new Interpreter<GuardBlackboard, GuardContext>(blob, registry);

// --- Step 5: Per-entity state ---
var blackboard = new GuardBlackboard { EnemyInRange = false };
var state      = new BehaviorTreeState();
var context    = new GuardContext(deltaTime: 0.016f);

// --- Step 6: Tick each frame ---
NodeStatus result = interpreter.Tick(ref blackboard, ref state, ref context);
// result == NodeStatus.Running (patrol is ongoing)
```

### Example 2: Wait node and timing

```csharp
// JSON with a wait node:
string json = """
{
  "TreeName": "PatrolLoop",
  "Root": {
    "Type": "Sequence",
    "Children": [
      { "Type": "Action", "Action": "MoveToWaypoint" },
      { "Type": "Wait",   "WaitTime": 2.5 },
      { "Type": "Action", "Action": "LookAround" }
    ]
  }
}
""";

BehaviorTreeBlob blob = TreeCompiler.CompileFromJson(json);

// The Wait node stores start time in BehaviorTreeState.AsyncData on first evaluation.
// On subsequent ticks it compares ctx.Time against start + 2.5 seconds.
// No allocation occurs; the 8-byte AsyncToken is stored inside BehaviorTreeState.
```

### Example 3: Parallel node with policy

```csharp
// RequireAll policy (0): succeed only when ALL children succeed
// RequireOne policy (1): succeed when ANY child succeeds

string json = """
{
  "TreeName": "CombatAndComms",
  "Root": {
    "Type": "Parallel",
    "Policy": 0,
    "Children": [
      { "Type": "Action", "Action": "FireWeapon" },
      { "Type": "Action", "Action": "ReportContact" }
    ]
  }
}
""";

// Parallel tracks per-child completion in LocalRegisters[3] (bitfield).
// Bits 0-15: success flags. Bits 16-31: finished flags.
// No extra allocation; up to 16 children are tracked in a single int32.
```

### Example 4: Hot reload — handle parameter change at runtime

```csharp
var reloadManager = new BTreeHotReloadManager();

// Register the current blob
Span<BehaviorTreeState> entityStates = GetAllGuardStates();
reloadManager.TryReload("GuardAI", currentBlob, entityStates,
    hardResetAction: static (span, i) => span[i] = default);

// Later, user modifies the patrol wait time from 2.5s to 1.0s.
// Only ParamHash changes, StructureHash stays the same.
BehaviorTreeBlob newBlob = TreeCompiler.CompileFromJson(updatedJson);

ReloadResult result = reloadManager.TryReload("GuardAI", newBlob, entityStates,
    hardResetAction: static (span, i) => span[i] = default);

// result == ReloadResult.SoftReload
// Entity states are preserved; the interpreter will pick up the new FloatParams
// from the new blob on the next tick.
```

### Example 5: Serialize to binary and load back

```csharp
// Save to disk (fast binary format)
BinaryTreeSerializer.Save(blob, "guard-ai.fbt");

// Load from disk
BehaviorTreeBlob loaded = BinaryTreeSerializer.Load("guard-ai.fbt");

// Validate integrity
var validation = TreeValidator.Validate(loaded);
if (!validation.IsValid)
    throw new InvalidOperationException(validation.ToString());
```

### Example 6: Visualize a compiled tree for debugging

```csharp
string dump = TreeVisualizer.Visualize(blob);
Console.WriteLine(dump);

// Sample output:
// Tree: GuardAI
// Nodes: 5, Methods: 3
//
// [0] Selector | Children: 2, Offset: 5
//   [1] Sequence | Children: 2, Offset: 3
//     [2] Condition "IsEnemyVisible" | Children: 0, Offset: 1
//     [3] Action "Attack" | Children: 0, Offset: 1
//   [4] Action "Patrol" | Children: 0, Offset: 1
```

---

## Node Behavior Reference

### Composite Nodes

```
+--------------------+--------------+--------------------+--------------------------+
| Node               | Returns      | Children consumed  | Resume behavior          |
+--------------------+--------------+--------------------+--------------------------+
| Sequence           | Success if   | All, left to right | Skips completed children |
|                    | all succeed  | until one fails    | (RunningNodeIndex test)  |
+--------------------+--------------+--------------------+--------------------------+
| Selector           | Success if   | Left to right      | Skips failed children    |
|                    | one succeeds | until one succeeds | (RunningNodeIndex test)  |
+--------------------+--------------+--------------------+--------------------------+
| Parallel(0)        | Success if   | All simultaneously | Bitfield in Reg[3]       |
| RequireAll         | all succeed  | Fail if any fails  |                          |
+--------------------+--------------+--------------------+--------------------------+
| Parallel(1)        | Success if   | All simultaneously | Bitfield in Reg[3]       |
| RequireOne         | one succeeds | Fail if all fail   |                          |
+--------------------+--------------+--------------------+--------------------------+
| ObserverSelector   | Same as      | Same as Selector   | Same as Selector         |
|                    | Selector     |                    | (abort logic TBD)        |
+--------------------+--------------+--------------------+--------------------------+
```

### Decorator Nodes

```
+--------------------+--------------+-----------------------------------------+
| Node               | Returns      | Behavior                                |
+--------------------+--------------+-----------------------------------------+
| Inverter           | Flipped      | Success->Failure, Failure->Success      |
|                    |              | Running passes through unchanged        |
+--------------------+--------------+-----------------------------------------+
| Repeater(N)        | Success      | Re-executes child N times (or forever   |
|                    |              | if N<0). Failure aborts. Counter in     |
|                    |              | LocalRegisters[0].                      |
+--------------------+--------------+-----------------------------------------+
| Cooldown(T)        | Failure if   | Gates child; only executes if T seconds |
|                    | on cooldown  | have passed since last success.         |
|                    | else child   | Last time stored in AsyncData.          |
+--------------------+--------------+-----------------------------------------+
| Wait(T)            | Success      | Blocks for T seconds. Start time in     |
|                    | after T sec  | AsyncData (AsyncToken). Zero alloc.     |
+--------------------+--------------+-----------------------------------------+
| ForceSuccess       | Success      | Runs child; returns Success always      |
|                    | always       | (unless child returns Running)          |
+--------------------+--------------+-----------------------------------------+
| ForceFailure       | Failure      | Runs child; returns Failure always      |
|                    | always       | (unless child returns Running)          |
+--------------------+--------------+-----------------------------------------+
| UntilSuccess       | Running      | Loops child each tick until child       |
|                    | -> Success   | returns Success                         |
+--------------------+--------------+-----------------------------------------+
| UntilFailure       | Running      | Loops child each tick until child       |
|                    | -> Success   | returns Failure                         |
+--------------------+--------------+-----------------------------------------+
```

---

## Best Practices

### Performance

1. **Share blobs across entities.** `BehaviorTreeBlob` is immutable once compiled. Create one per tree type and reuse it for all entities.

2. **Keep `TBlackboard` small.** The blackboard is copied by value on every tick (`ref` avoids copies in the hot path, but struct size matters for cache pressure). Keep it under ~128 bytes where possible.

3. **Avoid allocating inside delegates.** The zero-allocation guarantee only holds if action/condition delegates themselves do not allocate. Use pre-allocated buffers, ECS components, or object pools instead.

4. **Respect the Parallel child limit.** `Parallel` uses a 32-bit `LocalRegisters[3]` as a bitfield, supporting a maximum of 16 children. The interpreter silently truncates to 16; `TreeValidator` emits a warning.

5. **Do not nest Parallel or Repeater.** Nested `Parallel` nodes corrupt `LocalRegisters[3]` (both levels share the same register slot). Nested `Repeater` nodes corrupt `LocalRegisters[0]`. The compiler treats these as hard errors.

6. **Use binary serialization for production.** `BinaryTreeSerializer` is faster to load than JSON (`~190 µs` vs `~7 µs` compile from JSON, but the binary path avoids JSON parsing). Precompile and ship `.fbt` files.

### Determinism

7. **Use `IAIContext.Time` for timing, not `DateTime.UtcNow`.** The `Wait` and `Cooldown` nodes rely on `ctx.Time`. Provide a deterministic simulation clock through `IAIContext` to ensure replays are accurate.

8. **Do not store managed references in the blackboard.** `TBlackboard` must be a `struct`. If you need to reference entities or components, store their IDs (integers) and resolve them through `TContext`.

9. **Increment `TreeVersion` on tree abort or reset.** The `AsyncToken.IsValid(state.TreeVersion)` check uses `TreeVersion` to detect "zombie" async requests from before a reset. Call `state.Reset()` (which increments `TreeVersion`) rather than clearing the state manually.

10. **Hot reload ordering.** When hot-reloading a blob, update the `Interpreter` to use the new blob before the next `Tick`. The `BTreeHotReloadManager.TryReload` returns `HardReset` when the node structure changed — in this case all entity `BehaviorTreeState` values must be reset to `default` before ticking again.

---

## Architectural Decisions

### Why a flat bytecode array?

Alternative: an object graph of node instances (one heap object per node per entity).

Problem with the alternative: millions of pointer dereferences per frame for large entity counts, GC pressure from allocations per entity, cache misses.

Flat array: all nodes for a given tree are contiguous in memory. Reading the next node is an indexed array access (`Nodes[currentIndex]`). `SubtreeOffset` allows skipping entire subtrees in O(1) without walking the tree.

### Why generic `TBlackboard` and `TContext`?

Alternative: `object` or interface-typed blackboard and context.

Problem: boxing of value types, virtual dispatch overhead, inability to use `ref` parameters to avoid copies.

Generics: the JIT specializes `Interpreter<GuardBlackboard, GuardContext>` producing code with direct struct access and inlined method calls. No boxing. No virtual dispatch for blackboard reads.

### Why store state in a fixed-size struct rather than a class?

Alternative: a per-entity class holding execution state.

Problem: GC pressure; pointer indirection; false sharing across cache lines when many entity states are stored adjacent in an array.

Fixed-size struct (64 bytes = 1 cache line): an array of `BehaviorTreeState` is a single contiguous block. Entity state for entity N is at `array[N * 64]`. Sequential iteration is cache-friendly.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fbt.Compiler` | Depends on `Fbt.Kernel`. Provides `BTreeBuilder<BB,Ctx>` (fluent API for programmatic tree construction) and `FbtAutoDiscovery` (auto-registration of source-generated registrars). The `Fbt.Kernel` `Serialization/` sub-folder (`TreeCompiler`, `BinaryTreeSerializer`, etc.) provides the JSON-to-blob pipeline that `Fbt.Compiler` also uses via `ProjectReference`. |
| `Fbt.SourceGen` | Roslyn source generator. Reads `[BTreeAction]`, `[BTreeCondition]`, `[BTreeDefinition]`, and `[SharedAi*]` attributes from user assemblies and emits `[FbtRegistrar]`-annotated registrar classes that call `registry.Register(...)` for each discovered action/condition. |
| HROT AI systems | Consumer. HROT unit-AI modules create `BTreeBuilder` trees (or load JSON trees) and tick `Interpreter<UnitBlackboard, UnitContext>` every simulation frame for each entity. |
| `FastHSM` | Sibling project. Hierarchical State Machine runtime. `SharedAiConditionAttribute` and `SharedAiActionAttribute` in `Fbt.Kernel` exist specifically to share AI logic between BTree and HSM behaviors without duplication. |
