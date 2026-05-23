# Fbt.Examples.Console

**Project Path**: `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.Console/Fbt.Examples.Console.csproj`
**Date**: 2026-05-23
**Framework**: net8.0
**Output Type**: Executable

---

## README Validation

**Status: Missing.**

No `README.md` exists in the project folder (`examples/Fbt.Examples.Console/`) or in the `examples/` parent. The `Program.cs` file is self-documenting through its console output, but there is no standalone README. A README should be added to describe how to run the example and what it demonstrates.

---

## Executive Overview

`Fbt.Examples.Console` is the minimal, dependency-free entry point for learning FastBTree. It demonstrates the complete end-to-end usage of the library in the fewest possible lines: load a JSON behavior tree definition, compile it to a `BehaviorTreeBlob`, register action delegates, create an `Interpreter`, and tick it in a loop while printing results to stdout.

The example is deliberately bare-bones. There is no Raylib, no ImGui, no agent simulation - just the FastBTree kernel and a simple struct blackboard. This makes it ideal as a reference for developers integrating FastBTree into a non-game context (server-side AI, robotics, unit-test harnesses) where no rendering framework is present.

Key learning outcomes:
1. How to define a `DemoBlackboard` struct and a `DemoContext` struct implementing `IAIContext` and `ITreeTracer`.
2. How to construct and configure an `ActionRegistry<BB, Ctx>`.
3. How to run a tick loop and interpret `NodeStatus.Running` vs `NodeStatus.Success` vs `NodeStatus.Failure`.
4. How `BehaviorTreeState` persists the interpreter's position between ticks (enabling the `Wait` node to span multiple frames).

---

## Architecture

The project contains exactly two source files: `Program.cs` (the entire example) and `Fbt.Examples.Console.csproj`. There are no subfolders. The architecture is a flat procedural script.

```
+---[ Fbt.Examples.Console ]----------------------------------+
|                                                            |
|  Program.Main()                                            |
|    |                                                       |
|    +-- 1. Locate and read JSON file                        |
|    |       simple-patrol.json (relative to BaseDirectory)  |
|    |                                                       |
|    +-- 2. TreeCompiler.CompileFromJson(json)               |
|    |       -> BehaviorTreeBlob (Nodes[], MethodNames[])    |
|    |                                                       |
|    +-- 3. ActionRegistry<DemoBlackboard, DemoContext>       |
|    |       .Register("FindRandomPatrolPoint", delegate)    |
|    |       .Register("MoveToTarget", delegate)             |
|    |                                                       |
|    +-- 4. Interpreter<DemoBlackboard, DemoContext>(blob, r) |
|    |                                                       |
|    +-- 5. Tick loop (10 frames, dt=0.5s each)              |
|             interpreter.Tick(ref bb, ref state, ref ctx)   |
|             print NodeStatus + Blackboard values           |
+------------------------------------------------------------+
```

```
+---[ Data Flow ]---------------------------------------------+
|                                                            |
|  JSON string                                               |
|    |                                                       |
|    v TreeCompiler.CompileFromJson()                        |
|  BehaviorTreeBlob (immutable, shared)                      |
|    |                                                       |
|    v new Interpreter<BB, Ctx>(blob, registry)              |
|  Interpreter (stateless - state held externally)           |
|    |                                                       |
|    v .Tick(ref bb, ref state, ref ctx)                     |
|  Per-tick outputs:                                         |
|    - NodeStatus (Running / Success / Failure)              |
|    - Modified bb fields (PatrolPointX, PatrolPointY)       |
|    - state.RunningNodeIndex updated by kernel              |
+------------------------------------------------------------+
```

---

## Source Structure

```
Fbt.Examples.Console/
+-- Program.cs                  Entire example in one file:
|                               - DemoBlackboard struct
|                               - DemoContext struct (IAIContext + ITreeTracer)
|                               - Program.Main(): full load-compile-tick loop
|                               - FindRandomPatrolPoint() action delegate
|                               - MoveToTarget() action delegate
+-- Fbt.Examples.Console.csproj Targets net8.0, references Fbt.Kernel
```

---

## Public Types Defined

### DemoBlackboard

```csharp
public struct DemoBlackboard
{
    public int PatrolPointX;
    public int PatrolPointY;
    public int EnemyDistance;
    public bool EnemyVisible;
}
```

A minimal blackboard holding only the data needed by the patrol tree. `EnemyDistance` and `EnemyVisible` are declared but unused in the simple patrol demo; they exist to illustrate that a real blackboard would carry more data.

### DemoContext

