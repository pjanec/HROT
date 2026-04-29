# Onboarding Guide — Fluent BTree Workstream

**For:** A developer picking up this workstream from scratch.  
**Goal:** Build a type-safe C# fluent behavior tree authoring system on top of FastBTree,
with a Roslyn source generator, hot reload, and rich debug visualization.

---

## 1. What This Workstream Does

FastBTree doctrines are currently defined as raw JSON strings embedded in C# source files
(see `Hrot/Subsystems/Hrot.CGF/CgfNodes.cs`). This workstream replaces that with:

1. **`Fbt.Compiler`** — A fluent C# builder API (`BTreeBuilder<TBlackboard>`) that constructs
   behavior trees with full type safety, expression-based blackboard field binding, and
   debug metadata capture.

2. **`Fbt.SourceGen`** — A Roslyn incremental source generator that reads attributes on
   action/condition delegates and tree builder methods, emitting zero-boilerplate registration
   and catalog code.

3. **Hot reload** — `BTreeHotReloadManager` in `Fbt.Kernel` lets you reload a changed tree
   blob into live entity states without restarting the simulation.

4. **Debug visualization** — `BrainBlackboardRenderer` and `BTreeVisualizerRenderer` implement
   `IEntityAwareImGuiRenderer` so the Entity Inspector shows a typed blackboard view and a
   color-coded live BTree state tree.

5. **Sample project** — `Fbt.Examples.FluentBTree` is a self-contained console demo showing
   the full workflow.

---

## 2. Repository Layout

```
FDP/ExtDeps/FastBTree/
  src/
    Fbt.Kernel/                    Existing: core runtime, interpreter, blob
      BehaviorTreeBlob.cs          [MODIFY] Add NodeDebugMetadata[]
      Runtime/Interpreter.cs       [MODIFY] Implement hot reload check
      Serialization/TreeCompiler.cs [MODIFY] Add FlattenToBlob overload
      HotReload/                   [NEW] BTreeHotReloadManager.cs
    Fbt.Compiler/                  [NEW] Fluent builder, graph structures
    Fbt.SourceGen/                 [NEW] Roslyn incremental generator

  examples/
    Fbt.Examples.FluentBTree/      [NEW] Sample project
    Fbt.Examples.Console/          Existing
    Fbt.Demo.Visual/               Existing

  tests/
    Fbt.Tests/                     Existing (add tests here)

FDP/Engine/Fdp.Presentation/
  ImGui/Renderers/IImGuiRenderer.cs  [MODIFY] Add IEntityAwareImGuiRenderer
  ImGui/Utils/ComponentReflector.cs  [MODIFY] Dispatch extended renderer

FDP/Toolkits/Fdp.Toolkits/
  Behavior/DoctrineRegistry.cs       [MODIFY] Add ParamsDtoType to DoctrineDefinition

Hrot/Engine/Hrot.Presentation/
  Behavior/BrainBlackboardRenderer.cs  [NEW]
  Behavior/BTreeVisualizerRenderer.cs  [NEW]
```

---

## 3. Key Types to Know

| Type | Location | Role |
|------|----------|------|
| `BehaviorTreeBlob` | `Fbt.Kernel` | Immutable compiled tree; shared across all entities |
| `BehaviorTreeState` | `Fbt.Kernel` | 64-byte per-entity runtime state |
| `BrainBTreeState` | `Fdp.Toolkits` | Wraps `BehaviorTreeState`; lives in ECS |
| `BrainBlackboard` | `Fdp.Toolkits` | `fixed byte Memory[128]`; doctrine params |
| `DoctrineState` | `Fdp.Toolkits` | `ActiveDoctrineHash`, `InstanceId`, `BrainTier` |
| `DoctrineDefinition` | `Fdp.Toolkits` | Named doctrine with BTree and HSM interpreters |
| `DoctrineRegistry` | `Fdp.Toolkits` | Startup registry mapping int hashes to definitions |
| `TreeCompiler` | `Fbt.Kernel` | Compiles JSON or BuilderNode DOM to blob |
| `BuilderNode` | `Fbt.Kernel` | Intermediate DOM node |
| `Interpreter<TBB, TCtx>` | `Fbt.Kernel` | Executes a blob against entity state |
| `BTreeContext` | `Fdp.Toolkits` | FDP-specific `IAIContext` (Entity Self, EntityRepository World) |
| `IImGuiRenderer` | `Fdp.Presentation` | Interface for custom Entity Inspector type rendering |
| `ImGuiRendererRegistry` | `Fdp.Presentation` | Scans assemblies for `[ImGuiRenderer]`-tagged renderers |
| `ComponentReflector` | `Fdp.Presentation` | Draws ECS components in the Entity Inspector |
| `HsmBuilder` | `Fhsm.Compiler` | Reference: the HSM analogue of BTreeBuilder |
| `HsmActionGenerator` | `Fhsm.SourceGen` | Reference: the HSM analogue of BTreeActionGenerator |
| `HotReloadManager` | `Fhsm.Kernel` | Reference: the HSM hot reload implementation |

---

## 4. Mirror Pattern: FastHSM → FastBTree

This workstream mirrors how FastHSM is structured. When in doubt, look at the HSM
implementation first.

