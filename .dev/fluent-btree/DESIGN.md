# Fluent BTree Design

**Project:** FDP/HROT — FastBTree C# Fluent Builder, Source Generator, Hot Reload & Debug Visualization  
**Authored:** 2026-04-29  
**Input:** `.dev/fluent-btree/design-talk.md`

---

## 1. Overview

FastBTree doctrines in CGF are currently authored as raw JSON strings embedded inside
`CgfNodes.cs`, compiled at startup by `TreeCompiler.CompileFromJson`, and registered
manually in `CgfDoctrineSetup.RegisterAll`. This approach:

- Has no compile-time safety (typos in action/method names are runtime errors).
- Prevents blackboard DTO field references from being type-checked.
- Cannot carry debug metadata usable by the Entity Inspector.
- Requires string parsing at startup (performance overhead).
- Makes it impossible to hot-reload trees while the simulation is running.

The goal of this workstream is to replace JSON-based authoring with a fully type-safe
C# fluent builder, backed by a Roslyn source generator and a hot reload manager,
and to add rich debug visualization in the Entity Inspector for both the brain
blackboard and the live BTree execution state.

A sample project is also required to demonstrate the complete workflow from C# BTree
definition through source generation, assembly loading, and execution.

The architecture deliberately mirrors the FastHSM pattern already established in the
codebase (`Fhsm.Compiler`, `Fhsm.SourceGen`, `HotReloadManager`).

---

## 2. Key Design Decisions

### 2.1 Fluent Builder Instead of JSON

`BuilderNode` in `Fbt.Kernel/Serialization/` already serves as the intermediate
representation (DOM) between JSON and the flat `BehaviorTreeBlob`. The fluent builder
creates `BuilderNode` trees directly, bypassing `TreeCompiler.CompileFromJson`.
`TreeCompiler` gains a new `FlattenToBlob(BuilderNode root, string treeName)` overload
that accepts this pre-built DOM.

### 2.2 Lambda-Based Blackboard Parameter Binding

Reusable action/condition delegates accept a strongly-typed sub-DTO by reference rather
than raw blackboard bytes. The builder accepts an `Expression<Func<TBlackboard, TValue>>`
lambda to identify the target field. At setup time (not runtime) the **runtime builder**
(`BTreeBuilder<TBlackboard>` in `Fbt.Compiler`) does:

1. Walks the expression tree to extract the field/property name.
2. Uses `Marshal.OffsetOf<TBlackboard>(fieldName)` to compute the exact byte offset.
3. Registers a curried closure in the `ActionRegistry` that projects the blackboard into
   a `ref TValue` using `Unsafe.AddByteOffset` + `Unsafe.As` — zero allocation, no
   `unsafe` blocks in user or generated code.

The **Roslyn source generator** (`Fbt.SourceGen`) follows a different path: it must
not emit code that calls `Marshal.OffsetOf` at runtime (the target type is not loaded in
the compiler process). Instead, the generator uses Roslyn’s `ITypeSymbol` Semantic Model
APIs to compute the struct field byte offsets at compile time, then hardcodes the raw
integer directly into the generated `Unsafe.AddByteOffset` call. The generator also emits
a Roslyn diagnostic error (`BTreeDiagnostics.BlackboardTooLarge`) when the computed DTO
size exceeds `BehaviorConstants.BrainBlackboardByteSize` (128 bytes), preventing silent
memory corruption via out-of-bounds offset arithmetic.

### 2.3 Source Generator for Zero-Boilerplate Registration

A Roslyn incremental generator (`Fbt.SourceGen`) mirrors `Fhsm.SourceGen/HsmActionGenerator.cs`.
It scans for `[BTreeAction]`, `[BTreeCondition]`, and `[BTreeDefinition]` attributes and
emits:
- `FbtActionRegistrar.g.cs` — static `RegisterAll` method that builds all curried closures
  and registers them in the `ActionRegistry`.
- `FbtTreeCatalog.g.cs` — static methods returning pre-compiled `BehaviorTreeBlob` instances
  (structure computed at code-gen time, eliminating startup JSON parsing entirely).

### 2.4 Cross-Assembly Auto-Discovery

Each project that uses `Fbt.SourceGen` receives its own generated `[FbtRegistrar]`-tagged
class. At engine startup, `FbtAutoDiscovery.ScanAndRegister` reflects over all loaded
assemblies to find these classes and invoke their `RegisterAll` method.

