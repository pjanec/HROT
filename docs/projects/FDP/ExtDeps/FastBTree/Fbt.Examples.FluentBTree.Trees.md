# Fbt.Examples.FluentBTree.Trees

**Project Path**: `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree.Trees/Fbt.Examples.FluentBTree.Trees.csproj`
**Date**: 2026-05-23
**Framework**: net8.0
**Output Type**: Class Library (no executable)

---

## README Validation

**Status: Missing.**

No `README.md` exists in the `examples/Fbt.Examples.FluentBTree.Trees/` folder. The project is a pure library consumed by `Fbt.Examples.FluentBTree` and its tests. A README would benefit developers who want to understand the separation of tree definitions from the host application (especially important for the hot-reload workflow).

---

## Executive Overview

`Fbt.Examples.FluentBTree.Trees` is a deliberately isolated class library that holds only behavior tree definitions and the data types they operate on. It has no dependency on Raylib, ImGui, or any host-application framework.

The isolation exists for a specific reason: **hot-reload**. The `Fbt.Examples.FluentBTree` host application watches this project's output DLL. When a developer modifies a tree definition and rebuilds only this small library, the host picks up the new assembly and swaps the running tree without restarting. Because this project is a separate assembly, the rebuild is fast (seconds, not the full solution build) and the host application continues running.

This pattern also makes the tree definitions independently testable: a unit test project can reference `Fbt.Examples.FluentBTree.Trees` directly and verify tree behavior without starting Raylib.

Key learning outcomes:
1. How to structure the separation between "tree definitions" (this project) and "tree host" (the app project).
2. How `[BTreeDefinition]` marks a factory method as a hot-reload entry point.
3. How `[BTreeAction]` and `[BTreeCondition]` attributes on static methods feed the source generator.
4. The field-projection delegate pattern (`ReusableActionDelegate<TField, TCtx>`) for reusable action logic.
5. How `CombatBlackboard` layout discipline (`StructLayout.Sequential`, explicit padding) ensures stable field offsets.

---

## Architecture

```
+---[ Fbt.Examples.FluentBTree.Trees ]------------------------+
|                                                             |
|  CombatBlackboard (struct)                                  |
|    StructLayout.Sequential - stable field offsets           |
|    int AmmoCount                                            |
|    bool ThreatVisible                                       |
|    float EngagementRange                                    |
|                                                             |
|  CombatContext (struct : IAIContext, ITreeTracer)            |
|    DeltaTime, Time, FrameCount                              |
|    Stub implementations of world-query interfaces           |
|    No-op ITreeTracer                                        |
|                                                             |
|  CombatActions (static class)                               |
|    [BTreeCondition] CheckAmmo(ref int, ref state, ref ctx)  |
|    [BTreeCondition] HasThreat(ref bool, ref state, ref ctx) |
|    [BTreeAction]    AimAndFire(ref int, ref state, ref ctx) |
|    [BTreeAction]    HoldPosition(ref BB, ref state, ...)    |
|                                                             |
|  AmbushTree (static class)                                  |
|    CreateBuilder() -> BTreeBuilder<BB, Ctx>                 |
|    [BTreeDefinition("Ambush_BT")]                           |
|    BuildAmbushTree() -> BehaviorTreeBlob                    |
|    CreateInterpreter() -> Interpreter<BB, Ctx>              |
+-------------------------------------------------------------+
```

```
+---[ Hot-Reload Assembly Isolation ]------------------------+
|                                                           |
|  Solution                                                 |
|    +-- Fbt.Examples.FluentBTree.Trees.dll  <-- WATCHED     |
|    |     AmbushTree, CombatActions, CombatBlackboard       |
|    |                                                       |
|    +-- Fbt.Examples.FluentBTree.exe  <-- HOST (running)    |
|          FbtAssemblyHotReloader watches Trees .dll         |
|          On change: loads new assembly via                 |
|                     AssemblyLoadContext                    |
|          Discovers [BTreeDefinition] methods               |
|          Invokes factory -> new BehaviorTreeBlob           |
|          Swaps interpreter at next DrainPendingCallbacks() |
+-----------------------------------------------------------+
```

---

## Source Structure

