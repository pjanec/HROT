# Fbt.Compiler

| | |
|---|---|
| **Project path** | `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/Fbt.Compiler.csproj` |
| **Namespace root** | `Fbt.Compiler`, `Fbt.Compiler.Graph` |
| **Target framework** | net8.0 |
| **Depends on** | `Fbt.Kernel` |
| **Date** | 2026-05-23 |

---

## README Validation

| Location | Status |
|---|---|
| `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/` | **Missing** — no README.md in this folder |
| `FDP/ExtDeps/FastBTree/src/` | **Missing** — no README.md in the src folder |
| `FDP/ExtDeps/FastBTree/` | **Present and up-to-date** — the root README covers the overall library including compilation concepts. The `BTreeBuilder` fluent API and `FbtAutoDiscovery` features described herein are consistent with that documentation. The README's quick-start example uses `TreeCompiler.CompileFromJson`, which lives in `Fbt.Kernel`'s `Serialization` subfolder but is orchestrated by the compilation pipeline this project wraps. |

---

## Executive Overview

`Fbt.Compiler` is the **authoring and compilation layer** of the FastBTree library. It sits on top of `Fbt.Kernel` and provides:

1. **`BTreeBuilder<TBlackboard, TContext>`** — A type-safe fluent API for constructing behavior tree blobs programmatically in C# code, without requiring a JSON file. Every node type available in the runtime has a corresponding builder method.

2. **`BehaviorTreeGraph` / Graph node types** — A mutable document-object-model (DOM) representation of a behavior tree. Used by authoring tools and visual editors to manipulate tree structure at design time without coupling the editor to the flat bytecode format.

3. **`FbtAutoDiscovery`** — A reflection-based utility that scans loaded assemblies for types annotated with `[FbtRegistrar]` (emitted by `Fbt.SourceGen`) and invokes their `RegisterAll` method on an `ActionRegistry`. This allows the engine to pick up source-generated registrars without manual wiring.

4. **`BTreeSchemaExporter`** — A standalone tool utility that scans assemblies for `[BTreeAction]` and `[BTreeCondition]` methods and produces a `BTreeSchema` record (actions, conditions, DTO types). Intended for tooling and editor integration, not for runtime use.

5. **`ReusableConditionDelegate` / `ReusableActionDelegate`** — Delegate types that receive a projected sub-field of the blackboard (by reference) rather than the full blackboard. Used with the expression-based `BTreeBuilder.Condition<TValue>` and `BTreeBuilder.Action<TValue>` overloads to enable reusable AI logic across different blackboard types.