This pattern is already used for `ImGuiRendererRegistry.ScanAllAssemblies()` and for
`DoctrineSchemaDiscovery.AutoRegister` in HROT.

### 2.5 Hot Reload Mirroring FastHSM

`Fbt.Kernel` gains a `BTreeHotReloadManager` that reuses the `ReloadResult` enum
already present in `Fhsm.Kernel.HotReloadManager`. It compares `BehaviorTreeBlob.StructureHash`
and `ParamHash` to determine:
- **SoftReload** — only parameters changed; existing `BehaviorTreeState` instances are
  untouched (they will pick up new float/int params on the next tick via the updated blob).
- **HardReset** — structure changed; `BehaviorTreeState.Reset()` is called on every live
  instance to restart the tree from scratch.

In both non-`NoChange` cases, `BTreeHotReloadManager.TryReload` immediately patches the
`DoctrineRegistry` by replacing the active `BehaviorTreeBlob` inside the `DoctrineDefinition`,
so live entities execute the new logic starting on the very next tick.

The stub comment `// === HOT RELOAD CHECK (Stub for now) ===` in `Interpreter.Tick` is
implemented as part of this phase.

For live C# code changes (action/condition delegate bodies), a dedicated
`FbtAssemblyHotReloader` utility orchestrates the full ALC-based reload cycle: it watches
a directory for new DLLs, loads each into a new collectible `AssemblyLoadContext`, invokes
the generated `FbtActionRegistrar.RegisterAll` via reflection to overwrite delegate
pointers in the `ActionRegistry`, extracts new blobs from `FbtTreeCatalog`, and calls
`BTreeHotReloadManager.TryReload`. ALC operates in the same process memory space so that
`ref BrainBlackboard` and `ref BrainBTreeState` parameters still point to live ECS memory.
The old ALC is unloaded after all in-flight delegates complete.

### 2.6 Node Debug Metadata

`BehaviorTreeBlob` receives a non-serialized parallel array `NodeDebugMetadata[]` (managed,
`[NonSerialized]`). Each entry stores:
- `Label` — human-readable name (auto-generated from lambda or supplied explicitly).
- `SourceFile` and `LineNumber` — captured via `[CallerFilePath]` / `[CallerLineNumber]`
  in the builder fluent API.
- `CustomComment` — optional developer comment.
- `VisualId` — stable UUID that correlates a node in the running engine with a box in
  the visual editor. Every builder node method accepts an optional `Guid visualId`
  parameter (defaults to `default`, which the builder replaces with `Guid.NewGuid()`
  to ensure every node always has a unique, non-empty identifier). The authoring tool
  injects its own pre-assigned UUID when it writes the C# source, giving it a stable
  handle to highlight the correct box in real time.

### 2.7 Extended ImGui Renderer Interface

`IImGuiRenderer` (in `Fdp.Presentation.Renderers`) is extended by a new interface
`IEntityAwareImGuiRenderer` that receives the `IInspectableSession` and the `Entity`.
`ComponentReflector.DrawComponents` is updated to check for this extended interface first.
Simple renderers implementing only `IImGuiRenderer` continue to work unchanged.

### 2.8 BrainBlackboard Typed DTO Rendering

`DoctrineDefinition` receives an optional `Type? ParamsDtoType` property. When set, the
`BrainBlackboardRenderer` (implementing `IEntityAwareImGuiRenderer`) uses it to marshal
the 128-byte `BrainBlackboard.Memory` as the registered DTO and render its fields via
`ImGuiPropertyTree`, completely replacing the raw hex byte display.

### 2.9 BTree Live Visualizer in Entity Inspector

`BTreeVisualizerRenderer` implements `IEntityAwareImGuiRenderer` for `BrainBTreeState`.
Using `IInspectableSession` it reads the sibling `DoctrineState` to look up the active
`BehaviorTreeBlob` from the `DoctrineRegistry`. It then renders a recursive ImGui tree
with:
- Active execution path highlighted (green for current leaf, yellow for active composites)
  using `RunningNodeIndex` and `NodeIndexStack`.
- Per-node live state tooltips (Wait timer from `AsyncData`, loop count from `LocalRegisters`,
  Parallel bitmask from `LocalRegisters[3]`).
- Source file / line number and custom comment from `NodeDebugMetadata` on hover.
- `VisualId` from `NodeDebugMetadata` displayed in the tooltip (prefixed `"VisualId: "`)
  for every node, proving the authoring tool correlation survives the full compilation
  pipeline.