```
Fbt.Examples.FluentBTree.Trees/
+-- CombatBlackboard.cs         CombatBlackboard struct + CombatContext struct
+-- CombatActions.cs            Static action/condition delegates with attribute markers
+-- AmbushTree.cs               Tree factory: CreateBuilder(), BuildAmbushTree(),
|                               CreateInterpreter()
+-- Fbt.Examples.FluentBTree.Trees.csproj
                                References: Fbt.Kernel, Fbt.Compiler, Fbt.SourceGen (Analyzer)
                                No executable output
```

---

## Public API Reference

### CombatBlackboard

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct CombatBlackboard
{
    public int AmmoCount;                    // Current ammunition count
    [MarshalAs(UnmanagedType.U1)]
    public bool ThreatVisible;              // Is an enemy currently visible?
    // Explicit padding to align EngagementRange at offset 8
    public byte _pad0, _pad1, _pad2;
    public float EngagementRange;           // Maximum engagement distance
}
```

Layout (guaranteed by `LayoutKind.Sequential`):
- Offset 0: `AmmoCount` (int, 4 bytes)
- Offset 4: `ThreatVisible` (bool as U1, 1 byte)
- Offset 5: `_pad0, _pad1, _pad2` (3 padding bytes)
- Offset 8: `EngagementRange` (float, 4 bytes)
- Total: 12 bytes

The explicit layout is critical because `BTreeBuilder` captures field offsets via expression tree analysis at compile time. If the layout changes, the captured offsets become stale and the projections will read garbage.

### CombatContext

```csharp
public struct CombatContext : IAIContext, ITreeTracer
{
    public float DeltaTime { get; set; }
    public float Time { get; set; }
    public int FrameCount { get; set; }

    // IAIContext - all stubs returning immediate results
    public int RequestRaycast(Vector3 origin, Vector3 direction, float maxDistance);
    public RaycastResult GetRaycastResult(int requestId);
    public int RequestPath(Vector3 from, Vector3 to);
    public PathResult GetPathResult(int requestId);
    public float GetFloatParam(int index);
    public int GetIntParam(int index);

    // ITreeTracer - all no-ops
    public void TraceNodeEvaluated(int nodeIndex, NodeStatus status);
    public void TraceScopePushed(ushort newStackDepth);
    public void TraceScopePopped(ushort newStackDepth);
    public void TraceWaitStarted(int nodeIndex, float duration);
    public void TraceWaitCompleted(int nodeIndex, float duration);
}
```

### CombatActions

```csharp
public static class CombatActions
{
    // Condition: returns Success if ammo > 0
    [BTreeCondition]
    public static NodeStatus CheckAmmo(
        ref int ammo,
        ref BehaviorTreeState state,
        ref CombatContext ctx);

    // Condition: returns Success if threat is visible
    [BTreeCondition]
    public static NodeStatus HasThreat(
        ref bool threatVisible,
        ref BehaviorTreeState state,
        ref CombatContext ctx);

    // Action: decrements ammo by 1, returns Success
    [BTreeAction]
    public static NodeStatus AimAndFire(
        ref int ammo,
        ref BehaviorTreeState state,
        ref CombatContext ctx);

    // Action: uses state.AsyncData as tick counter
    //   tick < 2 -> returns Running ("Holding...")
    //   tick >= 2 -> resets AsyncData, returns Success ("Done holding.")
    [BTreeAction]
    public static NodeStatus HoldPosition(
        ref CombatBlackboard bb,
        ref BehaviorTreeState state,
        ref CombatContext ctx,
        int param);
}
```

The first three delegates use the 3-parameter "field projection" form: they receive a `ref` to a specific field of `CombatBlackboard` rather than the full blackboard. The fourth (`HoldPosition`) uses the 4-parameter "full blackboard" form.

### AmbushTree

```csharp
public static class AmbushTree
{
    // Creates a fluent builder with all action delegates pre-registered.
    // Returns the builder before compilation.
    public static BTreeBuilder<CombatBlackboard, CombatContext> CreateBuilder();

    // Source-generator entry point.
    // Must be a parameterless static method returning BehaviorTreeBlob.
    [BTreeDefinition("Ambush_BT")]
    public static BehaviorTreeBlob BuildAmbushTree();

