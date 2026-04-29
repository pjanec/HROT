# Task Detail

**Reference:** See [DESIGN.md](./DESIGN.md) for architecture and rationale.

---

## Phase 1: Fbt.Compiler — Fluent Builder Foundation

---

### TASK-FBT-001: Add `FlattenToBlob` Overload to `TreeCompiler`

**Design Reference:** DESIGN.md § 2.1, Phase 1

**Scope:**
- Modify `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Serialization/TreeCompiler.cs`.
- Add a public static method `FlattenToBlob(BuilderNode root, string treeName)` that accepts a pre-built `BuilderNode` tree and produces a `BehaviorTreeBlob`.
- After flattening, automatically invoke `TreeValidator.Validate(blob)`. If validation fails, throw `BehaviorTreeBuildException` with the validator's error message. This prevents corrupt blobs (e.g., nested Parallel nodes, nested Repeater nodes) from ever reaching the interpreter.
- The existing `CompileFromJson` method continues to work by parsing JSON into a `BuilderNode` tree and calling the new method internally (refactoring, no external behavior change).
- Out of scope: modifying the JSON format or `JsonTreeData`.

**Constraints:**
- The new overload must be deterministic — the same `BuilderNode` tree must always produce the same blob (same hashes).
- `StructureHash` must depend only on node types and hierarchy, not on method names or parameter values.
- `ParamHash` must depend only on `FloatParams[]` and `IntParams[]`.
- The flat node array must follow depth-first order matching the existing compiler output.
- If the size of any mapped blackboard DTO (`sizeof(TBlackboardDto)`) supplied via the builder's expression overloads exceeds `BehaviorConstants.BrainBlackboardByteSize` (128 bytes), `FlattenToBlob` must throw a `BehaviorTreeBuildException` before returning the blob — preventing silent memory corruption via `Unsafe.AddByteOffset`.

**Success Conditions:**

SC1 — `FlattenToBlob` produces the same node array as `CompileFromJson` for an equivalent tree:
```
// Setup: Build a Sequence[Action("Move"), Action("Attack")] BuilderNode tree manually
// Action: Call FlattenToBlob and CompileFromJson with equivalent inputs
// Assert: blob.Nodes.Length == 3
//         blob.Nodes[0].Type == NodeType.Sequence
//         blob.MethodNames.Contains("Move") && blob.MethodNames.Contains("Attack")
//         blob.StructureHash == (hash produced by CompileFromJson for same structure)
```

SC2 — `StructureHash` is identical for trees with same shape but different method names:
```
// Setup: BuilderNode tree A: Sequence[Action("Move")], tree B: Sequence[Action("Attack")]
// Action: FlattenToBlob(treeA) and FlattenToBlob(treeB)
// Assert: blobA.StructureHash == blobB.StructureHash
//         blobA.ParamHash == blobB.ParamHash (both have no float/int params)
```

SC3 — `ParamHash` differs when float params change:
```
// Setup: Build a Wait(1.0f) node and a Wait(2.0f) node
// Assert: blobA.ParamHash != blobB.ParamHash
```

SC4 — Existing `CompileFromJson` continues to pass all existing `SerializationTests` without modification.

SC5 — `FlattenToBlob` throws `BehaviorTreeBuildException` for a tree with nested Repeater nodes:
```
// Setup: BuilderNode tree with Repeater(child=Repeater(...))
// Assert: FlattenToBlob throws BehaviorTreeBuildException
//         Exception message contains "Repeater" and "nested"
```

---

### TASK-FBT-002: Create `BTreeBuilder<TBlackboard>` Fluent API

**Design Reference:** DESIGN.md § 2.1, Phase 1

**Scope:**
- Create new project `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/Fbt.Compiler.csproj` referencing `Fbt.Kernel`.
- Implement `BTreeBuilder<TBlackboard>` class in namespace `Fbt.Compiler` where `TBlackboard : struct`.
- Fluent methods for all existing node types:
  - Composites: `Selector(Action<BTreeBuilder<TBlackboard>> children, Guid visualId = default)`, `Sequence(...)`, `Parallel(int policy, ..., Guid visualId = default)`
  - Decorators: `Inverter(Action<BTreeBuilder<TBlackboard>> child, Guid visualId = default)`, `Repeater(int count, ..., Guid visualId = default)`, `Wait(float duration, Guid visualId = default)`, `Cooldown(float duration, Guid visualId = default)`
  - Leaves: `Action(NodeLogicDelegate<TBlackboard, TContext> delegate, Guid visualId = default)`, `Condition(NodeLogicDelegate<TBlackboard, TContext> delegate, Guid visualId = default)`
  - Every `visualId` parameter defaults to `default` (`Guid.Empty`), which the builder replaces with `Guid.NewGuid()` so every node always has a unique stable ID.
- `BehaviorTreeBlob Compile(string treeName)` method calls `TreeCompiler.FlattenToBlob`. If `TreeValidator.Validate` inside `FlattenToBlob` fails, the exception propagates immediately — there is no silent partial result.
- Out of scope: expression-based offset resolution (that is TASK-FBT-003).

**Constraints:**
- The builder is NOT generic over `TContext`. Context is bound at the `ActionRegistry`/`Interpreter` level.
- Each leaf method that accepts a delegate automatically generates a stable string key for the `ActionRegistry` (based on `delegate.Method.DeclaringType.FullName + "." + delegate.Method.Name`). The builder registers the delegate in an internal `ActionRegistry<TBlackboard, BTreeContext>` it accumulates.
- Method must be callable fluently: `new BTreeBuilder<MyBB>().Sequence(s => s.Action(MyAction).Action(MyOther)).Compile("TreeName")`.
- The builder also returns the accumulated `ActionRegistry` so callers can create an `Interpreter`.
- If `Compile()` produces a blob that fails `TreeValidator.Validate` (e.g., a `Repeater` nested inside another `Repeater`), it must throw `BehaviorTreeBuildException` immediately with a message identifying the illegal nesting. There must be no silent partial result or stub fallback.

**Success Conditions:**

SC1 — Simple sequence produces correct blob:
```
// Setup: BTreeBuilder<DemoBlackboard>().Sequence(s => s.Action(SomeAction).Action(OtherAction))
// Action: Compile("Test")
// Assert: blob.Nodes[0].Type == NodeType.Sequence, blob.Nodes[1].Type == NodeType.Action
//         blob.MethodNames.Length == 2
```

SC2 — Interpreter created from builder executes correctly:
```
// Setup: Builder with a single Condition returning Failure followed by Action
//        (i.e. a Sequence where condition fails)
// Action: Tick interpreter with a fresh blackboard and state
// Assert: Tick returns NodeStatus.Failure, action is never called
```

SC3 — Nested composites produce correct subtree offsets:
```
// Setup: Selector(Sequence(Cond, Action), Action)
// Assert: blob.Nodes[0].Type == Selector, SubtreeOffset == 5
//         blob.Nodes[1].Type == Sequence, SubtreeOffset == 3
```