### 2.10 Future Authoring Tool Support

Graph data structures in `Fbt.Compiler.Graph` (mirroring `Fhsm.Compiler.Graph`) describe
the mutable BTree DOM parseable from C# source using Roslyn. The authoring tool:
- Parses `.cs` files using Roslyn to reconstruct `BehaviorTreeGraph`.
- Writes edited trees back as fluent C# source.
- Compiles to a DLL and hot-reloads via `FbtAssemblyHotReloader`.
- Uses `VisualId` in `NodeDebugMetadata` to highlight the running node in real time.

`BTreeSchemaExporter` in `Fbt.Compiler` provides the authoring tool with a description
of all available actions, conditions, and blackboard DTO types. It scans loaded assemblies
for `[BTreeAction]` and `[BTreeCondition]` methods, extracts `TBlackboard`/`TValue` types
and field offsets, and emits a `BTreeSchema.json` file that populates the tool’s node
palette without requiring the tool to load engine assemblies directly.

### 2.11 Sample Project

`Fbt.Examples.FluentBTree` is a **visual application** using `Fdp.Presentation` (Raylib
window + ImGui overlay) demonstrating the complete workflow:
- Custom unmanaged blackboard DTO with named fields.
- `[BTreeAction]` and `[BTreeCondition]` annotated delegates.
- `[BTreeDefinition]` annotated builder method.
- Source generator emitting `FbtActionRegistrar.g.cs` and `FbtTreeCatalog.g.cs`.
- `FbtAutoDiscovery.ScanAndRegister` at startup.
- Live `BTreeVisualizerRenderer` ImGui window showing color-coded execution state.
- ImGui sliders/checkboxes for `CombatBlackboard` fields so the user can drive the tree
  interactively.
- A "Recompile & Reload" button that triggers `dotnet build`, then `FbtAssemblyHotReloader`
  to perform a live ALC hot reload, proving that the tree updates without restarting the
  application and that `VisualId` linkage, blackboard state preservation (SoftReload), or
  reset (HardReset) behave correctly.

---

## 3. Phases

### Phase 1: Fbt.Compiler — Fluent Builder Foundation

**Goal:** Create the `Fbt.Compiler` library providing a fluent API to build behavior trees
in C# and graph data structures for authoring tool support.

**New project:** `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/Fbt.Compiler.csproj`  
References: `Fbt.Kernel`

#### Tasks
- **FBT-001** Add `FlattenToBlob` overload to `TreeCompiler` accepting a `BuilderNode` tree directly (no JSON round-trip); auto-invokes `TreeValidator.Validate`.
- **FBT-002** Create `BTreeBuilder<TBlackboard>` fluent API class in `Fbt.Compiler`; all node methods accept optional `Guid visualId` parameter.
- **FBT-003** Implement expression-based offset resolution and `Unsafe` blackboard projection for `Condition<TBlackboard, TValue>` and `Action<TBlackboard, TValue>` builder methods.
- **FBT-004** Add `NodeDebugMetadata` class and `NodeDebugMetadata[]` parallel array to `BehaviorTreeBlob` (`[NonSerialized]`).
- **FBT-005** Create graph data structures (`BehaviorTreeGraph`, `BehaviorTreeNode`, `CompositeNode`, `DecoratorNode`, `LogicNode`) in `Fbt.Compiler.Graph`.
- **FBT-006** Tests for `BTreeBuilder<TBlackboard>` — correct blob output, correct offset resolution, correct curried delegate execution, validation exception tests.
- **FBT-007** `BTreeSchemaExporter` — reflection-based utility producing `BTreeSchema.json` for the authoring tool.

### Phase 2: Fbt.SourceGen — Roslyn Source Generator

**Goal:** Create a Roslyn incremental source generator that eliminates registration boilerplate
and emits compile-time pre-built blobs.

**New project:** `FDP/ExtDeps/FastBTree/src/Fbt.SourceGen/Fbt.SourceGen.csproj`  
References: `Microsoft.CodeAnalysis.CSharp` (analyzer reference, like `Fhsm.SourceGen`)