    // Convenience: compile + get registry + create interpreter.
    // Use in the sample app and in unit tests.
    public static Interpreter<CombatBlackboard, CombatContext> CreateInterpreter();
}
```

---

## Dependencies

| Package / Project | Version / Path | Purpose |
|---|---|---|
| `Fbt.Kernel` | (project ref) | `BehaviorTreeBlob`, `Interpreter`, `BehaviorTreeState`, `NodeStatus`, `IAIContext`, `ITreeTracer` |
| `Fbt.Compiler` | (project ref) | `BTreeBuilder`, `BTreeDefinitionAttribute`, `BTreeActionAttribute`, `BTreeConditionAttribute` |
| `Fbt.SourceGen` | (analyzer) | Source-generates `FbtTreeCatalog.GetAmbush_BT()` and `HsmActionRegistrar.RegisterAll()`; not in output |

---

## The Ambush Tree Logic

```
Selector
+-- Sequence (engage if armed and threat visible)
|   +-- Condition: HasThreat(ThreatVisible)    -> Failure if no threat
|   +-- Condition: CheckAmmo(AmmoCount)        -> Failure if ammo == 0
|   +-- Action:    AimAndFire(AmmoCount)       -> decrements ammo, Success
|
+-- Action: HoldPosition                       -> Running (tick 1), Success (tick 2)
```

Behavioral analysis:
- **Armed + threat visible**: Selector tries the Sequence first. Both conditions pass, AimAndFire fires. Ammo decrements.
- **Unarmed**: CheckAmmo returns Failure, Sequence fails. Selector falls through to HoldPosition.
- **No threat**: HasThreat returns Failure on the first check. Selector falls through to HoldPosition.
- **HoldPosition multi-tick**: Uses `state.AsyncData` as a tick counter. Returns Running on tick 1, Success on tick 2 and resets. This demonstrates multi-tick actions without timers.

---

## Usage Examples

### Example 1: Using AmbushTree in a Unit Test

```csharp
[Fact]
public void AimAndFire_DecrementAmmo_WhenArmedAndThreatVisible()
{
    var interpreter = AmbushTree.CreateInterpreter();
    var bb = new CombatBlackboard { AmmoCount = 3, ThreatVisible = true };
    var state = new BehaviorTreeState();
    var ctx = new CombatContext { DeltaTime = 0.016f, Time = 0f };

    var result = interpreter.Tick(ref bb, ref state, ref ctx);

    Assert.Equal(NodeStatus.Success, result);
    Assert.Equal(2, bb.AmmoCount); // decremented from 3 to 2
}

[Fact]
public void HoldPosition_WhenNoAmmo()
{
    var interpreter = AmbushTree.CreateInterpreter();
    var bb = new CombatBlackboard { AmmoCount = 0, ThreatVisible = true };
    var state = new BehaviorTreeState();
    var ctx = new CombatContext { DeltaTime = 0.016f };

    // Tick 1: HoldPosition returns Running
    var result1 = interpreter.Tick(ref bb, ref state, ref ctx);
    Assert.Equal(NodeStatus.Running, result1);

    // Tick 2: HoldPosition returns Success
    var result2 = interpreter.Tick(ref bb, ref state, ref ctx);
    Assert.Equal(NodeStatus.Success, result2);

    // Ammo unchanged (HoldPosition does not modify ammo)
    Assert.Equal(0, bb.AmmoCount);
}
```

### Example 2: Using [BTreeDefinition] for Hot-Reload Discovery

When `Fbt.SourceGen` processes this project, it generates something like:

```csharp
// Auto-generated by Fbt.SourceGen
namespace Fbt.Examples.FluentBTree.Generated
{
    public static class FbtTreeCatalog
    {
        public static BehaviorTreeBlob GetAmbush_BT()
            => Fbt.Examples.FluentBTree.AmbushTree.BuildAmbushTree();
    }
}
```

The `FbtAssemblyHotReloader` in the host app uses reflection to find types with `[HsmActionRegistrar]` (or the equivalent FBT registrar attribute) and invokes them. The `[BTreeDefinition("Ambush_BT")]` attribute on `BuildAmbushTree()` is the signal used during that reflection scan.

### Example 3: Adding a New Action to the Tree

To add a "Reload" action when ammo hits zero:

1. Add to `CombatActions.cs`:

```csharp
[BTreeAction]
public static NodeStatus ReloadWeapon(
    ref int ammo,
    ref BehaviorTreeState state,
    ref CombatContext ctx)
{
    ammo = 5; // Full reload
    Console.WriteLine("[ReloadWeapon] Reloaded. Ammo: 5");
    return NodeStatus.Success;
}
```

2. Update `AmbushTree.CreateBuilder()`:

```csharp
return new BTreeBuilder<CombatBlackboard, CombatContext>()
    .Selector(s => s
        .Sequence(seq => seq
            .Condition(dto => dto.ThreatVisible, CombatActions.HasThreat)
            .Condition(dto => dto.AmmoCount, CombatActions.CheckAmmo)
            .Action(dto => dto.AmmoCount, CombatActions.AimAndFire)
        )
        .Sequence(seq => seq             // <-- new: reload when out of ammo
            .Condition(dto => dto.AmmoCount, (ref int a, ref BehaviorTreeState s, ref CombatContext c)
                => a == 0 ? NodeStatus.Success : NodeStatus.Failure)
            .Action(dto => dto.AmmoCount, CombatActions.ReloadWeapon)
        )
        .Action(CombatActions.HoldPosition)
    );