SC4 — Duplicate action delegates share the same `MethodNames` entry (deduplication):
```
// Setup: Same delegate reference used twice in the tree
// Assert: blob.MethodNames.Length == 1
```

SC5 — `Compile()` throws `BehaviorTreeBuildException` for a tree with nested Repeater:
```
// Setup: new BTreeBuilder<DemoBlackboard>().Repeater(2, r => r.Repeater(3, ...)).Compile("T")
// Assert: throws BehaviorTreeBuildException
//         Exception.Message contains "Repeater" and "nested"
```

SC6 — `VisualId` parameter is stored and accessible via `NodeDebugMetadata`:
```
// Setup: var id = Guid.NewGuid();
//        BTreeBuilder<DemoBlackboard>().Action(SomeAction, visualId: id).Compile("T")
// Assert: blob.DebugMetadata[leafNodeIndex].VisualId == id.ToString()
```

SC7 — When `visualId` is omitted (default), builder auto-assigns a non-empty Guid:
```
// Setup: BTreeBuilder<DemoBlackboard>().Action(SomeAction).Compile("T")
// Assert: blob.DebugMetadata[leafNodeIndex].VisualId != Guid.Empty.ToString()
//         blob.DebugMetadata[leafNodeIndex].VisualId != string.Empty
```

---

### TASK-FBT-003: Expression-Based Blackboard Parameter Binding

**Design Reference:** DESIGN.md § 2.2, Phase 1

**Scope:**
- Add generic overloads to `BTreeBuilder<TBlackboard>` in `Fbt.Compiler`:
  ```
  BTreeBuilder<TBlackboard> Condition<TValue>(
      Expression<Func<TBlackboard, TValue>> fieldSelector,
      ReusableConditionDelegate<TValue> logic,
      ...)
      where TValue : unmanaged;

  BTreeBuilder<TBlackboard> Action<TValue>(
      Expression<Func<TBlackboard, TValue>> fieldSelector,
      ReusableActionDelegate<TValue> logic,
      ...)
      where TValue : unmanaged;
  ```
- Define `ReusableConditionDelegate<TValue>` and `ReusableActionDelegate<TValue>` delegate types:
  ```
  public delegate NodeStatus ReusableConditionDelegate<TValue>(
      ref TValue data, ref BehaviorTreeState state, ref BTreeContext ctx)
      where TValue : unmanaged;
  ```
- The builder uses `System.Linq.Expressions` to extract the field/property name from the lambda, then `Marshal.OffsetOf<TBlackboard>(name)` to compute byte offset at setup time.
- It registers a curried closure in the internal `ActionRegistry` using `Unsafe.AddByteOffset` + `Unsafe.As` to project the blackboard into `ref TValue`.
- Out of scope: source generator integration (that is TASK-FBT-011/012).

**Constraints:**
- `Marshal.OffsetOf` is only called once at tree-build time, never during `Interpreter.Tick`.
- `TBlackboard` must be `unmanaged` (enforced by the generic constraint on `BTreeBuilder<TBlackboard>`).
- `TValue` must be `unmanaged`.
- The lambda must be a direct field or property access (e.g., `dto => dto.AmmoCount`); nested access is not required to work and may throw a descriptive `ArgumentException` if the expression tree cannot be resolved.
- The auto-generated registry key must be stable across builds for the same `(delegateMethod, byteOffset)` pair.

**Success Conditions:**

SC1 — Correct byte offset computed from lambda:
```
// Setup: struct MyBb { public int FieldA; public float FieldB; }
// Action: BTreeBuilder<MyBb>.Condition(dto => dto.FieldB, SomeDelegate)
// Assert: The closure registered in ActionRegistry reads the blackboard at byte offset 4
//         (sizeof(int) = 4; FieldB follows FieldA)
```

SC2 — Reusable delegate receives correct value via ref:
```
// Setup: struct Bb { public int Counter; }; condition checks Counter > 0
// Action: Set blackboard.Counter = 5; Tick interpreter
// Assert: condition returns NodeStatus.Success
```

SC3 — Delegate can mutate the projected field:
```
// Setup: Action delegate that decrements ref int ammo
// Action: Set blackboard.AmmoCount = 3; Tick twice
// Assert: blackboard.AmmoCount == 1 after two successful ticks
```

SC4 — Wrong field type generates an error (at tree build time, not tick time):
```
// Note: Enforced by generic constraint TValue : unmanaged — managed reference types
//       cause a compile-time error. Test that a struct with a managed field in TValue
//       fails to compile (test via [DoesNotCompile] or notes in test description).
```

---

### TASK-FBT-004: Add `NodeDebugMetadata` to `BehaviorTreeBlob`

**Design Reference:** DESIGN.md § 2.6, Phase 1

**Scope:**
- Add `NodeDebugMetadata` class in `Fbt.Compiler` (not `Fbt.Kernel` — it is managed and debug-only):
  ```
  public class NodeDebugMetadata
  {
      public string Label = string.Empty;
      public string SourceFile = string.Empty;
      public int LineNumber;
      public string CustomComment = string.Empty;
      public string VisualId = string.Empty;  // For future authoring tool
  }
  ```
- Add `[NonSerialized] public NodeDebugMetadata[]? DebugMetadata` to `BehaviorTreeBlob` in `Fbt.Kernel`.
- Update `BTreeBuilder<TBlackboard>` to populate `DebugMetadata` when compiling. Every fluent method captures `[CallerFilePath]` and `[CallerLineNumber]` automatically. Caller can also pass an optional `string comment` and `string visualId` parameter.
- `BinaryTreeSerializer` and `TreeCompiler` must ignore/skip `DebugMetadata` (it is `[NonSerialized]`).

**Constraints:**
- `DebugMetadata` is allowed to be `null` (e.g., for blobs compiled via `CompileFromJson`).
- When non-null, `DebugMetadata.Length == blob.Nodes.Length` (one entry per node).
- All entries for composite/decorator nodes that have no explicit label get auto-labels (e.g., `"Sequence"`, `"Selector"`, `"Wait(2.0s)"`).

**Success Conditions:**

SC1 — Builder populates metadata with caller info:
```
// Setup: Call BTreeBuilder.Action(MyDelegate) from a known source file and line
// Assert: blob.DebugMetadata[nodeIndex].SourceFile == Path.GetFileName(callerFilePath)
//         blob.DebugMetadata[nodeIndex].LineNumber == callerLineNumber
```

SC2 — BinaryTreeSerializer round-trip does not corrupt anything:
```
// Setup: Compile blob with non-null DebugMetadata, save via BinaryTreeSerializer, load back
// Assert: Loaded blob executes correctly (Tick returns expected result)
//         DebugMetadata is null on the loaded blob (not serialized)
```

SC3 — Auto-labels for composites include type name:
```
// Setup: Sequence node with no explicit label
// Assert: blob.DebugMetadata[sequenceNodeIndex].Label == "Sequence"
```