#### Tasks
- **FBT-010** Define marker attributes: `[BTreeAction]`, `[BTreeCondition]`, `[BTreeDefinition]`, `[FbtRegistrar]` in a shared attribute assembly (can live in `Fbt.Kernel` or a new `Fbt.Attributes` project).
- **FBT-011** Implement `BTreeActionGenerator : IIncrementalGenerator` scanning for `[BTreeAction]`/`[BTreeCondition]` and emitting `FbtActionRegistrar.g.cs`.
- **FBT-012** Implement `BTreeDefinitionGenerator` scanning for `[BTreeDefinition]` and emitting `FbtTreeCatalog.g.cs`.
- **FBT-013** Implement `FbtAutoDiscovery.ScanAndRegister` static class in `Fbt.Compiler` for cross-assembly auto-discovery using `[FbtRegistrar]`.
- **FBT-014** Tests for source generator output (use `Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing`).

### Phase 3: BTreeHotReloadManager

**Goal:** Implement hot reload for behavior trees, mirroring the HSM `HotReloadManager`.

**Files modified:**
- `Fbt.Kernel/Runtime/Interpreter.cs` — implement the hot reload check stub.
- New file: `Fbt.Kernel/HotReload/BTreeHotReloadManager.cs`

#### Tasks
- **FBT-020** Implement `BTreeHotReloadManager` with `TryReload(string treeName, BehaviorTreeBlob newBlob, Span<BrainBTreeState> liveInstances)`, `ReloadResult` enum (NewTree / NoChange / SoftReload / HardReset), and `DoctrineRegistry` patching before returning.
- **FBT-021** Implement the hot reload check in `Interpreter.Tick` — compare `_blob.StructureHash` vs stored hash in state; call `state.Reset()` on structure change.
- **FBT-022** Tests for hot reload — SoftReload preserves state, HardReset clears state, NoChange is a no-op, old ALC GC'd after reload.
- **FBT-023** `FbtAssemblyHotReloader` — `FileSystemWatcher`-driven ALC load/unload orchestrator with `OnReloadCompleted`/`OnReloadFailed` events and thread-safe debounced reload queue.

### Phase 4: FDP Engine — Extended ImGui Rendering

**Goal:** Extend the ImGui rendering framework and add typed blackboard/BTree visualizers
in the Entity Inspector.

**Files modified:**
- `Fdp.Presentation/ImGui/Renderers/IImGuiRenderer.cs` — add `IEntityAwareImGuiRenderer`.
- `Fdp.Presentation/ImGui/Utils/ComponentReflector.cs` — check for extended interface.
- `Fdp.Toolkits/Behavior/DoctrineDefinition.cs` — add `ParamsDtoType`.

**New files in `Hrot.Presentation`** (or a suitable location with access to `DoctrineRegistry`):
- `Behavior/BrainBlackboardRenderer.cs`
- `Behavior/BTreeVisualizerRenderer.cs`

#### Tasks
- **FBT-030** Define `IEntityAwareImGuiRenderer` extending `IImGuiRenderer` with `bool RenderValue(IInspectableSession, Entity, object)`.
- **FBT-031** Update `ComponentReflector.DrawComponents` to prefer `IEntityAwareImGuiRenderer` when available (pass `session` and `entity` to it).
- **FBT-032** Add `Type? ParamsDtoType` to `DoctrineDefinition`.
- **FBT-033** Implement `BrainBlackboardRenderer : IEntityAwareImGuiRenderer` for `BrainBlackboard` — reads `DoctrineState`, looks up `ParamsDtoType`, marshals blackboard memory to typed DTO, renders via `ImGuiPropertyTree`.
- **FBT-034** Implement `BTreeVisualizerRenderer : IEntityAwareImGuiRenderer` for `BrainBTreeState` — reads sibling `DoctrineState`, retrieves `BehaviorTreeBlob`, renders color-coded recursive tree.
- **FBT-035** Tests for `ComponentReflector` extended renderer dispatch.
- **FBT-036** Tests for `BrainBlackboardRenderer` — verifies DTO field rendering with a mock session.
- **FBT-037** Tests for `BTreeVisualizerRenderer` — verifies correct node coloring and metadata display.

### Phase 5: Sample Project

**Goal:** Provide a self-contained, runnable example demonstrating the complete fluent BTree
workflow from definition to execution.

**New project:** `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/Fbt.Examples.FluentBTree.csproj`  
References: `Fbt.Kernel`, `Fbt.Compiler`  
Analyzer reference: `Fbt.SourceGen`