```csharp
public struct DemoContext : IAIContext, ITreeTracer
{
    public float DeltaTime { get; set; }
    public float Time { get; set; }
    public int FrameCount { get; set; }

    // IAIContext - async world queries (stubbed)
    public int RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance);
    public RaycastResult GetRaycastResult(int requestId);
    public int RequestPath(Vector3 from, Vector3 to);
    public PathResult GetPathResult(int requestId);
    public float GetFloatParam(int index);
    public int GetIntParam(int index);

    // ITreeTracer - trace callbacks (no-op)
    public void TraceNodeEvaluated(int nodeIndex, NodeStatus status);
    public void TraceScopePushed(ushort newStackDepth);
    public void TraceScopePopped(ushort newStackDepth);
    public void TraceWaitStarted(int nodeIndex, float duration);
    public void TraceWaitCompleted(int nodeIndex, float duration);
}
```

The stub implementations of `IAIContext` are important: `GetRaycastResult` always returns `IsReady = true` and `GetPathResult` always returns `IsReady = true, Success = true`. This allows the example to run without a world simulation. In a real game, these would hook into the physics/navmesh system.

The `ITreeTracer` no-op stubs show the interface surface without requiring a tracing backend.

---

## Dependencies

| Package / Project | Version / Path | Purpose |
|---|---|---|
| `Fbt.Kernel` | (project ref) | `BehaviorTreeBlob`, `Interpreter<BB,Ctx>`, `ActionRegistry`, `BehaviorTreeState`, `NodeStatus`, `IAIContext`, `ITreeTracer`, `TreeCompiler` |

No NuGet packages. This is the most minimal FastBTree consumer possible.

---

## The Patrol Tree

The example loads `simple-patrol.json` from a path relative to `AppContext.BaseDirectory`. The tree structure it expects:

```
Sequence (root or Repeater)
  |-- Action "FindRandomPatrolPoint"
  |-- Action "MoveToTarget"
  +-- Wait (2.0s)
```

With `DeltaTime = 0.5s` per tick:
- Frame 0: `FindRandomPatrolPoint` runs -> Success
- Frame 0 (cont): `MoveToTarget` runs -> Success
- Frame 0 (cont): Wait starts (2.0s remaining)
- Frame 1: Wait continues (1.5s remaining) -> Running
- Frame 2: Wait continues (1.0s remaining) -> Running
- Frame 3: Wait continues (0.5s remaining) -> Running
- Frame 4: Wait completes -> Sequence returns Success

This demonstrates that `BehaviorTreeState` correctly carries the Wait node's start-time across multiple ticks.

---

## Usage Examples

### Example 1: Running the Console Demo

```bash
cd FDP/ExtDeps/FastBTree/examples/Fbt.Examples.Console
dotnet run
```

Expected output (abbreviated):

```
=== FastBTree Console Demo ===

Loading tree from JSON: .../simple-patrol.json
Tree compiled: SimplePatrol
  Nodes: 5
  Methods: 2

Executing tree...

Frame 0 (Time: 0.0s):
  [Action] Found patrol point: (42, -17)
  [Action] Moving to target: (42, -17)
  Result: Running
  Blackboard: Point=(42, -17)

Frame 1 (Time: 0.5s):
  Result: Running
  Blackboard: Point=(42, -17)

Frame 4 (Time: 2.0s):
  Result: Success
  Blackboard: Point=(42, -17)

Demo complete!
```

### Example 2: Writing a Custom Action

The delegate signature for a full-blackboard action is:

```csharp
static NodeStatus MyCustomAction(
    ref DemoBlackboard bb,
    ref BehaviorTreeState state,
    ref DemoContext ctx,
    int paramIndex)          // index into blob.FloatParams or blob.IntParams
{
    // Read/write blackboard
    bb.PatrolPointX = 99;

    // Use context for time-based logic
    if (ctx.Time < 1.0f)
        return NodeStatus.Running;

    return NodeStatus.Success;
}
```

Register before creating the interpreter:

```csharp
var registry = new ActionRegistry<DemoBlackboard, DemoContext>();
registry.Register("FindRandomPatrolPoint", FindRandomPatrolPoint);
registry.Register("MoveToTarget", MoveToTarget);
registry.Register("MyCustomAction", MyCustomAction);
```

### Example 3: Resetting and Re-running the Tree

After a tree returns `NodeStatus.Success` or `NodeStatus.Failure`, call `state.Reset()` to restart it from the root:

```csharp
var state = new BehaviorTreeState();

for (int frame = 0; frame < 20; frame++)
{
    ctx.Time = frame * 0.5f;
    ctx.DeltaTime = 0.5f;

    var result = interpreter.Tick(ref bb, ref state, ref ctx);

    if (result == NodeStatus.Success || result == NodeStatus.Failure)
    {
        Console.WriteLine($"Tree completed with {result}, resetting.");
        state.Reset();
    }
}
```