SC4 — Wait decorator auto-label includes duration:
```
// Assert: blob.DebugMetadata[waitNodeIndex].Label starts with "Wait("
```

---

### TASK-FBT-005: Graph Data Structures for Authoring Tool

**Design Reference:** DESIGN.md § 2.10, Phase 1

**Scope:**
- Create `Fbt.Compiler.Graph` namespace in `Fbt.Compiler` project.
- Classes mirroring `Fhsm.Compiler.Graph`:
  - `BehaviorTreeGraph` — root container (`string TreeName`, `Guid TreeId`, `BehaviorTreeNode? RootNode`).
  - `BehaviorTreeNode` — abstract base (`Guid VisualId`, `NodeType Type`, `BehaviorTreeNode? Parent`, `float UiPosX`, `float UiPosY`, `string CustomComment`).
  - `CompositeNode : BehaviorTreeNode` — `List<BehaviorTreeNode> Children`, `int ParallelPolicy`.
  - `DecoratorNode : BehaviorTreeNode` — `BehaviorTreeNode? Child`, `float Duration`, `int RepeatCount`.
  - `LogicNode : BehaviorTreeNode` — `string DelegateName`, `string TargetDtoType`, `string TargetFieldName`.
- `BTreeBuilder<TBlackboard>.ToGraph(string treeName)` method that returns the `BehaviorTreeGraph` from the current builder state.

**Constraints:**
- Graph classes are mutable (for the authoring tool to modify them).
- They carry no runtime execution code — they are pure data structures.
- `VisualId` is a `Guid` that defaults to `Guid.NewGuid()` on construction.
- No dependency on `Fbt.Kernel` types other than `NodeType`.

**Success Conditions:**

SC1 — `ToGraph()` round-trips through `BTreeBuilder`:
```
// Setup: BTreeBuilder.Sequence(s => s.Action(A).Action(B)).ToGraph("MyTree")
// Assert: graph.RootNode is CompositeNode with Type == NodeType.Sequence
//         graph.RootNode.Children.Count == 2
//         graph.RootNode.Children[0] is LogicNode with DelegateName containing "A"
```

SC2 — All nodes have unique non-empty `VisualId`:
```
// Assert: All nodes in graph have non-default Guid (Guid.Empty)
//         All VisualIds are distinct
```

---

### TASK-FBT-006: Tests for Phase 1

**Design Reference:** DESIGN.md Phase 1

**Scope:**
- Unit tests in `Fbt.Tests` (existing test project) covering FBT-001 through FBT-005.
- Tests should be added to `tests/Fbt.Tests/Unit/` and `tests/Fbt.Tests/Integration/`.
- Must include a dedicated `BuilderValidationTests` class that tests the negative path — proving invalid tree constructs are rejected at compile time rather than crashing at runtime.

**Success Conditions:**
- All success conditions listed in FBT-001 through FBT-005 have corresponding passing test methods.
- No existing tests broken.
- `BuilderValidationTests.NestedRepeater_ThrowsBehaviorTreeBuildException` — asserts that `.Repeater(2, r => r.Repeater(3, ...))` throws.
- `BuilderValidationTests.NestedParallel_ThrowsBehaviorTreeBuildException` — asserts that `.Parallel(0, p => p.Parallel(0, ...))` throws.
- `BuilderValidationTests.DtoTooLarge_ThrowsBehaviorTreeBuildException` — asserts that mapping a DTO type whose `sizeof` exceeds 128 bytes throws at `Compile()` time.
- `BuilderValidationTests.ValidTree_DoesNotThrow` — control test: a correctly structured tree compiles without exception.

---

### TASK-FBT-007: `BTreeSchemaExporter`

**Design Reference:** DESIGN.md § 2.10, Phase 1

**Scope:**
- Add `BTreeSchemaExporter` static class to `Fbt.Compiler`.
- `static BTreeSchema Export(IEnumerable<Assembly> assemblies)` — scans the provided assemblies for methods annotated with `[BTreeAction]` or `[BTreeCondition]` and produces a `BTreeSchema` object.
- `BTreeSchema` is a serializable record containing:
  - `ActionDescriptor[] Actions` — each with `MethodName`, `DeclaringType`, `BlackboardDtoType` (resolved from the first generic `TValue` parameter of the delegate, or the full blackboard type), `FieldName`, `FieldOffset` (from `Marshal.OffsetOf`).
  - `ConditionDescriptor[] Conditions` — same shape as `ActionDescriptor`.
  - `string[] BlackboardDtoTypes` — distinct fully-qualified type names of all referenced DTOs.
- `BTreeSchemaExporter.ExportToJson(BTreeSchema schema, string outputPath)` — serialises the schema to a JSON file at `outputPath` using `System.Text.Json`.
- The schema exporter is a standalone tool utility — it does not run during normal engine startup. It can be invoked from a standalone CLI tool or from the authoring tool host.

**Constraints:**
- No dependency on HROT or any application layer — only `Fbt.Kernel`, `Fbt.Compiler`, and BCL.
- Field offset computation uses `Marshal.OffsetOf<TBlackboardDto>(fieldName)` — this is acceptable here because the schema exporter runs in a full .NET runtime, not inside Roslyn.
- `ExportToJson` must not throw on assemblies that contain no `[BTreeAction]` or `[BTreeCondition]` methods; it should emit an empty schema.

**Success Conditions:**

SC1 — Scanner finds all `[BTreeAction]` methods in a test assembly:
```
// Setup: Assembly with two [BTreeAction] methods and one [BTreeCondition]
// Assert: schema.Actions.Length == 2, schema.Conditions.Length == 1
```

SC2 — Field offset is correct for a projected delegate:
```
// Setup: [BTreeAction] targeting dto => dto.AmmoCount in a struct where AmmoCount is at offset 4
// Assert: schema.Actions[0].FieldOffset == 4
```

SC3 — `ExportToJson` produces valid JSON that round-trips through `System.Text.Json.JsonSerializer.Deserialize<BTreeSchema>`:
```
// Assert: Deserialised schema.Actions.Length == original schema.Actions.Length
```

SC4 — Empty assembly produces empty schema without throwing.

---

## Phase 2: Fbt.SourceGen — Roslyn Source Generator

---

### TASK-FBT-010: Define Marker Attributes

**Design Reference:** DESIGN.md § 2.3, Phase 2

**Scope:**
- Add the following attribute classes to `Fbt.Kernel` (or create a lightweight `Fbt.Attributes` project if attribute separation from kernel is preferred):
  - `[BTreeAction]` — marks a static method as an auto-registrable BTree action delegate.
  - `[BTreeCondition]` — marks a static method as an auto-registrable BTree condition delegate.
  - `[BTreeDefinition(string treeName)]` — marks a static method returning `BTreeBuilder<TBlackboard>` (or `BehaviorTreeBlob`) as a named tree to auto-catalog.
  - `[FbtRegistrar]` — applied by the generator to the emitted registrar class; used by `FbtAutoDiscovery` to find it via reflection.