`Fbt.Compiler` is the bridge between the human-authored representation of behavior trees (C# code, JSON, visual graphs) and the `BehaviorTreeBlob` format that `Fbt.Kernel`'s `Interpreter` executes.

---

## Architecture

### Overall Pipeline

```
+------------------+    +------------------+    +-------------------+
|   JSON file      |    |  C# BTreeBuilder |    | Visual Editor     |
|  (human-editable)|    |  (fluent C# API) |    | (BehaviorTreeGraph|
+------------------+    +------------------+    |  DOM)             |
         |                       |              +-------------------+
         |                       |                       |
         v                       v                       v
+------------------+    +------------------+    +-------------------+
| TreeCompiler     |    | BTreeBuilder     |    | (future: graph    |
| .CompileFromJson |    | .Compile()       |    |  compiler)        |
+------------------+    +------------------+    +-------------------+
         |                       |
         v                       v
+------------------------------------------------------+
|                   BuilderNode tree                   |
|   (intermediate mutable representation)              |
+------------------------------------------------------+
                          |
                          v
+------------------------------------------------------+
|              TreeCompiler.FlattenToBlob              |
|   - Depth-first traversal                            |
|   - Deduplication of method names, float/int params  |
|   - SubtreeOffset calculation                        |
|   - StructureHash + ParamHash                        |
|   - TreeValidator.Validate()                         |
+------------------------------------------------------+
                          |
                          v
+------------------------------------------------------+
|              BehaviorTreeBlob                        |
|   (immutable, shareable, zero-alloc at runtime)      |
+------------------------------------------------------+
                          |
                 +--------+--------+
                 |                 |
                 v                 v
    +-------------------+  +--------------------+
    | Interpreter.Tick  |  | BinaryTreeSerializer|
    | (Fbt.Kernel)      |  | .Save()             |
    +-------------------+  +--------------------+
```

### BTreeBuilder Internal Structure

```
+-------------------------------------------+
|  BTreeBuilder<TBlackboard, TContext>       |
|                                            |
|  _entries: List<BuilderEntry>              |
|  _registry: ActionRegistry<BB, Ctx>        |
|                                            |
|  +-BuilderEntry                            |
|    +-BuilderNode  (type, params)           |
|    +-NodeDebugMetadata (label, file, line) |
|    +-List<BuilderEntry> ChildEntries       |
|    +-TargetFieldName (expression bindings) |
|    +-TargetDtoType   (expression bindings) |
+-------------------------------------------+
          |
          | .Compile(treeName)
          v
+-------------------------------------------+
|  TreeCompiler.FlattenToBlob(root, name)    |
|  + populate DebugMetadata[]                |
+-------------------------------------------+
          |
          v
  BehaviorTreeBlob
```

### BTreeBuilder Leaf Binding Modes

The builder supports two modes for registering leaf (Action/Condition) delegates:

**Mode 1: Direct delegate** — The full `NodeLogicDelegate<TBlackboard, TContext>` is registered under a key derived from the delegate's method name and declaring type.

```
BTreeBuilder.Action(myDelegate)
  -> key = "MyNamespace.MyClass.MyMethod"
  -> registry.Register(key, myDelegate)
  -> BuilderNode.MethodName = key
```

**Mode 2: Expression-projected field** — A `ReusableConditionDelegate<TValue, TContext>` or `ReusableActionDelegate<TValue, TContext>` is registered with a curried wrapper that projects a blackboard field by byte offset using `Unsafe.AddByteOffset`. This allows the same condition logic to be reused across different blackboard types without knowing the full blackboard structure.

```
BTreeBuilder.Condition<CombatData>(
    bb => bb.Combat,            // field selector expression
    (ref CombatData d, ...) => ...)
  -> offset = Marshal.OffsetOf<TBlackboard>("Combat")
  -> key = "Namespace.Class.Method@<offset>"
  -> curried wrapper: Unsafe.As<TBlackboard, CombatData>(ref Unsafe.AddByteOffset(ref bb, offset))
  -> registry.Register(key, curried)
```

### Graph DOM vs Flat Bytecode

```
+------------------------------------------+
|           BehaviorTreeGraph              |
|  (mutable DOM for authoring tools)       |
|                                          |
|  RootNode: BehaviorTreeNode              |
|    BehaviorTreeNode (abstract)           |
|      |- CompositeNode (Sequence, etc.)   |
|      |    Children: List<BehaviorTreeNode>|
|      |- DecoratorNode (Inverter, etc.)   |
|      |    Child: BehaviorTreeNode?       |
|      |- LogicNode (Action, Condition)    |
|           DelegateName: string           |
+------------------------------------------+
                    |
                    | (future: graph compiler)
                    |
                    v
+------------------------------------------+
|          BehaviorTreeBlob                |
|  (flat bytecode, used at runtime)        |
+------------------------------------------+
```

The graph DOM provides a richer representation suitable for a node editor (position, comments, visual IDs). The flat bytecode is optimized purely for execution speed. The `BTreeBuilder.ToGraph()` method converts a builder tree to the graph DOM; the reverse path (graph to blob) is reserved for future visual editor tooling.

### FbtAutoDiscovery Flow

```
Application startup
      |
      v
FbtAutoDiscovery.ScanAndRegister<TBB, TCtx>(registry)
      |
      +-- foreach assembly in AppDomain.CurrentDomain.GetAssemblies()
      |     foreach type with [FbtRegistrar] attribute
      |       foreach public static method named "RegisterAll"
      |         method.Invoke(null, [registry])
      |           -> calls registry.Register("ActionName", delegate) for each action
      |
      v
ActionRegistry is fully populated
      |
      v
new Interpreter<TBB, TCtx>(blob, registry)  // delegates are bound
```

The `[FbtRegistrar]` attribute and the `RegisterAll` method are both emitted by the `Fbt.SourceGen` Roslyn source generator. The generator reads `[BTreeAction]` and `[BTreeCondition]` attributes from user-defined methods and emits the registrar class automatically at build time.

---

## Source Structure

### Root Level (`Fbt.Compiler` namespace)

| File | Type | Description |
|---|---|---|
| `BTreeBuilder.cs` | `class BTreeBuilder<TBlackboard, TContext>` | Fluent API for programmatic behavior tree construction. Produces a `BehaviorTreeBlob` via `.Compile()` or a graph DOM via `.ToGraph()`. |
| `BTreeSchema.cs` | `record BTreeSchema`, `record ActionDescriptor`, `record ConditionDescriptor` | Schema types produced by `BTreeSchemaExporter`. |
| `BTreeSchemaExporter.cs` | `static class BTreeSchemaExporter` | Scans assemblies for `[BTreeAction]`/`[BTreeCondition]` methods via reflection. Serializes results to JSON. |
| `FbtAutoDiscovery.cs` | `static class FbtAutoDiscovery` | Auto-registers delegates by scanning assemblies for `[FbtRegistrar]`-annotated types. |
| `ReusableDelegates.cs` | `delegate ReusableConditionDelegate<TValue, TCtx>`, `delegate ReusableActionDelegate<TValue, TCtx>` | Delegates for expression-projected field bindings. |

### `Fbt.Compiler.Graph` namespace (`Graph/`)

| File | Type | Description |
|---|---|---|
| `BehaviorTreeGraph.cs` | `class BehaviorTreeGraph` | Root container for the mutable tree DOM. Holds `TreeName`, `TreeId`, and `RootNode`. |
| `BehaviorTreeNode.cs` | `abstract class BehaviorTreeNode` | Base for all graph nodes. Carries `VisualId`, `Type`, `Parent`, UI position, and a comment. |
| `CompositeNode.cs` | `class CompositeNode : BehaviorTreeNode` | Composite (Sequence, Selector, Parallel). Holds `List<BehaviorTreeNode> Children` and `ParallelPolicy`. |
| `DecoratorNode.cs` | `class DecoratorNode : BehaviorTreeNode` | Decorator (Inverter, Repeater, Cooldown, Wait). Holds a single `Child`, `Duration`, and `RepeatCount`. |
| `LogicNode.cs` | `class LogicNode : BehaviorTreeNode` | Leaf (Action, Condition). Holds `DelegateName`, `TargetDtoType`, `TargetFieldName`. |

### Serialization infrastructure (in `Fbt.Kernel`, used by compiler)

The following types are defined in `Fbt.Kernel` (in its `Serialization/` subfolder) but are integral to the compilation pipeline:

| Type | Package location | Role |
|---|---|---|
| `BuilderNode` | `Fbt.Kernel / Serialization` | Intermediate tree representation created by `BTreeBuilder` and consumed by `TreeCompiler` |
| `TreeCompiler` | `Fbt.Kernel / Serialization` | Flattens `BuilderNode` to `BehaviorTreeBlob`; computes hashes; validates |
| `TreeValidator` | `Fbt.Kernel / Serialization` | Post-flatten validation: offsets, payload indices, illegal nesting |
| `JsonTreeData` / `JsonNode` | `Fbt.Kernel / Serialization` | JSON deserialization model |
| `BinaryTreeSerializer` | `Fbt.Kernel / Serialization` | Binary save/load |

---

## Public API Reference

### `BTreeBuilder<TBlackboard, TContext>` (class)

The central type of `Fbt.Compiler`. All methods return `this` (the current builder instance) except `Compile`, `GetRegistry`, and `ToGraph`, enabling fluent chaining.

```csharp
public sealed class BTreeBuilder<TBlackboard, TContext>
    where TBlackboard : struct
    where TContext : struct, IAIContext
```

#### Constructors

```csharp
// Creates a new builder with a fresh ActionRegistry.
public BTreeBuilder();
```

#### Composite methods (return `this`)

```csharp
// Adds a Sequence node. Children are defined by the 'children' lambda.
public BTreeBuilder<BB,Ctx> Sequence(
    Action<BTreeBuilder<BB,Ctx>> children,
    Guid visualId = default,
    [CallerFilePath] string sourceFile = "",
    [CallerLineNumber] int lineNumber = 0);

// Adds a Selector node.
public BTreeBuilder<BB,Ctx> Selector(
    Action<BTreeBuilder<BB,Ctx>> children,
    Guid visualId = default, ...);

// Adds a Parallel node with the given policy (0=RequireAll, 1=RequireOne).
public BTreeBuilder<BB,Ctx> Parallel(
    int policy,
    Action<BTreeBuilder<BB,Ctx>> children,
    Guid visualId = default, ...);

// Adds an ObserverSelector node (priority-abort selector).
public BTreeBuilder<BB,Ctx> ObserverSelector(
    Action<BTreeBuilder<BB,Ctx>> children,
    Guid visualId = default, ...);
```

#### Decorator methods (return `this`)

```csharp
public BTreeBuilder<BB,Ctx> Inverter(Action<BB,Ctx>> child, ...);
public BTreeBuilder<BB,Ctx> Repeater(int count, Action<BB,Ctx>> child, ...);
public BTreeBuilder<BB,Ctx> Cooldown(float duration, Action<BB,Ctx>> child, ...);
public BTreeBuilder<BB,Ctx> ForceSuccess(Action<BB,Ctx>> child, ...);
public BTreeBuilder<BB,Ctx> ForceFailure(Action<BB,Ctx>> child, ...);
public BTreeBuilder<BB,Ctx> UntilSuccess(Action<BB,Ctx>> child, ...);
public BTreeBuilder<BB,Ctx> UntilFailure(Action<BB,Ctx>> child, ...);
```

#### Leaf methods (return `this`)

```csharp
// Wait leaf: blocks for 'duration' seconds.
public BTreeBuilder<BB,Ctx> Wait(float duration, ...);

// Subtree leaf: delegates to an external named tree.
public BTreeBuilder<BB,Ctx> Subtree(string treeName, ...);

// Action leaf using a full NodeLogicDelegate.
public BTreeBuilder<BB,Ctx> Action(
    NodeLogicDelegate<BB, Ctx> action, ...);

// Condition leaf using a full NodeLogicDelegate.
public BTreeBuilder<BB,Ctx> Condition(
    NodeLogicDelegate<BB, Ctx> condition, ...);

// Action leaf with field projection (reusable across blackboard types).
public BTreeBuilder<BB,Ctx> Action<TValue>(
    Expression<Func<BB, TValue>> fieldSelector,
    ReusableActionDelegate<TValue, Ctx> logic, ...)
    where TValue : unmanaged;

// Condition leaf with field projection.
public BTreeBuilder<BB,Ctx> Condition<TValue>(
    Expression<Func<BB, TValue>> fieldSelector,
    ReusableConditionDelegate<TValue, Ctx> logic, ...)
    where TValue : unmanaged;
```

#### Terminal methods

```csharp
// Compiles the tree to a BehaviorTreeBlob (with DebugMetadata).
// Throws BehaviorTreeBuildException on validation failure.
// Exactly one root node must be present.
// Passes registry.TryGetDeactivator as the isResourceOwning callback to TreeCompiler
// so that Action/Condition nodes with registered deactivators have NodeDefinition.IsResourceOwning set.
// Produced blobs are stamped Version = 2.
public BehaviorTreeBlob Compile(string treeName);

// Overload accepting an external isResourceOwning predicate.
// Falls back to the internal registry when isResourceOwning is null.
// Used by tooling that builds trees without a typed registry.
public BehaviorTreeBlob Compile(string treeName, Func<string, bool>? isResourceOwning);

// Returns the accumulated ActionRegistry.
public ActionRegistry<BB, Ctx> GetRegistry();

// Converts the builder state to a BehaviorTreeGraph DOM.
public BehaviorTreeGraph ToGraph(string treeName);
```

### `BTreeSchema` and related records

```csharp
public record BTreeSchema(
    ActionDescriptor[] Actions,
    ConditionDescriptor[] Conditions,
    string[] BlackboardDtoTypes);

public record ActionDescriptor(
    string MethodName,
    string DeclaringType,
    string BlackboardDtoType,
    string FieldName,
    int FieldOffset);       // -1 when scanned at runtime (real offsets from SourceGen)

public record ConditionDescriptor(
    string MethodName,
    string DeclaringType,
    string BlackboardDtoType,
    string FieldName,
    int FieldOffset);
```

### `BTreeSchemaExporter` (static class)

```csharp
public static class BTreeSchemaExporter
{
    // Scans assemblies for [BTreeAction] and [BTreeCondition] methods.
    // Non-reflectable assemblies are silently skipped.
    public static BTreeSchema Export(IEnumerable<Assembly> assemblies);

    // Serializes schema to an indented JSON file.
    public static void ExportToJson(BTreeSchema schema, string outputPath);
}
```

Notes:
- `FieldOffset` is always `-1` in the output of `Export`. Actual byte offsets are computed at compile time by `Fbt.SourceGen` via the Roslyn semantic model.
- This is a **tooling utility**, not intended for use in game/simulation hot paths.

### `FbtAutoDiscovery` (static class)

```csharp
public static class FbtAutoDiscovery
{
    // Scans AppDomain.CurrentDomain for [FbtRegistrar] types and calls RegisterAll.
    // TBlackboard must be unmanaged (stronger constraint than the registry requires).
    // Non-reflectable assemblies and type-mismatch registrars are silently skipped.
    public static void ScanAndRegister<TBlackboard, TContext>(
        ActionRegistry<TBlackboard, TContext> registry)
        where TBlackboard : unmanaged
        where TContext : struct, IAIContext;
}
```

### `ReusableConditionDelegate<TValue, TContext>` (delegate)

```csharp
public delegate NodeStatus ReusableConditionDelegate<TValue, TContext>(
    ref TValue data,
    ref BehaviorTreeState state,
    ref TContext ctx)
    where TValue : unmanaged
    where TContext : struct, IAIContext;
```

### `ReusableActionDelegate<TValue, TContext>` (delegate)

```csharp
public delegate NodeStatus ReusableActionDelegate<TValue, TContext>(
    ref TValue data,
    ref BehaviorTreeState state,
    ref TContext ctx)
    where TValue : unmanaged
    where TContext : struct, IAIContext;
```

### Graph DOM Types

```csharp
// Root container for the mutable authoring DOM.
public class BehaviorTreeGraph
{
    public string TreeName;
    public Guid TreeId;
    public BehaviorTreeNode? RootNode;
}

// Base class for all graph nodes.
public abstract class BehaviorTreeNode
{
    public Guid VisualId;
    public NodeType Type;
    public BehaviorTreeNode? Parent;
    public float UiPosX;
    public float UiPosY;
    public string CustomComment;
}

// Composites: Sequence, Selector, Parallel.
public class CompositeNode : BehaviorTreeNode
{
    public List<BehaviorTreeNode> Children;
    public int ParallelPolicy;
}

// Decorators: Inverter, Repeater, Cooldown, Wait.
public class DecoratorNode : BehaviorTreeNode
{
    public BehaviorTreeNode? Child;
    public float Duration;
    public int RepeatCount;
}

// Leaves: Action, Condition.
public class LogicNode : BehaviorTreeNode
{
    public string DelegateName;
    public string TargetDtoType;    // set for expression-projected bindings
    public string TargetFieldName;  // set for expression-projected bindings
}
```

---

## Dependencies

`Fbt.Compiler` has one project dependency and no external package dependencies:

| Dependency | Type | Notes |
|---|---|---|
| `Fbt.Kernel` | ProjectReference | Provides `BehaviorTreeBlob`, `NodeDefinition`, `NodeStatus`, `NodeType`, `NodeLogicDelegate`, `ActionRegistry`, `IAIContext`, `BTreeActionAttribute`, `BTreeConditionAttribute`, `FbtRegistrarAttribute`, `TreeCompiler`, `BinaryTreeSerializer`, `TreeValidator`, `BuilderNode` |

BCL dependencies:

| Assembly | Usage |
|---|---|
| `System.Reflection` | `BTreeSchemaExporter`, `FbtAutoDiscovery` — assembly scanning |
| `System.Text.Json` | `BTreeSchemaExporter.ExportToJson` |
| `System.Linq` | `BTreeSchemaExporter.Export` — DTO type deduplication |
| `System.Linq.Expressions` | `BTreeBuilder.Condition<TValue>` / `Action<TValue>` — field selector extraction |
| `System.Runtime.InteropServices` | `Marshal.OffsetOf` — byte offset computation in expression bindings |
| `System.Runtime.CompilerServices` | `[CallerFilePath]`, `[CallerLineNumber]`, `Unsafe.AddByteOffset`, `Unsafe.As` |

Project-level settings:
- `AllowUnsafeBlocks = true` — required for `Unsafe.AddByteOffset` in expression-projected bindings
- `Nullable = enable`
- `TreatWarningsAsErrors = true`
- `LangVersion = latest` — enables C# records, init-only properties

---

## Usage Examples

### Example 1: Fluent BTreeBuilder — simple guard AI

```csharp
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fbt.Serialization;

// Define structs for this AI type
struct GuardBB { public bool EnemyVisible; public int EnemyId; }
struct GuardCtx : IAIContext, ITreeTracer { /* ... implement interface ... */ }

var builder = new BTreeBuilder<GuardBB, GuardCtx>();

builder.Selector(b => b
    .Sequence(b2 => b2
        .Condition(
            (ref GuardBB bb, ref BehaviorTreeState st, ref GuardCtx ctx, int _) =>
                bb.EnemyVisible ? NodeStatus.Success : NodeStatus.Failure)
        .Action(
            (ref GuardBB bb, ref BehaviorTreeState st, ref GuardCtx ctx, int _) =>
            {
                ctx.IssueAttackOrder(bb.EnemyId);
                return NodeStatus.Success;
            }))
    .Action(
        (ref GuardBB bb, ref BehaviorTreeState st, ref GuardCtx ctx, int _) =>
        {
            ctx.PatrolStep();
            return NodeStatus.Running;
        }));

BehaviorTreeBlob blob = builder.Compile("GuardAI");
ActionRegistry<GuardBB, GuardCtx> registry = builder.GetRegistry();

var interpreter = new Interpreter<GuardBB, GuardCtx>(blob, registry);
```

### Example 2: Repeater and Cooldown decorators

```csharp
var builder = new BTreeBuilder<SoldierBB, SoldierCtx>();

builder.Sequence(b => b
    // Try the flanking maneuver up to 3 times before giving up
    .Repeater(3, b2 => b2
        .Action((ref SoldierBB bb, ref BehaviorTreeState st, ref SoldierCtx ctx, int _) =>
        {
            ctx.MoveToFlankPosition();
            return NodeStatus.Success;
        }))
    // Fire, but only once every 1.5 seconds
    .Cooldown(1.5f, b2 => b2
        .Action((ref SoldierBB bb, ref BehaviorTreeState st, ref SoldierCtx ctx, int _) =>
        {
            ctx.FireWeapon();
            return NodeStatus.Success;
        })));

BehaviorTreeBlob blob = builder.Compile("FlankAndFire");
```

### Example 3: Expression-projected field binding (reusable delegates)

This pattern allows the same condition logic to be reused across different blackboard structs. The byte offset of the field is computed once at tree-build time, not at tick time.

```csharp
// Shared condition logic operating on a sub-field of any blackboard
static NodeStatus HasAmmo(
    ref WeaponState weapon,
    ref BehaviorTreeState st,
    ref UnitCtx ctx)
    => weapon.AmmoCount > 0 ? NodeStatus.Success : NodeStatus.Failure;

static NodeStatus Reload(
    ref WeaponState weapon,
    ref BehaviorTreeState st,
    ref UnitCtx ctx)
{
    weapon.StartReload();
    return NodeStatus.Running;
}

// UnitBlackboard has a field named 'Weapon' of type WeaponState
var builder = new BTreeBuilder<UnitBlackboard, UnitCtx>();

builder.Sequence(b => b
    // Condition with field projection: bb.Weapon is projected to ref WeaponState
    .Condition<WeaponState>(bb => bb.Weapon, HasAmmo)
    // Action with field projection
    .Action<WeaponState>(bb => bb.Weapon,
        (ref WeaponState w, ref BehaviorTreeState st, ref UnitCtx ctx, int _) =>
        {
            ctx.FireWeapon();
            return NodeStatus.Success;
        }));

BehaviorTreeBlob blob = builder.Compile("FireOrReload");
// The delegate key is: "MyNamespace.MyClass.HasAmmo@<byteOffset>"
// The curried wrapper uses Unsafe.AddByteOffset to project bb.Weapon without boxing
```

### Example 4: Auto-discovery with source-generated registrar

When `Fbt.SourceGen` is referenced in the user project, it generates a registrar class at compile time. At startup, call `FbtAutoDiscovery.ScanAndRegister` once:

```csharp
using Fbt.Compiler;
using Fbt.Runtime;

// In your game/simulation startup:
var registry = new ActionRegistry<UnitBlackboard, UnitContext>();

// Scans all loaded assemblies for [FbtRegistrar] classes and calls their RegisterAll.
// This picks up the auto-generated registrar from Fbt.SourceGen without manual wiring.
FbtAutoDiscovery.ScanAndRegister(registry);

// Now registry contains all [BTreeAction] and [BTreeCondition] delegates from the assembly.
var interpreter = new Interpreter<UnitBlackboard, UnitContext>(someBlob, registry);
```

The source-generated registrar looks like this (illustration only, not hand-written):

```csharp
[FbtRegistrar]
internal static class GeneratedFbtRegistrar
{
    public static void RegisterAll(ActionRegistry<UnitBlackboard, UnitContext> registry)
    {
        registry.Register("Attack",       UnitActions.Attack);
        registry.Register("Patrol",       UnitActions.Patrol);
        registry.RegisterCondition("IsEnemyVisible", UnitConditions.IsEnemyVisible);
        // ... all [BTreeAction] and [BTreeCondition] methods in the assembly
    }
}
```

### Example 5: Export schema for visual editor tooling

```csharp
using Fbt.Compiler;
using System.Reflection;

// Scan all loaded assemblies for [BTreeAction] / [BTreeCondition] methods
var assemblies = AppDomain.CurrentDomain.GetAssemblies();
BTreeSchema schema = BTreeSchemaExporter.Export(assemblies);

Console.WriteLine($"Found {schema.Actions.Length} actions, {schema.Conditions.Length} conditions");

// Export to JSON for use by a visual tree editor
BTreeSchemaExporter.ExportToJson(schema, "btree-schema.json");

// btree-schema.json will contain:
// {
//   "Actions": [
//     { "MethodName": "Attack", "DeclaringType": "MyGame.UnitActions", ... }
//   ],
//   "Conditions": [ ... ],
//   "BlackboardDtoTypes": [ "MyGame.UnitBlackboard" ]
// }
```

### Example 6: Convert builder to graph DOM and inspect

```csharp
var builder = new BTreeBuilder<GuardBB, GuardCtx>();
builder.Sequence(b => b
    .Condition((ref GuardBB bb, ref BehaviorTreeState st, ref GuardCtx ctx, int _) =>
        NodeStatus.Failure)
    .Action((ref GuardBB bb, ref BehaviorTreeState st, ref GuardCtx ctx, int _) =>
        NodeStatus.Success));

// Get the mutable graph DOM (before compiling)
BehaviorTreeGraph graph = builder.ToGraph("TestTree");

// Inspect the graph
Console.WriteLine(graph.TreeName);                    // "TestTree"
Console.WriteLine(graph.RootNode?.Type);              // Sequence
var composite = (CompositeNode)graph.RootNode!;
Console.WriteLine(composite.Children.Count);          // 2
Console.WriteLine(composite.Children[0].Type);        // Condition
Console.WriteLine(composite.Children[1].Type);        // Action

// The visual editor can manipulate composite.Children, change node positions,
// add comments, then re-serialize to JSON or re-compile.
```

### Example 7: Full pipeline — build, compile, serialize, reload

```csharp
// 1. Build
var builder = new BTreeBuilder<UnitBB, UnitCtx>();
builder.Selector(b => b
    .Sequence(b2 => b2
        .Condition((ref UnitBB bb, ref BehaviorTreeState st, ref UnitCtx ctx, int _) =>
            bb.HasTarget ? NodeStatus.Success : NodeStatus.Failure)
        .Action((ref UnitBB bb, ref BehaviorTreeState st, ref UnitCtx ctx, int _) =>
        {
            ctx.Attack(bb.TargetId);
            return NodeStatus.Success;
        }))
    .Wait(3.0f));

// 2. Compile
BehaviorTreeBlob blob = builder.Compile("UnitCombat");
ActionRegistry<UnitBB, UnitCtx> registry = builder.GetRegistry();

// 3. Save to binary
BinaryTreeSerializer.Save(blob, "unit-combat.fbt");

// 4. Later: load and create interpreter
BehaviorTreeBlob loaded = BinaryTreeSerializer.Load("unit-combat.fbt");
// Note: loaded blob has no delegates; registry must be provided separately
var interpreter = new Interpreter<UnitBB, UnitCtx>(loaded, registry);

// 5. Tick entities
var bb    = new UnitBB { HasTarget = true, TargetId = 42 };
var state = new BehaviorTreeState();
var ctx   = new UnitCtx(deltaTime: 0.016f);
NodeStatus result = interpreter.Tick(ref bb, ref state, ref ctx);
```

---

## Builder Internals: Key Implementation Details

### CallerFilePath / CallerLineNumber

All builder methods accept optional `[CallerFilePath]` and `[CallerLineNumber]` parameters. The C# compiler fills these in automatically at the call site. The resulting `NodeDebugMetadata` objects are attached to the compiled `BehaviorTreeBlob.DebugMetadata[]` array (in depth-first node order), enabling source-location-aware debugging.

```csharp
// The compiler fills in sourceFile and lineNumber automatically:
builder.Sequence(b => b.Action(MyAction));
// => NodeDebugMetadata { Label="MyAction", SourceFile="MyFile.cs", LineNumber=42 }
```

### Delegate Key Generation

For direct delegate bindings, the key is derived from the delegate's `Method.DeclaringType.FullName` and `Method.Name`, separated by `.`. If multiple overloads exist, the first registered wins (no overload resolution).

For expression-projected bindings, the key includes the byte offset:
`"Namespace.Class.Method@<byteOffset>"`.

This offset-tagged key allows the same method to be registered with different blackboard types and different field offsets simultaneously in the same registry.

### ChildCount Encoding

`NodeDefinition.ChildCount` is a `byte`, limiting composites to 255 children. In practice, `Parallel` is further limited to 16 children due to the bitfield storage in `LocalRegisters[3]`.

The builder does not enforce an explicit composite-child limit; the `TreeValidator` emits a warning for `Parallel > 16` children and the interpreter silently truncates.

### Subtree Offset Calculation

`BuilderNode.CalculateSubtreeSize()` recursively counts all nodes in a subtree:

```
size(node) = 1 + sum(size(child) for child in node.Children)
```

This is computed before flattening; the result becomes `NodeDefinition.SubtreeOffset`. The flat array position of the next sibling of node at index `i` is always `i + Nodes[i].SubtreeOffset`.

---

## Architectural Decisions

### Why BTreeBuilder over pure JSON?

JSON trees are human-readable and tool-friendly but are strings — no type safety, no IDE completion, and no compile-time verification of action names. `BTreeBuilder` expresses tree structure in C# with full type checking: if `MyAction` does not match the `NodeLogicDelegate<TBlackboard, TContext>` signature, the code will not compile.

JSON remains appropriate for data-driven scenarios (level designers editing trees without recompiling) and for serialization to disk.

### Why separate Graph DOM from Bytecode?

The `BehaviorTreeGraph` DOM carries editor-specific data: node positions, comments, stable visual IDs (GUIDs). These are meaningless to the runtime and would increase the bytecode footprint unnecessarily.

The flat bytecode is optimized purely for sequential read access by the interpreter. Carrying mutable lists and position data into the runtime format would break cache-locality.

### Why is BTreeBuilder generic but BehaviorTreeGraph is not?

`BTreeBuilder<TBlackboard, TContext>` is generic so it can register typed delegates into `ActionRegistry<TBlackboard, TContext>`. The graph DOM (`BehaviorTreeGraph`) stores delegate names as strings, not delegates, so it has no need for type parameters. This allows the graph DOM to be used by non-generic tooling (serializers, visual editors) that does not know the concrete blackboard type.

### Why is FbtAutoDiscovery a reflection-based scan rather than a static list?

The source generator (`Fbt.SourceGen`) emits the registrar class at compile time. `FbtAutoDiscovery` locates it at runtime by scanning for `[FbtRegistrar]`. This decouples the registrar from the startup code: adding a new action only requires adding the `[BTreeAction]` attribute; no manual wiring of the new action into a startup list is needed.

The trade-off is a small one-time reflection cost at startup. This is paid once and the results are stored in the registry for the duration of the process.

---

## Best Practices

### Using BTreeBuilder

1. **Compile once, reuse.** Call `.Compile()` once during initialization. The resulting `BehaviorTreeBlob` and `ActionRegistry` can be shared across all entities using that tree type for the lifetime of the process.

2. **Prefer expression-projected delegates for shared logic.** If the same condition (e.g., "has ammo") applies to multiple unit types with different blackboard layouts, write the condition against the sub-field type and use `Condition<TValue>(bb => bb.FieldName, logic)`. This avoids duplicating the condition logic per blackboard type.

3. **Use `[BTreeDefinition]` for named tree catalog entries.** Mark methods returning a `BTreeBuilder` or `BehaviorTreeBlob` with `[BTreeDefinition("TreeName")]`. The source generator will emit a catalog so all available trees can be discovered programmatically.

4. **Call `builder.GetRegistry()` before discarding the builder.** The registry accumulates all delegate bindings. It is needed to construct the `Interpreter`. If you discard the builder and only keep the blob, you lose the registry and cannot execute the tree.

5. **Use `ToGraph()` for editor round-trips.** If a visual editor needs to display or modify a tree that was built programmatically, call `ToGraph()` to get the mutable DOM, let the editor modify it, then re-serialize the modified graph to JSON for recompilation.

### Schema Exporter

6. **Run `BTreeSchemaExporter` at tool time, not at runtime.** Schema export involves full assembly reflection which is not suitable for hot paths. Call it from a build script, editor plugin, or standalone tool to produce a JSON schema file for the visual tree editor.

7. **Note that `FieldOffset = -1` in runtime-scanned schemas.** Real offsets are only available in source-generator output. Do not rely on `FieldOffset` from `BTreeSchemaExporter` for runtime dispatch.

### Auto-Discovery

8. **Call `FbtAutoDiscovery.ScanAndRegister` once per `ActionRegistry`.** Subsequent calls will overwrite existing registrations for the same action names. This is harmless but wasteful.

9. **Load game assemblies before calling `ScanAndRegister`.** `FbtAutoDiscovery` scans `AppDomain.CurrentDomain.GetAssemblies()`. Assemblies loaded after the scan will not be picked up. If using `FbtAssemblyHotReloader`, its `AssemblyReloadHandler` callback handles re-registration for hot-loaded assemblies.

---

## Related Projects

| Project | Relationship |
|---|---|
| `Fbt.Kernel` | **Direct dependency.** Provides the runtime types (`BehaviorTreeBlob`, `Interpreter`, `ActionRegistry`, `TreeCompiler`, `BinaryTreeSerializer`, etc.) that `Fbt.Compiler` builds on top of. |
| `Fbt.SourceGen` | **Consumer of attributes defined in `Fbt.Kernel`.** Reads `[BTreeAction]`, `[BTreeCondition]`, `[BTreeDefinition]`, and `[SharedAi*]` attributes and emits `[FbtRegistrar]`-annotated registrars. `FbtAutoDiscovery` in `Fbt.Compiler` locates and invokes these registrars at runtime. |
| HROT AI modules | **Primary consumers.** HROT unit-AI modules use `BTreeBuilder` to define trees in C#. The generated blobs are ticked by `Interpreter<UnitBlackboard, UnitContext>` each simulation frame. |
| FastHSM | **Sibling library.** `SharedAiConditionAttribute` / `SharedAiActionAttribute` (defined in `Fbt.Kernel`) allow logic written for BTree to be shared with HSM transitions without duplication. `Fbt.SourceGen` emits adapters for both systems from the same annotated method. |
| NodeEdit (visual editor) | **Potential consumer of Graph DOM.** The `BehaviorTreeGraph` / `CompositeNode` / `DecoratorNode` / `LogicNode` types are designed to be consumed by a visual node editor. The schema produced by `BTreeSchemaExporter` populates the editor's action/condition palette. |