This pattern is the basis for a tree that keeps cycling (instead of using a Repeater node in the JSON).

### Example 4: Implementing a Condition

A condition node is a function returning `NodeStatus.Success` (condition met) or `NodeStatus.Failure` (condition not met). In the JSON it uses `"Type": "Condition"`:

```csharp
static NodeStatus IsEnemyVisible(
    ref DemoBlackboard bb,
    ref BehaviorTreeState state,
    ref DemoContext ctx,
    int paramIndex)
{
    return bb.EnemyVisible ? NodeStatus.Success : NodeStatus.Failure;
}

// Register it like any other action
registry.Register("IsEnemyVisible", IsEnemyVisible);
```

JSON usage:

```json
{ "Type": "Condition", "Action": "IsEnemyVisible" }
```

---

## Architecture Diagram: IAIContext Stub Pattern

```
+---[ IAIContext Stub Pattern ]-------------------------------+
|                                                            |
|  interface IAIContext                                       |
|    RequestRaycast()  -> returns int requestId              |
|    GetRaycastResult(id) -> RaycastResult { IsReady, ... }  |
|    RequestPath()     -> returns int requestId              |
|    GetPathResult(id) -> PathResult { IsReady, Success }    |
|    GetFloatParam(i)  -> float                              |
|    GetIntParam(i)    -> int                                |
|                                                            |
|  DemoContext (stub implementation):                        |
|    RequestRaycast()  -> 0 (immediate fake ID)              |
|    GetRaycastResult()-> { IsReady = true }                 |
|    RequestPath()     -> 0                                  |
|    GetPathResult()   -> { IsReady = true, Success = true } |
|    GetFloatParam()   -> 1.0f                               |
|    GetIntParam()     -> 1                                  |
|                                                            |
|  A real game context would:                                |
|    RequestRaycast() -> submit to physics thread, return ID |
|    GetRaycastResult(id) -> check if result ready (async)   |
|    This allows trees to do async world queries             |
+------------------------------------------------------------+
```

---

## Architecture Diagram: Tick State Persistence

```
+---[ BehaviorTreeState Persistence ]------------------------+
|                                                            |
|  Tick N:                                                   |
|    Interpreter evaluates Sequence[0] (FindPatrolPoint)     |
|    -> Success                                              |
|    Interpreter evaluates Sequence[1] (MoveToTarget)        |
|    -> Success                                              |
|    Interpreter evaluates Sequence[2] (Wait 2.0s)           |
|    -> state.RunningNodeIndex = 2                           |
|    -> state.WaitStartTime = ctx.Time                       |
|    -> returns NodeStatus.Running                           |
|                                                            |
|  Tick N+1:                                                 |
|    Interpreter resumes at state.RunningNodeIndex = 2       |
|    elapsed = ctx.Time - state.WaitStartTime                |
|    if elapsed < 2.0: return Running                        |
|    else: advance, return to parent                         |
|                                                            |
|  Key insight: BehaviorTreeState is a VALUE TYPE (struct).  |
|  The caller owns the memory and passes it by ref.          |
|  The interpreter NEVER allocates state - zero GC pressure. |
+------------------------------------------------------------+
```

---

## Best Practices Illustrated

1. **Minimal blackboard surface.** Only the data actually needed by the tree's actions is placed in the blackboard struct. Keeping it small improves cache efficiency and reduces the cognitive load of action delegates.

2. **All async world-query interfaces are stubbed to immediate results.** The `IAIContext` stub pattern lets you test trees without a real game world. Production code replaces the stubs with actual engine calls.

3. **No-op `ITreeTracer` is valid.** The `ITreeTracer` interface methods can all be empty implementations. Tracing is opt-in; the kernel does not require a real tracer.

4. **`BehaviorTreeState` is always a local or member variable, never pooled.** It is a struct; create one per agent instance and pass by `ref`.

5. **Path resolution uses `AppContext.BaseDirectory` with a fallback.** The example demonstrates robust relative-path resolution that works both from the IDE (F5) and from `dotnet run` in a shell.

---

## Architecture Diagram: Blackboard and Context Lifetimes