- Attribute definitions must be available to both user code and the source generator.

**Constraints:**
- Attributes must have `AttributeUsage` set appropriately:
  - `[BTreeAction]`, `[BTreeCondition]`: `AttributeTargets.Method`.
  - `[BTreeDefinition]`: `AttributeTargets.Method`, with a required `string TreeName` constructor parameter.
  - `[FbtRegistrar]`: `AttributeTargets.Class`.

**Success Conditions:**

SC1 — Attributes compile without error when applied to static methods with the expected signatures.

SC2 — `[BTreeDefinition]` exposes the tree name via reflection:
```
// Assert: attr.TreeName == "Ambush_BT"
```

---

### TASK-FBT-011: Implement `BTreeActionGenerator`

**Design Reference:** DESIGN.md § 2.3, Phase 2

**Scope:**
- Create `FDP/ExtDeps/FastBTree/src/Fbt.SourceGen/Fbt.SourceGen.csproj` mirroring `Fhsm.SourceGen.csproj`.
- Implement `BTreeActionGenerator : IIncrementalGenerator` that:
  1. Finds all static methods in the user assembly annotated with `[BTreeAction]` or `[BTreeCondition]`.
  2. Emits `{AssemblyName}.Generated.FbtActionRegistrar` class tagged with `[FbtRegistrar]`.
  3. Emitted class has static `RegisterAll(ActionRegistry<TBlackboard, BTreeContext> registry)` method that registers each found method by name.