```

3. Save and rebuild only this project. The host app will hot-reload within seconds.

---

## Architecture Diagram: Field Projection Delegate

```
+---[ Field Projection: dto => dto.AmmoCount ]---------------+
|                                                           |
|  Expression<Func<CombatBlackboard, int>>                  |
|    = (dto) => dto.AmmoCount                               |
|                                                           |
|  Compiler extracts:                                       |
|    MemberExpression -> FieldInfo AmmoCount                |
|    FieldOffset = 0 (first field in struct)                |
|                                                           |
|  Generated shim (pseudo-code):                            |
|    void Shim(ref CombatBlackboard bb,                     |
|              ref BehaviorTreeState state,                 |
|              ref CombatContext ctx)                       |
|    {                                                      |
|        ref int ammo = ref Unsafe.As<CombatBlackboard,int> |
|                           (ref bb);                       |
|        // offset 0 -> direct ref                         |
|        AimAndFire(ref ammo, ref state, ref ctx);          |
|    }                                                      |
|                                                           |
|  Result: AimAndFire receives a ref to the exact field.    |
|  It does NOT see the full blackboard.                     |
+-----------------------------------------------------------+
```

---

## Architecture Diagram: Source Generator Output

```
+---[ Fbt.SourceGen Processing ]-----------------------------+
|                                                           |
|  Input: CombatActions.cs                                  |
|    [BTreeAction]    AimAndFire(ref int, ...)               |
|    [BTreeCondition] CheckAmmo(ref int, ...)                |
|    [BTreeCondition] HasThreat(ref bool, ...)               |
|    [BTreeAction]    HoldPosition(ref BB, ...)              |
|                                                           |
|  Output: Generated/FbtActionRegistrar.cs (conceptual)     |
|    public static class HsmActionRegistrar                  |
|    {                                                       |
|        public static void RegisterAll(                    |
|            ActionRegistry<CombatBlackboard, CombatContext> |
|            registry)                                      |
|        {                                                  |
|            registry.Register("AimAndFire", ...);          |
|            registry.Register("CheckAmmo", ...);           |
|            registry.Register("HasThreat", ...);           |
|            registry.Register("HoldPosition", ...);        |
|        }                                                  |
|    }                                                      |
|                                                           |
|  Input: AmbushTree.cs                                     |
|    [BTreeDefinition("Ambush_BT")]                         |
|    BuildAmbushTree() -> BehaviorTreeBlob                  |
|                                                           |
|  Output: Generated/FbtTreeCatalog.cs                      |
|    public static class FbtTreeCatalog                     |
|    {                                                       |
|        public static BehaviorTreeBlob GetAmbush_BT()      |
|            => AmbushTree.BuildAmbushTree();               |
|    }                                                      |
+-----------------------------------------------------------+
```

---

## Best Practices Illustrated

1. **Tree definitions live in a separate library.** This is the fundamental hot-reload enabler. The library has no host-app dependencies (no Raylib, no window handles), so it can be rebuilt fast and independently.

2. **`[StructLayout(LayoutKind.Sequential)]` with explicit padding.** Stable ABI between the Trees library and the host. Without this, the C# runtime may insert implicit padding that changes field offsets across builds.

3. **Field-projection delegates are more reusable than full-BB delegates.** `CheckAmmo` takes a `ref int` - it could work with any blackboard that has an ammo-like integer field. Full-BB delegates like `HoldPosition` are needed only when the action must read/write multiple fields.

4. **`CreateInterpreter()` is the test-friendly entry point.** Tests call this single method and get a fully wired `Interpreter` without having to understand the `Builder` / `Compile` / `GetRegistry` sequence.

5. **`state.AsyncData` is the correct slot for multi-tick action state.** `HoldPosition` stores its tick counter in `state.AsyncData` (a `ulong` field reserved per-node for action-private data). This avoids polluting the blackboard with action-internal counters.

---

## Extended Usage: Expanding the Blackboard

When the tree needs to track more state, extend `CombatBlackboard` with new fields. Because the builder captures field offsets by expression analysis, adding fields at the end is safe without recompiling the app (though a hot-reload HardReset will occur if the struct layout changes):

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct CombatBlackboard
{
    public int AmmoCount;
    [MarshalAs(UnmanagedType.U1)]
    public bool ThreatVisible;
    public byte _pad0, _pad1, _pad2;
    public float EngagementRange;
    // New fields - added at the end to preserve offsets of existing fields
    public int EnemyId;            // ID of the detected enemy
    public float LastShotTime;     // Time of last shot fired
}
```

