# Task Tracker — Fluent BTree

**Design reference:** [DESIGN.md](./DESIGN.md)  
**Task specs:** [TASK-DETAIL.md](./TASK-DETAIL.md)

Legend: `[ ]` not started / `[>]` in progress / `[x]` completed

---

## Phase 1: Fbt.Compiler — Fluent Builder Foundation

- [x] **FBT-001** Add `TreeCompiler.FlattenToBlob(BuilderNode, string)` overload
- [x] **FBT-002** Create `BTreeBuilder<TBlackboard>` fluent API in new `Fbt.Compiler` project
- [x] **FBT-003** Expression-based offset resolution + `Unsafe` blackboard projection
- [x] **FBT-004** Add `NodeDebugMetadata` class + `[NonSerialized] NodeDebugMetadata[]?` to `BehaviorTreeBlob`
- [x] **FBT-005** Graph data structures (`BehaviorTreeGraph`, `BehaviorTreeNode`, etc.) in `Fbt.Compiler.Graph`
- [x] **FBT-006** Unit/integration tests for Phase 1
- [x] **FBT-007** `BTreeSchemaExporter` for authoring tool node palette

---

## Phase 2: Fbt.SourceGen — Roslyn Source Generator

- [x] **FBT-010** Define marker attributes (`[BTreeAction]`, `[BTreeCondition]`, `[BTreeDefinition]`, `[FbtRegistrar]`)
- [x] **FBT-011** `BTreeActionGenerator : IIncrementalGenerator` -- emits `FbtActionRegistrar.g.cs`
- [x] **FBT-012** `BTreeDefinitionGenerator` -- emits `FbtTreeCatalog.g.cs`
- [x] **FBT-013** `FbtAutoDiscovery.ScanAndRegister` -- cross-assembly auto-discovery
- [x] **FBT-014** Source generator tests

---

## Phase 3: BTreeHotReloadManager

- [x] **FBT-020** `BTreeHotReloadManager` with `TryReload`, `ReloadResult` enum, and `DoctrineRegistry` patching
- [x] **FBT-021** Implement hot reload check in `Interpreter.Tick` (replace stub comment)
- [x] **FBT-022** Hot reload tests
- [x] **FBT-023** `FbtAssemblyHotReloader` (FileSystemWatcher + collectible ALC + thread-safe reload queue)

---

## Phase 4: FDP Engine — Extended ImGui Rendering

- [x] **FBT-030** Define `IEntityAwareImGuiRenderer : IImGuiRenderer`
- [x] **FBT-031** Update `ComponentReflector.DrawComponents` to dispatch to extended renderer
- [x] **FBT-032** Add `Type? ParamsDtoType` to `DoctrineDefinition`
- [x] **FBT-033** `BrainBlackboardRenderer : IEntityAwareImGuiRenderer` (typed DTO display)
- [x] **FBT-034** `BTreeVisualizerRenderer : IEntityAwareImGuiRenderer` (live tree display)
- [x] **FBT-035** Tests for `ComponentReflector` extended dispatch
- [x] **FBT-036** Tests for `BrainBlackboardRenderer`
- [x] **FBT-037** Tests for `BTreeVisualizerRenderer`

---

## Phase 5: Sample Project

- [x] **FBT-040** Create `CombatBlackboard` DTO + `CombatContext`
- [x] **FBT-041** Sample action and condition delegates
- [x] **FBT-042** `[BTreeDefinition("Ambush_BT")]` builder method
- [ ] **FBT-043** Wire `FbtAutoDiscovery` in visual Raylib/ImGui app with live `BTreeVisualizerRenderer`
- [x] **FBT-044** Tests for sample project (headless execution)
- [ ] **FBT-045** "Recompile & Reload" button — live ALC hot reload demo in visual app