| FastHSM | FastBTree (new) |
|---------|----------------|
| `Fhsm.Compiler/HsmBuilder` | `Fbt.Compiler/BTreeBuilder<TBlackboard>` |
| `Fhsm.Compiler/StateMachineGraph` | `Fbt.Compiler.Graph/BehaviorTreeGraph` |
| `Fhsm.Compiler/StateNode` | `Fbt.Compiler.Graph/BehaviorTreeNode` |
| `Fhsm.SourceGen/HsmActionGenerator` | `Fbt.SourceGen/BTreeActionGenerator` |
| `[HsmAction]` / `[HsmGuard]` | `[BTreeAction]` / `[BTreeCondition]` |
| `Fhsm.Kernel/HotReloadManager` | `Fbt.Kernel/HotReload/BTreeHotReloadManager` |
| `ReloadResult` enum | Same `ReloadResult` enum (or share it) |

---

## 5. Build Commands

All commands run from the repository root (`d:\Work\IOS-IG-SimHost-FDP-2`).

**Build entire solution:**
```
dotnet build IOS-IG-SimHost.sln
```

**Build just FastBTree:**
```
dotnet build FDP\ExtDeps\FastBTree\FastBTree.sln
```

**Run FastBTree tests:**
```
dotnet test FDP\ExtDeps\FastBTree\FastBTree.sln
```

**Run just `Fbt.Tests`:**
```
dotnet test FDP\ExtDeps\FastBTree\tests\Fbt.Tests\Fbt.Tests.csproj
```

**Run the sample project:**
```
dotnet run --project FDP\ExtDeps\FastBTree\examples\Fbt.Examples.FluentBTree\Fbt.Examples.FluentBTree.csproj
```

---

## 6. Where to Start

**Day 1:** Start with FBT-001 (add `FlattenToBlob` to `TreeCompiler`) and FBT-002 (create
`Fbt.Compiler` project and `BTreeBuilder<TBlackboard>`). These are independent of FDP
engine code and can be developed and tested entirely within the FastBTree solution.

**Day 2:** Add FBT-003 (expression-based offset resolution) and FBT-004 (`NodeDebugMetadata`).
Write tests (FBT-006) as you go — the test project already exists at `tests/Fbt.Tests`.

**Day 3:** Start on FBT-010 (attributes) and FBT-011/012 (source generator). Reference
`FDP/ExtDeps/FastHSM/src/Fhsm.SourceGen/HsmActionGenerator.cs` as the starting point;
the structure is nearly identical.

**Day 4:** FBT-020/021 (hot reload). The stub in `Interpreter.Tick` marks the exact spot
where the check goes.

**Day 5:** FBT-030 through FBT-034 (ImGui renderers). These touch FDP.Presentation and
Hrot.Presentation; use a second terminal to test the engine build separately.

**Day 6:** FBT-040 through FBT-044 (sample project). This is a good integration test of
everything above.

---

## 7. Critical Constraints (Do Not Break)

- **`Fbt.Kernel` has no external dependencies** (only BCL). Keep it that way. `Fbt.Compiler`
  may take `System.Linq.Expressions`. `Fbt.SourceGen` takes `Microsoft.CodeAnalysis.CSharp`
  as an analyzer reference.

- **`NodeDebugMetadata[]` is `[NonSerialized]`** — it must not appear in any serialized
  output. Check `BinaryTreeSerializer` in `Fbt.Kernel` after adding the field.

- **Blackboard structs must be `[StructLayout(LayoutKind.Sequential)]`** — required for
  `Marshal.OffsetOf` to return deterministic byte offsets.

- **`IEntityAwareImGuiRenderer` must be backward-compatible** — existing renderers
  implementing `IImGuiRenderer` require no changes.

- **Source generator: use `IIncrementalGenerator`** (not `ISourceGenerator`). The FastHSM
  generator uses the older API — write the new one using `IIncrementalGenerator`.

---

## 8. Reference Files

| What | Where |
|------|-------|
| HSM builder reference | `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmBuilder.cs` |
| HSM source generator reference | `FDP/ExtDeps/FastHSM/src/Fhsm.SourceGen/HsmActionGenerator.cs` |
| HSM hot reload reference | `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HotReload/HotReloadManager.cs` |
| Existing BTree interpreter | `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs` |
| Existing tree compiler | `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Serialization/TreeCompiler.cs` |
| Existing renderer interface | `FDP/Engine/Fdp.Presentation/ImGui/Renderers/IImGuiRenderer.cs` |
| Existing renderer registry | `FDP/Engine/Fdp.Presentation/ImGui/Renderers/ImGuiRendererRegistry.cs` |
| Existing component reflector | `FDP/Engine/Fdp.Presentation/ImGui/Utils/ComponentReflector.cs` |
| CGF JSON strings (to be replaced) | `Hrot/Subsystems/Hrot.CGF/CgfNodes.cs` |
| CGF doctrine setup (to be replaced) | `Hrot/Subsystems/Hrot.CGF/CgfDoctrineSetup.cs` |
| DoctrineRegistry | `FDP/Toolkits/Fdp.Toolkits/Behavior/DoctrineRegistry.cs` |
| BrainBlackboard | `FDP/Toolkits/Fdp.Toolkits/Behavior/BrainBlackboard.cs` |