- For methods with typed `TValue` overloads (from FBT-003), the generator emits the offset-projection closure. For simple delegates matching `NodeLogicDelegate<TBlackboard, TContext>`, it registers them directly.
- The generator must use Roslyn's `ITypeSymbol` Semantic Model APIs to compute struct field byte offsets at compile time (by summing `FieldSymbol.GetOffset()` or equivalent layout calculation), then hardcode the resulting integer directly into the generated `Unsafe.AddByteOffset` call. `Marshal.OffsetOf` must NOT appear in the generated code.
- The generator must emit a Roslyn diagnostic error (`BTreeDiagnostics.BlackboardTooLarge`) if the size of a referenced blackboard DTO type (resolved via Roslyn's `ITypeSymbol`) exceeds 128 bytes, preventing the build from proceeding.

**Constraints:**
- Generator must use `IIncrementalGenerator` (Roslyn v4 API), not `ISourceGenerator` (like `Fhsm.SourceGen`).
- Must handle multiple assemblies cleanly (each emits its own registrar).
- Must not generate code for abstract classes or interfaces.
- The emitted code must not contain any `unsafe` block, `fixed` statement, or pointer type. The zero-pointer `ref T` projection must use only `System.Runtime.CompilerServices.Unsafe.As` and `System.Runtime.CompilerServices.Unsafe.AddByteOffset` with hardcoded integer offsets — this is the core memory safety guarantee of the entire system.

**Success Conditions:**

SC1 — Generator emits `FbtActionRegistrar.g.cs` containing a `RegisterAll` method:
```
// Setup: Assembly with one [BTreeAction] static method "MyAction"
// Assert: Generated file contains "RegisterAll" and "MyAction"
```

SC2 — Generated `RegisterAll` compiles without error.

SC3 — `RegisterAll` invocation correctly populates the ActionRegistry with all marked methods.

---

### TASK-FBT-012: Implement `BTreeDefinitionGenerator`

**Design Reference:** DESIGN.md § 2.3, Phase 2

**Scope:**
- Extend `Fbt.SourceGen` to find methods annotated with `[BTreeDefinition("TreeName")]`.
- Emit `FbtTreeCatalog.g.cs` with static `Get{TreeName}()` methods returning `BehaviorTreeBlob`.
- The generator must statically evaluate the `BTreeBuilder` expression at compile time using Roslyn's Semantic Model to read the builder call chain, then emit the fully flattened `BehaviorTreeBlob` directly as static array initialization data (e.g., `private static readonly NodeDefinition[] _ambush_nodes = new NodeDefinition[] { ... };`). This eliminates startup JSON parsing and all runtime tree-building cost entirely.
- Calling the annotated method at startup is explicitly forbidden — it defeats the zero-startup-cost goal of the source generator and must not be used as a fallback.

**Constraints:**
- The catalog method must return `BehaviorTreeBlob`, not `BTreeBuilder<T>`.
- The `BehaviorTreeBlob` returned by the generated `Get{TreeName}()` must be structurally identical to what `BTreeBuilder.Compile(treeName)` would produce at runtime. Verified by comparing `StructureHash` and `ParamHash` in tests.

**Success Conditions:**

SC1 — Generator emits `FbtTreeCatalog.g.cs` with a method named after the tree:
```
// Setup: [BTreeDefinition("Ambush_BT")] static method
// Assert: Generated file contains "GetAmbush_BT()"
```

SC2 — The generated `Get{TreeName}()` method does NOT call the annotated builder method at runtime — it returns statically initialised data:
```
// Setup: Inspect generated FbtTreeCatalog.g.cs source text
// Assert: The method body contains no call to BuildAmbushTree() or any reflection
//         The body contains only static field reads and constructor calls
```

SC3 — The blob from the generated catalog has the same `StructureHash` as one produced by `BTreeBuilder.Compile("Ambush_BT")` at runtime:
```
// Assert: FbtTreeCatalog.GetAmbush_BT().StructureHash == BuildAmbushTree().Compile("Ambush_BT").StructureHash
```

SC4 — Source generator emits Roslyn diagnostic error `BTreeDiagnostics.BlackboardTooLarge` when a DTO exceeds 128 bytes:
```
// Setup: [BTreeAction] method projecting onto a DTO struct whose sizeof > 128
// Assert: Build fails with diagnostic error containing "BlackboardTooLarge"
//         No FbtActionRegistrar.g.cs emitted for that method
```

---

### TASK-FBT-013: Implement `FbtAutoDiscovery`

**Design Reference:** DESIGN.md § 2.4, Phase 2

**Scope:**
- Add `FbtAutoDiscovery` static class to `Fbt.Compiler`.
- Method `ScanAndRegister(ActionRegistry<BrainBlackboard, BTreeContext> actionReg, DoctrineRegistry doctrineReg)`.
  - Wait: `DoctrineRegistry` is in `Fdp.Toolkits`. If `Fbt.Compiler` should not depend on FDP, expose a more generic overload and let the HROT/FDP integration layer call it. Use an overload accepting only `ActionRegistry` for the base case, with a HROT-specific extension in `Hrot.CGF` that also registers into `DoctrineRegistry`.
- Scans `AppDomain.CurrentDomain.GetAssemblies()` for types annotated with `[FbtRegistrar]`.
- Invokes `RegisterAll` on each found type via reflection.

**Constraints:**
- Must wrap assembly scans in `try/catch` to skip non-reflectable assemblies (COM, dynamic, etc.) — matching the pattern in `ImGuiRendererRegistry.ScanAllAssemblies`.
- `FbtAutoDiscovery` must not hard-code any assembly names.

**Success Conditions:**

SC1 — Scanner finds and invokes a `[FbtRegistrar]` class in a separately loaded test assembly:
```
// Setup: Dynamically load an assembly containing a [FbtRegistrar]-annotated class with
//        a known RegisterAll that writes a flag
// Action: Call FbtAutoDiscovery.ScanAndRegister
// Assert: The flag was set (RegisterAll was called)
```

SC2 — Scanner does not throw when a non-reflectable assembly is present.

---

### TASK-FBT-014: Tests for Phase 2

**Design Reference:** DESIGN.md Phase 2

**Scope:**
- Source generator tests using `Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing` or manual compilation.
- Auto-discovery integration tests.

**Success Conditions:**
- All success conditions for FBT-010 through FBT-013 have passing test methods.

---

## Phase 3: BTreeHotReloadManager

---

### TASK-FBT-020: Implement `BTreeHotReloadManager`

**Design Reference:** DESIGN.md § 2.5, Phase 3

**Scope:**
- Create `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/HotReload/BTreeHotReloadManager.cs`.
- `BTreeHotReloadManager` class (non-generic, manages string-keyed blobs and a reference to the `DoctrineRegistry` for patching).
- `ReloadResult` enum in the same namespace: `NewTree, NoChange, SoftReload, HardReset`.
- Public method:
  ```
  public ReloadResult TryReload(
      string treeName,
      BehaviorTreeBlob newBlob,
      Span<BrainBTreeState> liveInstances)
  ```
- Internally:
  1. Compare `StructureHash` and `ParamHash` between old and new blobs.
  2. On any result other than `NoChange`, patch the active `DoctrineDefinition` inside the `DoctrineRegistry` by replacing its `BTreeInterpreter` with a new `Interpreter` constructed from `newBlob` and the existing `ActionRegistry`. This ensures live entities execute the new logic on the very next tick.
  3. On `HardReset`, additionally call `instance.State.Reset()` for every entry in `liveInstances`.
  4. On `SoftReload`, do not mutate instance states — the interpreter picks up the new float/int params via the updated blob automatically.

**Constraints:**
- `BrainBTreeState` is in `Fdp.Toolkits`. If keeping `Fbt.Kernel` free of FDP dependencies, use a generic overload:
  ```
  public ReloadResult TryReload<TState>(
      string treeName,
      BehaviorTreeBlob newBlob,
      Span<TState> liveInstances,
      Action<TState> hardResetAction)
      where TState : unmanaged
  ```
  And provide an extension method in `Fdp.Toolkits` that calls it with `state => state.State.Reset()`. Use this approach.
- The `DoctrineRegistry` reference must be injected at construction time: `BTreeHotReloadManager(DoctrineRegistry registry)`. The registry is in `Fdp.Toolkits`; the DI is handled by the host, not the manager itself.
- Must never throw; guard against null `newBlob`.
- Registry patching must happen before the method returns, so the calling site can be certain the new blob is active.

**Success Conditions:**

SC1 — `TryReload` returns `NewTree` on first call for a tree name:
```
// Assert: result == ReloadResult.NewTree
//         liveInstances not mutated
```

SC2 — `TryReload` returns `NoChange` when hashes are identical:
```
// Setup: Register a blob; call TryReload with identical blob (same hashes)
// Assert: result == ReloadResult.NoChange
```

SC3 — `TryReload` returns `SoftReload` when only `ParamHash` differs:
```
// Setup: Two blobs with same StructureHash but different ParamHash (different float params)
// Assert: result == ReloadResult.SoftReload
//         liveInstances[0].State.RunningNodeIndex unchanged
```

SC4 — `TryReload` returns `HardReset` and calls reset on all instances when structure changed:
```
// Setup: Two blobs with different StructureHash
//        liveInstances[0].State.RunningNodeIndex = 5 (non-zero running state)
// Assert: result == ReloadResult.HardReset
//         liveInstances[0].State.RunningNodeIndex == 0 after call
//         liveInstances[0].State.TreeVersion incremented by 1
```

SC5 — Empty `liveInstances` span: HardReset returns without error:
```
// Assert: TryReload with Span<BrainBTreeState>.Empty returns HardReset, no exception
```

SC6 — `DoctrineRegistry` is updated before `TryReload` returns:
```
// Setup: Register a blob B1 in DoctrineRegistry; call TryReload with B2 (different StructureHash)
// Assert: After TryReload returns, DoctrineRegistry.TryGetDefinition(treeName).BTreeInterpreter.Blob
//         == B2 (the new blob is active)
//         This is verified before any entity tick occurs
```

---

### TASK-FBT-021: Implement Hot Reload Check in `Interpreter.Tick`

**Design Reference:** DESIGN.md § 2.5, Phase 3

**Scope:**
- Modify `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs`.
- Replace the stub comment `// === HOT RELOAD CHECK (Stub for now) ===` with a real check:
  - Store the blob's `StructureHash` at construction time in a private field `_expectedStructureHash`.
  - At the start of each `Tick`, if `state.TreeVersion != 0` and the blob's `StructureHash != _expectedStructureHash`, call `state.Reset()` (structure mismatch). This ensures that if the `Interpreter` is recreated with a new blob (via `BTreeHotReloadManager`) the entity state is reset safely.
  - This is a lightweight guard; the full hot-reload orchestration lives in `BTreeHotReloadManager`.

**Constraints:**
- The check must not allocate.
- The check must not break any existing tests.

**Success Conditions:**

SC1 — A new `Interpreter` constructed with a different blob (different `StructureHash`) safely resets an entity's state on first tick:
```
// Setup: Tick interpreter A, entity enters Running state
//        Create interpreter B with different StructureHash, same registry
// Action: Tick interpreter B with same entity state
// Assert: State is reset (RunningNodeIndex == 0 at start of execution)
//         Tick completes without exception
```

---

### TASK-FBT-022: Tests for Phase 3

**Design Reference:** DESIGN.md Phase 3

**Success Conditions:**
- All success conditions for FBT-020, FBT-021, and FBT-023 have passing test methods.
- All existing `InterpreterTests` continue to pass.
- `AlcHotReloaderTests.OldAlc_IsUnloaded_AfterReload` — proves the old `WeakReference<AssemblyLoadContext>` is collected by the GC after a reload cycle (verifying no memory leak from the old ALC).

---

### TASK-FBT-023: Implement `FbtAssemblyHotReloader`

**Design Reference:** DESIGN.md § 2.5, Phase 3

**Scope:**
- Create `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/HotReload/FbtAssemblyHotReloader.cs`.
- `FbtAssemblyHotReloader` — a class that orchestrates the full ALC-based reload cycle:
  1. Takes a watch directory path at construction time and starts a `FileSystemWatcher` monitoring `*.dll` changes.
  2. On detection of a new DLL: load it into a new collectible `AssemblyLoadContext` (`new AssemblyLoadContext(name, isCollectible: true)`).
  3. Via reflection, find the `[FbtRegistrar]`-annotated class in the new assembly and call its `RegisterAll(actionReg, doctrineReg)` to overwrite all action/condition delegate pointers.
  4. Extract new `BehaviorTreeBlob` instances from the `FbtTreeCatalog` class emitted into the new assembly.
  5. For each discovered blob, call `BTreeHotReloadManager.TryReload(treeName, newBlob, liveInstances)` to patch the `DoctrineRegistry` and optionally reset entity states.
  6. Unload the old `AssemblyLoadContext` by calling `oldAlc.Unload()`. Store a `WeakReference<AssemblyLoadContext>` to the old ALC so callers can verify it has been GC'd.
- `FbtAssemblyHotReloader` must provide:
  - `event Action<string>? OnReloadCompleted` — fired with tree name after a successful reload.
  - `event Action<string, Exception>? OnReloadFailed` — fired with path and exception on failure.
  - `void Dispose()` — stops the `FileSystemWatcher` and unloads the current ALC.

**Constraints:**
- `FileSystemWatcher` must use a debounce delay (e.g., 200 ms) to avoid double-firing during multi-file writes.
- All ALC operations must occur on a dedicated thread (not the game tick thread); the `RegisterAll` and `TryReload` calls must be deferred to the next safe engine update boundary via a thread-safe queue.
- Must not hard-code any assembly names — discovery is driven entirely by `[FbtRegistrar]` attribute scanning.
- Core types (`BrainBlackboard`, `BehaviorTreeState`) must be resolved from the Default ALC so they remain type-identical across hot-reloaded assemblies.

**Success Conditions:**

SC1 — `FbtAssemblyHotReloader` fires `OnReloadCompleted` after detecting and loading a new DLL:
```
// Setup: Create a temp directory; point watcher at it; copy a valid DLL into the directory
// Assert: OnReloadCompleted fires within 1 second; event arg contains tree name
```

SC2 — Action delegate pointers in `ActionRegistry` are updated after reload:
```
// Setup: Old DLL registers a delegate that returns Failure
//        New DLL registers a same-named delegate that returns Success
// Action: Trigger reload; tick interpreter after reload
// Assert: Tick returns Success (new delegate is active)
```

SC3 — Old ALC is unloaded and GC'd after reload:
```
// Setup: Load old ALC; trigger reload; call GC.Collect()
// Assert: weakRef.TryGetTarget(out _) == false (old ALC was collected)
```

SC4 — `OnReloadFailed` fires when the DLL contains no `[FbtRegistrar]` class:
```
// Setup: Copy a DLL without any [FbtRegistrar] class into the watch directory
// Assert: OnReloadFailed fires; no crash; old delegates remain active
```

---

## Phase 4: FDP Engine — Extended ImGui Rendering

---

### TASK-FBT-030: Define `IEntityAwareImGuiRenderer`

**Design Reference:** DESIGN.md § 2.7, Phase 4

**Scope:**
- Add `IEntityAwareImGuiRenderer` interface to `FDP/Engine/Fdp.Presentation/ImGui/Renderers/IImGuiRenderer.cs` (same file, or a new file in the same folder):
  ```
  public interface IEntityAwareImGuiRenderer : IImGuiRenderer
  {
      bool RenderValue(IInspectableSession session, Entity entity, object value);
  }
  ```
- `IInspectableSession` is already in `Fdp.Presentation.Abstractions`; `Entity` is in `Fdp.Core`.

**Constraints:**
- Must not break any existing code implementing `IImGuiRenderer`.
- The default `RenderValue(object value)` from the base interface still serves as a fallback (returns `false`).

**Success Conditions:**

SC1 — A class implementing `IEntityAwareImGuiRenderer` also satisfies `IImGuiRenderer` (Liskov check):
```
// Assert: typeof(IEntityAwareImGuiRenderer).IsAssignableTo(typeof(IImGuiRenderer))
```

---

### TASK-FBT-031: Update `ComponentReflector` Dispatch

**Design Reference:** DESIGN.md § 2.7, Phase 4

**Scope:**
- Modify `FDP/Engine/Fdp.Presentation/ImGui/Utils/ComponentReflector.cs`.
- In the `DrawComponents` loop where `renderer.RenderValue(data)` is currently called, add a check:
  ```
  bool handled = false;
  if (renderer is IEntityAwareImGuiRenderer entityRenderer)
      handled = entityRenderer.RenderValue(session, e, data);
  else if (renderer != null)
      handled = renderer.RenderValue(data);
  if (!handled)
      ImGuiPropertyTree.Render(data, contextType: type, out doubleClickedPath);
  ```
- `session` and `e` are already available in `DrawComponents(IInspectableSession session, Entity e, ...)`.

**Constraints:**
- The existing `ImGuiRendererRegistry.GetRenderer(type)` lookup is unchanged.
- No performance regression for components that do not use the extended interface.

**Success Conditions:**

SC1 — When a renderer implements `IEntityAwareImGuiRenderer`, it receives session and entity:
```
// Setup: Register a mock IEntityAwareImGuiRenderer that sets a flag with the passed entity
// Action: DrawComponents with a matching component type
// Assert: Flag set; correct entity passed
```

SC2 — When a renderer implements only `IImGuiRenderer`, it receives just the value (unchanged behavior):
```
// Assert: The simple RenderValue(object) overload is called, not the extended one
```

---

### TASK-FBT-032: Add `ParamsDtoType` to `DoctrineDefinition`

**Design Reference:** DESIGN.md § 2.8, Phase 4

**Scope:**
- Modify `FDP/Toolkits/Fdp.Toolkits/Behavior/DoctrineRegistry.cs`.
- Add `Type? ParamsDtoType { get; init; }` to `DoctrineDefinition`.
- Update `CgfDoctrineSetup.RegisterAll` to populate `ParamsDtoType` for all existing BTree doctrines that have a params DTO (e.g., `MoveToLocationParams`, `FollowRouteParams`, `FireAtTargetParams`).

**Constraints:**
- `ParamsDtoType` must be `unmanaged` at runtime (enforced by a `Debug.Assert` or comment, not a type constraint at compile time since `DoctrineDefinition` is non-generic).
- No breaking change: `ParamsDtoType = null` remains valid and means "no typed DTO".

**Success Conditions:**

SC1 — `DoctrineDefinition` accepts a `ParamsDtoType`:
```
// Assert: new DoctrineDefinition { Name = "MoveTo", ..., ParamsDtoType = typeof(MoveToLocationParams) }
//         compiles and stores the type correctly.
```

SC2 — `DoctrineRegistry.TryGetDefinition` returns the definition with `ParamsDtoType` set:
```
// Assert: def.ParamsDtoType == typeof(MoveToLocationParams)
```

---

### TASK-FBT-033: Implement `BrainBlackboardRenderer`

**Design Reference:** DESIGN.md § 2.8, Phase 4

**Scope:**
- Create `Hrot/Engine/Hrot.Presentation/Behavior/BrainBlackboardRenderer.cs`.
- Implements `IEntityAwareImGuiRenderer` for `BrainBlackboard`.
- Annotated with `[ImGuiRenderer(typeof(BrainBlackboard))]` for auto-discovery.
- In `RenderValue(IInspectableSession session, Entity entity, object value)`:
  1. Read `DoctrineState` component from session for the entity.
  2. Look up the `DoctrineDefinition` from `DoctrineRegistry` via `ActiveDoctrineHash`.
  3. If `ParamsDtoType != null`, use `Marshal.PtrToStructure` (or `Unsafe.As`) to interpret `BrainBlackboard.Memory` as the DTO type.
  4. Pass the boxed DTO to `ImGuiPropertyTree.Render` to display all fields.
  5. Fallback: if no DTO type, display raw hex bytes (16 bytes per row).
- The renderer needs access to `DoctrineRegistry`. Since it is registered as a singleton, the registry can be injected via a static property set during startup or passed through `IInspectableSession`.

**Constraints:**
- `Marshal.PtrToStructure` is only called once per frame per entity, on the display path (not the hot tick path) — acceptable performance.
- `DoctrineRegistry` must be accessible from the renderer. Recommended approach: add a static settable property `DoctrineRegistry? DoctrineRegistryAccessor` to the renderer class, set at startup in `CgfSubsystem` initialization.
- The renderer must check that `session.HasComponent<DoctrineState>(entity)` before reading it.

**Success Conditions:**

SC1 — Renderer displays DTO field names when `ParamsDtoType` is set:
```
// Setup: Mock IInspectableSession returning a DoctrineState with known ActiveDoctrineHash
//        DoctrineRegistry entry with ParamsDtoType = typeof(MoveToLocationParams)
//        BrainBlackboard.Memory with MoveToLocationParams written at offset 0
// Assert: RenderValue returns true (handled)
//         ImGuiPropertyTree.Render is called with a boxed MoveToLocationParams
```

SC2 — Renderer falls back gracefully when no `ParamsDtoType`:
```
// Assert: RenderValue returns true (handled); raw bytes shown
```

SC3 — Renderer does not throw when entity has no `DoctrineState`:
```
// Assert: RenderValue returns false (fallback to default rendering)
```

---

### TASK-FBT-034: Implement `BTreeVisualizerRenderer`

**Design Reference:** DESIGN.md § 2.9, Phase 4

**Scope:**
- Create `Hrot/Engine/Hrot.Presentation/Behavior/BTreeVisualizerRenderer.cs`.
- Implements `IEntityAwareImGuiRenderer` for `BrainBTreeState`.
- Annotated with `[ImGuiRenderer(typeof(BrainBTreeState))]`.
- In `RenderValue(IInspectableSession session, Entity entity, object value)`:
  1. Read `DoctrineState` from session to get `ActiveDoctrineHash`.
  2. Retrieve `BehaviorTreeBlob` from `DoctrineDefinition.BTreeInterpreter._blob` (via a getter exposed on `DoctrineDefinition`, or via a separate blob registry).
  3. Call recursive `DrawNode(blob, ref state, index: 0)` to render the tree.
- `DrawNode` logic:
  - Determine if node is on the active execution path: `state.RunningNodeIndex == index` (green) or `IsAncestralPath(ref state, index)` (yellow) or inactive (white).
  - Render `ImGui.TreeNodeEx` with node type and debug label.
  - On hover: `ImGui.SetTooltip` showing `DebugMetadata.SourceFile`, `LineNumber`, `CustomComment`, and `VisualId` (prefixed with `"VisualId: "` so the authoring tool link is human-readable).
  - For running `Wait` node: decode `AsyncData` and show elapsed/remaining time.
  - For running `Repeater`: show `LocalRegisters[0]` / target count.
  - For running `Parallel`: show bitmask from `LocalRegisters[3]`.
  - Recursively draw children using `SubtreeOffset`.

**Constraints:**
- Access to the internal `_blob` field of `Interpreter<BrainBlackboard, BTreeContext>` requires either:
  - Exposing a `BehaviorTreeBlob Blob { get; }` property on `Interpreter<TBlackboard, TContext>` (preferred), or
  - Storing the blob separately in `DoctrineDefinition`.
  - Preferred: add `public BehaviorTreeBlob Blob => _blob;` to `Interpreter`.
- Must not allocate during rendering (no LINQ, no boxing of struct fields if avoidable).
- `DebugMetadata` may be null; renderer must handle this gracefully (show only node type).

**Success Conditions:**

SC1 — Renderer returns `true` for a known `BrainBTreeState` with a matching doctrine:
```
// Setup: Mock session, entity with DoctrineState, registry with a BTree doctrine
// Assert: RenderValue returns true
```

SC2 — Active node highlighted: `DrawNode` selects green color for the node at `RunningNodeIndex`:
```
// Setup: Create a blob with 3 nodes; set state.RunningNodeIndex = 2
// Assert: DrawNode uses a different color style for node at index 2 vs others
//         (verifiable via a test-double ImGui API or by inspecting color state logic)
```

SC3 — Renderer does not throw when `DebugMetadata` is null:
```
// Assert: No exception thrown; node label falls back to NodeType.ToString()
```

SC4 — `VisualId` from `NodeDebugMetadata` is visible in the tooltip for the running node:
```
// Setup: Compile blob with explicit VisualId="test-uuid-123" on a leaf node
//        Set state.RunningNodeIndex to that leaf
// Assert: DrawNode tooltip text for that node contains "test-uuid-123"
```

---

### TASK-FBT-035 through FBT-037: Tests for Phase 4

**Design Reference:** DESIGN.md Phase 4

**Scope:**
- Tests for `ComponentReflector` extended dispatch (FBT-035).
- Tests for `BrainBlackboardRenderer` (FBT-036).
- Tests for `BTreeVisualizerRenderer` (FBT-037).

**Success Conditions:** All success conditions for FBT-030 through FBT-034 have passing tests.

---

## Phase 5: Sample Project

---

### TASK-FBT-040: Create `CombatBlackboard` DTO

**Design Reference:** DESIGN.md § 2.11, Phase 5

**Scope:**
- Create `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/CombatBlackboard.cs`.
- `CombatBlackboard` unmanaged struct:
  ```
  [StructLayout(LayoutKind.Sequential)]
  public struct CombatBlackboard
  {
      public int AmmoCount;
      public bool ThreatVisible;
      // Padding: 3 bytes (to align EngagementRange)
      public byte _pad0, _pad1, _pad2;
      public float EngagementRange;
  }
  ```
- Also define `CombatContext : IAIContext` — a minimal, self-contained mock context for the sample.

**Constraints:**
- `CombatBlackboard` must be `unmanaged` and `StructLayout(LayoutKind.Sequential)` — required for `Marshal.OffsetOf` to work correctly.
- No FDP/HROT dependencies in this project.

**Success Conditions:**

SC1 — `Marshal.OffsetOf<CombatBlackboard>("AmmoCount")` == 0.
SC2 — `Marshal.OffsetOf<CombatBlackboard>("EngagementRange")` == `sizeof(int) + 1 + 3` == 8.

---

### TASK-FBT-041: Implement Sample Action and Condition Delegates

**Design Reference:** DESIGN.md § 2.11, Phase 5

**Scope:**
- Create `CombatActions.cs` with:
  - `[BTreeCondition] CheckAmmo(ref int ammo, ref BehaviorTreeState, ref CombatContext)` — returns `Success` if `ammo > 0`.
  - `[BTreeCondition] HasThreat(ref bool threatVisible, ref BehaviorTreeState, ref CombatContext)` — returns `Success` if `true`.
  - `[BTreeAction] AimAndFire(ref int ammo, ref BehaviorTreeState, ref CombatContext)` — decrements `ammo`, prints to console, returns `Success`.
  - `[BTreeAction] HoldPosition(ref CombatBlackboard, ref BehaviorTreeState, ref CombatContext)` — non-projected, acts on full blackboard, prints "holding position", returns `Running` for 2 ticks then `Success`.

**Constraints:**
- Delegates using the `TValue` projection must use the `ReusableConditionDelegate<TValue>` / `ReusableActionDelegate<TValue>` signature from FBT-003.
- `HoldPosition` may use the standard `NodeLogicDelegate<CombatBlackboard, CombatContext>` signature.

**Success Conditions:**

SC1 — `CheckAmmo` with `ammo = 0` returns `Failure`.
SC2 — `AimAndFire` decrements `ammo` by 1 on each call.
SC3 — `HoldPosition` returns `Running` on first tick, `Success` on second.

---

### TASK-FBT-042: Implement `[BTreeDefinition]` Builder Method

**Design Reference:** DESIGN.md § 2.11, Phase 5

**Scope:**
- Create `AmbushTree.cs` with:
  ```
  [BTreeDefinition("Ambush_BT")]
  public static BTreeBuilder<CombatBlackboard> BuildAmbushTree()
  {
      return new BTreeBuilder<CombatBlackboard>()
          .Selector(s => s
              .Sequence(seq => seq
                  .Condition(dto => dto.ThreatVisible, HasThreat)
                  .Condition(dto => dto.AmmoCount, CheckAmmo)
                  .Action(dto => dto.AmmoCount, AimAndFire)
              )
              .Action(HoldPosition)
          );
  }
  ```

**Success Conditions:**

SC1 — `BuildAmbushTree().Compile("Ambush_BT")` produces a blob with `blob.TreeName == "Ambush_BT"`.
SC2 — Blob structure: Selector(0) → Sequence(1) → Condition(2) → Condition(3) → Action(4) → Action(5).

---

### TASK-FBT-043: Wire Auto-Discovery and Build Visual Application

**Design Reference:** DESIGN.md § 2.11, Phase 5

**Scope:**
- The sample project `Fbt.Examples.FluentBTree` is a **visual application** using `Fdp.Presentation` (Raylib window + ImGui overlay), not a headless console app.
- `Program.Main` must:
  1. Open a Raylib window and initialise the ImGui overlay.
  2. Call `FbtAutoDiscovery.ScanAndRegister(actionReg, ...)` to bind all actions from the generated registrar.
  3. Get the blob from the generated catalog `FbtTreeCatalog.GetAmbush_BT()`.
  4. Create `Interpreter<CombatBlackboard, CombatContext>(blob, actionReg)`.
  5. Run the game loop: tick the interpreter once per frame; render the `BTreeVisualizerRenderer` in an ImGui window showing the live color-coded tree state.
  6. Display the `CombatBlackboard` typed fields via `BrainBlackboardRenderer` in a separate ImGui window.
- The sample application must provide a controllable `CombatBlackboard`: ImGui sliders/checkboxes for `AmmoCount` and `ThreatVisible` so the user can manually drive the entity through different doctrine branches.

**Constraints:**
- The sample must compile with `Fdp.Presentation` referenced. If Raylib is not available in the build environment, the project may gracefully fall back to a console-only mode controlled by a compile-time constant — but the visual path is the primary deliverable.
- No FDP/HROT application-layer dependencies beyond `Fdp.Presentation` and `Fdp.Toolkits`.

**Success Conditions:**

SC1 — Application opens a window and renders at least one frame without exception.
SC2 — `BTreeVisualizerRenderer` ImGui window shows green highlight on the currently executing node.
SC3 — Changing `AmmoCount` to 0 via the ImGui slider causes the visualizer to switch the green-highlighted node to `HoldPosition` on the next tick.

---

### TASK-FBT-044: Tests for Sample Project

**Design Reference:** DESIGN.md Phase 5

**Scope:**
- Create `Fbt.Examples.FluentBTree.Tests` project (or add tests to `Fbt.Tests`).
- Tests for FBT-040 through FBT-043 success conditions (logic/execution tests only — rendering tests in FBT-043 SC2/SC3 are manual visual verification).

**Success Conditions:**
- `CheckAmmo(ammo=0)` → `Failure` test passes.
- `AimAndFire` decrement test passes.
- Selector fallback to `HoldPosition` when ammo exhausted: 10-tick headless simulation produces expected result sequence.

---

### TASK-FBT-045: Sample Project Hot Reload Integration

**Design Reference:** DESIGN.md § 2.5, Phase 5

**Scope:**
- Add a "Recompile & Reload" button to the ImGui overlay in `Fbt.Examples.FluentBTree`.
- When clicked:
  1. Programmatically invoke `dotnet build` on `Fbt.Examples.FluentBTree.csproj` via `System.Diagnostics.Process`, redirecting stdout/stderr to an ImGui log window.
  2. On successful build, the `FbtAssemblyHotReloader` (FBT-023) automatically detects the new DLL in the build output directory and performs the ALC reload.
  3. The `BTreeVisualizerRenderer` immediately reflects the updated tree structure and color-coding in the next rendered frame.
- To prove the hot reload is live (not a restart), the CombatBlackboard state (e.g., current `AmmoCount`) must be preserved across the reload for a `SoftReload`, and reset to default for a `HardReset`.
- Add an ImGui status label showing the last reload result (`NewTree`, `SoftReload`, `HardReset`, or `NoChange`).

**Constraints:**
- The recompile button is for the sample/demo scenario only — it does not belong in production engine code.
- The ALC loading and `BTreeHotReloadManager.TryReload` must be driven by `FbtAssemblyHotReloader` — no duplicated reload logic.

**Success Conditions:**

SC1 — Clicking "Recompile & Reload" triggers a build; the ImGui log shows build output.
SC2 — After a reload where only `Wait` duration was changed (SoftReload), `AmmoCount` is unchanged in the blackboard display.
SC3 — After a reload where a new Sequence node was added (HardReset), the visualizer shows the new tree structure and `AmmoCount` is reset to its initial value.
SC4 — The ImGui status label shows the correct `ReloadResult` enum value after each reload.