```
+---[ Blackboard and Context Lifetime Rules ]----------------+
|                                                           |
|  DemoBlackboard bb = new DemoBlackboard();                |
|    - Value type (struct); owned by caller                 |
|    - Persists across all ticks                            |
|    - Action delegates READ and WRITE bb fields            |
|    - The interpreter NEVER reads bb directly              |
|    - Only action delegates touch bb contents              |
|                                                           |
|  BehaviorTreeState state = new BehaviorTreeState();       |
|    - Value type (struct); owned by caller                 |
|    - Persists across all ticks                            |
|    - Interpreter reads/writes:                            |
|        RunningNodeIndex (which node is suspended)         |
|        WaitStartTime    (when a Wait node began)          |
|        AsyncData        (64-bit per-node scratch space)   |
|    - Call state.Reset() to restart tree from root         |
|                                                           |
|  DemoContext ctx = new DemoContext();                      |
|    - Value type (struct); owned by caller                 |
|    - Updated by caller each tick before Tick()            |
|    - Action delegates read ctx.Time, ctx.DeltaTime        |
|    - Interpreter reads ctx for Wait timing                |
|    - Should NOT be stored or reused across threads        |
+-----------------------------------------------------------+
```

---

## Architecture Diagram: NodeStatus Decision Tree

```
+---[ How NodeStatus Drives the Interpreter ]----------------+
|                                                           |
|  Action/Condition delegate returns:                       |
|                                                           |
|  NodeStatus.Success                                       |
|    -> Parent node continues to next child (Sequence)      |
|       OR Selector reports success and skips remaining     |
|                                                           |
|  NodeStatus.Failure                                       |
|    -> Sequence fails entirely (bubbles up)                |
|       OR Selector tries next child                        |
|                                                           |
|  NodeStatus.Running                                       |
|    -> Interpreter suspends; stores RunningNodeIndex       |
|    -> Next Tick() resumes at this exact node              |
|    -> Parent does NOT advance; tree is suspended here     |
|    -> Caller MUST call Tick() again next frame            |
|                                                           |
|  The simple-patrol tree:                                  |
|    Sequence                                               |
|      [0] FindRandomPatrolPoint -> Success (always)        |
|      [1] MoveToTarget          -> Success (always)        |
|      [2] Wait(2.0)             -> Running (2s elapsed)    |
|                                     then Success          |
|    -> Sequence returns Success when all children succeed  |
+-----------------------------------------------------------+
```

---

## Extended Blackboard Patterns

Real-world blackboards are larger and are often split by concern. The following shows a production-style blackboard for a patrol-gather-combat agent:

```csharp
// Minimal patrol data
public struct PatrolBlackboard
{
    public int PatrolIndex;
    public float WaypointReachedTime;
}

// Extended combat blackboard - separated from patrol data
public struct CombatBlackboard
{
    public bool EnemyVisible;
    public int EnemyEntityId;
    public float EnemyDistance;
    public int ShotsFired;
    public float LastFireTime;
}

// Composite: multiple concerns in one flat struct
// (required by FastBTree - one blackboard per tree)
public struct AgentBlackboard
{
    // Patrol
    public int PatrolIndex;
    public float WaypointReachedTime;
    // Combat
    public bool EnemyVisible;
    public int EnemyEntityId;
    public float EnemyDistance;
    public int ShotsFired;
    public float LastFireTime;
    // Gather
    public int ResourceCount;
    public bool CarryingResources;
}
```

Keep the blackboard as a simple `struct` with primitive fields. Avoid reference types (classes, strings) inside blackboards as they increase GC pressure and complicate serialization.

---

## How to Write a Multi-Tick Action

Actions that span multiple ticks use `state.AsyncData` (a `ulong` per-node scratch value):

```csharp
static NodeStatus GatherResource(
    ref DemoBlackboard bb,
    ref BehaviorTreeState state,
    ref DemoContext ctx,
    int paramIndex)
{
    // AsyncData stores the gather start time
    // ulong bit-cast from float
    if (state.AsyncData == 0)
    {
        // First call: record start time
        state.AsyncData = (ulong)BitConverter.SingleToInt32Bits(ctx.Time);
        Console.WriteLine("  [Gather] Started gathering...");
    }

    float startTime = BitConverter.Int32BitsToSingle((int)state.AsyncData);
    float elapsed = ctx.Time - startTime;

    if (elapsed < 3.0f)
    {
        Console.WriteLine($"  [Gather] Gathering... {elapsed:F1}s / 3.0s");
        return NodeStatus.Running;
    }

    // Done: increment resource count, reset scratch
    bb.ResourceCount++;
    state.AsyncData = 0;
    Console.WriteLine($"  [Gather] Complete! Total: {bb.ResourceCount}");
    return NodeStatus.Success;
}
```

Register and use in a JSON tree:

```json
{ "Type": "Action", "Action": "GatherResource" }
```

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fbt.Kernel` | Sole dependency. Provides the entire runtime. |
| `Fbt.Demo.Visual` | Full-featured Raylib visual demo showing multi-agent trees |
| `Fbt.Examples.FluentBTree` | Shows fluent C# API as an alternative to JSON trees, plus hot-reload |
| `Fbt.Examples.FluentBTree.Trees` | Tree definitions (library project) consumed by FluentBTree example |