#### Tasks
- **FBT-040** Create `CombatBlackboard` unmanaged struct (fields: `AmmoCount`, `ThreatVisible`, `EngagementRange`).
- **FBT-041** Implement `[BTreeAction]` and `[BTreeCondition]` annotated delegates (e.g., `CheckAmmo`, `HasThreat`, `AimAndFire`, `HoldPosition`).
- **FBT-042** Implement `[BTreeDefinition("Ambush_BT")]` builder method using `BTreeBuilder<CombatBlackboard>`.
- **FBT-043** Wire `FbtAutoDiscovery.ScanAndRegister` in a **visual Raylib/ImGui application** with live `BTreeVisualizerRenderer` and interactive `CombatBlackboard` sliders.
- **FBT-044** Tests verifying the sample tree executes correctly with known blackboard state (headless).
- **FBT-045** "Recompile & Reload" button in the visual app demonstrating live ALC hot reload with SoftReload/HardReset behaviour.

---

## 4. Constraints and Invariants

- `Fbt.Kernel` must remain dependency-free except for standard .NET BCL. `Fbt.Compiler` carries `System.Linq.Expressions` dependency, which is acceptable.
- `NodeDebugMetadata[]` is `[NonSerialized]` — it must not affect `BinaryTreeSerializer` output.
- Byte offsets computed at builder/source-gen time must exactly match `Marshal.OffsetOf<T>` results. Sequential layout (`StructLayout(LayoutKind.Sequential)`) must be enforced on all blackboard DTOs.
- `IEntityAwareImGuiRenderer` must be backward-compatible: existing `IImGuiRenderer` implementations require no changes.
- `BTreeHotReloadManager.TryReload` must never throw; it must be safe to call with `liveInstances` of length 0.
- The sample project must build and run standalone (`dotnet run`) without any HROT or FDP dependencies.
- All projects in FastBTree must target the same TFM as the main solution (verify in `Directory.Build.props`).
- No new dependencies on `unsafe` in user-facing action/condition delegates; all pointer arithmetic lives inside the generated curried closures.

---

## 5. Project Dependency Map

```
Fbt.Kernel          (no external deps)
    ^
    |
Fbt.Compiler        (+ System.Linq.Expressions)
    ^
    |
Fbt.SourceGen       (+ Microsoft.CodeAnalysis.CSharp — analyzer ref only)

Fbt.Examples.FluentBTree  -> Fbt.Kernel, Fbt.Compiler
                              [analyzer] Fbt.SourceGen

Fdp.Toolkits        -> Fbt.Kernel (already)
Fdp.Presentation    -> Fdp.Core (already)
                    -> (new) IEntityAwareImGuiRenderer lives here

Hrot.Presentation   -> Fdp.Presentation, Fdp.Toolkits
                    -> (new) BrainBlackboardRenderer, BTreeVisualizerRenderer live here
```

No circular dependencies introduced. `Fbt.SourceGen` is consumed only as an analyzer reference.

---

## 6. Files Modified vs Created

| Status | Path |
|--------|------|
| **Modified** | `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Serialization/TreeCompiler.cs` — add `FlattenToBlob(BuilderNode, string)` |
| **Modified** | `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/BehaviorTreeBlob.cs` — add `NodeDebugMetadata[]` |
| **Modified** | `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs` — implement hot reload check |
| **Created** | `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/` — new project |
| **Created** | `FDP/ExtDeps/FastBTree/src/Fbt.SourceGen/` — new project |
| **Created** | `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/HotReload/BTreeHotReloadManager.cs` |
| **Created** | `FDP/ExtDeps/FastBTree/examples/Fbt.Examples.FluentBTree/` — new project |
| **Modified** | `FDP/Engine/Fdp.Presentation/ImGui/Renderers/IImGuiRenderer.cs` — add `IEntityAwareImGuiRenderer` |
| **Modified** | `FDP/Engine/Fdp.Presentation/ImGui/Utils/ComponentReflector.cs` — dispatch extended renderer |
| **Modified** | `FDP/Toolkits/Fdp.Toolkits/Behavior/DoctrineRegistry.cs` — add `ParamsDtoType` to `DoctrineDefinition` |
| **Created** | `Hrot/Engine/Hrot.Presentation/Behavior/BrainBlackboardRenderer.cs` |
| **Created** | `Hrot/Engine/Hrot.Presentation/Behavior/BTreeVisualizerRenderer.cs` |
| **Modified** | `FDP/ExtDeps/FastBTree/FastBTree.sln` — add new projects |
| **Modified** | `IOS-IG-SimHost.sln` — add `Fbt.Compiler`, `Fbt.SourceGen`, sample project |