After adding fields, update `AmbushTree.CreateBuilder()` if the new fields should affect tree logic:

```csharp
.Sequence(seq => seq
    .Condition(dto => dto.ThreatVisible, CombatActions.HasThreat)
    .Condition(dto => dto.AmmoCount, CombatActions.CheckAmmo)
    .Condition(dto => dto.LastShotTime, CombatActions.CheckFireRate)  // new
    .Action(dto => dto.AmmoCount, CombatActions.AimAndFire)
)
```

---

## Diagram: CombatBlackboard Memory Layout

```
+---[ CombatBlackboard Memory Layout ]----------------------+
|                                                           |
|  Offset  Size  Field                                      |
|  ------  ----  -----                                      |
|  0       4     AmmoCount (int)                            |
|  4       1     ThreatVisible (bool as U1)                 |
|  5       1     _pad0                                      |
|  6       1     _pad1                                      |
|  7       1     _pad2                                      |
|  8       4     EngagementRange (float)                    |
|  ------  ----  -----                                      |
|  Total: 12 bytes                                          |
|                                                           |
|  Expression dto => dto.AmmoCount                          |
|    -> MemberExpression.Field = AmmoCount                  |
|    -> FieldOffset = 0 (first field, int)                  |
|    -> shim reads: ref int at base+0                       |
|                                                           |
|  Expression dto => dto.ThreatVisible                      |
|    -> FieldOffset = 4                                     |
|    -> shim reads: ref bool at base+4                      |
|                                                           |
|  Padding bytes 5-7 ensure EngagementRange is aligned      |
|  at offset 8 (4-byte boundary for float)                  |
+-----------------------------------------------------------+
```

---

## Diagram: BTreeDefinition Attribute Discovery

```
+---[ [BTreeDefinition] Discovery at Hot-Reload ]------------+
|                                                            |
|  New assembly loaded by FbtAssemblyHotReloader             |
|    |                                                       |
|    v foreach type in newAssembly.GetTypes()                |
|  For each public static method:                            |
|    GetCustomAttribute(typeof(BTreeDefinitionAttribute))    |
|    |                                                       |
|    +-- found? -> treeName = attr.TreeName                  |
|    |             blob = (BehaviorTreeBlob)method.Invoke()  |
|    |             results.Add((treeName, blob))             |
|    |                                                       |
|    v  (no attribute) -> skip                               |
|                                                           |
|  Result: list of (treeName, blob) pairs                    |
|  One entry per [BTreeDefinition]-tagged method             |
|  In this project: just ("Ambush_BT", AmbushTree blob)      |
+------------------------------------------------------------+
```

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fbt.Kernel` | Runtime - `Interpreter`, `BehaviorTreeBlob`, `BehaviorTreeState` |
| `Fbt.Compiler` | Builder API and hot-reload support |
| `Fbt.SourceGen` | Generates registration boilerplate (analyzer, not output) |
| `Fbt.Examples.FluentBTree` | The host application that loads and hot-reloads this library |
| `Fbt.Examples.Console` | Simpler example using JSON trees instead of fluent builder |
