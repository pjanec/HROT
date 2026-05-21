# Blueprint Subsystem — Task Detail

**Reference documents:**
- Architecture: [Blueprint_Subsystem_Architecture_v1.2.md](./Blueprint_Subsystem_Architecture_v1.2.md) + [InlinePatches](./Blueprint_Subsystem_Architecture_v1.2_InlinePatches.md) + [FinalResolutions](./Blueprint_Subsystem_Architecture_v1.2_FinalResolutions.md)
- Roadmap: [Blueprint_Subsystem_Implementation_Roadmap_v1.1.md](./Blueprint_Subsystem_Implementation_Roadmap_v1.1.md)

---

<!-- Tasks will be appended below, grouped by design document area -->

---

## TASK-P0-001 -- Project Skeleton & Filesystem Placement

**Phase:** 0 -- Infrastructure
**Design Reference:** [Roadmap v1.1 §4 M0](./Blueprint_Subsystem_Implementation_Roadmap_v1.1.md#m0--project-skeletons--filesystem-placement), [Roadmap v1.1 §2 Filesystem layout](./Blueprint_Subsystem_Implementation_Roadmap_v1.1.md#2-filesystem-layout-in-the-engine-repository), [Architecture v1.2 §3.2](./Blueprint_Subsystem_Architecture_v1.2.md#32-spec--projects-and-references)
**Effort:** 1-2 days

### Scope

What IS included:

- Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Hrot.Blueprints.Core.csproj` targeting `net8.0`. References `Fdp.Core` only (no Fdp.Toolkits, no Fdp.Presentation). Contains a placeholder `.cs` file so the project is non-empty.
- Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Generators/Hrot.Blueprints.Generators.csproj` targeting `netstandard2.0`. Package references: `Microsoft.CodeAnalysis.CSharp 4.8.0` and `Microsoft.CodeAnalysis.Analyzers 3.3.4`, both with `PrivateAssets="all"`. Project reference to `Hrot.Blueprints.Core` with `PrivateAssets="all"`. Contains a placeholder `BlueprintIncrementalGenerator.cs` stub (class decorated with `[Generator]`, empty body).
- Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Hrot.Blueprints.Editor.csproj` targeting `net8.0`. References `Hrot.Blueprints.Core`, `Fdp.Core`, `Fdp.Presentation`, `Fdp.Toolkits`. Contains a placeholder `.cs` file.
- Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj` targeting `net8.0`, type xUnit test project. References `Hrot.Blueprints.Core`, `Fdp.Core`, `xUnit`. Contains one placeholder `[Fact]` test that asserts `true`.
- Add `Blueprints/` subdirectory under `FDP/Toolkits/Fdp.Toolkits/` with placeholder files as listed in Roadmap §2. No new `.csproj` -- this folder is part of the existing `Fdp.Toolkits.csproj`.
- Create `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/` directory (empty; receives `.bp.json` files later).
- Add all four new projects to the solution (`IOS-IG-SimHost.sln` or, if scoped to FDP only, `FDP/FDP.sln`).
- Modify `Hrot.AI.Behaviors.csproj` with the properties and project references specified in Roadmap M0 acceptance: `EmitCompilerGeneratedFiles`, `CompilerGeneratedFilesOutputPath`, `DebugType`, `DebugSymbols`, generator `ProjectReference` with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`, `AdditionalFiles` glob for `Blueprints\**\*.bp.json`.

What is NOT included:

- Any production type definitions beyond empty stubs (those are TASK-P0-002).
- Any test logic beyond a single always-passing `[Fact]` placeholder.
- Any content in the `Blueprints/` folders under `Fdp.Toolkits` or `Hrot.AI.Behaviors` (folders exist, files come later).
- Modification of `AiHotReloadCoordinator.cs`, `FdpJsonOptionsRegistry.cs`, `IEntityCommandBuffer.cs`, or `GlobalComponentIds.cs` -- those are later milestones.
- Hrot.Blueprints.Editor project wired into any editor entry point.

### Constraints

- `Hrot.Blueprints.Generators` MUST target `netstandard2.0`. Targeting `net8.0` is a silent failure: the project builds, but the VS host cannot load the analyzer. See Roadmap M0 Risk note.
- `Fdp.Toolkits.Blueprints` is NOT a separate `.csproj`. It is a folder added inside the existing `Fdp.Toolkits` project. Do not create `Fdp.Toolkits.Blueprints.csproj`.
- `Hrot.Blueprints.Core` must reference `Fdp.Core` only -- never `Fdp.Toolkits` directly. See Architecture v1.2 §3.3 dependency direction.
- There is no `Hrot.Blueprints.Engine` assembly. That concept was removed. Do not create it.
- All new code must build with zero errors and zero warnings before this task is considered done. A broken build is a blocking defect.

### Success Conditions

- SC1: Running `dotnet build` against the solution produces zero errors and zero warnings for all five new assemblies. Verify by inspecting MSBuild output -- no CS or NETSDK error codes appear on any of the new project lines.
- SC2: Generator load verification. Add a file `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/test-malformed.bp.json` containing invalid JSON (e.g., `{`). Run `dotnet build Hrot.AI.Behaviors.csproj`. Assert that MSBuild output contains at least one diagnostic originating from `BlueprintIncrementalGenerator`. Remove the file afterward. This confirms the analyzer host loads `netstandard2.0` correctly.
- SC3: Running `dotnet test Hrot.Blueprints.Tests.csproj` reports 1 test passed, 0 failed.
- SC4: The `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/` directory exists and the `.csproj` `AdditionalFiles` glob resolves to zero items (empty folder). Verify by running `dotnet build` with verbosity=detailed and confirming no AdditionalFiles items appear in the item group trace -- this avoids a spurious empty-file diagnostic.

---

## TASK-P0-002 -- Asset Schema Types

**Phase:** 0 -- Infrastructure
**Design Reference:** [Roadmap v1.1 §4 M1](./Blueprint_Subsystem_Implementation_Roadmap_v1.1.md#m1--asset-schema--json-io), [Architecture v1.2 §5](./Blueprint_Subsystem_Architecture_v1.2.md#5-asset-schema)
**Effort:** 1-2 days

### Scope

What IS included:

- All types from Architecture v1.2 §5 implemented in the `Hrot.Blueprints.Core.Assets` namespace. This includes, exhaustively: `BlueprintAsset`, `BlueprintDispatchKind` enum, `BlackboardTierHint` enum, `AiPrimitiveDecl`, `AiPrimitiveIntent` enum, `AiPrimitiveHosting` enum, `VariableDecl`, `ParameterDecl`, `EventDispatcherDecl`, `CustomEventDecl`, `BlueprintTypeRef`, `Graph`, `GraphKind` enum, `Node` (abstract base), all concrete `Node` subclasses listed in the `[JsonDerivedType]` attributes, `Pin`, `Link`, `AssetMetadata`, `GraphMetadata`, `NodeMetadata`, and the `Header` class.
- Concrete `Node` subclasses: `FunctionCallNode`, `BranchNode`, `SequenceNode`, `GetVariableNode`, `SetVariableNode`, `LiteralNode`, `EventEntryNode`, `ReturnNode`, `CastNode`, `ArrayMakeNode`, `ArrayGetNode`, `LatentDelayNode`, `CallEventDispatcherNode`, `BindEventDispatcherNode`, `CallCustomEventNode`, `CallPeerBlueprintNode`, `ChannelCommandNode`, `WaitForChannelNode`, `WaitForEventNode`. All must appear in `[JsonDerivedType]` declarations on the `Node` base class with the exact discriminator strings from Architecture v1.2 §5.3.
- `[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]` on `Node` base class.
- `BlueprintJsonServices` static class in `Hrot.Blueprints.Core` (not in `Assets` sub-namespace) with at minimum: `Serialize(BlueprintAsset)` returning `string`, and `Deserialize(string)` returning `BlueprintAsset?`. Both must use `FdpJsonOptionsRegistry.DefaultRelaxed` as the base options. If `[JsonPolymorphic]` conflicts with the engine options (see Roadmap M1 Risk), implement a `CreateExtended` workaround using a custom `JsonSerializerOptions` instance that copies `DefaultRelaxed` settings and adds the polymorphic type resolver.

What is NOT included:

- Any compiler, validator, IR, or runtime code.
- `BlueprintDefinition`, `BlueprintLatentCursor`, `IBlueprintCompiler`, `IBlueprintDebugSession`, or any other type outside the asset schema.
- `JsonAestheticFormatter.FlattenNumericArrays` integration -- referenced in Roadmap M1 but scoped to that milestone's test acceptance; the formatter is applied in TASK-P0-003 where it is needed for byte-identical round-trip comparison.
- Catalog types (`EngineEventCatalog`, `ChannelCommandCatalog`, `WaitPrimitiveCatalog`) -- those live in `Fdp.Toolkits.Blueprints` and are a later milestone.
- `Pin` field-level validation rules or default-value parsing.

### Constraints

- Every concrete `Node` subclass that exists in Architecture v1.2 §5.3 must be present. Missing node types are a hard defect because the generator (added to `Hrot.AI.Behaviors` in TASK-P0-001) will receive `.bp.json` files containing these discriminator values in later tasks.
- `ChannelCommandNode`, `WaitForChannelNode`, and `WaitForEventNode` are new additions in v1.2 (marked NEW in §5.3). They must be included -- the architecture explicitly adds them. See also InlinePatches for context on channel command lowering.
- All `Node` subclasses must be `sealed`. The abstract base `Node` must not be `sealed`.
- Use `System.Text.Json` exclusively. Do not add Newtonsoft.Json or any other JSON library reference.
- Do not duplicate `FdpJsonOptionsRegistry` logic. Call `FdpJsonOptionsRegistry.DefaultRelaxed` directly; do not re-configure or shadow its options.

### Success Conditions

- SC1: `Hrot.Blueprints.Core` compiles without errors or warnings after adding all types.
- SC2: Reflection enumeration. In a test (or LINQPad script), call `typeof(Node).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(Node)))` and assert the count equals the number of concrete node types listed in Architecture v1.2 §5.3 `[JsonDerivedType]` attributes (19 as of v1.2). This catches a missing or misnamed subclass.
- SC3: Discriminator round-trip for each node kind. For each concrete `Node` subtype `T`: construct `new T { Id = Guid.NewGuid() }`, serialize via `BlueprintJsonServices.Serialize` wrapping it in a minimal `BlueprintAsset` with one graph, deserialize, assert the deserialized node's runtime type is `T`. Run for all 19 subtypes.
- SC4: `BlueprintJsonServices.Deserialize` on a JSON string that contains an unknown field (e.g., `"unknownField": "ignored"`) returns a non-null `BlueprintAsset` without throwing. Missing optional list fields (e.g., omitting `"variables"`) produce an empty `List<T>`, not null.

---

## TASK-P0-003 -- Asset JSON Round-Trip Tests

**Phase:** 0 -- Infrastructure
**Design Reference:** [Roadmap v1.1 §4 M1](./Blueprint_Subsystem_Implementation_Roadmap_v1.1.md#m1--asset-schema--json-io), [Architecture v1.2 §5.4](./Blueprint_Subsystem_Architecture_v1.2.md#54-sample-bpjson-files)
**Effort:** 1 day

### Scope

What IS included:

- xUnit test class `AssetJsonRoundTripTests` in `Hrot.Blueprints.Tests`.
- Three hand-written sample `BlueprintAsset` objects (constructed in C# or loaded from embedded test JSON resources), one for each dispatch kind: `Library`, `AiPrimitive`, `Instance`. The AiPrimitive sample must include at least one `ChannelCommandNode`, one `WaitForChannelNode`, and one `WaitForEventNode` to exercise the new node types. The Instance sample must include at least two graph kinds (`Function` and `Event`).
- Round-trip test method for each sample: serialize to JSON string via `BlueprintJsonServices.Serialize`, deserialize via `BlueprintJsonServices.Deserialize`, re-serialize, assert the two JSON strings are byte-identical (after applying `JsonAestheticFormatter.FlattenNumericArrays` if that formatter normalizes whitespace -- otherwise use a canonical re-serialization approach that eliminates property-ordering variance).
- Polymorphic node round-trip: one test that constructs a `Graph` containing one of every concrete `Node` subtype, serializes, deserializes, and asserts each deserialized node has the correct runtime type (matching the discriminator).
- Unknown-field tolerance test: deserialize a JSON string that contains an extra `"editorNotes": "ignored"` field at top level and inside a `Node` object; assert no exception is thrown and the returned asset has correct known fields.
- Missing-field defaulting test: deserialize a minimal JSON string containing only `"name"`, `"dispatch"`, and `"assetId"` fields; assert all list fields (`Variables`, `Graphs`, `Parameters`, etc.) are non-null empty lists.

What is NOT included:

- Tests for compiler, validator, IR, runtime, or hot reload.
- File I/O or disk-resident `.bp.json` files (all sample data is in-memory or embedded resources).
- Performance or allocation tests.
- Tests for `FdpJsonOptionsRegistry` itself -- it is engine code.

### Constraints

- Tests must pass with `dotnet test` in isolation (no engine host, no ECS running).
- The round-trip comparison must detect a genuine mismatch: do not compare object graphs with `Equals` that might hide missing fields. Compare the final JSON text.
- Each of the 19 concrete `Node` subtypes must appear in at least one test assertion (either the polymorphic round-trip test or the dispatch-kind sample). Missing coverage of any subtype is a defect.
- The sample AiPrimitive asset must match the structure shape shown in Architecture v1.2 §5.4 (MoveToAndFire pattern) closely enough that it exercises `AiPrimitiveDecl`, `Parameters`, and `WorkingState` fields, in addition to `ChannelCommandNode` and wait nodes.

### Success Conditions

- SC1: `dotnet test --filter "FullyQualifiedName~AssetJsonRoundTripTests"` reports all tests passed, zero failed, zero skipped.
- SC2: Round-trip fidelity for Library dispatch. Construct a `BlueprintAsset` with `Dispatch = Library` and at least two `Graph` objects each containing at least one `FunctionCallNode`. Serialize, deserialize, re-serialize. Assert the two JSON strings are identical character-for-character.
- SC3: Round-trip fidelity for AiPrimitive dispatch. Construct a `BlueprintAsset` with `Dispatch = AiPrimitive`, `Primitive.Intent = Action`, `Primitive.Hostings = [BTreeAction, HsmAction]`, one `ParameterDecl`, one `WorkingState` entry, and a graph containing at least `ChannelCommandNode`, `WaitForChannelNode`, `WaitForEventNode`. Serialize, deserialize, re-serialize. Assert identical.
- SC4: Round-trip fidelity for Instance dispatch. Construct a `BlueprintAsset` with `Dispatch = Instance`, at least one `VariableDecl`, at least two graphs (one `Function`, one `Event`). Serialize, deserialize, re-serialize. Assert identical.
- SC5: Polymorphic node coverage. Construct one `Graph` with 19 nodes (one per concrete `Node` subtype). Serialize and deserialize. For each node in the deserialized graph, assert `node.GetType() == expectedType` for its index position. No `JsonException` is thrown.
- SC6: Tolerance for unknown fields. Deserialize `{"name":"X","dispatch":"Library","assetId":"00000000-0000-0000-0000-000000000001","unknownField":"ignored","graphs":[]}` without exception. Assert returned asset `Name == "X"` and `Dispatch == BlueprintDispatchKind.Library`.
- SC7: Default initialization for missing fields. Deserialize `{"name":"Y","dispatch":"Instance","assetId":"00000000-0000-0000-0000-000000000002"}`. Assert `Variables` is non-null and empty; `Graphs` is non-null and empty; `EventDispatchers` is non-null and empty.

---

## TASK-TH-001 -- MockSimulationView

**Phase:** 1 -- Test Harness
**Design Reference:** [Test Harness DD §3](./Blueprint_Subsystem_Test_Harness_Detailed_Design.md#3-mocksimulationview--read-only-projection), [Architecture v1.2](./Blueprint_Subsystem_Architecture_v1.2.md)
**Effort:** 1 day

### Scope

What IS included:

- `MockSimulationView : ISimulationView` in namespace `Hrot.Blueprints.Tests.Mocks`, wrapping a real `EntityRepository` instance passed at construction.
- Full forwarding of all read methods to the underlying repo: `IsAlive`, `GetComponentRO<T>`, `GetManagedComponentRO<T>`, `HasComponent<T>`, `HasManagedComponent<T>`, `HasSingleton<T>`, `GetSingletonRO<T>`, `Query()`.
- `GetCommandBuffer()` returning the `MockEntityCommandBuffer` passed at construction (same instance every call).
- Time/tick state as `internal`-mutable, publicly read-only: `float Time`, `float DeltaTime`, `uint Tick`. `internal void AdvanceTime(float dt)` increments `_time += dt`, sets `_deltaTime = dt`, increments `_tick`.
- Per-tick stable event stream cache: `Dictionary<Type, object> _eventStreamsByType`. `internal void BeginTick(IReadOnlyDictionary<Type, IReadOnlyList<object>> published)` clears and repopulates the cache.
- `ReadEvents<T>()` returning the cached `IReadOnlyList<T>` for the current tick, or `Array.Empty<T>()` if no events of type `T` were published this tick.
- xUnit test class `MockSimulationViewContractTests` in `Hrot.Blueprints.Tests` containing the three contract tests from Test Harness DD §3.9.

What is NOT included:

- Any write-path methods on `ISimulationView` (none exist on the interface; the mock adds none).
- Event queue publication (that belongs to `MockEntityCommandBuffer.PublishEvent` and `BlueprintTestFixture.PublishEventForNextTick`).
- Threading or multi-tick concurrency support.
- Direct singleton mutation path on the view.

### Constraints

- `MockSimulationView` must implement the full `ISimulationView` surface listed in Test Harness DD §3.2; implementing a subset is a defect.
- `AdvanceTime` and `BeginTick` are `internal`; test code must not call them directly -- all time advancement goes through `BlueprintTestFixture.TickFrame`.
- `ReadEvents<T>()` must return the same `IReadOnlyList<T>` object reference for repeated calls within the same tick (QV-3). Returning a new list each call is a defect.
- Events published during a tick via `ecb.PublishEvent` must not appear in that tick's `ReadEvents<T>()` stream; they appear only after the next `BeginTick` call.
- The `MockEntityCommandBuffer` instance provided at construction must be returned verbatim by `GetCommandBuffer()`; the view must not create a second ECB.

### Success Conditions

- SC1: Construct `new MockSimulationView(repo, ecb)`. Call `var e = repo.CreateEntity(); repo.AddComponent(e, new TestComponent { Value = 42 })`. Call `ref readonly var r = ref view.GetComponentRO<TestComponent>(e)`. Assert `r.Value == 42`. Then call `ref var w = ref repo.GetComponentRW<TestComponent>(e); w.Value = 99`. Assert `r.Value == 99` (same backing chunk memory; no copy was made by the mock).
- SC2: Call `view.AdvanceTime(0.016f)` three times. Assert `view.Time` is approximately `0.048f` (within float epsilon), `view.DeltaTime == 0.016f`, `view.Tick == 3u`.
- SC3: Call `view.BeginTick(dict)` where `dict` contains a `TestEvent` list of two items. Call `view.ReadEvents<TestEvent>()` twice and store both results. Assert `object.ReferenceEquals(first, second)` is `true`. Assert `first.Count == 2`. Call `view.BeginTick(emptyDict)`. Assert `view.ReadEvents<TestEvent>().Count == 0` (stream was reset).
- SC4: After `view.BeginTick(dict)` with two `TestEvent` entries, simulate mid-tick publish by calling `view.BeginTick` a second time with a different dict. Assert that the list object returned by a call made before the second `BeginTick` still reflects only the first dict's data (captured reference remains valid; this tests that `BeginTick` does not mutate previously-returned lists).
- SC5: `dotnet test --filter "FullyQualifiedName~MockSimulationViewContractTests"` reports all three tests from Test Harness DD §3.9 passed, zero failed.

---

## TASK-TH-002 -- MockEntityCommandBuffer

**Phase:** 1 -- Test Harness
**Design Reference:** [Test Harness DD §4](./Blueprint_Subsystem_Test_Harness_Detailed_Design.md#4-mockentitycommandbuffer--deferred-write-ecb), [Architecture v1.2 §13.5](./Blueprint_Subsystem_Architecture_v1.2.md)
**Effort:** 1 day

### Scope

What IS included:

- `MockEntityCommandBuffer : IEntityCommandBuffer` in namespace `Hrot.Blueprints.Tests.Mocks`.
- `internal abstract class EcbOp` with `abstract void Apply(EntityRepository repo)`.
- All sealed `EcbOp` subclasses per Test Harness DD §4.4: `EcbOp_CreateEntityRecord` (playback no-op), `EcbOp_DestroyEntity`, `EcbOp_AddComponentUnmanaged<T>`, `EcbOp_AddEmptyComponentUnmanaged<T>`, `EcbOp_RemoveComponentUnmanaged<T>`, `EcbOp_SetComponentUnmanaged<T>`, `EcbOp_AddComponentManaged<T>`, `EcbOp_RemoveComponentManaged<T>`, `EcbOp_SetSingletonUnmanaged<T>`, `EcbOp_PublishEventUnmanaged<T>`.
- `CreateEntity()`: calls `_repo.CreateEntity()` immediately (QCB-1), records `EcbOp_CreateEntityRecord`, returns the real `Entity` handle.
- All `IEntityCommandBuffer` methods queuing the corresponding `EcbOp` subclass.
- `AddEmptyComponent<T>(Entity)` -- includes this even if the engine's `IEntityCommandBuffer` interface does not yet carry it, per Test Harness DD §4.7 note about forward compatibility.
- `internal void Playback()`: iterates `_ops` in insertion order, calls `Apply` on each, then clears the list.
- `internal IReadOnlyList<EcbOp> OpsForInspection` and `internal int OpCount`.
- xUnit test class `MockEntityCommandBufferContractTests` containing the four contract tests from Test Harness DD §4.10.

What is NOT included:

- Parallel or concurrent playback.
- Deterministic reordering of ops (insertion order is the playback order, always).
- Per-tick stable event list tracking (that belongs to `MockSimulationView`).
- Phase-violation enforcement beyond what the `IEntityCommandBuffer` interface shape naturally prevents.

### Constraints

- Every `Apply` implementation that targets an `Entity` must guard with `repo.IsAlive(Entity)` before calling any repo mutation, per Test Harness DD §4.4.
- `EcbOp_SetComponentUnmanaged<T>.Apply` must additionally check `repo.HasComponent<T>(Entity)` before the write.
- `EcbOp_AddEmptyComponentUnmanaged<T>.Apply` must call `repo.AddComponent(entity, default(T))` to match engine semantics (not a manual zero-fill).
- `_ops` must be `List<EcbOp>` -- insertion order equals playback order exactly; no sorting, no deduplication.
- After `Playback()` completes, `_ops` must be cleared; `OpCount` must be zero.

### Success Conditions

- SC1: `var e = ecb.CreateEntity()`. Assert `ecb.OpCount == 1`. Assert `repo.IsAlive(e) == true`. Assert `repo.HasComponent<TestComponent>(e) == false` (no component queued yet).
- SC2: Create entity `e`. Call `ecb.AddComponent(e, new TestComponent { Value = 7 })`. Assert `ecb.OpCount == 2`. Assert `repo.HasComponent<TestComponent>(e) == false`. Call `ecb.Playback()`. Assert `ecb.OpCount == 0`. Assert `repo.HasComponent<TestComponent>(e) == true`. Assert `repo.GetComponentRO<TestComponent>(e).Value == 7`.
- SC3: Create entity `e`. Call `ecb.AddEmptyComponent<TestBigComponent>(e)`. Call `ecb.Playback()`. Assert `repo.HasComponent<TestBigComponent>(e) == true`. Read the component and assert all its bytes are zero (use `MemoryMarshal.AsBytes` or an `unsafe` fixed pointer).
- SC4: Create entity `e` via `repo.CreateEntity()`. Call `ecb.DestroyEntity(e)`. Assert `repo.IsAlive(e) == true`. Call `ecb.Playback()`. Assert `repo.IsAlive(e) == false`.
- SC5: Add entity `e` to the world directly. Queue `ecb.SetComponent(e, new TestComponent { Value = 1 })`, then `{ Value = 2 }`, then `{ Value = 3 }`. Call `ecb.Playback()`. Assert `repo.GetComponentRO<TestComponent>(e).Value == 3` (last write wins via insertion-order playback).
- SC6: `dotnet test --filter "FullyQualifiedName~MockEntityCommandBufferContractTests"` reports all four tests from Test Harness DD §4.10 passed, zero failed.

---

## TASK-TH-003 -- BlueprintTestFixture Core Infrastructure

**Phase:** 1 -- Test Harness
**Design Reference:** [Test Harness DD §2](./Blueprint_Subsystem_Test_Harness_Detailed_Design.md#2-fixture-architecture), [Test Harness DD §5](./Blueprint_Subsystem_Test_Harness_Detailed_Design.md#5-blueprinttestfixture--the-per-test-umbrella), [Architecture v1.2](./Blueprint_Subsystem_Architecture_v1.2.md)
**Effort:** 2-3 days

### Scope

What IS included:

- `BlueprintTestFixture : IDisposable` in namespace `Hrot.Blueprints.Tests` with all public properties from Test Harness DD §2.4: `World`, `View`, `Ecb`, `Registry`, `TickSystem`, `MaintenanceSystem`, `Compiler`, `DebugSession`.
- `BlueprintTestFixtureOptions` record with properties and defaults per Test Harness DD §2.4 and §7.4: `VerifyAlcUnloadOnDispose = true`, `GcReclaimRetries = 3`, `GcReclaimDelayMs = 50`, `VerboseLeakDiagnostics = false`.
- Constructor: instantiates `EntityRepository`, `MockSimulationView(World)`, `MockEntityCommandBuffer(World)`, `BlueprintRegistry`, `CapturingDebugSession`, `BlueprintTickSystem(Registry)`, `BlueprintMaintenanceSystem()`, `BlueprintCompiler()`; wires `DebugProbe.Sink = DebugSession`.
- `CompileAndLoad(BlueprintAsset, CompilerMode)` delegating to `CompileAndLoadMany`.
- `CompileAndLoadMany(IReadOnlyList<BlueprintAsset>, CompilerMode)` per Test Harness DD §5.1: compile each asset via `Compiler.Compile`, accumulate generated source, invoke `InMemoryRoslynCompiler`, load into a new collectible `AssemblyLoadContext`, append to `_activeAlcs` and `_alcWeakRefs`, discover `[BlueprintRegistrar]` types and invoke them via `BeginStaging`/`CommitStaging`, throw `BlueprintCompileException` with diagnostic detail on compile failure.
- `SimulateReload(IReadOnlyList<BlueprintAsset>)` per Test Harness DD §5.2: capture old ALCs, compile+load new versions, commit, then unload old ALCs.
- `TickFrame(float deltaTime)` with system execution order per Test Harness DD §5.3: (1) `Ecb.Playback()`, (2) `View.AdvanceTime(dt)`, (3) `View.BeginTick(SnapshotPendingEvents()); _pendingEvents.Clear()`, (4) `TickSystem.Execute(View)`, (5) each registered aux system in order, (6) `MaintenanceSystem.Execute(View)`, (7) `_tickActions?.Invoke(View, Ecb)`.
- `PublishEventForNextTick<T>(T evt)` adding to `_pendingEvents` per Test Harness DD §3.6 Pattern B.
- `RegisterTickAction(Action<ISimulationView, IEntityCommandBuffer>)` appending to the `_tickActions` multicast delegate.
- `AddSimulationSystem(IEcsModuleSystem)` appending to `_auxSimulationSystems`.
- Slot inspection helpers per Test Harness DD §5.4: `HasSlot(BlueprintAsset, Entity)`, `GetSlotEntry(BlueprintAsset, Entity)`, `GetBlueprintState(BlueprintAsset, Entity)`, all delegating to `TryGetSlotAcrossTiers` which checks all three blackboard tiers (`BlueprintBlackboard1024`, `BlueprintBlackboard4096`, `BlueprintBlackboard16384`).
- `BlueprintStateView` helper class with `GetField<T>(string fieldName)` (reads from `_def.StateFields` by name), `GetCursor()` returning `GetField<BlueprintLatentCursor>("Cursor")`, and `int StateSize`.
- `SnapshotAllBlackboards()` returning `ImmutableArray<byte>` per Test Harness DD §5.7.
- `AttachBlueprint(BlueprintAsset, Entity)` per Test Harness DD §5.5 including `ChooseTier(int stateSize)` (<=928 -> B1024, <=3936 -> B4096, else B16384), `EnsureTierComponent(Entity, BlackboardTier)` with header-magic initialization, and `BlueprintBlackboardPartitions.TryAttach` call followed by `def.InitDefault` invocation.
- `SetChannelStatus<TChannel>(Entity, NodeStatus)` per Test Harness DD §5.6.
- `ForceGcReclaim()` helper: three retry iterations of `GC.Collect / GC.WaitForPendingFinalizers / GC.Collect` with `Thread.Sleep(20)` between.
- `GetAlcWeakReferences()` returning `IReadOnlyList<WeakReference<AssemblyLoadContext>>` backed by `_alcWeakRefs`.
- `private SnapshotPendingEvents()` producing the `IReadOnlyDictionary<Type, IReadOnlyList<object>>` passed to `View.BeginTick`.

What is NOT included:

- The GC-reclaim verification loop inside `Dispose()` -- that is TASK-TH-005 scope.
- `CapturingDebugSession` implementation -- that is a separate task (Test Harness DD §10).
- `InMemoryRoslynCompiler` -- that belongs to the Compiler DD.
- `BlueprintBlackboardPartitions` implementation -- that belongs to the Runtime DD.
- Any test cases -- this task produces the infrastructure class only.

### Constraints

- The `TickFrame` system execution order must exactly match the order listed above; any deviation is a phase-rule defect that will surface as incorrect ECS mutation timing in tests.
- `CompileAndLoad` must throw `BlueprintCompileException` (not swallow or return null) when the compiler emits any diagnostics with severity Error.
- `SimulateReload` must capture old ALCs before calling `CompileAndLoadMany`, and unload them only after the staging commit completes -- not before, to avoid a window where no valid delegates are registered.
- The fixture must be usable without ever calling `CompileAndLoad` (for pure ECS entity tests with no Blueprint code); all properties must be initialized in the constructor.
- `GetAlcWeakReferences()` must return a view over the live `_alcWeakRefs` field (not a copy), so callers can observe GC reclaim as it happens.

### Success Conditions

- SC1: `new BlueprintTestFixture()` completes without error. Assert `fixture.World != null`, `fixture.View != null`, `fixture.Ecb != null`, `fixture.Registry != null`, `fixture.TickSystem != null`, `fixture.MaintenanceSystem != null`, `fixture.Compiler != null`, `fixture.DebugSession != null`.
- SC2: Call `fixture.PublishEventForNextTick(new TestEvent { Value = 5 })`. Register a tick action that captures `view.ReadEvents<TestEvent>()`. Call `fixture.TickFrame(0.016f)`. Assert the captured list has count 1 and `[0].Value == 5`. Call `fixture.TickFrame(0.016f)` again (no new publish). Assert the captured list in the second tick has count 0.
- SC3: Create entity `e = fixture.World.CreateEntity()`. Call `fixture.Ecb.AddComponent(e, new TestComponent { Value = 99 })`. Call `fixture.TickFrame(0.016f)`. Assert `fixture.View.HasComponent<TestComponent>(e) == true` and `fixture.View.GetComponentRO<TestComponent>(e).Value == 99` (ECB was played back at start of TickFrame).
- SC4: After `fixture.CompileAndLoad(libraryAsset)`, assert `fixture.GetAlcWeakReferences().Count == 1` and the single weak reference has a live target.
- SC5: Assert `ChooseTier(928) == BlackboardTier.B1024`, `ChooseTier(929) == BlackboardTier.B4096`, `ChooseTier(3936) == BlackboardTier.B4096`, `ChooseTier(3937) == BlackboardTier.B16384`.
- SC6: For an Instance-dispatch asset that has been compiled and loaded, call `fixture.AttachBlueprint(asset, entity)`. Assert `fixture.HasSlot(asset, entity) == true`. Call `fixture.GetBlueprintState(asset, entity)` -- assert it returns without exception and `StateSize > 0`.

---

## TASK-TH-004 -- BlueprintAssetBuilder Fluent API

**Phase:** 1 -- Test Harness
**Design Reference:** [Test Harness DD §6](./Blueprint_Subsystem_Test_Harness_Detailed_Design.md#6-blueprintassetbuilder--fluent-test-asset-construction), [Architecture v1.2 §5](./Blueprint_Subsystem_Architecture_v1.2.md#5-asset-schema)
**Effort:** 1 day

### Scope

What IS included:

- `BlueprintAssetBuilder` in namespace `Hrot.Blueprints.Tests.Builders` with static factory methods `Library(string)`, `AiPrimitive(string)`, `Instance(string)` per Test Harness DD §6.2.
- Fluent methods on `BlueprintAssetBuilder`: `WithAssetId`, `WithTierHint`, `WithWorldSingleton`, `WithIntent`, `WithHostings`, `WithParameter`, `WithWorkingStateField`, `WithVariable`, `WithCallablePeer`, `WithCustomEvent`, `WithGraph(string, Action<GraphBuilder>)`, `WithGraph(string, GraphKind, Action<GraphBuilder>)`, `WithEventGraph`.
- `Build()` producing a fully-populated `BlueprintAsset`; all list fields must be non-null (empty lists if unused); `Header.SubsystemType = "Hrot.Blueprints"` and `Header.SchemaVersion = "1.0"`.
- `private Guid NewSyntheticGuid(params object[])` using SHA256 over `_assetId` bytes plus UTF-8 string representations of parts, taking the first 16 bytes as a `Guid`. Deterministic for the same inputs.
- `GraphBuilder` in the same namespace with: constructor taking `(string name, GraphKind kind, Guid assetId)`, `Entry()`, `Return(NodeStatus)`, `Delay(float)`, `ChannelCommand(string channelType, string actionId, Action<NodeBuilder>)`, `WaitForChannel(string channelType)`, `SetVariable(string variableName, string valueExpression)`, `Branch(string conditionExpression, Action<GraphBuilder> trueBranch, Action<GraphBuilder> falseBranch)`, `Build()` producing a `Graph`.
- Automatic exec-wire chaining in `GraphBuilder`: each method that adds a node auto-wires the previous node's exec-out pin to the new node's exec-in pin via an internal `LinkExec` helper.
- `NodeBuilder` helper for attaching additional data pins to nodes like `ChannelCommandNode` inside the `ChannelCommand` callback.
- `SyntheticGuidHelper.Compute(Guid assetId, Guid graphId, params object[] parts)` static utility used by `GraphBuilder` for deterministic pin and node IDs.

What is NOT included:

- Builder coverage of every possible node type beyond those listed in §6.3.
- `WithEventDispatcher` builder method (not documented in §6).
- Asset validation via the compiler (the builder is a pure construction utility).
- JSON serialization or deserialization (the builder produces `BlueprintAsset` objects only).

### Constraints

- `AiPrimitive(string)` factory must pre-initialize `_primitive` with `Intent = Action` and an empty `Hostings` list, per Test Harness DD §6.2.
- `WithIntent` and `WithHostings` must throw `InvalidOperationException` (with a clear message) when called on a non-AiPrimitive builder (`_primitive is null`).
- `NewSyntheticGuid` must be deterministic: calling the same builder sequence twice must produce assets with identical field GUIDs -- verified by double-build comparison.
- `GraphBuilder.LinkExec` must silently do nothing when `fromNode == Guid.Empty` (no predecessor yet), to handle the first node in a graph without a guard at every call site.
- `WithCallablePeer` records the peer builder's `_assetId` (not the peer builder itself) -- there is no retained reference to the peer builder after `WithCallablePeer` returns.

### Success Conditions

- SC1: `BlueprintAssetBuilder.Library("Foo").Build()` produces `Dispatch == BlueprintDispatchKind.Library`, `Name == "Foo"`, `Graphs.Count == 0`, and all list fields (`Variables`, `Parameters`, `CustomEvents`, `EventDispatchers`, `CallablePeers`, `WorkingState`) are non-null and empty.
- SC2: Build an AiPrimitive asset with `WithIntent(Condition)`, `WithHostings(BTreeCondition)`, `WithParameter("Threshold", typeof(float))`, `WithWorkingStateField("Phase", typeof(int))`, and `WithGraph("Main", g => g.Entry().Return(NodeStatus.Success))`. Assert `Dispatch == AiPrimitive`, `Primitive.Intent == Condition`, `Primitive.Hostings.Count == 1`, `Parameters.Count == 1`, `WorkingState.Count == 1`, `Graphs.Count == 1`, `Graphs[0].Nodes.Count == 2`, `Graphs[0].Links.Count == 1`.
- SC3: Call the same builder sequence twice (same asset name, same parameters). Serialize both results via `BlueprintJsonServices.Serialize`. Assert the two JSON strings are identical (determinism of `NewSyntheticGuid`).
- SC4: Build an Instance asset with `WithVariable("HP", typeof(int))` and `WithCustomEvent("OnHit", ("Damage", typeof(int)))`. Assert `Variables.Count == 1`, `CustomEvents.Count == 1`, `CustomEvents[0].Parameters.Count == 1`.
- SC5: `Assert.Throws<InvalidOperationException>(() => BlueprintAssetBuilder.Library("L").WithIntent(AiPrimitiveIntent.Condition))`. `Assert.Throws<InvalidOperationException>(() => BlueprintAssetBuilder.Instance("I").WithHostings(AiPrimitiveHosting.BTreeCondition))`.
- SC6: Build a graph via `WithGraph("G", g => g.Entry().Delay(2.0f).Return(NodeStatus.Success))`. Assert `Graphs[0].Nodes.Count == 3` (EventEntryNode, LatentDelayNode, ReturnNode). Assert `Graphs[0].Links.Count == 2`. Assert the first link's `From.NodeId == entryNode.Id` and `To.NodeId == delayNode.Id`.

---

## TASK-TH-005 -- ALC Lifecycle and Unload Verification

**Phase:** 1 -- Test Harness
**Design Reference:** [Test Harness DD §7](./Blueprint_Subsystem_Test_Harness_Detailed_Design.md#7-alc-lifecycle-and-unload-verification), [Test Harness DD §2.6](./Blueprint_Subsystem_Test_Harness_Detailed_Design.md#26-disposal-contract), [Test Harness DD §2.7](./Blueprint_Subsystem_Test_Harness_Detailed_Design.md#27-what-alc-reclaimed-actually-means)
**Effort:** 1 day

### Scope

What IS included:

- `Dispose()` on `BlueprintTestFixture` per Test Harness DD §2.6: (1) call `Unload()` on each ALC in `_activeAlcs` and clear the list; (2) if `options.VerifyAlcUnloadOnDispose` is true, run the retry loop -- per retry: `GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();` then check `AllAlcsReclaimed()`, then `Thread.Sleep(GcReclaimDelayMs)` if not yet reclaimed; (3) after exhausting retries, if any `WeakReference.TryGetTarget` still returns true, throw `InvalidOperationException` with a message including the leaked ALC count and the "Common causes" guidance text from §2.6.
- `private bool TryReclaimAllAlcs(int maxRetries, int delayMs)` per Test Harness DD §7.3.
- `private bool AllAlcsReclaimed()` iterating `_alcWeakRefs`.
- `VerboseLeakDiagnostics` option honored: when `true`, the disposal path may attempt best-effort static-field enumeration for diagnostic purposes; a no-op stub is acceptable for Slice 1 as long as the flag exists and is read.
- xUnit test class `AlcUnloadTests` in `Hrot.Blueprints.Tests` containing three tests from Test Harness DD §7.5:
  - `Fixture_DisposeAfterCompileAndLoad_ReclaimsAlc`: load a minimal Library asset; capture `alcRef = fixture.GetAlcWeakReferences().Single()` while the fixture is live; call `fixture.Dispose()`; then run the GC retry loop externally and assert `alcRef.TryGetTarget(out _) == false`.
  - `Fixture_AfterMultipleReloads_AllOldAlcsReclaimed`: load v1, reload to v2, reload to v3; call `fixture.ForceGcReclaim()`; assert the first two weak references no longer have live targets; assert the third (v3, the active ALC) still has a live target.
  - `Fixture_LeakedDelegate_DetectsAndThrows`: deliberately capture a delegate into the reloadable ALC; assert `Assert.Throws<InvalidOperationException>(() => fixture.Dispose())` fires; then set the delegate to null and call `fixture.Dispose()` again via a try/finally cleanup path.

What is NOT included:

- `VerboseLeakDiagnostics` full static-field enumeration implementation (stub is sufficient per §7.4).
- Multi-ALC simultaneous-load tests (those belong to Runtime or HotReload DD test suites).
- Debugger-attachment interaction (test-runner CI environment only; noted in §7.2 but not testable in automated CI).

### Constraints

- The GC retry loop must issue three separate calls per iteration -- `GC.Collect()`, `GC.WaitForPendingFinalizers()`, `GC.Collect()` -- not one; per Test Harness DD §7.3 rationale (first collect finds unreachable objects, second finalizes them, third collects finalized).
- `Dispose()` must not throw if `_alcWeakRefs` is empty (no ALCs were loaded); `AllAlcsReclaimed()` must return `true` immediately in that case.
- With `VerifyAlcUnloadOnDispose = false`, `Dispose()` must only call `Unload()` on each ALC and clear the list; it must not call `GC.Collect` or `Thread.Sleep` or inspect any `WeakReference`.
- The `Fixture_LeakedDelegate_DetectsAndThrows` test must null out the captured delegate before the cleanup `Dispose()` call so the deliberately-created leak does not persist into the test runner's process.
- The `InvalidOperationException` thrown by `Dispose()` must include the count of un-reclaimed ALCs in its message and must not silently succeed with a partial unload.

### Success Conditions

- SC1: `new BlueprintTestFixture(new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false }).Dispose()` (no assets loaded) completes without exception in under 10 ms.
- SC2: Load a minimal Library asset. Capture `var alcRef = fixture.GetAlcWeakReferences().Single()`. Call `fixture.Dispose()`. Run `GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect()` in the test body. Assert `alcRef.TryGetTarget(out _) == false`.
- SC3: Load v1, call `fixture.SimulateReload(new[]{v2})`, call `fixture.SimulateReload(new[]{v3})`. Call `fixture.ForceGcReclaim()`. Assert `fixture.GetAlcWeakReferences()[0].TryGetTarget(out _) == false` (v1 ALC reclaimed). Assert `fixture.GetAlcWeakReferences()[1].TryGetTarget(out _) == false` (v2 ALC reclaimed). Assert `fixture.GetAlcWeakReferences()[2].TryGetTarget(out _) == true` (v3 ALC still active).
- SC4: `dotnet test --filter "FullyQualifiedName~AlcUnloadTests"` reports all three tests from Test Harness DD §7.5 passed, zero failed.
- SC5: Verify the error message thrown in the leak scenario contains the string `"ALC(s) not GC-reclaimed"` (or equivalent text referencing the count and cause guidance); the exact phrasing must match what a developer would see in a failing test output.

---

## TASK-TH-006 -- TickFrame Refinements (Patches 1 + 2 Applied)

**Phase:** 1 -- Test Harness
**Design Reference:** Test Harness DD Inline Patches, Patches 1 and 2, correcting �3 and �5 of the main TH-DD.
**Effort:** 1 day

This task corrects TASK-TH-001 and TASK-TH-003 to match the inline patches.

### Scope

What IS included:

- Remove `_eventStreamsByType` field and `BeginTick(IReadOnlyDictionary<...>)` method from `MockSimulationView`. `ReadEvents<T>()` now delegates directly to `_repo.Bus.Read<T>()`.
- Remove `PublishEventForNextTick<T>` and `SnapshotPendingEvents` from `BlueprintTestFixture`. Remove `_pendingEvents` field.
- Correct `BlueprintTestFixture.TickFrame` execution order to: (1) `_repo.Bus.SwapBuffers()`, (2) `View.AdvanceTime(dt)`, (3) `TickSystem.Execute(View)`, (4) each aux system, (5) `MaintenanceSystem.Execute(View)`, (6) `Ecb.Playback(_repo)`, (7) `_tickActions?.Invoke(View, Ecb)`.
- Update contract test `ReadEvents_SameListThroughoutTick` (from �8.3) to use the native bus (Patch 1's updated test body -- publish via `fixture.World.Bus.Publish(evt); fixture.World.Bus.SwapBuffers()`).
- Update contract test `IsAlive_AfterEcbDestroy_RemainsTrueUntilPlayback` (from �8.3) to check entity alive BEFORE TickFrame (not mid-frame since ECB now plays back at end).

What is NOT included:

- Any logic beyond correcting TH-001 and TH-003 for Patches 1 and 2.
- New features beyond those specified in the patches.

### Constraints

- After this task, no test may call `view.BeginTick(...)` or `fixture.PublishEventForNextTick(...)` -- those methods must not exist.
- The TickFrame order must exactly match the patched order above.
- All existing tests that were passing before TH-006 must still pass.

### Success Conditions

- SC1: `MockSimulationView` has no `BeginTick` method and no `_eventStreamsByType` field. `ReadEvents<T>()` is a one-liner delegating to `_repo.Bus.Read<T>()`.
- SC2: `BlueprintTestFixture` has no `PublishEventForNextTick` method. Verify by checking test code must use `fixture.Ecb.PublishEvent(evt)` to inject events.
- SC3: Publish `new TestEvent { Value = 1 }` via `fixture.World.Bus.Publish(...)`, call `fixture.World.Bus.SwapBuffers()`. Register a tick action that reads `view.ReadEvents<TestEvent>()`. Call `fixture.TickFrame(0.016f)`. Assert the list count is 1 inside the tick action.
- SC4: `fixture.Ecb.DestroyEntity(e)` before TickFrame -- entity is alive. After `fixture.TickFrame(0.016f)` -- entity is gone (ECB played back at END of TickFrame).
- SC5: `fixture.Ecb.AddComponent(e, new TestComponent { Value = 7 })`. Assert `HasComponent` is false before TickFrame. After TickFrame, assert value is 7.
- SC6: `dotnet test` all prior TH-001 through TH-005 tests still pass after this refactoring.

---

## TASK-TH-007 -- Mock Contract Tests (�8)

**Phase:** 1 -- Test Harness
**Design Reference:** [Test Harness DD �8](./Blueprint_Subsystem_Test_Harness_Detailed_Design.md#8-the-mock-contract-enforcement-matrix) with corrections from inline patches applied per TASK-TH-006.
**Effort:** 0.5 days

### Scope

What IS included:

- xUnit test class `MockContractTests` in `Hrot.Blueprints.Tests.Mocks` containing the 8 contract tests from TH-DD �8.3.
- All 8 tests: `IsAlive_AfterEcbDestroy_RemainsTrueUntilPlayback`, `GetComponentRO_ReturnsRefIntoChunkMemory`, `ReadEvents_SameListThroughoutTick` (patched version using `FdpEventBus`), `MockView_DoesNotExposeDirectSingletonSetter`, `Playback_PreservesInsertionOrder`, `TierUpgrade_HappensInBeforeSync_NotInSimulation`, `AddEmptyComponent_LargeUnmanaged_DefaultInitsAfterPlayback`, `CreateEntity_ReturnsRealHandleImmediately`.
- In-file or shared test-only `[StructLayout(LayoutKind.Sequential)] internal struct TestComponent { public int Value; }` and `TestEvent { public int Value; }`.

What is NOT included:

- Tests for compiler, runtime, or hot reload.
- Performance benchmarks.

### Constraints

- Each test must use `using var fixture = new BlueprintTestFixture()`.
- `ReadEvents_SameListThroughoutTick` uses the patched version (native `FdpEventBus`, no `BeginTick`).
- The exact test body from TH-DD �8.3 is the reference; minor adaptations for Patch 1/2 corrections are expected.

### Success Conditions

- SC1: `dotnet test --filter "FullyQualifiedName~MockContractTests"` reports all 8 tests passed, zero failed, zero skipped.
- SC2: `GetComponentRO_ReturnsRefIntoChunkMemory` -- write value 99 via the repo's RW path after first read; assert the first-read ref reflects the new value (same chunk memory, not a copy).
- SC3: `Playback_PreservesInsertionOrder` -- queue 3 sequential SetComponent ops with values 1, 2, 3; after TickFrame, value is 3.
- SC4: `TierUpgrade_HappensInBeforeSync_NotInSimulation` -- after one TickFrame, B1024 component is removed and B4096 is present.
- SC5: `MockView_DoesNotExposeDirectSingletonSetter` -- reflection finds zero methods named "SetSingleton" on `MockSimulationView`.

---

## TASK-TH-008 -- CapturingDebugSession (�10)

**Phase:** 1 -- Test Harness
**Design Reference:** [Test Harness DD �10](./Blueprint_Subsystem_Test_Harness_Detailed_Design.md#10-capturing-debug-session)
**Effort:** 1 day

### Scope

What IS included:

- `CapturingDebugSession : IBlueprintProbeSink, IBlueprintDebugSession` in namespace `Hrot.Blueprints.Tests` exactly as specified in �10.3.
- Records: `NodeEnterRecord(Entity Self, string NodeId, float Time)`, `PinValueRecord(Entity Self, string PinId, object Value)`, `BreakpointKey(string NodeId)`.
- `BreakpointHit` record with at least `Entity Self` property.
- Events `OnBreakpointHit: Action<BreakpointHit>?`, `OnNodeExecuted: Action<NodeExecuted>?`, `OnPinValueChanged: Action<PinValueChanged>?` (use minimal placeholder records if not yet defined elsewhere).
- All inspection helpers: `Hit(string nodeId)`, `HitCount(string nodeId)`, `HitsFor(Entity self)`.
- Stub `Continue()`, `StepOver()`, `StepInto()`, `StepOut()` implementations.
- `IBlueprintDebugSession` surface: `SetBreakpoint`, `ClearBreakpoint`, `IsAnyBreakpointActive`, `IsAnyWatchActive`.
- `BlueprintTestFixture.DebugSession` property wired as `CapturingDebugSession`; `DebugProbe.Sink = DebugSession` set in constructor.
- Two usage-pattern tests from �10.4 (`Debug_TraceMode_RecordsAllNodeEntries`, `Debug_Breakpoint_FiresWhenNodeEntered`) tagged `[Trait("Category", "RequiresCompiler")]` and marked with `Skip = "Requires Phase 3 compiler"`.

What is NOT included:

- Actual pause/suspend simulation logic.
- Source-location resolution via DebugMap.
- Conditional breakpoints.
- Full `IBlueprintDebugSession` implementation from Debug Protocol DD.

### Constraints

- Must implement full `IBlueprintProbeSink`: `OnNodeEnter(Entity, string)` and `OnPinValueChanged<T>(Entity, string, T)`.
- `OnNodeEnter` must check breakpoints and fire `OnBreakpointHit` event if matched.
- The two pattern tests must be skipped in CI until Phase 3 compiler exists.

### Success Conditions

- SC1: `CapturingDebugSession` compiles implementing both `IBlueprintProbeSink` and `IBlueprintDebugSession`.
- SC2: `DebugProbe.Sink = session; DebugProbe.NodeEnter(someEntity, "n-001")`. Assert `session.Hit("n-001") == true`, `session.HitCount("n-001") == 1`.
- SC3: Set breakpoint, simulate `DebugProbe.NodeEnter(entity, nodeId.ToString())`. Assert `OnBreakpointHit` event raised once.
- SC4: `session.IsAnyBreakpointActive == true` after setting; `false` after clearing.
- SC5: Multiple `DebugProbe.OnPinValueChanged` calls accumulate in `session.PinValues`.
- SC6: `fixture.DebugSession != null` and `DebugProbe.Sink` references same instance.

---

## TASK-TH-009 -- TestData Infrastructure (�11)

**Phase:** 1 -- Test Harness
**Design Reference:** [Test Harness DD �11](./Blueprint_Subsystem_Test_Harness_Detailed_Design.md#11-test-data-infrastructure)
**Effort:** 1 day

### Scope

What IS included:

- `Hrot.Blueprints.Tests/TestAssets/` directory with 9 hand-written valid `.bp.json` files: `LibraryMath.bp.json`, `InstanceCounter.bp.json`, `InstanceCounterV1ModifiedBody.bp.json`, `InstanceCounterV2WithBonus.bp.json`, `HealthRegen.bp.json`, `HasVisibleTarget.bp.json`, `MoveToAndFire.bp.json`, `DoorActor.bp.json`, `DoorSensor.bp.json`.
- `TestAssets/Invalid/` subdirectory with: `ConditionWithRunning.bp.json`, `ConditionWithDelay.bp.json`, `AiPrimitiveParamsTooLarge.bp.json`, `InstanceStateExceedsLargestTier.bp.json`.
- `Hrot.Blueprints.Tests/Snapshots/` directory with empty subdirectories: `Schedule/`, `Emit/`, `DebugMap/`.
- `TestData` static class in `Hrot.Blueprints.Tests` with `LoadAsset(string)`, `LoadSnapshot(string)`, `SampleAssets` constants class, `ReadOrRegenerateSnapshot(string, string)` helper (writes when `BLUEPRINT_REGENERATE_SNAPSHOTS=1`, compares otherwise), and `ResolveTestAssetsDir()` walk-up helper.
- `TestEventDefinitions.cs` with `HitEvent { Entity Target, Entity Attacker, float Damage, Vector3 Direction }` and any other Slice 1 demo event structs.
- `.csproj` updates: `<Content Include="TestAssets\**\*" CopyToOutputDirectory="PreserveNewest" />` and same for `Snapshots\`.
- `[Theory] [MemberData] SampleAssetLoadTests` that calls `TestData.LoadAsset(name)` for all 9 sample names and asserts no exception.

What is NOT included:

- Filling out `Snapshots/` with actual content (Phase 3 Compiler tasks do that).
- The `InvokeBTreeAction`/`InvokeHsmAction` helpers (TASK-TH-010).
- Populating `Invalid/` assets beyond minimal valid JSON that is semantically wrong.

### Constraints

- All 9 main sample `.bp.json` files must be syntactically valid per the schema from TASK-P0-002 (parseable by `BlueprintJsonServices.Deserialize`).
- `Invalid/` files must also be syntactically valid JSON -- they are semantically invalid only.
- `MoveToAndFire.bp.json` must have `Dispatch: "AiPrimitive"` with at least one `ChannelCommandNode`.
- `HealthRegen.bp.json` must have `Dispatch: "Instance"` with `CurrentHealth` and `MaxHealth` variables.
- `ResolveTestAssetsDir()` must work in CI (bin/ output dir) and locally.

### Success Conditions

- SC1: `dotnet test --filter "FullyQualifiedName~SampleAssetLoadTests"` all 9 tests pass.
- SC2: `LoadAsset("Invalid/ConditionWithRunning")` returns a non-null `BlueprintAsset` (parses OK, semantically invalid).
- SC3: `TestData.LoadSnapshot("Schedule/LibraryMath.ir.txt")` throws `FileNotFoundException` (snapshot not yet created).
- SC4: All `SampleAssets.*` constants match their filenames exactly.
- SC5: `dotnet build` succeeds; `TestAssets/` and `Snapshots/` directories are present in output.

---

## TASK-TH-010 -- BehaviorRegistry Wiring + InvokeBTree/Hsm Helpers + MockDispatcherSystem (�12 resolutions + Patches 3, Q-12.1 through Q-12.4)

**Phase:** 1 -- Test Harness
**Design Reference:** [Test Harness DD �12](./Blueprint_Subsystem_Test_Harness_Detailed_Design.md#12-open-questions-for-implementation), [Test Harness Inline Patches Q-12.1 through Q-12.4 and Patch 3](./Blueprint_Subsystem_Test_Harness_Detailed_Design_InlinePatches.md)
**Effort:** 2 days

### Scope

What IS included:

- `BehaviorRegistry BehaviorRegistry { get; }` and `HsmActionDispatcher HsmDispatcher { get; }` properties on `BlueprintTestFixture`. Both initialized in constructor. `Dispose()` calls `HsmDispatcher.ClearAll()` BEFORE ALC unload.
- `InvokeRegistrarMethod(MethodInfo method, BlueprintRegistryStaging staging)` helper that resolves registrar parameters by type (BlueprintRegistry, BehaviorRegistry, HsmActionDispatcher) per Q-12.1.
- `InvokeBTreeAction(BlueprintAsset asset, Entity entity, int paramIndex = 0)` per Q-12.2: constructs stack `BTreeContext { World = _repo, Self = entity, Time = View.Time }`, calls BTreeTick thunk (stub throwing `NotImplementedException` until Phase 3 compiler).
- `unsafe InvokeHsmAction(BlueprintAsset asset, Entity entity)` per Patch 3/Q-12.3: uses `_repo.UnmanagedHandle` directly, no `GCHandle.Alloc/Free`.
- `unsafe bool InvokeHsmGuard(BlueprintAsset asset, Entity entity, ushort eventId = 0)`.
- Private helpers `ResolveBTreeTickMethod`, `ResolveHsmActionMethod`, `ResolveHsmGuardMethod` as stubs throwing `NotImplementedException("Requires compiled blueprint assembly")`.
- `MockDispatcherSystem<TChannel> : IEcsModuleSystem, IProfiledSystem` abstract base class in `Hrot.Blueprints.Tests.MockSystems` per Q-12.4 resolution.
- Three concrete dispatchers: `MockLocomotionDispatcher`, `MockWeaponDispatcher`, `MockInteractionDispatcher` -- each with `Func<TChannel, NodeStatus> NextStatus`, `int InvokeCount`, `int LastObservedActionInstanceId`.
- If actual engine channel types don't exist, placeholder structs in `MockSystems/Placeholders.cs` with `// TODO: replace with real engine type` comment.
- xUnit `MockDispatcherSystemTests` with 3 tests: construction, invocation when matching entity exists, status control via NextStatus lambda.

What is NOT included:

- Actual Blueprint compilation or thunk resolution (stubs only for Phase 1).
- Full ECS system integration beyond test needs.

### Constraints

- `HsmDispatcher.ClearAll()` called BEFORE `Unload()` on ALCs in `Dispose()`.
- `MockDispatcherSystem<TChannel>` casts `ISimulationView` to `EntityRepository` for writable ref access.
- `InvokeHsmAction` and `InvokeHsmGuard` must be `unsafe` methods.

### Success Conditions

- SC1: `new BlueprintTestFixture()` -- `fixture.BehaviorRegistry != null`, `fixture.HsmDispatcher != null`.
- SC2: Dispose a fixture with one loaded ALC -- `HsmDispatcher.ClearAll()` was called before unload.
- SC3: Add `MockLocomotionDispatcher`, create entity with `LocomotionChannel { ActiveAction = 1 }`, call `fixture.TickFrame(0.016f)`. Assert `dispatcher.InvokeCount == 1`.
- SC4: `dispatcher.NextStatus = _ => NodeStatus.Running`. After TickFrame, entity's `LocomotionChannel.Status == NodeStatus.Running`.
- SC5: `dotnet test --filter "FullyQualifiedName~MockDispatcherSystemTests"` all 3 tests pass.
- SC6: `dotnet build` succeeds with zero errors.

---

## TASK-RT-001 -- BlueprintRegistry

**Phase:** 2 -- Runtime
**Design Reference:** [Runtime DD §2](./Blueprint_Subsystem_Runtime_Detailed_Design.md#2-blueprintregistry--definition-store-and-lookup), [Runtime DD Inline Patches -- Hot-path Correction 1 and Q-12.4](./Blueprint_Subsystem_Runtime_Detailed_Design_InlinePatches.md)
**Effort:** 1-2 days

### Scope

What IS included:

- `BlueprintRegistry` sealed class in `Fdp.Toolkit.Blueprints` namespace, file `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintRegistry.cs`.
- Private `Snapshot` sealed class with `ById`, `ByName`, `WorldSingletons`, and `WorldSingletonList` fields (the last one per Hot-path Correction 1).
- All public methods from §2.2: `RegisterLibrary`, `RegisterAiPrimitive`, `RegisterInstance`, `TryGetById`, `TryGetByName`, `GetAll`, `RegisterWorldSingleton`, `TryGetWorldSingleton`, `GetAllWorldSingletons`.
- `BeginStaging()` returning a new `BlueprintRegistryStaging`.
- `CommitStaging(BlueprintRegistryStaging)` building a new `Snapshot` via `Interlocked.Exchange` and materializing `WorldSingletonList` (per Hot-path Correction 1).
- `OnRegistryChanged: event Action?` -- fires after every `CommitStaging`.
- `BlueprintRegistryStaging` class (inner or sibling file) with `Definitions` and `WorldSingletons` dictionaries and `Add` / `AddWorldSingleton` methods.
- `GetAllWorldSingletons()` return type must be `IReadOnlyList<(int, BlackboardTier)>` (not `IEnumerable<...>`), returning `_current.WorldSingletonList` directly (zero-allocation).
- Explicit `BlueprintId` collision guard in `RegisterDirect` (throw `InvalidOperationException` with asset names) and in `BlueprintRegistryStaging.Add`.
- `RegisterWorldSingleton` validation: throw if `blueprintId` not yet in `ById`.
- Per §2.4 threading model: `CommitStaging` uses `Interlocked.Exchange(ref _current, next)`.

What is NOT included:

- `EnsureWorldSingletonAttached` or `InitializeWorldSingletonBlueprints` methods (removed per Q-12.4).
- Any tick system code.
- Any hot-reload coordinator code.
- Performance tracing or `IProfiledSystem` on the registry itself.

### Constraints

- Lock-free read pattern: `var snapshot = _current;` then work with that snapshot -- never re-read `_current` mid-operation.
- `CommitStaging` must atomically publish the new snapshot before firing `OnRegistryChanged`.
- The `WorldSingletonList` must be built INSIDE `CommitStaging`, not lazily on first `GetAllWorldSingletons` call.
- Namespace is `Fdp.Toolkit.Blueprints` (with 'k' -- `Fdp.Toolkit`, not `Fdp.Toolkits`) -- confirm by checking existing `Fdp.Toolkits` assembly's namespace conventions.

### Success Conditions

- SC1: Register 3 Blueprints (one Library, one AiPrimitive, one Instance). Assert `TryGetById` returns true for each. Assert `TryGetByName` returns true for each. Assert `GetAll().Count == 3`.
- SC2: `CommitStaging` with 2 Instance Blueprints: `TryGetById` for each returns true immediately after; `TryGetById` for a non-existent ID returns false.
- SC3: `GetAllWorldSingletons()` returns an `IReadOnlyList<...>` (not `IEnumerable`). After `CommitStaging` with 1 world-singleton entry, `GetAllWorldSingletons().Count == 1`. Second call returns the same list reference (no new allocation).
- SC4: Duplicate `blueprintId` in `RegisterDirect` throws `InvalidOperationException` mentioning both asset names. Duplicate in `BlueprintRegistryStaging.Add` also throws.
- SC5: `RegisterWorldSingleton` for unknown `blueprintId` throws `InvalidOperationException`.
- SC6: After two consecutive `CommitStaging` calls (simulating two reloads), `TryGetById` returns only the second staging's entries (first snapshot is discarded).
- SC7: `OnRegistryChanged` fires exactly once per `CommitStaging` call, even if staging has zero entries.

---

## TASK-RT-002 -- BlueprintDefinition, Delegate Types, and BlueprintLatentCursor

**Phase:** 2 -- Runtime
**Design Reference:** [Runtime DD §3](./Blueprint_Subsystem_Runtime_Detailed_Design.md#3-blueprintdefinition-and-delegate-signatures)
**Effort:** 1 day

### Scope

What IS included:

- `BlueprintDefinition` sealed record in `Fdp.Toolkit.Blueprints` namespace with all fields from §3.2: `Name`, `Kind`, `StructureHash`, `StateSize`, `InitDefault`, `Tick`, `EventHandlers`, `StateClrType`, `StateFields`.
- `BlueprintFieldDescriptor` sealed record with `Name, ClrType, OffsetBytes, SizeBytes, CategoryOrEmpty` positional params.
- Three delegate type definitions in the same namespace: `InitDefaultDelegate`, `TickDelegate`, `EventHandlerDelegate` -- signatures exactly as specified in §3.3, including the `uint instanceVersion` parameter on `TickDelegate` (per Q-18.1 of Compiler DD patches) and `float deltaTime` on `EventHandlerDelegate` (per Q-18.3).
- `BlueprintLatentCursor` struct in `FDP/Toolkits/Fdp.Toolkits/Blueprints/BlueprintLatentCursor.cs`: 16 bytes (`[StructLayout(LayoutKind.Sequential, Size = 16)]`), fields `uint ResumeAt` and `float WaitUntilTime` (8 bytes used + 8 bytes reserved padding). It must be `unmanaged`.
- `BlueprintRegistrarAttribute` class: `[AttributeUsage(AttributeTargets.Class, Inherited = false)] public sealed class BlueprintRegistrarAttribute : Attribute`.
- `BlueprintDispatchKind` enum (if not already defined in `Hrot.Blueprints.Core`) with values `Library = 0`, `AiPrimitive = 1`, `Instance = 2`. If it is already defined in `Hrot.Blueprints.Core.Assets`, do NOT redefine it -- just reference it.
- `BlackboardTierHint` and `BlackboardTier` enums (if not already defined from TASK-P0-002): `B1024 = 0`, `B4096 = 1`, `B16384 = 2`. Same rule: reference if already defined.

What is NOT included:

- Any `BlueprintDefinition` instantiation or population (that is the compiler's job via `[BlueprintRegistrar].Register`).
- Any event-handler dispatch logic.
- `BlueprintBlackboard*` components (TASK-RT-003).

### Constraints

- `BlueprintDefinition` must be `sealed record` (not class, not struct) for immutability and value equality.
- All delegates must exactly match §3.3 signatures -- any parameter mismatch will cause a compilation failure when the compiler-generated registrar code is loaded.
- `BlueprintLatentCursor` must be exactly 16 bytes. Verify with `[StructLayout(LayoutKind.Sequential, Size = 16)]` and a `static_assert`-equivalent: `_ = sizeof(BlueprintLatentCursor) == 16 ? 0 : throw new ...;` or a unit test.
- `EventHandlers` default is `new Dictionary<string, EventHandlerDelegate>(StringComparer.Ordinal)` (ordinal comparison, not invariant).
- `StateFields` default is `Array.Empty<BlueprintFieldDescriptor>()`.

### Success Conditions

- SC1: `new BlueprintDefinition { Name="X", Kind=Library, StructureHash=0, StateSize=0 }` compiles with required fields; accessing `def.Tick` returns `null`; `def.EventHandlers.Count == 0`.
- SC2: `typeof(BlueprintLatentCursor).IsValueType == true`. `Unsafe.SizeOf<BlueprintLatentCursor>() == 16`.
- SC3: `typeof(BlueprintLatentCursor).IsUnmanaged()` equivalent -- construct a `Span<BlueprintLatentCursor>` without error (compile-time proof of unmanaged).
- SC4: `BlueprintRegistrarAttribute` can be applied to a class: `[BlueprintRegistrar] public static class TestRegistrar { }` compiles without error.
- SC5: Reflection: `typeof(TickDelegate).GetMethod("Invoke").GetParameters().Length == 7` (stateBytes, view, ecb, self, time, deltaTime, instanceVersion). `typeof(EventHandlerDelegate).GetMethod("Invoke").GetParameters().Length == 8` (stateBytes, view, ecb, self, time, deltaTime, payload).
- SC6: `BlueprintDefinition` structural equality: two definitions with identical fields compare equal via `==`.

---

## TASK-RT-003 -- BlueprintBlackboard Components and Slot-Table Types

**Phase:** 2 -- Runtime
**Design Reference:** [Runtime DD §4](./Blueprint_Subsystem_Runtime_Detailed_Design.md#4-blueprintblackboard-components--layout)
**Effort:** 1-2 days

### Scope

What IS included:

- Three component structs in `Fdp.Toolkit.Blueprints` namespace, files `Components/BlueprintBlackboard1024.cs`, `Components/BlueprintBlackboard4096.cs`, `Components/BlueprintBlackboard16384.cs`:
  - `BlueprintBlackboard1024`: `TotalSize=1024`, `HeaderSize=32`, `MaxSlots=4`, `SlotTableSize=64`, `PayloadStart=96`, `PayloadSize=928`. `public fixed byte Memory[TotalSize]`. `[StructLayout(LayoutKind.Sequential)]` + `[ComponentId(GlobalComponentIds.BlueprintBlackboard1024)]`.
  - `BlueprintBlackboard4096`: analogous, `TotalSize=4096`, `MaxSlots=8`, `SlotTableSize=128`, `PayloadStart=160`, `PayloadSize=3936`.
  - `BlueprintBlackboard16384`: analogous, `TotalSize=16384`, `MaxSlots=16`, `SlotTableSize=256`, `PayloadStart=288`, `PayloadSize=16096`.
- `BlueprintBlackboardHeader` struct (`[StructLayout(LayoutKind.Sequential, Size = 32)]`) with all 9 fields from §4.3: `MagicAndVersion (uint)`, `SlotCount (byte)`, `MaxSlots (byte)`, `FreeListHead (ushort)`, `PayloadStart (ushort)`, `PayloadSize (ushort)`, `PayloadFree (ushort)`, `PayloadHighWater (ushort)`, `Reserved (ulong)`. Magic constant `0x42504257`.
- `BlueprintSlotEntry` struct (`[StructLayout(LayoutKind.Sequential, Size = 16)]`) with fields: `BlueprintId (int)`, `InstanceVersion (uint)`, `PayloadOffset (ushort)`, `PayloadSize (ushort)`, `StructureHash (ulong)`. Constant `BlueprintBlackboardPartitions.SlotEntrySize = 16`.
- `BlueprintFreeBlockHeader` struct (`[StructLayout(LayoutKind.Sequential, Size = 4)]`) with `NextFreeOffset (ushort)` and `Size (ushort)`.
- Registration of the three component IDs in `GlobalComponentIds` (engine-side static class, 3 new int constants: `BlueprintBlackboard1024`, `BlueprintBlackboard4096`, `BlueprintBlackboard16384`).

What is NOT included:

- `BlueprintBlackboardPartitions` allocator implementation (TASK-RT-004).
- Any tick or maintenance system code.
- `BlueprintSlotEntry.InstanceVersion` bump logic -- that is in the allocator.

### Constraints

- All three tier structs must be `unsafe` structs (they use `fixed byte`). Compilation requires `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in the csproj.
- `sizeof(BlueprintBlackboard1024) == 1024`. Enforce with a const-expression: `public const int _SizeCheck = TotalSize == 1024 ? 0 : -1;` or a unit test assertion.
- `sizeof(BlueprintBlackboardHeader) == 32` -- enforce similarly.
- `sizeof(BlueprintSlotEntry) == 16` -- enforce similarly.
- `sizeof(BlueprintFreeBlockHeader) == 4` -- enforce similarly.
- `[ComponentId(...)]` attribute must reference `GlobalComponentIds.BlueprintBlackboard1024` etc., not inline integer literals.
- The `Memory` field covers the ENTIRE component including header and slot table. There are NO separate header or slot-table fields on the struct -- all access is via pointer arithmetic in `BlueprintBlackboardPartitions`.

### Success Conditions

- SC1: `Unsafe.SizeOf<BlueprintBlackboard1024>() == 1024`, `Unsafe.SizeOf<BlueprintBlackboard4096>() == 4096`, `Unsafe.SizeOf<BlueprintBlackboard16384>() == 16384`. All three checked in one `[Fact]`.
- SC2: `Unsafe.SizeOf<BlueprintBlackboardHeader>() == 32`, `Unsafe.SizeOf<BlueprintSlotEntry>() == 16`, `Unsafe.SizeOf<BlueprintFreeBlockHeader>() == 4`.
- SC3: Constant arithmetic: `BlueprintBlackboard1024.PayloadStart == 96` (32 header + 64 slot table). `BlueprintBlackboard1024.PayloadSize == 928` (1024 - 96). Same checks for 4096 and 16384 tiers.
- SC4: `BlueprintBlackboard1024.MaxSlots * BlueprintBlackboardPartitions.SlotEntrySize == BlueprintBlackboard1024.SlotTableSize`.
- SC5: Each struct carries a `[ComponentId]` attribute. Reflection: `typeof(BlueprintBlackboard1024).GetCustomAttribute<ComponentIdAttribute>()?.Id == GlobalComponentIds.BlueprintBlackboard1024`. Verify for all three.
- SC6: `default(BlueprintBlackboard1024)` is zeroed: create one on the stack, pin it, assert first 4 bytes are 0 (magic not set -- uninitialized component).
- SC7: `dotnet build` succeeds with zero errors, including the `unsafe` blocks.

---

## TASK-RT-004 -- BlueprintBlackboardPartitions (Partition Allocator)

**Phase:** 2 -- Runtime
**Design Reference:** [Runtime DD §5](./Blueprint_Subsystem_Runtime_Detailed_Design.md#5-partition-allocator--algorithm-and-api), [Runtime DD §4.6 layout invariants](./Blueprint_Subsystem_Runtime_Detailed_Design.md#46-layout-invariants)
**Effort:** 3-4 days

### Scope

What IS included:

- `BlueprintBlackboardPartitions` static unsafe class in `Fdp.Toolkit.Blueprints` namespace with all public methods from §5.2:
  - `Initialize(byte* memory, int totalSize, byte maxSlots)` -- §5.3 (idempotent)
  - `TryGetSlotOffset(byte* memory, int blueprintId, out int payloadOffset)` -- §5.4 (hot path, linear scan over `SlotCount` not `MaxSlots`)
  - `TryAttach(byte* memory, int blueprintId, int requestedSize, ulong structureHash, out int payloadOffset)` -- §5.5 (free-list-first + bump fallback, 8-byte alignment)
  - `TryDetach(byte* memory, int blueprintId)` -- §5.6 (dense-compact slot table on detach, sorted-insert + coalesce free list)
  - `GetSlotCount(byte* memory)` -- returns `header.SlotCount`
  - `GetSlot(byte* memory, int slotIndex)` -- `ref BlueprintSlotEntry` access
  - `ResetSlot(byte* memory, int slotIndex, ulong newStructureHash)` -- §5.7 (zero payload, update hash, bump `InstanceVersion`)
  - `CopyToLargerTier(byte* src, int srcSize, byte* dst, int dstSize, byte dstMaxSlots)` -- §5.8 (adjust slot `PayloadOffset` by `payloadShift`, copy payload bytes, shift free-list offsets)
- Public constants: `SlotEntrySize = 16`, `FreeBlockHeaderSize = 4`, `Alignment = 8`.
- Private helpers: `TryAllocateFromFreeList`, `BumpAllocate`, `ReturnToFreeList` (sorted insert with coalescing), `AlignUp(int value, int alignment)`, `SumAllocated`.
- Private constant `HeaderMagicV1 = 0x42504257u`.
- 15 test scenarios from §5.10 (all listed, implemented in `Hrot.Blueprints.Tests/Runtime/PartitionAllocator/`).

What is NOT included:

- `BlueprintTickSystem` or `BlueprintMaintenanceSystem` calls (TASK-RT-005/RT-006).
- Any managed heap allocation -- the allocator is entirely pointer-based.
- Defragmentation (Slice 2 concern).

### Constraints

- `TryAttach` rounds `requestedSize` up to `Alignment = 8`. Free blocks are always alignment-sized.
- `TryGetSlotOffset` iterates `0 .. header.SlotCount`, NOT `0 .. header.MaxSlots` -- dense packing invariant.
- `TryDetach` must dense-compact the slot table (move last slot into freed position) AFTER returning the payload to the free list.
- `ReturnToFreeList` must sort by ascending offset and coalesce with both predecessor and successor free blocks.
- `ResetSlot` zeroes payload bytes (`Unsafe.InitBlock`) then updates `slot.StructureHash` and does `slot.InstanceVersion += 1`. Does NOT touch `slot.PayloadOffset`, `slot.PayloadSize`, or `slot.BlueprintId`.
- `CopyToLargerTier`: the payload shift is `dstSlotTableSize - srcSlotTableSize` = `(dstMaxSlots - srcMaxSlots) * SlotEntrySize`. All `PayloadOffset` fields in copied slot entries must be adjusted by this amount.
- On `TryAllocateFromFreeList`, if the remaining block after a split would be smaller than `FreeBlockHeaderSize`, treat it as an exact-fit (no residual block). This prevents orphaned space.
- All methods are `unsafe`; the class-level `unsafe` modifier enables this.

### Success Conditions

- SC1: `Initialize` on a zeroed buffer -> `header.MagicAndVersion == 0x42504257`, `SlotCount == 0`, `PayloadFree == PayloadSize`, `FreeListHead == 0`, `PayloadHighWater == PayloadStart`.
- SC2: Single `TryAttach` -> `payloadOffset == PayloadStart` (bump allocation from start of payload). `SlotCount == 1`. `PayloadFree == PayloadSize - alignedSize`.
- SC3: `TryDetach` middle slot (of 3) -> slot table compacts (slot 2 moves to position 1), free list has 1 block covering released payload.
- SC4: Attach 2 same-size, detach both in reverse -> free list has 1 coalesced block (the two blocks merged).
- SC5: After detach + reattach of same-or-smaller size -> reattached slot gets the freed offset (free-list reuse, not bump advance).
- SC6: `TryAttach` when `SlotCount == MaxSlots` -> returns false.
- SC7: `TryAttach` with `requestedSize > PayloadFree` -> returns false.
- SC8: `ResetSlot(memory, i, newHash)` -> payload zeroed, `slot.StructureHash == newHash`, `slot.InstanceVersion` incremented by 1, `slot.PayloadOffset` unchanged.
- SC9: `CopyToLargerTier(src=1024, dst=4096)` -- all source slot `BlueprintId` values preserved in dest; each dest slot's `PayloadOffset == srcSlot.PayloadOffset + (128-64) = srcSlot.PayloadOffset + 64`; payload bytes copied correctly.
- SC10: All 15 test scenarios from §5.10 pass in `PartitionAllocatorTests.cs`.
- SC11: `LayoutInvariants_HoldAfterEveryOperation` -- runs a 50-step sequence of random attach/detach/reset operations and asserts all 7 invariants from §4.6 after each step.
- SC12: `dotnet build` with zero errors (unsafe code compiles).

---

## TASK-RT-005 -- BlueprintTickSystem + World-Singleton Dispatch

**Phase:** 2 -- Runtime
**Design Reference:** [Runtime DD §6](./Blueprint_Subsystem_Runtime_Detailed_Design.md#6-blueprinttickystem--simulation-phase-ticking), [Runtime DD §8](./Blueprint_Subsystem_Runtime_Detailed_Design.md#8-world-singleton-dispatch), [Runtime DD §9 (reload reconciliation inline)](./Blueprint_Subsystem_Runtime_Detailed_Design.md#9-reload-reconciliation-per-slot-soft--hard), [Runtime DD Patches -- Q-12.1, Q-12.2, Q-12.3, Q-12.4, Hot-path Correction 2](./Blueprint_Subsystem_Runtime_Detailed_Design_InlinePatches.md)
**Effort:** 3-4 days

### Scope

What IS included:

- `BlueprintTickSystem` sealed class in `Fdp.Toolkit.Blueprints.Systems` namespace with `[UpdateInPhase(SystemPhase.Simulation)]`, `[UpdateBefore(typeof(LocomotionDispatcherSystem))]`, `[UpdateBefore(typeof(WeaponDispatcherSystem))]`, `[UpdateBefore(typeof(InteractionDispatcherSystem))]`.
- Constructor `BlueprintTickSystem(BlueprintRegistry registry)`.
- Optional constructor overload `BlueprintTickSystem(BlueprintRegistry registry, IReloadLogSink? logSink = null)` -- defaults to `NullReloadLogSink.Instance`.
- Lazy query fields: `_query1024`, `_query4096`, `_query16384` (IEntityQuery?, initialized via `??=` inside `Execute`).
- `Execute(ISimulationView view)` calling: `TickTier_1024`, `TickTier_4096`, `TickTier_16384`, `TickWorldSingletons`.
- Three per-tier methods `TickTier_1024/4096/16384` using `MemoryMarshal.CreateSpan` pattern (not `fixed`): `ref byte memoryRef = ref Unsafe.As<BlueprintBlackboard1024, byte>(ref bb)`, then `byte* memory = (byte*)Unsafe.AsPointer(ref memoryRef)`.
- Per-slot loop with reload reconciliation: `if (slot.StructureHash != def.StructureHash) { ResetSlot + InitDefault + OnHardReset(sink) }` then `def.Tick(span, view, ecb, entity, view.Time, view.DeltaTime, slot.InstanceVersion)`.
- `TickWorldSingletons(ISimulationView view, IEntityCommandBuffer ecb)` iterating `_registry.GetAllWorldSingletons()` (zero-allocation with pre-materialized list).
- For each world-singleton: lazy init via `EnsureAndTickSingleton<TBB>` (private method) -- checks if singleton component exists, if not creates it; checks if slot exists, if not attaches + runs `InitDefault`; then reconciles hash and ticks. Uses `MemoryMarshal.CreateSpan` pattern.
- Private `FindSlotIndex(byte* slotTable, int slotCount, int blueprintId)` helper.
- `IReloadLogSink` interface and `NullReloadLogSink` (sealed, singleton `Instance` property).
- `IProfiledSystem` implementation: `ProfileName => "BlueprintTickSystem"`.

What is NOT included:

- Per-Blueprint profiling (Slice 2).
- Per-slot try/catch exception sandboxing (Slice 2).
- `BlueprintRegistry.EnsureWorldSingletonAttached` or `InitializeWorldSingletonBlueprints` methods -- these do NOT exist (removed per Q-12.4).
- `BlueprintMaintenanceSystem` (TASK-RT-006).

### Constraints

- Lazy query init via `??=` inside `Execute`, NOT in a constructor or OnAttach callback (Q-12.3).
- `(EntityRepository)view` cast is canonical; no hedging comment (Q-12.2).
- `[UpdateBefore]` on exactly the three confirmed dispatcher names from Q-12.1.
- `MemoryMarshal.CreateSpan(ref Unsafe.Add(ref memoryRef, slot.PayloadOffset), slot.PayloadSize)` -- do NOT use `new Span<byte>(ptr, len)` or `fixed` blocks.
- `GetAllWorldSingletons()` returns `IReadOnlyList<...>` (zero-alloc per Hot-path Correction 1). The `foreach` should use the list directly.
- `TickWorldSingletons` must handle the case where no world-singletons are registered (returns immediately with zero iterations).
- `EnsureAndTickSingleton<TBB>` checks `repo.HasSingleton<TBB>()`, creates with `repo.SetSingletonUnmanaged<TBB>(default)` if absent, then uses `BlueprintBlackboardPartitions.Initialize` if header magic missing, `TryGetSlotOffset` to check for existing slot, `TryAttach` + `InitDefault` if not found.

### Success Conditions

- SC1: Create entity with `BlueprintBlackboard1024`, attach Instance Blueprint. After `fixture.TickFrame(dt)`, verify `def.Tick` was called (using hand-crafted fake delegate that sets a flag). Assert flag is set.
- SC2: Two Instance Blueprints on same entity (both in B1024 tier) -- both `Tick` delegates called in slot-table order within the same frame.
- SC3: Phase ordering test (§11.2) -- `BlueprintTickSystem` runs before `MockLocomotionDispatcher` within the same `TickFrame`. Assert channel command set by Blueprint is visible to the dispatcher in the same frame.
- SC4: Reload reconciliation -- soft: attach Blueprint, tick twice (count=2), reload with same hash, tick once -> count=3 (preserved). Hard: reload with new hash -> count=1 (reset).
- SC5: World-singleton Blueprint registered via `registry.RegisterWorldSingleton(id, BlackboardTier.B1024)`. After `fixture.TickFrame(dt)`, singleton component exists and `TryGetSlotOffset` returns true. Second TickFrame doesn't re-attach.
- SC6: Allocation budget -- after 100 warm-up frames + 1000 steady-state frames with 100 entities x 1 Blueprint each, `GC.GetAllocatedBytesForCurrentThread()` delta is 0 bytes (allocation-free).
- SC7: `IReloadLogSink.OnHardReset` is called exactly once per slot per hard-reset event (structure hash changed).
- SC8: `dotnet build` with zero errors.

---

## TASK-RT-006 -- BlueprintMaintenanceSystem

**Phase:** 2 -- Runtime
**Design Reference:** [Runtime DD §7](./Blueprint_Subsystem_Runtime_Detailed_Design.md#7-blueprintmaintenancesystem--tier-upgrade)
**Effort:** 1-2 days

### Scope

What IS included:

- `BlueprintMaintenanceSystem` sealed class in `Fdp.Toolkit.Blueprints.Systems` with `[UpdateInPhase(SystemPhase.BeforeSync)]`.
- Lazy query fields: `_queryUpgrade1024to4096` and `_queryUpgrade4096to16384` (IEntityQuery?, `??=` in `Execute` per Q-12.3).
- `Execute(ISimulationView view)` calling `UpgradeTier_1024_to_4096(repo)` and `UpgradeTier_4096_to_16384(repo)`.
- `UpgradeTier_1024_to_4096(EntityRepository repo)`: query `.With<BB1024>().With<BB4096>()`, foreach entity: get refs to both components via `GetComponentRW`, call `BlueprintBlackboardPartitions.CopyToLargerTier(src=BB1024, dst=BB4096)`, call `repo.RemoveComponent<BlueprintBlackboard1024>(entity)`. Use `ref byte` + `Unsafe.AsPointer` pattern (no `fixed` blocks, per Hot-path Correction 2).
- `UpgradeTier_4096_to_16384` -- identical structure with BB4096 -> BB16384.
- `IProfiledSystem` implementation: `ProfileName => "BlueprintMaintenanceSystem"`.

What is NOT included:

- Tier downgrade (Slice 2).
- Direct-skip upgrade (1024 -> 16384 in one step is NOT supported; two-frame catch-up instead).
- Any registry interaction.

### Constraints

- `RemoveComponent` during `BeforeSync` is allowed directly (not via ECB) -- structural mutations outside Simulation phase are direct.
- Queries use `??=` lazy init in `Execute`, not constructor/OnAttach.
- The "two component simultaneously present" signal is the ONLY upgrade detection mechanism -- no flags, no extra state.
- `ref byte memoryRef = ref Unsafe.As<BB, byte>(ref bb)` + `(byte*)Unsafe.AsPointer(ref memoryRef)` pattern for both src and dst pointers passed to `CopyToLargerTier`.

### Success Conditions

- SC1: `TierUpgrade_1024Full_TwoFrameMigrationTo4096` test (§11.5): attach 5 Blueprints where 5th overflows BB1024 -> ECB queues AddEmptyComponent<BB4096>. After Frame 1: both components present. After Frame 2: only BB4096, all 5 slots valid.
- SC2: `TierUpgrade_4096_to_16384` -- symmetric test for the upper boundary.
- SC3: Entity with only BB1024 (no BB4096) is NOT touched by `BlueprintMaintenanceSystem`.
- SC4: After tier upgrade, `fixture.GetBlueprintState(asset, entity)` returns the same field values as before upgrade (state preserved through `CopyToLargerTier`).
- SC5: `ReplayDeterminismTests` -- two fixture instances executing identical operation sequences produce byte-identical blackboard snapshots.
- SC6: `ProfileName == "BlueprintMaintenanceSystem"` (constant string).
- SC7: `dotnet build` zero errors.

---

## TASK-RT-007 -- Runtime Test Suite

**Phase:** 2 -- Runtime
**Design Reference:** [Runtime DD §11](./Blueprint_Subsystem_Runtime_Detailed_Design.md#11-runtime-test-strategy)
**Effort:** 2-3 days

### Scope

What IS included:

All test files listed in Runtime DD §11.1, populated with the named tests from §11.2 through §11.6, plus the allocation test from §10.3:

- `Runtime/BlueprintRegistry/RegistrationTests.cs`: RegisterLibrary/AiPrimitive/Instance happy paths.
- `Runtime/BlueprintRegistry/LookupTests.cs`: TryGetById/Name found + not-found.
- `Runtime/BlueprintRegistry/StagingTests.cs`: BeginStaging/CommitStaging, atomicity (TryGetById after CommitStaging).
- `Runtime/BlueprintRegistry/CollisionTests.cs`: duplicate BlueprintId throws.
- `Runtime/BlueprintRegistry/EventTests.cs`: OnRegistryChanged fires after CommitStaging.
- `Runtime/PartitionAllocator/` -- all 15 test files from §5.10 (all already specified in TASK-RT-004 success conditions; the test implementations go here).
- `Runtime/BlueprintTickSystem/PhaseOrderingTests.cs` -- §11.2 test.
- `Runtime/BlueprintTickSystem/SingleSlotTickTests.cs` -- §11.3.
- `Runtime/BlueprintTickSystem/MultiSlotPerEntityTests.cs` -- §11.3.
- `Runtime/BlueprintTickSystem/MultiEntityTickOrderingTests.cs`.
- `Runtime/BlueprintTickSystem/ChannelCommandSameFrameTests.cs`.
- `Runtime/BlueprintTickSystem/WorldSingletonTickTests.cs`.
- `Runtime/BlueprintTickSystem/ReloadReconciliationTests.cs` -- §11.4 (soft + hard).
- `Runtime/BlueprintMaintenanceSystem/TierUpgrade_1024_to_4096_Tests.cs` -- §11.5.
- `Runtime/BlueprintMaintenanceSystem/TierUpgrade_4096_to_16384_Tests.cs`.
- `Runtime/BlueprintMaintenanceSystem/TwoFrameUpgradeTimingTests.cs`.
- `Runtime/BlueprintMaintenanceSystem/ReplayDeterminismTests.cs` -- §11.6.
- `Runtime/AllocationFreeTests.cs` -- §10.3 (tightened budget: 0 bytes/frame).

Tests use `BlueprintTestFixture` from Phase 1. Tests that depend on `CompileAndLoad` must use hand-crafted fake generated code (see PHASES.md Phase 2 guidance) until the compiler is built in Phase 3. The fake class pattern:

```csharp
public static class FakeInstanceBp
{
    public const int BlueprintId = unchecked((int)0xDEADBEEF);
    public const ulong StructureHash = 0x0123456789ABCDEF;
    [StructLayout(LayoutKind.Sequential)]
    public struct State { public BlueprintLatentCursor Cursor; public int TickCount; }
    public static int StateSize => Unsafe.SizeOf<State>();
    public static void InitDefault(Span<byte> bytes) { bytes.Clear(); }
    public static void Tick(Span<byte> bytes, ISimulationView view, IEntityCommandBuffer ecb,
        Entity self, float time, float deltaTime, uint instanceVersion)
    {
        ref var s = ref Unsafe.As<byte, State>(ref MemoryMarshal.GetReference(bytes));
        s.TickCount++;
    }
    public static BlueprintDefinition MakeDefinition() => new BlueprintDefinition
    {
        Name = "FakeInstance", Kind = BlueprintDispatchKind.Instance,
        StructureHash = StructureHash, StateSize = StateSize,
        InitDefault = InitDefault, Tick = Tick,
    };
}
```

Tests register this via `fixture.Registry.CommitStaging(staging)` where staging has `staging.Add(FakeInstanceBp.BlueprintId, FakeInstanceBp.MakeDefinition())`.

What is NOT included:

- Tests requiring actual compiled Blueprint assemblies (those are Phase 3 Compiler tests).
- Performance benchmarks beyond the allocation-free test.
- Instrumented/profiling tests (Slice 2).

### Constraints

- All tests must be self-contained (no test-to-test dependencies, no shared mutable state outside `BlueprintTestFixture`).
- The allocation-free test in `AllocationFreeTests.cs` must assert 0 bytes/frame (not 64), reflecting the improved `GetAllWorldSingletons()` from Hot-path Correction 1.
- Phase-ordering test must verify that a channel command written by Blueprint is readable by `MockLocomotionDispatcher` within the same `TickFrame(dt)`.
- Hard-reload reconciliation test must verify: `InstanceVersion` bumped by exactly 1, payload bytes zeroed, `OnHardReset` called on the log sink.
- Two-frame tier upgrade test must verify at the frame level: end of Frame N = both components; end of Frame N+1 = only new tier.
- Replay determinism test must snapshot blackboards byte-by-byte via `fixture.SnapshotAllBlackboards()`.

### Success Conditions

- SC1: `dotnet test --filter "FullyQualifiedName~Runtime"` reports zero failures, zero skipped (all non-compiler-dependent tests).
- SC2: All 14 partition allocator test scenarios pass (listed in §5.10).
- SC3: Phase-ordering test passes: Blueprint channel command is visible to `MockLocomotionDispatcher` in the same `TickFrame`.
- SC4: Multi-slot per-entity test: two Blueprints on one entity, both `TickCount` fields incremented after one `TickFrame`.
- SC5: Tier upgrade two-frame test: Frame 1 has both components, Frame 2 has only the new tier.
- SC6: Replay determinism test: two fixtures with identical inputs produce `Assert.Equal(snapshotA, snapshotB)`.
- SC7: Allocation-free test: 1000 frames with 100 entities x 1 Blueprint allocate 0 bytes/frame.

---

## Phase 3 -- Compiler

## TASK-CP-000 -- Implement Static Catalog Stubs

**Phase:** 3 -- Compiler
**Design Reference:** [Compiler DD section 14](./Blueprint_Subsystem_Compiler_Detailed_Design.md#14-catalog-abstraction-node-registry-and-type-lookup), [Architecture v1.2 section 15](./Blueprint_Subsystem_Architecture_v1.2.md#15-catalogs-and-authoring-integration)
**Effort:** 0.5 days

### Scope

**What IS included:**
- Define `IEngineEventCatalog`, `IChannelCommandCatalog`, and `IWaitPrimitiveCatalog` interfaces in `Hrot.Blueprints.Core.Compiler.Catalogs`.
- Define the related catalog DTO records and `WaitKind` enum in the compiler core (`EngineEventCatalogEntry`, `ChannelCommandCatalogEntry`, `WaitPrimitiveCatalogEntry`).
- Implement `BuiltInEngineEventCatalog` in `Fdp.Toolkit.Blueprints` with `HitEvent`, `BehaviorFinishedEvent`, `TargetVisibleEvent`, and `TargetHeardEvent`.
- Implement `BuiltInChannelCommandCatalog` with `MoveTo`, `FollowRoute`, `AimAndFire`, `OpenDoor`, and `EjectPassengers`.
- Implement `BuiltInWaitPrimitiveCatalog` with locomotion/weapon/interaction channel waits, `BehaviorFinishedEvent`, and `PathfindingResult` ring buffer wait.
- Add concrete implementation samples (documentation only, no code changes) for:
  - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Catalogs/CatalogInterfaces.cs`
  - `FDP/Toolkits/Fdp.Toolkits/Blueprints/Catalogs/BuiltInEngineEventCatalog.cs`
  - `FDP/Toolkits/Fdp.Toolkits/Blueprints/Catalogs/BuiltInChannelCommandCatalog.cs`
  - `FDP/Toolkits/Fdp.Toolkits/Blueprints/Catalogs/BuiltInWaitPrimitiveCatalog.cs`

**What is NOT included:**
- Attribute-driven reflection catalog discovery (Slice 2 work).
- Runtime engine type changes.
- Compiler Stage 4 algorithm changes beyond consuming these catalogs.

### Constraints

- The catalog implementations must reference the real engine types directly via `typeof()`.
- Catalog interfaces and DTOs must remain in the compiler core assembly so validation and emission stages can depend on them without taking runtime-engine assembly dependencies.

### Concrete Code Implementation (Documentation Sample Only)

`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Compiler/Catalogs/CatalogInterfaces.cs`

```csharp
using System;
using System.Collections.Generic;

namespace Hrot.Blueprints.Core.Compiler.Catalogs
{
    public record EngineEventCatalogEntry(string Name, Type EventType);

    public record ChannelCommandCatalogEntry(string Name, Type ChannelType, ushort ActionId, Type ParamsType);

    public enum WaitKind { Channel, Event, RingBufferResult }
    public record WaitPrimitiveCatalogEntry(string Name, WaitKind Kind, Type TargetType);

    public interface IEngineEventCatalog
    {
        IReadOnlyList<EngineEventCatalogEntry> GetEntries();
    }

    public interface IChannelCommandCatalog
    {
        IReadOnlyList<ChannelCommandCatalogEntry> GetEntries();
    }

    public interface IWaitPrimitiveCatalog
    {
        IReadOnlyList<WaitPrimitiveCatalogEntry> GetEntries();
    }
}
```

`FDP/Toolkits/Fdp.Toolkits/Blueprints/Catalogs/BuiltInEngineEventCatalog.cs`

```csharp
using System.Collections.Generic;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Perception.Events;

namespace Fdp.Toolkit.Blueprints.Catalogs
{
    public class BuiltInEngineEventCatalog : IEngineEventCatalog
    {
        public IReadOnlyList<EngineEventCatalogEntry> GetEntries() => new List<EngineEventCatalogEntry>
        {
            // Core demo events
            new EngineEventCatalogEntry("OnHit", typeof(HitEvent)),
            new EngineEventCatalogEntry("OnBehaviorFinished", typeof(BehaviorFinishedEvent)),

            // Perception events
            new EngineEventCatalogEntry("OnTargetVisible", typeof(TargetVisibleEvent)),
            new EngineEventCatalogEntry("OnTargetHeard", typeof(TargetHeardEvent))
        };
    }
}
```

`FDP/Toolkits/Fdp.Toolkits/Blueprints/Catalogs/BuiltInChannelCommandCatalog.cs`

```csharp
using System.Collections.Generic;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Executors;

namespace Fdp.Toolkit.Blueprints.Catalogs
{
    public class BuiltInChannelCommandCatalog : IChannelCommandCatalog
    {
        public IReadOnlyList<ChannelCommandCatalogEntry> GetEntries() => new List<ChannelCommandCatalogEntry>
        {
            // Locomotion
            new ChannelCommandCatalogEntry("Locomotion/MoveTo", typeof(LocomotionChannel), NavigationConstants.ActionIdMoveTo, typeof(MoveToParams)),
            new ChannelCommandCatalogEntry("Locomotion/FollowRoute", typeof(LocomotionChannel), NavigationConstants.ActionIdFollowRoute, typeof(FollowRouteParams)),

            // Weapon
            new ChannelCommandCatalogEntry("Weapon/AimAndFire", typeof(WeaponChannel), CombatConstants.ActionIdAimAndFire, typeof(AimAndFireParams)),

            // Interaction (Including the OpenDoor dummy added previously)
            new ChannelCommandCatalogEntry("Interaction/OpenDoor", typeof(InteractionChannel), BehaviorConstants.ActionIdOpenDoor, typeof(OpenDoorParams)),
            new ChannelCommandCatalogEntry("Interaction/EjectPassengers", typeof(InteractionChannel), BehaviorConstants.ActionIdEjectPassengers, typeof(EjectPassengersParams))
        };
    }
}
```

`FDP/Toolkits/Fdp.Toolkits/Blueprints/Catalogs/BuiltInWaitPrimitiveCatalog.cs`

```csharp
using System.Collections.Generic;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Navigation;

namespace Fdp.Toolkit.Blueprints.Catalogs
{
    public class BuiltInWaitPrimitiveCatalog : IWaitPrimitiveCatalog
    {
        public IReadOnlyList<WaitPrimitiveCatalogEntry> GetEntries() => new List<WaitPrimitiveCatalogEntry>
        {
            // Channel Waits (Lower to reading the channel's Status field)
            new WaitPrimitiveCatalogEntry("WaitForChannel:Locomotion", WaitKind.Channel, typeof(LocomotionChannel)),
            new WaitPrimitiveCatalogEntry("WaitForChannel:Weapon", WaitKind.Channel, typeof(WeaponChannel)),
            new WaitPrimitiveCatalogEntry("WaitForChannel:Interaction", WaitKind.Channel, typeof(InteractionChannel)),

            // Event Waits (Lower to reading the event bus for the current entity)
            new WaitPrimitiveCatalogEntry("WaitForEvent:BehaviorFinishedEvent", WaitKind.Event, typeof(BehaviorFinishedEvent)),

            // Async buffer waits
            new WaitPrimitiveCatalogEntry("WaitForRingBufferResult:PathfindingResult", WaitKind.RingBufferResult, typeof(PathfindingBatchData))
        };
    }
}
```

### Success Conditions

- SC1: `IEngineEventCatalog`, `IChannelCommandCatalog`, and `IWaitPrimitiveCatalog` exist under `Hrot.Blueprints.Core.Compiler.Catalogs`.
- SC2: `BuiltInEngineEventCatalog` exposes `OnHit`, `OnBehaviorFinished`, `OnTargetVisible`, and `OnTargetHeard` mapped with direct `typeof(...)` references.
- SC3: `BuiltInChannelCommandCatalog` exposes `Locomotion/MoveTo`, `Locomotion/FollowRoute`, `Weapon/AimAndFire`, `Interaction/OpenDoor`, and `Interaction/EjectPassengers` with channel type, action id, and params type.
- SC4: `BuiltInWaitPrimitiveCatalog` exposes channel waits, `BehaviorFinishedEvent` wait, and `PathfindingResult` ring buffer wait.
- SC5: `dotnet build` zero errors.

---
## TASK-CP-001 -- Compiler Infrastructure and IR Data Model

**Phase:** 3 -- Compiler
**Design Reference:** [Compiler DD §1](./Blueprint_Subsystem_Compiler_Detailed_Design.md#1-architecture-of-the-compiler-library), [Compiler DD §2](./Blueprint_Subsystem_Compiler_Detailed_Design.md#2-pipeline-overview-and-stage-contracts), [Compiler DD §3](./Blueprint_Subsystem_Compiler_Detailed_Design.md#3-ir-data-model)
**Effort:** 2-3 days

### Scope

What IS included:

- Full directory tree under `Hrot.Blueprints.Core/Compiler/` from §1.1 (all files + subfolders: Ir/, Stages/, Lowering/, Emit/, Roslyn/, Determinism/).
- `IBlueprintCompiler` interface and `BlueprintCompiler` class (§1.2): `Compile(asset, options)` and `Validate(asset, options?)` entry points.
- `CompileOptions` record (per Patch 1: `SiblingSignatures: IReadOnlyList<BlueprintSignature>` NOT `SiblingAssets`), `CompilerMode` enum, `CompileResult` record, `ValidationOptions` record, `ValidationResult` record.
- `BlueprintSignature` record (Patch 1: Path, AssetId, Name, SanitizedName, BlueprintId, Dispatch, ExportedFunctionNames, Hostings, DeclaredCallablePeers) and `BlueprintSignatureParser` (lightweight JSON parser, extracts only signature fields).
- Complete `IrOperation` abstract record hierarchy (§3.4): all 26+ sealed record types listed in §3.4 including `IrOp_ReadInstanceVersion` (Q-18.1 addition).
- `IrAsset`, `IrGraph`, `IrBlock`, `IrStatement`, `IrValue`, `IrBlockId`, `IrTerminator` hierarchy (§3.2, §3.3): all 5 terminator types.
- `IrField`, `IrCustomEvent`, `IrGraphKind` enum, `IrTypeRef`, `IrDebugAnnotation` (§3.2, §3.5, §3.6).
- `DiagnosticSink`, `Diagnostic` record, `DiagnosticCodes` static class with all codes BP0001-BP9999 (stubs are fine -- full codes added per-stage; §2.2 and §5.7).
- `FnvHasher` (FNV-1a 32-bit for BlueprintId, 64-bit for StructureHash) in `Determinism/`.
- `DeterministicEnumerable` helpers in `Determinism/` (OrderBy-by-Id wrappers).
- `Sanitizer` in `Emit/`: `SanitizeName(name)`, `GeneratedFileName(asset, isRegistrar)` -- class name must be `SanitizedName_{BlueprintId:X8}_Bp` per Q-18.4.
- `BlueprintIdHash.Compute(Guid)` static method (FNV-1a 32-bit, used to compute int BlueprintId from Guid).
- Stub implementations for all stage classes (empty `Run` methods that throw `NotImplementedException`) to make the directory structure and `BlueprintCompiler.Compile` pipeline skeleton compile.

What is NOT included:

- Any stage implementation beyond stubs.
- Catalogs, Roslyn compilation, emission templates.

### Constraints

- `CompileOptions` uses `SiblingSignatures` not `SiblingAssets` (Patch 1).
- `BlueprintSignature` must be a `record` type (structural equality for Roslyn caching).
- All IR types (`IrAsset`, `IrBlock`, etc.) are `record` or `sealed record` for immutability.
- `IrOp_ReadInstanceVersion` must be in the hierarchy per Q-18.1.
- `DiagnosticCodes` must be a static class with `public const string` fields.
- `FnvHasher` must be deterministic (same input = same output across all runs, no random seed).

### Success Conditions

- SC1: `dotnet build` with zero errors across `Hrot.Blueprints.Core` (all stubs compile).
- SC2: `IBlueprintCompiler` has exactly two public methods: `Compile` and `Validate`.
- SC3: `CompileOptions.SiblingSignatures` property exists (type `IReadOnlyList<BlueprintSignature>`); `SiblingAssets` property does NOT exist.
- SC4: `IrOp_ReadInstanceVersion` exists in the hierarchy (Q-18.1).
- SC5: `GeneratedFileName("MoveToAndFire", false)` with BlueprintId `0xA1B2C3D4` returns `"MoveToAndFire_A1B2C3D4_Bp.g.cs"`.
- SC6: `FnvHasher.Hash32(guid.ToByteArray()) == FnvHasher.Hash32(guid.ToByteArray())` (deterministic).
- SC7: All `DiagnosticCodes.BP0001` through `BP9001` constants declared.

---

## TASK-CP-002 -- Pipeline Stages 1-5 (Parse through Schedule)

**Phase:** 3 -- Compiler
**Design Reference:** [Compiler DD §4](./Blueprint_Subsystem_Compiler_Detailed_Design.md#4-stage-1--parse), [§5](./Blueprint_Subsystem_Compiler_Detailed_Design.md#5-stage-2--validate), [§6](./Blueprint_Subsystem_Compiler_Detailed_Design.md#6-stage-3--normalize), [§7](./Blueprint_Subsystem_Compiler_Detailed_Design.md#7-stage-4--type-resolve), [§8](./Blueprint_Subsystem_Compiler_Detailed_Design.md#8-stage-5--schedule)
**Effort:** 5-7 days

### Scope

What IS included:

- `Stage1_Parse.Run(string json, DiagnosticSink sink)` -- §4.2: JSON deserialization, BP0001/BP0002/BP0010/BP0011 diagnostics.
- `Stage2_Validate.Run(asset, ctx)` -- §5.2: full validator chain:
  - `V_AssetStructure`, `V_DispatchKindCompatibility` (§5.3, all BP1010-BP1031), `V_NodeStructure`, `V_LinkStructure`, `V_GraphStructure`, `V_VariablesAndState` (§5.5, BP1200/BP1201/BP1210/BP1211 with tier budget math), `V_AiPrimitiveIntent` (§5.4, BP1100/BP1101), `V_LatentRules`, `V_ChannelCommandReferences`, `V_EventGraphReferences`, `V_WaitNodeReferences`, `V_PeerReferences` (§5.6, BP1300-BP1302, using `SiblingSignatures` not `SiblingAssets` per Patch 1), `V_TypeReferences`, `V_DeterminismOrdering`.
  - Performance requirement from §5.8: validation < 1ms for small assets, < 10ms for medium.
- `Stage3_Normalize.Run(asset, ctx)` -- §6.3: `MaterializeDefaultPinLiterals`, `InsertImplicitCasts`, `EliminateOrphanNodes`. All synthesized GUIDs deterministic per §6.4 (SHA256-based `SynthesizedGuid`). Diagnostics BP2001/BP2002/BP2003.
- `Stage4_TypeResolve.Run(asset, ctx)` -- §7.4: resolve all `BlueprintTypeRef` to `IrTypeRef`, verify link compatibility (BP1500/BP1501/BP1502/BP1503). `ITypeRegistry` interface with `TryResolve` and `TryGetCoercion`. `StaticTypeRegistry` with coercion table from §7.3. Wildcard 2-pass walk (§7.5). `TypedAsset` record.
- `Stage5_Schedule.Run(typedAsset, ctx)` -- §8: `GraphScheduler` class, topological sort, block allocation (`AllocBlock`), value allocation (`AllocValue`), CSE via `pinValueCache` (§8.5), latent block splitting (§8.6: `IrTerm_Suspend` + `IrOp_WaitForChannel/Event/Delay` marker), diagnostics BP4001-BP4004, BFS block numbering + deterministic labeling, `StructureHash = 0` (set in Stage 6). `IrAsset` built from §8.8 skeleton.
- `ValidationContext` record with all required fields.
- `INodeRegistry`, `IEngineEventCatalog`, `IChannelCommandCatalog`, `IWaitPrimitiveCatalog` interfaces (stubs for catalog binding, per §14).
- `IrPrinter.PrettyPrint(IrAsset)` for golden tests (deterministic, human-readable).

What is NOT included:

- Stage 6 dispatch lowering (CP-003).
- Catalog actual implementations (CP-004 covers `BuiltInNodeRegistry`, etc.).
- End-to-end compile, Roslyn, emission.

### Constraints

- `V_PeerReferences` uses `ctx.SiblingSignatures` (type `IReadOnlyDictionary<Guid, BlueprintSignature>`), not `ctx.SiblingsById` (type with full `BlueprintAsset`). It validates: (a) node's `TargetPeerAssetId` in `asset.CallablePeers`, (b) target found in siblings, (c) target has the named exported function.
- `Stage5_Schedule` block labels follow §8.3 conventions exactly: entry=`"entry"`, branch=`"branch_{nodeId_short}_true"`, etc.
- `GraphScheduler` must use monotonic `IrBlockId(n)` starting at 0, and BFS order for determinism.
- `SynthesizedGuid` in Normalize must use SHA256 over `(purpose, graphId.ToString(), pinId.ToString())` byte payload.

### Success Conditions

- SC1: `Stage1_Parse.Run` returns a non-null `BlueprintAsset` for valid JSON and emits BP0002 for malformed JSON.
- SC2: `V_AiPrimitiveIntent` emits BP1100 for `ReturnNode(Running)` in a Condition graph and BP1101 for a `LatentDelayNode` in a Condition graph.
- SC3: `V_VariablesAndState` emits BP1210 when Instance variable total exceeds 16096 bytes.
- SC4: `V_PeerReferences` emits BP1301 when a sibling is in `CallablePeers` but absent from `SiblingSignatures`.
- SC5: `Stage5_Schedule` splits a block at a `WaitForChannelNode`: block before has `IrTerm_Suspend` terminator; block after is the resume block.
- SC6: `IrPrinter.PrettyPrint` is deterministic -- two calls on the same `IrAsset` produce identical output.
- SC7: `dotnet build` zero errors; `dotnet test --filter "Stage1|Stage2|Stage3|Stage4|Stage5"` target passes for test file stubs (not yet golden tests).

---

## TASK-CP-003 -- Stage 6: Lower (Dispatch-Aware Transformations)

**Phase:** 3 -- Compiler
**Design Reference:** [Compiler DD §9](./Blueprint_Subsystem_Compiler_Detailed_Design.md#9-stage-6--lower-dispatch-aware)
**Effort:** 4-6 days

### Scope

What IS included:

- `Stage6_Lower.Run(asset, mode, sink)` -- §9.2: entry point calling `ComputeFieldLayouts`, `ComputeStructureHash`, dispatch switch, `DebugProbeInsertion`.
- `FieldLayout.ComputeFieldLayouts(asset)` -- §9.3: assigns `Offset` and `Size` to all `IrField` instances. `Parameters` start at offset 0, `WorkingState` at offset 8 (after StructureHash header), `Variables` at offset 16 (after `BlueprintLatentCursor` 16 bytes). Uses alignment rules from §9.3.
- `StructureHashComputation.Compute(asset)` -- §9.4: FNV-1a 64-bit over `Dispatch;{fields}` canonical string. Fields appended as `Name|Type.FullName|Offset|Size;`.
- `LibraryLowering.Apply(asset, sink)` -- §9.9: defensive double-check for latent ops, BP5001 if no function graphs.
- `AiPrimitiveLowering.Apply(asset, sink)` -- §9.5-9.6: synthesize `__phase` byte field in WorkingState (§9.6 `EnsurePhaseByteInWorkingState`), restructure each graph with latent ops into phase-switch dispatch pattern (§9.5 full algorithm): synthesized entry block with switch on `workingState.__phase`, phase-0 block (initial: command + phase advance + ReturnStatus.Running), phase-N check blocks (GetComponentRO + status switch). Each `IrOp_WaitForChannel` marker consumed by lowering. `WaitLowering_AiPrimitive` handles `IrOp_LatentDelay` (§9.8 Delay variant: adds `WaitUntilTime: float` field to WorkingState, emits time-comparison check).
- `InstanceLowering.Apply(asset, sink)` -- §9.10: for each graph with latent ops, calls `WaitLowering_Instance.Apply(graph)`. `WaitLowering_Instance` (§9.7): cursor switch pattern -- entry block switches on `state.Cursor.ResumeAt`; initial block captures `ResumeAt=n`, `InstanceVersion=instanceVersion`, returns void; resume blocks check `IrOp_CheckCursorVersion` (staleness check, per Q-18.1), then wait-condition check. `IrOp_LatentDelay` uses `state.Cursor.WaitUntilTime` (§9.8 Instance variant).
- `DebugProbeInsertion.Apply(asset, mode)` -- §9.11: no-op in Release; inserts `IrOp_DebugProbe_NodeEnter` at start of every block with a real source NodeId in Debug/Trace modes; Trace mode also adds `IrOp_DebugProbe_PinValue` per value-producing statement.
- `SynthesizedGuids` helper class with deterministic Guid methods for synthesized fields/nodes.

What is NOT included:

- Stage 7 emission (CP-004).
- Channel command lowering for emission (that is in the emitter, §10.9, CP-004).

### Constraints

- `IrOp_CheckCursorVersion` must be emitted at the start of each resume block in Instance lowering per Q-18.1.
- `__phase` field MUST be the first field in WorkingState (before user-declared fields) per §9.6.
- `StructureHash` is computed AFTER field layouts are finalized (so Offset/Size values are stable inputs to the hash).
- Phase numbering is DFS-order from the entry node of the graph (phase 0 = initial entry; phases 1..N = post-Wait in order of occurrence).
- All synthesized `IrBlockId` values are monotonically assigned and deterministic.
- `AiPrimitiveLowering` must handle multiple Wait ops in a single graph (N phases).
- `InstanceLowering` must handle multiple Wait ops (N resume labels).

### Success Conditions

- SC1: AiPrimitive graph with one `WaitForChannelNode` produces: entry dispatch block (switch __phase), phase-0 block (command + phase=1 + Running), phase-1 block (GetComponentRO + status switch), success/failure paths.
- SC2: Instance graph with one `WaitForChannelNode` produces: entry dispatch (switch Cursor.ResumeAt), initial block (ResumeAt=1, InstanceVersion=instanceVersion, return void), resume block (CheckCursorVersion + GetComponentRO + status switch).
- SC3: `StructureHash` changes when a variable's name changes (same type, same offset) -- verified by test.
- SC4: `StructureHash` changes when a variable's type changes -- verified by test.
- SC5: `StructureHash` does NOT change when graph body changes (only layout changes matter).
- SC6: Library asset with no function graphs emits BP5001.
- SC7: Debug mode inserts `IrOp_DebugProbe_NodeEnter` at start of each block with a non-null NodeId.
- SC8: `dotnet build` zero errors.

---

## TASK-CP-004 -- Stage 7: Emit (C# Code Generation)

**Phase:** 3 -- Compiler
**Design Reference:** [Compiler DD §10](./Blueprint_Subsystem_Compiler_Detailed_Design.md#10-stage-7--emit-c-generation), [Compiler DD Patches C1, C2](./Blueprint_Subsystem_Compiler_Detailed_Design_InlinePatches_v2.md), [Compiler DD Patches Q-18.1, Q-18.3, Q-18.4](./Blueprint_Subsystem_Compiler_Detailed_Design_InlinePatches.md)
**Effort:** 5-7 days

### Scope

What IS included:

- `CSharpEmitter` class (§10.2): `StringBuilder _sb`, `int _currentLine`, `int _indent`, `Write/WriteLine/Indent/Outdent`, `EmitNodeStart/EmitNodeEnd` for debug map tracking.
- `EmissionContext` class: per-asset mutable state (local counter, label-for-block resolver, variable/param name resolution, custom event name resolver, library class resolver). `NextLocalCounter(string prefix)` returns deterministic suffix.
- `BlockEmitter.Emit(e, block, isEntry)` -- §10.6: label + braced scope + statements + terminator.
- `StatementEmitter.Emit(e, stmt)` -- §10.7: full `switch` on all `IrOperation` subtypes. Must handle all 26+ ops from §3.4. `TypeRefToCSharp(IrTypeRef)` helper.
- `TerminatorEmitter.Emit(e, term)` -- §10.8: all 5 terminator types. `IrTerm_Suspend` throws (should have been lowered).
- `ChannelCommandLowering.Emit(e, op, resultValue)` -- §10.9: `GetComponentRW` + `ActiveAction = id` + `fixed (byte* paramSlot)` + params struct init + `ActionInstanceId++`.
- Library emission class -- §10.3: static class `{SanitizedName}_{BlueprintId:X8}_Bp` with `BlueprintId const`, one method per function graph.
- AiPrimitive emission class -- §10.4: `Params` struct, `WorkingState` struct, `InitDefaultWorkingState`, `TickCore`, BTree thunk (`BTreeTick`), HSM thunks (`HsmActivity`, `HsmGuard`), BlueprintCall thunk (`Call`). Only emit thunks for declared hostings.
- Instance emission class -- §10.5 + Q-18.1 + Q-18.3: `State` struct, `VarIds` nested class, `StateSize`, `InitDefault(Span<byte>)`, `Tick(ref State, view, ecb, self, time, deltaTime, uint instanceVersion)` (Q-18.1), engine event poll loops in `Tick`, custom event handlers `Event_{EventName}(ref State, view, ecb, self, time, deltaTime, ...)` (Q-18.3), `TickThunk` and event thunks with correct delegate signatures (Patch C2).
- Registrar emission for ALL dispatch kinds (Patch C1): `[BlueprintRegistrar]` attribute, `Register(BlueprintRegistryStaging staging, [BehaviorRegistry behReg]?)` -- `staging.Add(id, def)`, static `HsmActionDispatcher.RegisterAction(...)` call (no instance parameter).
- `DebugMapBuilder`: records `(NodeId, GraphId, startLine, endLine)` pairs during emission, builds `DebugMap` object.
- `Stage7_Emit.Run(asset, mode, sink)` entry point returning `(string GeneratedSource, DebugMap DebugMap)`.

What is NOT included:

- Roslyn in-memory compilation (CP-005).
- Incremental generator (CP-005).
- Catalogs implementation (CP-005).

### Constraints

- Class name MUST be `{SanitizedName}_{BlueprintId:X8}_Bp` -- NOT just `{SanitizedName}_Bp` (Q-18.4).
- Registrar file name: `BlueprintRegistrar_{SanitizedName}_{BlueprintId:X8}_Bp.g.cs` (Sanitizer §10.10).
- ALL registrar `Register` methods use `BlueprintRegistryStaging staging` (Patch C1). Library and Instance use `staging.Add(id, def)`. AiPrimitive with BTree hosting adds `BehaviorRegistry behReg` parameter.
- `HsmActionDispatcher.RegisterAction(...)` is a static call -- no `hsmDispatcher` parameter (Patch C1).
- `Tick` method signature includes `uint instanceVersion` as last parameter (Q-18.1).
- `Event_<CustomName>` includes `float deltaTime` (Q-18.3).
- `IrTerm_Suspend` in TerminatorEmitter throws `InvalidOperationException` with message "should have been lowered in Stage 6".
- `ChannelCommandLowering.Emit` uses `NextLocalCounter("ch")` for deterministic local variable suffixes.
- Emission is deterministic: same input IR = identical output string.

### Success Conditions

- SC1: Library emission golden test -- compile `LibraryMath` through stages 1-7, compare generated source to checked-in snapshot `Snapshots/Emit/LibraryMath.cs.txt`.
- SC2: AiPrimitive golden test -- `MoveToAndFire` generated source matches snapshot. Registrar has `Register(BlueprintRegistryStaging staging, BehaviorRegistry behReg)` with `staging.Add(...)` and `HsmActionDispatcher.RegisterAction(...)` static call (per Patch C1).
- SC3: Instance golden test -- `HealthRegen` generated source matches snapshot. `Tick` signature includes `uint instanceVersion`. Custom event handlers include `float deltaTime`.
- SC4: Determinism test -- two calls on same IR produce identical source strings.
- SC5: `IrTerm_Suspend` appearing in a lowered IR throws `InvalidOperationException`.
- SC6: Generated class name for asset named "MoveToAndFire" with BlueprintId 0xA1B2C3D4 is `MoveToAndFire_A1B2C3D4_Bp`.
- SC7: `dotnet build` zero errors; `dotnet test --filter "Stage7"` passes.

---

## TASK-CP-005 -- Stage 8: Roslyn + Incremental Generator + Debug Map + Determinism + Catalogs

**Phase:** 3 -- Compiler
**Design Reference:** [Compiler DD §11](./Blueprint_Subsystem_Compiler_Detailed_Design.md#11-stage-8--roslyn-finalize), [§12](./Blueprint_Subsystem_Compiler_Detailed_Design.md#12-determinism-enforcement), [§13](./Blueprint_Subsystem_Compiler_Detailed_Design.md#13-debug-map-generation), [§14](./Blueprint_Subsystem_Compiler_Detailed_Design.md#14-catalogs-integration), [Compiler DD Patches Patch1, Patch2](./Blueprint_Subsystem_Compiler_Detailed_Design_InlinePatches.md)
**Effort:** 3-5 days

### Scope

What IS included:

- `InMemoryRoslynCompiler.Compile(source, virtualSourcePath, assemblyName, sink)` -- §11.2: `CSharpCompilation.Create` with `deterministic: true, allowUnsafe: true`, `EmitOptions` with portable PDB, `EmbeddedText.FromSource`, returns `(byte[] Pe, byte[] Pdb)`. Throws `BlueprintCompileException` on failure; emits BP7001 diagnostics.
- `MetadataReferenceResolver` -- §11.3 with Patch 2: `ForRuntimeAssemblies(IEnumerable<Assembly>)` filters with BOTH `!a.IsDynamic` AND `!string.IsNullOrEmpty(a.Location)`. Constructor takes `IReadOnlyList<MetadataReference>`. `Resolve()` returns the list.
- `EmbeddedTextHelper` in `Roslyn/`.
- `BlueprintCompileException` (custom exception type).
- `Stage8_RoslynFinalize.Run(generatedSource, virtualPath, assemblyName, references, sink)` returning `(byte[] Pe, byte[] Pdb)`.
- `BlueprintIncrementalGenerator` (per Patch 1 -- in project `Hrot.Blueprints.Generators`): implements `IIncrementalGenerator` with Initialize(context) -- 4 providers: (1) raw file text from `.bp.json` AdditionalTexts, (2) per-file `BlueprintSignature` via `BlueprintSignatureParser.Parse`, (3) `siblingCatalog = signatures.Collect()`, (4) `compileResults = rawFiles.Combine(siblingCatalog).Select(CompileOneAsset)`. Registers with `RegisterSourceOutput`. `CompileOneAsset` uses `BlueprintCompiler` passing `SiblingSignatures`.
- `BlueprintSignatureParser.Parse(path, text)` -- lightweight; extracts `AssetId`, `Name`, `Dispatch`, `Hostings`, exported function graph names from JSON; does NOT parse nodes/links.
- `DebugMap` record (§13): `AssetId`, `IReadOnlyList<NodeDebugInfo>` where `NodeDebugInfo` has `NodeId`, `GraphId`, `StartLine`, `EndLine`, `BlockLabel`.
- `DebugMapSerializer` -- JSON serialization of `DebugMap`; deterministic field ordering.
- `EngineEventCatalogEntry` record, `IEngineEventCatalog` interface, `BuiltInEngineEventCatalog` stub implementation (§14).
- `IChannelCommandCatalog` interface, `BuiltInChannelCommandCatalog` stub.
- `IWaitPrimitiveCatalog` interface, `BuiltInWaitPrimitiveCatalog` stub.
- `INodeRegistry` interface, `BuiltInNodeRegistry` stub.

What is NOT included:

- Hot-reload ALC loading (that is the Hot Reload DD / Editor DD).
- Actual catalog entries (these are populated by the engine team or integration task).

### Constraints

- `MetadataReferenceResolver.ForRuntimeAssemblies` MUST have BOTH predicates: `!a.IsDynamic` AND `!string.IsNullOrEmpty(a.Location)`. Patch 2 is emphatic: the `Location == ""` check catches in-memory ALC assemblies that are NOT `IsDynamic`.
- `BlueprintIncrementalGenerator` must NOT put both passes inside a single callback (Patch 1: this would break Roslyn incremental caching). Must use `.Combine(siblingCatalog)` pattern.
- `BlueprintSignature` record must implement structural equality (it is a `record` type, so this is automatic).
- `DebugMap` JSON serialization must be deterministic (sorted field order).
- `InMemoryRoslynCompiler` uses `deterministic: true` in `CSharpCompilationOptions`.

### Success Conditions

- SC1: `InMemoryRoslynCompiler.Compile` on valid generated source (from CP-004) produces non-empty PE and PDB byte arrays.
- SC2: PDB contains embedded source text (verified by extracting with `PortablePdbInspector` or equivalent).
- SC3: `InMemoryRoslynCompiler.Compile` on invalid C# throws `BlueprintCompileException` and emits BP7001 diagnostic.
- SC4: `MetadataReferenceResolver.ForRuntimeAssemblies` does not include assemblies where `a.Location == ""` (test: create a collectible ALC, load a small assembly via `LoadFromStream`, call `ForRuntimeAssemblies(AppDomain.CurrentDomain.GetAssemblies())`, assert the in-memory assembly is not in the result).
- SC5: `BlueprintSignatureParser.Parse` on a `.bp.json` extracts AssetId, Name, Dispatch, exported function graph names -- without throwing, even if graph/node data is malformed.
- SC6: `DebugMap` JSON serialization produces identical output for identical inputs.
- SC7: `dotnet build` zero errors in `Hrot.Blueprints.Core` and `Hrot.Blueprints.Generators`.

---

## TASK-CP-006 -- Compiler Test Suite

**Phase:** 3 -- Compiler
**Design Reference:** [Compiler DD §17](./Blueprint_Subsystem_Compiler_Detailed_Design.md#17-compiler-test-strategy)
**Effort:** 4-6 days

### Scope

What IS included:

- All test files from §17.2, populated with tests from §17.3 through §17.10:
- `Stage1_ParseTests.cs`: §17.3 (4 tests named there).
- `Stage2_ValidationTests/`: all 5 files -- `V_DispatchKindCompatibilityTests.cs`, `V_AiPrimitiveIntentTests.cs`, `V_VariablesAndStateTests.cs`, `V_PeerReferencesTests.cs`, `V_AllValidatorsCoverageTests.cs` (reflection test).
- `Stage3_NormalizationTests.cs`: normalize happy path + BP2001 + BP2003.
- `Stage4_TypeResolveTests.cs`: resolve built-in types, coercion table, BP1500/BP1501/BP1502/BP1503.
- `Stage5_ScheduleTests/`: `GoldenIrTests.cs` (snapshot per §17.5), `DataFlowCseTests.cs`, `LatentBlockSplitTests.cs`.
- `Stage6_LoweringTests/`: `LibraryLoweringTests.cs`, `AiPrimitiveLoweringTests.cs` (phase-byte state machine), `InstanceLoweringTests.cs` (cursor switch), `ChannelCommandLoweringTests.cs`, `DebugProbeInsertionTests.cs`.
- `Stage7_EmitTests/`: `LibraryEmitGoldenTests.cs`, `AiPrimitiveEmitGoldenTests.cs`, `InstanceEmitGoldenTests.cs`, `ThunkEmissionTests.cs`, `SanitizerTests.cs`.
- `Stage8_RoslynTests/`: `InMemoryCompileTests.cs` (§17.7), `PdbEmbeddedSourceTests.cs`, `MetadataReferenceResolverTests.cs` (Patch 2 tests for both predicates).
- `Determinism/`: `CompilerDeterminismTests.cs` (§17.8: same input -> identical output, parallel, hash stability), `BlueprintIdHashTests.cs`, `StructureHashTests.cs`.
- `EndToEnd/`: 5 files (§17.9): `MoveToAndFire_EndToEndTests.cs`, `HealthRegen_EndToEndTests.cs`, `HasVisibleTarget_EndToEndTests.cs`, `DoorActor_DoorSensor_EndToEndTests.cs`, `MathUtilsLib_EndToEndTests.cs`.
- `Snapshots/Schedule/*.ir.txt`, `Snapshots/Emit/*.cs.txt`, `Snapshots/DebugMap/*.dbgmap.json` (checked-in golden files).
- `TestAssets/*.bp.json` -- sample assets for all 5 Slice 1 demos.
- `TestData` helper class (§17.11), `BlueprintAssetBuilder` fluent builder for test setup.

What is NOT included:

- Performance tests (§17.10) -- excluded from main test suite per §17.10 ("run only in nightly CI or on-demand").

### Constraints

- `V_AllValidatorsCoverageTests` must use reflection over `DiagnosticCodes` to assert every declared code is covered.
- End-to-end tests use the real `BlueprintTestFixture` from Phase 1.
- End-to-end tests for AiPrimitives invoke via BTree thunk AND HSM thunk (§17.9).
- Golden snapshot tests use `BLUEPRINT_REGENERATE_SNAPSHOTS=1` environment variable to regenerate (§17.6).
- MoveToAndFire and HealthRegen end-to-end tests include reload tests (soft + hard per §17.9 last two facts).
- `MetadataReferenceResolverTests.cs` contains `ForRuntimeAssemblies_WithDynamicAssemblies_FiltersThem` AND `ForRuntimeAssemblies_WithInMemoryAlcAssembly_FiltersIt` (both from Patch 2).
- AiPrimitive emit golden tests must have registrar in snapshot with `BlueprintRegistryStaging staging` (not `BlueprintRegistry`) and no `HsmActionDispatcher` parameter (Patch C1).

### Success Conditions

- SC1: `dotnet test --filter "FullyQualifiedName~Compiler"` reports zero failures, zero skipped.
- SC2: `V_AllValidatorsCoverageTests` passes -- every `DiagnosticCodes.BP*` constant has a test.
- SC3: Stage 7 golden tests pass -- emitted source byte-identical to checked-in snapshots.
- SC4: Stage 8 tests pass -- `ForRuntimeAssemblies_WithInMemoryAlcAssembly_FiltersIt` verifies that an assembly loaded via `LoadFromStream` is excluded.
- SC5: Determinism test with 16 parallel compiles -- all produce identical `GeneratedSource`.
- SC6: End-to-end `MoveToAndFire_EndToEndTests` passes (phase-advance, 3-tick sequence per §17.9).
- SC7: End-to-end `HealthRegen_EndToEndTests` passes (Instance with latent + `InstanceVersion` preservation/reset).
- SC8: `StructureHash_FieldOrderChanges_HashChanges` and `StructureHash_FieldTypeChange_HashChanges` both pass.

---

## Phase 4 -- Hot Reload

---

## TASK-HR-001 -- AiHotReloadCoordinator Core

**Phase:** 4 -- Hot Reload
**Design Reference:** [Hot Reload DD §1](./Blueprint_Subsystem_Hot_Reload_Detailed_Design.md#1-overview-and-what-changes), [§2](./Blueprint_Subsystem_Hot_Reload_Detailed_Design.md#2-the-reload-sequence-in-detail), [§3](./Blueprint_Subsystem_Hot_Reload_Detailed_Design.md#3-background-thread-phase--file-watch-alc-load-attribute-scan), [§4](./Blueprint_Subsystem_Hot_Reload_Detailed_Design.md#4-main-thread-phase--drainpendingcallbacks), [§5](./Blueprint_Subsystem_Hot_Reload_Detailed_Design.md#5-registry-staging-coordination), [§6](./Blueprint_Subsystem_Hot_Reload_Detailed_Design.md#6-error-rollback), [§7](./Blueprint_Subsystem_Hot_Reload_Detailed_Design.md#7-alc-unload-and-managed-delegate-lifetime), [§8](./Blueprint_Subsystem_Hot_Reload_Detailed_Design.md#8-pdb-loading-developer-mode-option), [Hot Reload DD Patches 1-4](./Blueprint_Subsystem_Hot_Reload_Detailed_Design_InlinePatches.md)
**Effort:** 4-6 days

### Scope

**What IS included:**
- `AiHotReloadCoordinator` sealed class with constructor `(BehaviorRegistry behaviorRegistry, BlueprintRegistry blueprintRegistry, AiHotReloadCoordinatorOptions options)` -- per Patch 2 (no `HsmActionDispatcher` parameter).
- Fields: `_behaviorRegistry`, `_blueprintRegistry`, `_options`, `_currentAlc` (main-thread-only per Patch 1), `_pendingReloads : ConcurrentQueue<PendingReload>`.
- Public events: `Action? OnReloadCompleted`, `Action<Exception>? OnReloadFailed`.
- `StartWatching(string dllPath)` -- starts `FileSystemWatcher` on the given DLL path. On change: spawns background task (ThreadPool or `Task.Run`) that calls `LoadAndScan` + enqueues to `_pendingReloads`.
- `StopWatching()` -- disposes the FileSystemWatcher.
- `DrainPendingCallbacks()` -- must be called on main thread (caller responsibility). Dequeues one `PendingReload`, calls `ApplyReload` wrapped in try/catch per Patch 1. On failure: logs, fires `OnReloadFailed`, calls `pending.NewAlc.Unload()`, does NOT touch `_currentAlc`.
- Private `LoadAndScan(string dllPath)` -- creates collectible `AssemblyLoadContext` with name `AiBehaviors_{timestamp}_{guid}`, calls `LoadAssemblyInto`, calls `ScanForRegistrars`, returns `PendingReload` (NO `OldAlc` field per Patch 1).
- Private `ApplyReload(PendingReload pending)` -- §2 full sequence: (1) `HsmActionDispatcher.ClearAll()` static call, (2) `_blueprintRegistry.BeginStaging()`, (3) invoke registrars, (4) `CommitStaging`, (5) `var oldAlc = _currentAlc; _currentAlc = pending.NewAlc; oldAlc?.Unload()`.
- Private `ScanForRegistrars(Assembly assembly)` -- reflects over all types with `[BlueprintRegistrar]` attribute, finds `Register` methods, builds `IReadOnlyList<ResolvedRegistrar>`.
- `ResolvedRegistrar` record: `Type DeclaringType`, `MethodInfo RegisterMethod`, `IReadOnlyList<RegistrarParameter> Parameters`.
- `RegistrarParameter` record: `string Name`, `Type ParameterType`, `int OrdinalIndex`.
- Private `ResolveRegistrarArgument(Type paramType, BlueprintRegistryStaging staging)` -- dispatches: `BlueprintRegistryStaging` -> staging, `BehaviorRegistry` -> `_behaviorRegistry`, `BlueprintRegistry` -> throws `HotReloadRegistrarException` (Patch 4), `HsmActionDispatcher` -> throws `HotReloadRegistrarException` (Patch 2), anything else -> throws.
- Private `InvokeRegistrar(ResolvedRegistrar registrar, BlueprintRegistryStaging staging)` -- resolves all arguments then calls `RegisterMethod.Invoke(null, args)`.
- `HotReloadRegistrarException` custom exception type.
- `AiHotReloadCoordinatorOptions` record: `ILogger? Logger`, `bool LoadPdbOnDeveloperMode = false`, `TimeSpan FileWatcherDebounce = 500ms`.
- `[BlueprintRegistrar]` attribute class (if not already defined elsewhere).
- PDB loading path (§8): when `options.LoadPdbOnDeveloperMode = true`, `LoadAssemblyInto` looks for a co-located `.pdb` file and calls `alc.LoadFromStream(peStream, pdbStream)` with both streams.
- ALC name includes timestamp + Guid for debuggability.
- Public `ApplyQuickReload(AssemblyLoadContext newAlc, Assembly newAssembly)` method -- per Patch 3: scans registrars, creates `PendingReload`, calls `ApplyReload` directly (synchronous, not queued). On failure: fires `OnReloadFailed`, calls `pending.NewAlc.Unload()`, re-throws.

**What is NOT included:**
- `SimulateReload` test harness integration (HR-002).
- Hot reload tests (HR-003).
- Editor UI (Editor DD).

### Constraints

- `_currentAlc` is ONLY written in `ApplyReload` (the success branch, after `CommitStaging`) and read nowhere outside the main thread. Background `LoadAndScan` does NOT capture `_currentAlc`.
- `HsmActionDispatcher.ClearAll()` is called BEFORE `BeginStaging` in every `ApplyReload` call. The sequence is: ClearAll, BeginStaging, invoke registrars, CommitStaging, swap `_currentAlc`.
- `ResolveRegistrarArgument` must throw explicitly for both `BlueprintRegistry` (Patch 4) and `HsmActionDispatcher` (Patch 2), with error messages specifically mentioning the reason (RCU contract for registry, static class for HSM).
- The `PendingReload` record MUST NOT have an `OldAlc` field.
- `ApplyQuickReload` calls `ApplyReload` directly (same code path as `DrainPendingCallbacks`), NOT a separate impl.
- FileSystemWatcher debounce: multiple events from the same file within `FileWatcherDebounce` time window are coalesced into one `LoadAndScan` call.

### Success Conditions

- SC1: `Reload_Failure_DoesNotMutateCurrentAlc` test (from Patch 1): after a reload where the registrar throws, `GetCurrentAlc()` returns the same object as before.
- SC2: `Reload_FailureThenSuccess_LiveCodeNeverInterrupted` test (from Patch 1): original code accessible after failed reload; subsequent success reloads cleanly.
- SC3: `ResolveRegistrarArgument_BlueprintRegistry_ThrowsExplicitly` (Patch 4): message contains "BlueprintRegistryStaging" and "RCU contract".
- SC4: `ResolveRegistrarArgument_HsmActionDispatcher_ThrowsExplicitly` (Patch 2): message contains "static class".
- SC5: AiPrimitive registrar with 2 parameters `(BlueprintRegistryStaging, BehaviorRegistry)` is invoked correctly (no `HsmActionDispatcher` parameter needed).
- SC6: After successful `ApplyReload`, `_blueprintRegistry.TryGetById` returns the new definitions (not the old ones).
- SC7: `ApplyQuickReload` on same code path as `DrainPendingCallbacks` -- same ALC tracking, same `_currentAlc` update, same rollback behavior on failure.
- SC8: `dotnet build` zero errors.

---

## TASK-HR-002 -- SimulateReload Test Harness Integration

**Phase:** 4 -- Hot Reload
**Design Reference:** [Hot Reload DD §9](./Blueprint_Subsystem_Hot_Reload_Detailed_Design.md#9-test-harness-integration--simulatereload), [Hot Reload DD Patch 3](./Blueprint_Subsystem_Hot_Reload_Detailed_Design_InlinePatches.md#patch-3--quick-reload-goes-through-the-coordinator-supersedes-114)
**Effort:** 1-2 days

### Scope

**What IS included:**
- `BlueprintTestFixture.SimulateReload(IReadOnlyList<BlueprintAsset> newAssets)` method (§9): compiles each asset in-memory using `BlueprintCompiler` (same pipeline as normal compile), creates a collectible ALC, loads the compiled PE+PDB bytes into the ALC, then calls `_coordinator.ApplyQuickReload(alc, assembly)` -- per Patch 3 (coordinator owns ALC).
- `BlueprintTestFixture.SimulateQuickReload(BlueprintAsset asset)` convenience overload: calls `SimulateReload(new[] { asset })`.
- `BlueprintTestFixture.SimulateReloadWithThrowingRegistrar()` test helper: creates a fake compiled assembly where `Register` throws `InvalidOperationException`, calls `_coordinator.ApplyQuickReload`. Used by Patch 1 failure tests.
- `BlueprintTestFixture.GetCurrentAlc()` method: returns coordinator's current `_currentAlc` (requires `[InternalsVisibleTo]` or test-specific accessor).
- `BlueprintTestFixture.ForceGcReclaim()` helper: calls `GC.Collect() + GC.WaitForPendingFinalizers()` twice to encourage ALC finalization.
- `BlueprintTestFixture.BehaviorRegistry` public property exposing the mock `BehaviorRegistry` instance.
- Corresponding `Dispose` update: `_coordinator?.StopWatching()` + `_currentAlc?.Unload()` via coordinator.

**What is NOT included:**
- `AiHotReloadCoordinator` itself (HR-001).
- Hot reload tests (HR-003).

### Constraints

- `SimulateReload` uses `BlueprintCompiler` from Phase 3 with `EmitPdbWithEmbeddedSource = true` to get PE+PDB bytes.
- `MetadataReferenceResolver.ForRuntimeAssemblies` used for reference resolution (with both `!IsDynamic` and `!string.IsNullOrEmpty(Location)` filters -- Compiler DD Patch 2).
- The fake-registrar assembly in `SimulateReloadWithThrowingRegistrar` must have a class with `[BlueprintRegistrar]` and a `Register(BlueprintRegistryStaging)` method that throws, so `ScanForRegistrars` finds it and `InvokeRegistrar` triggers the exception.

### Success Conditions

- SC1: `SimulateReload([v2asset])` -> `_registry.TryGetByName(v2asset.Name, out def)` returns true.
- SC2: `SimulateReload` followed by `fixture.TickFrame(dt)` -> tick system uses new `BlueprintDefinition` delegates.
- SC3: `GetCurrentAlc()` returns a non-null `AssemblyLoadContext` after any `SimulateReload` or `CompileAndLoad`.
- SC4: After `SimulateReload(v2)`, the old ALC (from v1 load) is eligible for GC (`ForceGcReclaim()` after dropping the v1 ref clears it).
- SC5: `dotnet build` zero errors.

---

## TASK-HR-003 -- Hot Reload Test Suite

**Phase:** 4 -- Hot Reload
**Design Reference:** [Hot Reload DD §10](./Blueprint_Subsystem_Hot_Reload_Detailed_Design.md#10-hot-reload-test-strategy)
**Effort:** 3-4 days

### Scope

**What IS included:**
All test files from Hot Reload DD §10, including tests from the patches:
- `HotReload/Coordinator/ReloadSequenceTests.cs`: file-watcher-driven reload, queue draining, successful apply, event callbacks.
- `HotReload/Coordinator/FailureRollbackTests.cs`: Patch 1 tests (`Reload_Failure_DoesNotMutateCurrentAlc`, `Reload_FailureThenSuccess_LiveCodeNeverInterrupted`).
- `HotReload/Coordinator/AlcLifecycleTests.cs`: ALC unload on success, no leak on failure, chained reloads (R1 success -> R2 failure -> R3 success, verify R2 ALC unloaded, R1 ALC unloaded at R1 success not R3).
- `HotReload/Coordinator/RegistrarInjectionTests.cs`: Patch 2 (`HsmActionDispatcher` throws), Patch 4 (`BlueprintRegistry` throws), 2-param registrar invoked correctly.
- `HotReload/Coordinator/QuickReloadTests.cs`: Patch 3 tests (`QuickReload_AfterPreviousQuickReload_UnloadsThePreviousQuickReloadAlc` from Patch 3 doc), concurrent Quick Reload + file-watcher coexistence.
- `HotReload/RuntimeIntegration/SoftReloadTests.cs`: hash unchanged -> slot payload preserved -> tick continues from saved state.
- `HotReload/RuntimeIntegration/HardReloadTests.cs`: hash changed -> slot payload zeroed -> `InstanceVersion` bumped -> tick re-runs InitDefault.
- `HotReload/RuntimeIntegration/AiPrimitiveReloadTests.cs`: working-state reset on hash change.
- `HotReload/RuntimeIntegration/LatentCursorReloadTests.cs`: latent cursor in flight at reload time -> soft reload resumes cleanly; hard reload resets cursor to ResumeAt=0.
- `HotReload/PdbLoading/PdbLoadTests.cs`: when `LoadPdbOnDeveloperMode = true`, loaded assembly has pdb symbols accessible.

All tests use `BlueprintTestFixture.SimulateReload` and `SimulateQuickReload` from HR-002.

**What is NOT included:**
- Editor UI integration tests (Editor DD).
- HSM dispatcher snapshot rollback (Slice 2 per Patch 2 §11.3 still open).

### Constraints

- ALC lifecycle tests must use `WeakReference` + `ForceGcReclaim()` pattern to verify finalization.
- All test classes must be self-contained (no shared mutable state between tests outside the fixture).
- Reload tests with runtime integration use `BlueprintTestFixture` from Phase 1 (wired with real `EntityRepository` + real `BlueprintTickSystem`).

### Success Conditions

- SC1: `dotnet test --filter "FullyQualifiedName~HotReload"` reports zero failures, zero skipped.
- SC2: `Reload_Failure_DoesNotMutateCurrentAlc`: `GetCurrentAlc()` returns same reference before and after failed reload.
- SC3: Chained reload test: R1 -> R2 fail -> R3 success -- `_currentAlc` at each step is correct; no ALC leaked.
- SC4: Soft reload: Instance Blueprint state survives (tick counter preserved).
- SC5: Hard reload: payload zeroed, `InstanceVersion` incremented by exactly 1.
- SC6: Latent cursor hard reload: cursor's `ResumeAt` is 0 after reset.
- SC7: `BlueprintRegistry` registrar parameter -> `HotReloadRegistrarException` with "RCU contract" in message.
- SC8: `HsmActionDispatcher` registrar parameter -> `HotReloadRegistrarException` with "static class" in message.

## Phase 5 -- Debug Protocol

---

## TASK-DBG-000 -- Blueprint Time Controller Adapter

**Phase:** 5 -- Debug Protocol
**Design Reference:** [Debug Protocol DD Inline Patches (Patch 1)](./Blueprint_Subsystem_Debug_Protocol_Detailed_Design_InlinePatches.md#patch-1--soft-pause-via-time-controller-request-supersedes-16-64-65-7x), [Editor DD section 13](./Blueprint_Subsystem_Editor_Detailed_Design.md#13-time-controller-adapter)
**Effort:** 0.5 days

### Scope

**What IS included:**
- Define the `IBlueprintTimeController` interface in `Hrot.Blueprints.Core.Debug` with the following members: `bool IsPausedByDebugger { get; }`, `void RequestPause()`, `void RequestResume()`, and `void RequestStepOneTick()`.
- Implement `MasterSyncTimeControllerAdapter : IBlueprintTimeController` in `Hrot.Blueprints.Editor.Debug` that wraps the engine's native `Fdp.Toolkit.Time.Controllers.MasterSyncController`.
- **Pause Logic:** `RequestPause()` must call `_masterSync.SwitchToDeterministic(new HashSet<int>())` to halt local simulation time advancement while keeping the UI loop alive.
- **Resume Logic:** `RequestResume()` must call `_masterSync.SwitchToContinuous()`.
- **Step Logic:** `RequestStepOneTick()` must call `_masterSync.Step(1.0f / 60.0f)`.
- Add concrete implementation samples (documentation only, no code changes) for:
  - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Debug/IBlueprintTimeController.cs`
  - `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/MasterSyncTimeControllerAdapter.cs`
  - DI registration snippet for later `TASK-ED-001` integration.

**What is NOT included:**
- Injecting the adapter into the Dependency Injection container (this is handled in `TASK-ED-001` Editor Infrastructure).
- Implementing the actual Debug Session that consumes this interface (this is `TASK-DBG-001`).
- Modifying any core engine time controllers.

### Constraints

- The adapter must execute its operations without blocking the calling thread. `RequestPause()` must return immediately (Soft Pause semantics) to prevent UI deadlocks.
- The `IBlueprintTimeController` interface must reside in the `Hrot.Blueprints.Core` assembly so the debug protocol can depend on it without referencing Editor or Engine Toolkit types.

### Concrete Code Implementation (Documentation Sample Only)

`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/Debug/IBlueprintTimeController.cs`

```csharp
namespace Hrot.Blueprints.Core.Debug
{
    /// <summary>
    /// Abstracts the engine's time control for the Blueprint debugger.
    /// Provides soft-pause semantics (returns immediately; halts on next frame).
    /// </summary>
    public interface IBlueprintTimeController
    {
        bool IsPausedByDebugger { get; }
        void RequestPause();
        void RequestResume();
        void RequestStepOneTick();
    }
}
```

`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Debug/MasterSyncTimeControllerAdapter.cs`

```csharp
using System;
using System.Collections.Generic;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Time.Controllers;
using Hrot.Blueprints.Core.Debug;

namespace Hrot.Blueprints.Editor.Debug
{
    /// <summary>
    /// Adapts the engine's native MasterSyncController to the Blueprint debug protocol.
    /// </summary>
    public sealed class MasterSyncTimeControllerAdapter : IBlueprintTimeController
    {
        private readonly MasterSyncController _masterSync;

        // 60 Hz fixed delta for stepping
        private const float StepDeltaSeconds = 1.0f / 60.0f;

        public MasterSyncTimeControllerAdapter(MasterSyncController masterSync)
        {
            _masterSync = masterSync ?? throw new ArgumentNullException(nameof(masterSync));
        }

        /// <summary>
        /// True if the engine is currently in lockstep/paused mode.
        /// </summary>
        public bool IsPausedByDebugger => _masterSync.GetMode() == TimeMode.Deterministic;

        /// <summary>
        /// Requests a soft pause. The current tick will finish, and time advancement
        /// will halt on the next frame.
        /// </summary>
        public void RequestPause()
        {
            // Transitioning to deterministic mode with an empty slave roster
            // effectively pauses the local simulation clock without waiting for network ACKs.
            _masterSync.SwitchToDeterministic(new HashSet<int>());
        }

        /// <summary>
        /// Resumes continuous time advancement.
        /// </summary>
        public void RequestResume()
        {
            _masterSync.SwitchToContinuous();
        }

        /// <summary>
        /// Advances the simulation clock by exactly one 60Hz frame.
        /// </summary>
        public void RequestStepOneTick()
        {
            if (IsPausedByDebugger)
            {
                _masterSync.Step(StepDeltaSeconds);
            }
        }
    }
}
```

DI wiring sample (implemented later in `TASK-ED-001`):

```csharp
// Inside your Editor bootstrap/DI setup:
services.AddSingleton<IBlueprintTimeController>(sp =>
{
    var masterSync = sp.GetRequiredService<MasterSyncController>();
    return new MasterSyncTimeControllerAdapter(masterSync);
});
```

### Success Conditions

- SC1: `IBlueprintTimeController` exists in `Hrot.Blueprints.Core.Debug` with the exact API (`IsPausedByDebugger`, `RequestPause`, `RequestResume`, `RequestStepOneTick`).
- SC2: `MasterSyncTimeControllerAdapter` exists in `Hrot.Blueprints.Editor.Debug` and wraps `MasterSyncController` via constructor injection.
- SC3: `RequestPause()` calls `_masterSync.SwitchToDeterministic(new HashSet<int>())` and returns immediately (soft pause semantics).
- SC4: `RequestResume()` calls `_masterSync.SwitchToContinuous()`.
- SC5: `RequestStepOneTick()` calls `_masterSync.Step(1.0f / 60.0f)`.
- SC6: `dotnet build` zero errors.

---
## TASK-DBG-001 -- Debug Session Interface and DebugProbe Dispatcher

**Phase:** 5 -- Debug Protocol
**Design Reference:** [Debug Protocol DD §1](./Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md#1-overview-and-design-goals), [§2](./Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md#2-iblueprintdebugsession-interface), [§3](./Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md#3-debugprobe-static-dispatcher--deeper-look), [Debug Protocol DD Patches 1-2](./Blueprint_Subsystem_Debug_Protocol_Detailed_Design_InlinePatches.md)
**Effort:** 2-3 days

### Scope

**What IS included:**
- `IBlueprintDebugSession` interface (§2.1): all methods and events listed, with `OnPinValueChanged<T>(T)` constrained to `where T : unmanaged` per Patch 2.
- `IBlueprintProbeSink` interface: `OnNodeEnter`, `OnPinValueChanged<T> where T : unmanaged`, `OnPeerCallEnter`, `OnPeerCallExit`.
- `IBlueprintTimeController` interface (provided by `TASK-DBG-000`): `RequestPause()`, `RequestResume()`, `RequestStepOneTick()`, `IsPausedByDebugger` property.
- `BlueprintDebugSession` constructor explicitly expects `IBlueprintTimeController` dependency from `TASK-DBG-000`: `(BlueprintRegistry registry, ISimulationView view, IBlueprintTimeController timeController)`.
- `DebugProbe` static class (§3): `public static IBlueprintProbeSink? Sink` field, static probe methods (`NodeEnter`, `PinValueChanged<T> where T : unmanaged`, `PeerCallEnter`, `PeerCallExit`), all using `Sink?.OnX(...)` null-conditional dispatch. Thread-safety note from §3.4: `Sink` assignment is a single-reference write (atomic on 64-bit platforms); no lock needed for the read path.
- `BlueprintDebugSession` class skeleton with constructor `(BlueprintRegistry registry, ISimulationView view, IBlueprintTimeController timeController)` per Patch 1 -- stub implementations of `IBlueprintDebugSession` that throw `NotImplementedException` (filled in by subsequent tasks).
- `MockTimeController` for tests: exposes `PauseWasRequested: bool`, `PauseRequestCount: int`, `ResumeCount: int`, `StepRequestCount: int`.
- All event and data record types: `BreakpointHit`, `PinValueChanged` (per Patch 2: `byte[] ValueBytes` + `Type ValueType`, NOT `object Value`), `NodeHistoryEntry`, `WatchId`, `BreakpointId`.

**What is NOT included:**
- Breakpoint matching logic (DBG-003).
- Watch implementation (DBG-004).
- Debug map loading (DBG-002).

### Constraints

- `DebugProbe` MUST NOT contain any locking primitives -- probe calls must be allocation-free and lock-free.
- `IBlueprintTimeController.RequestPause()` is called without blocking -- callers must return within nanoseconds of the call.
- `PinValueChanged` record uses `byte[] ValueBytes` (NOT `object Value`) per Patch 2.
- `Sink` is `public static IBlueprintProbeSink?` -- writable for test injection.

### Success Conditions

- SC1: `DebugProbe.NodeEnter(entity, "n1")` with null Sink -- no exception, zero allocation.
- SC2: `DebugProbe.PinValueChanged<int>(entity, "p1", 42)` with null Sink -- no exception, zero allocation.
- SC3: `IBlueprintTimeController.RequestPause()` called by session on breakpoint hit (verified via `MockTimeController.PauseWasRequested`).
- SC4: `PinValueChanged` record has `ValueBytes` property (not `Value` of type `object`).
- SC5: `dotnet build` zero errors.

---

## TASK-DBG-002 -- Debug Map Format and Node-ID Resolution

**Phase:** 5 -- Debug Protocol
**Design Reference:** [Debug Protocol DD §4](./Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md#4-debug-map-format), [§5](./Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md#5-node-id-resolution-and-structure-hash-safety)
**Effort:** 2-3 days

### Scope

**What IS included:**
- `DebugMap` record and all nested types from §4 (if not already created by CP-005): `NodeDebugInfo` (NodeId, GraphId, NodeKind, StartLine, EndLine, BlockLabel, SourceNodeId), `GraphDebugInfo`, `DebugMapHeader` (AssetId, AssetName, StructureHash, CompilerVersion).
- `IBlueprintDebugSession.RegisterDebugMap(Guid assetId, DebugMap map)` implementation: stores map in `_debugMaps` dictionary, re-indexes `_nodeById` cache.
- `IBlueprintDebugSession.UnregisterDebugMap(Guid assetId)` implementation: removes map, clears stale watch associations.
- Node-ID resolution (§5): `ResolveNode(string nodeIdString)` looks up node in all registered maps. `TryFindNodeAcrossAllMaps(string nodeIdString, out NodeDebugInfo node)`.
- Structure-hash safety (§5.3): when `RegisterDebugMap` is called, if a map is already registered for the same `AssetId` with a different `StructureHash`, existing breakpoints on that asset are cleared + `OnBreakpointListChanged` fired; existing watches on that asset are marked stale + `OnWatchStale` event fired.
- `IBlueprintDebugSession.GetNodeHistory(Entity entity)` implementation: returns snapshot of execution history ring-buffer for the entity; `NodeHistoryEntry` has NodeId, SimTick, SimTime.
- `ExecutionHistory` ring-buffer: per-entity, capacity 256 entries (configurable), per §2.3.
- `DebugMapSerializer` (if not already in CP-005): JSON load from `.dbgmap.json` with deterministic field ordering. `DebugMapSerializer.Load(string json)` and `Save(DebugMap map)`.

**What is NOT included:**
- Breakpoint matching (DBG-003).
- Watch expression storage (DBG-004).
- Compiler-side map generation (CP-005, already done).

### Constraints

- `RegisterDebugMap` replaces existing map atomically (one dictionary insert); callers may not call `RegisterDebugMap` concurrently.
- `NodeHistoryEntry` ring-buffer must not allocate on write (pre-allocated array, index wrapping).
- Structure-hash mismatch on `RegisterDebugMap` fires `OnBreakpointListChanged` and `OnWatchStale` -- test must verify this.

### Success Conditions

- SC1: `RegisterDebugMap` + `ResolveNode("n-abc123")` returns correct `NodeDebugInfo`.
- SC2: `RegisterDebugMap` with mismatched `StructureHash` fires `OnBreakpointListChanged` and marks watches stale.
- SC3: `GetNodeHistory` returns up to 256 recent entries in chronological order.
- SC4: `NodeHistoryEntry` ring-buffer write: zero heap allocations.
- SC5: `dotnet build` zero errors.

---

## TASK-DBG-003 -- Breakpoints and Step Semantics

**Phase:** 5 -- Debug Protocol
**Design Reference:** [Debug Protocol DD §6](./Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md#6-breakpoints), [§7](./Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md#7-step-semantics-for-visual-scripts), [Debug Protocol DD Patch 1](./Blueprint_Subsystem_Debug_Protocol_Detailed_Design_InlinePatches.md#patch-1--soft-pause-via-time-controller-request-supersedes-16-64-65-7x)
**Effort:** 3-4 days

### Scope

**What IS included:**
- `IBlueprintDebugSession.SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId)` -> `BreakpointId`: stores in `_breakpoints` dict, returns unique id.
- `IBlueprintDebugSession.RemoveBreakpoint(BreakpointId id)`.
- `IBlueprintDebugSession.GetBreakpoints()` -> immutable list.
- `IBlueprintDebugSession.OnNodeEnter(Entity self, string nodeId)` implementation (§6.3): resolves node; if node has a breakpoint AND `_isPaused == false`, calls `HandleBreakpointHit` (per Patch 1: captures snapshot, calls `_timeController.RequestPause()`, fires `OnBreakpointHit`, returns immediately -- NO blocking).
- Hit count tracking on `Breakpoint` record.
- `IBlueprintDebugSession.Continue()` implementation (Patch 1): clears pause state, calls `_timeController.RequestResume()`.
- `IBlueprintDebugSession.StepOver()` implementation (Patch 1): sets `_stepMode = StepMode.Over`, captures step-from context, clears pause, calls `_timeController.RequestStepOneTick()`.
- `IBlueprintDebugSession.StepInto()` implementation (Patch 1): `StepMode.Into`.
- `IBlueprintDebugSession.StepOut()` implementation (Patch 1): `StepMode.Out`.
- `StepMode` enum: `None`, `Over`, `Into`, `Out`.
- `HandleStepMatchingForNode(Entity self, string nodeId)` -- §7 matching logic adapted for soft pause (per Patch 1): if condition matched, calls `HandleBreakpointHit` with pseudo-breakpoint; no thread-blocking.
- `OnPeerCallEnter` / `OnPeerCallExit` implementations: update `_currentCallDepth` counter (per entity), notify `OnPeerCallChanged` event.
- `_currentCallDepth` per-entity dictionary.
- `CaptureStateSnapshot(Entity self, Guid assetId)` -- reads slot bytes from `ISimulationView.GetComponentRO` for the entity's blackboard component, stores as `StateSnapshot` record.

**What is NOT included:**
- Watch expression storage (DBG-004).
- Multi-entity debug views (DBG-005).

### Constraints

- `HandleBreakpointHit` MUST NOT block the thread (Patch 1). It MUST call `_timeController.RequestPause()` before returning.
- `_isPaused` guards re-entrant pause -- only the first breakpoint hit per paused-already session requests pause.
- `StepOver` logic: matches next `OnNodeEnter` for same entity AND (same call depth OR shallower depth).
- `StepInto` logic: matches next `OnNodeEnter` for same entity at any depth.
- `StepOut` logic: matches first `OnNodeEnter` for same entity at shallower depth.

### Success Conditions

- SC1: `Breakpoint_FiresOnNodeEntry_RequestsPauseOncePerFrame` test (from Patch 1 doc): `MockTimeController.PauseWasRequested == true`, `PauseRequestCount == 1` even if multiple entities hit same BP.
- SC2: `Continue()` calls `_timeController.RequestResume()`; `_isPaused` becomes `false`.
- SC3: `StepOver` test: tick 1 hits BP, tick 2 advances via `RequestStepOneTick`, `_stepMode` clears after match.
- SC4: `HandleBreakpointHit` records correct `BreakpointHit.Self`, `BreakpointHit.SimulationTime`, `BreakpointHit.Tick`.
- SC5: `dotnet build` zero errors.

---

## TASK-DBG-004 -- Watch Expressions and Pin-Value Snapshotting

**Phase:** 5 -- Debug Protocol
**Design Reference:** [Debug Protocol DD §8](./Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md#8-watch-expressions-and-pin-value-snapshotting), [Debug Protocol DD Patch 2](./Blueprint_Subsystem_Debug_Protocol_Detailed_Design_InlinePatches.md#patch-2--constrain-pinvaluechangedt-to-unmanaged-write-into-byte-buffer-supersedes-83)
**Effort:** 2-3 days

### Scope

**What IS included:**
- `Watch` sealed class per Patch 2: `Id: WatchId`, `AssetId`, `GraphId`, `PinId`, `PinIdString`, `DisplayName`, `ExpectedType`, `ExpectedSizeBytes`, 64-byte `private readonly byte[] _valueBuffer`, `ReadOnlySpan<byte> LastValueBytes`, `Entity LastUpdateEntity`, `uint LastUpdateTick`, `int UpdateCount`, `bool HasEverBeenWritten`, `bool IsStale`, `internal void WriteValue<T>(T value, Entity self, uint tick) where T : unmanaged` using `Unsafe.WriteUnaligned`.
- `IBlueprintDebugSession.AddWatch(Guid assetId, Guid graphId, Guid pinId, string displayName, Type expectedType)` -> `WatchId`: creates `Watch`, registers in `_watchesByPinIdString` lookup.
- `IBlueprintDebugSession.RemoveWatch(WatchId id)`.
- `IBlueprintDebugSession.GetWatch(WatchId id)` -> `Watch?`.
- `IBlueprintDebugSession.GetWatches(Guid assetId)` -> all watches for asset.
- `OnPinValueChanged<T>(Entity self, string pinId, T value) where T : unmanaged` implementation: lookup in `_watchesByPinIdString`; call `watch.WriteValue`; if listeners: fire `OnPinValueChanged` event with `PinValueChanged { ValueBytes = watch.LastValueBytes.ToArray(), ValueType = watch.ExpectedType, ... }`.
- `MarshalFromBytes(byte[] bytes, Type type)` helper: `MemoryMarshal.Read<T>` dispatch for primitives, reflection-based for structs (UI decode only, called in editor rendering, not on probe path).
- `StateInspector` (§8.5): `InspectState(Entity self, Guid assetId, DebugMap map, BlueprintRegistry registry)` -> reads slot bytes from view, maps field offsets to variable names using debug map + field layout, returns `IReadOnlyList<VariableSnapshot>`. `VariableSnapshot` record: `Name`, `Type`, `ValueBytes`.
- `OnWatchStale` event on session: fired when a `RegisterDebugMap` causes structure-hash mismatch (from DBG-002), also fired per-watch when removed.

**What is NOT included:**
- Breakpoint logic (DBG-003).
- Multi-entity (DBG-005).
- Editor UI (Editor DD).

### Constraints

- `Watch._valueBuffer` is allocated ONCE at construction, reused for every update -- zero per-update allocation.
- `OnPinValueChanged<T>` path with no listeners: zero allocation (no `ToArray()`).
- `Watch.WriteValue<T>` throws `InvalidOperationException` if `Unsafe.SizeOf<T>() > 64`.
- `MarshalFromBytes` is called only in UI/inspection path, not in probe path.

### Success Conditions

- SC1: `AddWatch` + `OnPinValueChanged<int>` with no listener: zero allocations (measured with `AllocationBenchmark`).
- SC2: `AddWatch` + `OnPinValueChanged<int>` with listener: exactly one allocation (the `byte[]` in `PinValueChanged`).
- SC3: `Watch.WriteValue<Matrix4x4>` stores 64 bytes, `LastValueBytes.Length == 64`.
- SC4: `Watch.WriteValue` with a struct larger than 64 bytes: throws `InvalidOperationException`.
- SC5: `MarshalFromBytes(bytes, typeof(int))` correctly decodes a 4-byte little-endian int.
- SC6: `StateInspector.InspectState` returns named `VariableSnapshot` entries matching the debug map's variable list.
- SC7: `dotnet build` zero errors.

---

## TASK-DBG-005 -- Multi-Entity Debugging, PDB Integration, Hot Reload Interaction

**Phase:** 5 -- Debug Protocol
**Design Reference:** [Debug Protocol DD §9](./Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md#9-multi-entity-debugging), [§10](./Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md#10-pdb-integration-for-source-line-breakpoints), [§11](./Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md#11-hot-reload-interaction)
**Effort:** 2-3 days

### Scope

**What IS included:**
- Multi-entity execution history: per-entity ring-buffer already provisioned in DBG-002; `GetNodeHistory(Entity entity)` returns entity-specific history.
- Entity filter on session: `IBlueprintDebugSession.SetEntityFilter(Entity? entity)` -- when set, probes from non-matching entities are skipped (breakpoints don't fire, history not updated). `GetEntityFilter()` accessor.
- `IBlueprintDebugSession.GetActiveEntities(Guid assetId)` -> list of entities currently executing that blueprint (tracked via `OnPeerCallEnter`/`Exit` or tick probe).
- PDB integration (§10): `IBlueprintDebugSession.RegisterPdbLocator(Guid assetId, Func<string> pdbPathResolver)` -- when a breakpoint fires, if a PDB locator is registered, the session populates `BreakpointHit.SourceFilePath` and `BreakpointHit.SourceLine` by looking up the node's start line in the debug map (matches to line in generated `.g.cs`). The locator is just a path provider -- the actual PDB is opened by the IDE debugger.
- Hot reload interaction (§11): `IBlueprintDebugSession.OnHotReloadBegin()` and `OnHotReloadCompleted(Guid[] reloadedAssetIds)` methods. `OnHotReloadBegin`: if paused, calls `Continue()` first; marks watches on affected assets stale; fires `OnSessionStateChanged`. `OnHotReloadCompleted`: re-resolves debug maps for reloaded assets (if coordinator provides updated maps); fires `OnBreakpointListChanged` for affected assets. Wiring to coordinator via event subscription.

**What is NOT included:**
- Actual IDE/DAP protocol adapter (Slice 2).
- Multi-process debugging (Slice 2).

### Constraints

- Entity filter must be applied consistently: if filter is set, `OnNodeEnter` returns immediately without breakpoint check for non-matching entities.
- `OnHotReloadBegin()` must call `Continue()` if `_isPaused == true` -- cannot leave session in paused state across a reload.
- Hot reload interaction tests must verify: (a) paused session resumes on reload begin, (b) watches on reloaded asset become stale, (c) debug maps updated after reload completed.

### Success Conditions

- SC1: Entity filter set to entity A; entity B triggers breakpoint node -> session stays unpaused.
- SC2: Entity filter set to entity A; entity A triggers breakpoint -> session pauses.
- SC3: `OnHotReloadBegin()` while paused: session calls `Continue()`, `_isPaused == false`.
- SC4: `OnHotReloadCompleted(new[] { assetId })`: watches on that asset marked stale.
- SC5: `dotnet build` zero errors.

---

## TASK-DBG-006 -- Debug Protocol Test Suite

**Phase:** 5 -- Debug Protocol
**Design Reference:** [Debug Protocol DD §12](./Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md#12-test-strategy)
**Effort:** 3-4 days

### Scope

**What IS included:**
All test files from §12, updated to reflect Patches 1 and 2:
- `Debug/DebugProbe/ProbeDispatchTests.cs`: null sink no-op, non-null sink calls forwarded, allocation-free dispatch.
- `Debug/Session/BreakpointTests.cs`: `Breakpoint_FiresOnNodeEntry_RequestsPauseOncePerFrame` (Patch 1 version), hit count, remove breakpoint, structure-hash mismatch clears breakpoints.
- `Debug/Session/StepTests.cs`: StepOver, StepInto, StepOut under soft-pause model (Patch 1); verify `MockTimeController.StepRequestCount == 1`.
- `Debug/Session/WatchTests.cs`: add watch, `OnPinValueChanged` writes to buffer, zero-alloc path, listener-attached path (1 alloc), stale watch on reload.
- `Debug/Session/StateInspectorTests.cs`: `InspectState` returns named variable snapshots, `MarshalFromBytes` roundtrips.
- `Debug/Session/NodeHistoryTests.cs`: ring buffer fills, wraps, entity-specific history.
- `Debug/Session/MultiEntityTests.cs`: entity filter, `GetActiveEntities`.
- `Debug/Session/HotReloadInteractionTests.cs`: pause cleared on reload begin, watches stale on reload completed.
- `Debug/DebugMap/DebugMapLoadTests.cs`: load from JSON, structure-hash safety, node resolution.
- `Debug/Benchmarks/ProbeOverheadBenchmarks.cs`: probe-call overhead < 50ns (Q-13.5 CI gate); allocation benchmark confirming zero allocs in no-listener Trace mode.
- `MockTimeController.cs` in test helpers.

All tests use `BlueprintTestFixture` from Phase 1 (with `MockTimeController` from DBG-001).

**What is NOT included:**
- DAP protocol adapter tests (Slice 2).
- End-to-end editor integration tests (Editor DD).

### Constraints

- All tests compile with `FullyQualifiedName~Debug` filter.
- `ProbeOverheadBenchmarks` must run as BenchmarkDotNet benchmarks and be included in the M12 CI gate (§12.5 acceptance criteria from DD).
- `WatchTests.cs` zero-alloc test uses the existing `AllocationBenchmark` pattern from the test harness.
- `Breakpoint_FiresOnNodeEntry_RequestsPauseOncePerFrame` asserts `MockTimeController.PauseRequestCount == 1` (not > 1, even if multiple entities hit the same BP).

### Success Conditions

- SC1: `dotnet test --filter "FullyQualifiedName~Debug"` reports zero failures, zero skipped.
- SC2: `ProbeOverheadBenchmarks` confirms < 50ns per probe call in no-sink path.
- SC3: Watch zero-alloc test passes in no-listener mode (0 allocations).
- SC4: `Breakpoint_FiresOnNodeEntry_RequestsPauseOncePerFrame` passes with `PauseRequestCount == 1`.
- SC5: `DebugMapLoadTests` confirm stale-watch firing on structure-hash mismatch.
- SC6: Step tests confirm `StepRequestCount == 1` per step command.

---

## Phase 6 -- Editor

---

## TASK-ED-001 -- Editor Infrastructure: Window Lifecycle, IWindowRegistrar, Time-Controller Adapter

**Phase:** 6 -- Editor
**Design Reference:** [Editor DD §1](./Blueprint_Subsystem_Editor_Detailed_Design.md#1-overview-and-design-goals), [§2](./Blueprint_Subsystem_Editor_Detailed_Design.md#2-window-architecture-and-lifecycle), [§3](./Blueprint_Subsystem_Editor_Detailed_Design.md#3-iwindowregistrar-wiring), [§13](./Blueprint_Subsystem_Editor_Detailed_Design.md#13-time-controller-adapter)
**Effort:** 2-3 days

### Scope

**What IS included:**
- `IBlueprintEditorWindow` interface (§2.1): `DrawUI()`, `OnActivated()`, `OnDeactivated()`, `string Title`, `bool IsVisible`, `void ToggleVisible()`.
- `BlueprintEditorWindowBase` abstract class implementing `IBlueprintEditorWindow` with default boilerplate.
- `BlueprintEditorModule` class (§2.3 + §3): implements `IEditorModule`, owns all Blueprint editor windows, registers via `IWindowRegistrar` in `OnEditorActivated()`. Subscribes to `coordinator.OnReloadCompleted` (per Patch 2).
- Window singleton registry in `BlueprintEditorModule`: holds one instance of each window type, creates on first access.
- `IWindowRegistrar` wiring (§3): `RegisterMenuEntry`, `RegisterToolbarEntry`, `RegisterShortcut` for Blueprint-related editor features.
- `DirtyTracker` class (§3.2): tracks which asset Guids have unsaved edits. `MarkDirty(Guid)`, `MarkClean(Guid)`, `IsDirty(Guid)`, `DirtyAssets: IReadOnlySet<Guid>`.
- `EditorSelectionStore` (§3.2): holds the currently selected `BlueprintAsset?`. `SelectAsset(BlueprintAsset?)`, `SelectedAsset: BlueprintAsset?`, `OnSelectionChanged` event.
- `IOutputConsole` interface (§3.2): `LogInfo`, `LogWarning`, `LogError`, `LogDebug`, `LogDiagnostic(Diagnostic d)`.
- `EditorState` class (§3.2): in-memory registry of currently-loaded-and-editable assets. `SetInMemoryAsset(BlueprintAsset)`, `GetInMemoryAsset(Guid) -> BlueprintAsset?`, `RemoveInMemoryAsset(Guid)`.
- `IBlueprintTimeController` adapter (§13): `EngineTimeControllerAdapter` wrapping the engine's actual time-control mechanism. The class resolves the concrete engine type via a constructor parameter (per Q-16.1: class name discovered during M13 implementation). `MockTimeController` for tests.
- `IAssetCatalog` interface (§10.5, Patch 1): `EnumerateAll() -> IEnumerable<AssetCatalogEntry>`. `AssetCatalogEntry` record: `Guid AssetId`, `string Path`. `FileSystemAssetCatalog` implementation: walks a configured directory for `*.bp.json` files.
- DI registration helpers (§3.3): extension method `AddBlueprintEditor(this IServiceCollection services, ...)`.

**What is NOT included:**
- Any window UI rendering (ED-002 through ED-005).
- Quick Reload / Full Rebuild pipelines (ED-005).

### Constraints

- `BlueprintEditorModule.OnReloadCompleted` routes by `info.Source` per Patch 2 (no disk read for Quick Reload).
- `DirtyTracker` and `EditorSelectionStore` are shared singletons injected into all windows.
- `EngineTimeControllerAdapter` class name is a placeholder; actual engine type discovered in M13 but the adapter interface is fully implemented.

### Success Conditions

- SC1: `BlueprintEditorModule.OnEditorActivated()` registers all window menu entries without throwing.
- SC2: `DirtyTracker.MarkDirty(id)` -> `IsDirty(id) == true`; `MarkClean(id)` -> `IsDirty(id) == false`.
- SC3: `FileSystemAssetCatalog.EnumerateAll()` returns one entry per `*.bp.json` file in the configured directory.
- SC4: `EngineTimeControllerAdapter` implements `IBlueprintTimeController` with all four members.
- SC5: `dotnet build` zero errors.

---

## TASK-ED-002 -- Asset Browser and Graph Editor Windows

**Phase:** 6 -- Editor
**Design Reference:** [Editor DD §4](./Blueprint_Subsystem_Editor_Detailed_Design.md#4-asset-browser-window), [§5](./Blueprint_Subsystem_Editor_Detailed_Design.md#5-graph-editor-window)
**Effort:** 5-7 days

### Scope

**What IS included:**
- `AssetBrowserWindow` class (§4): ImGui table listing all `.bp.json` assets found by `IAssetCatalog`. Columns per §4.3 (Name, Dispatch, Hostings, Status). Double-click opens in Graph Editor. New/Save/Delete actions. Dirty indicator. `FilterBar` text filter. Context menu per row.
- `AssetBrowserWindow.RefreshCatalog()`: calls `catalog.EnumerateAll()`, updates displayed list.
- `NewAssetDialog` (§4.5): modal dialog for new asset creation (Name, Dispatch selector, AiPrimitive Hosting checkboxes). Creates in-memory `BlueprintAsset`, marks dirty.
- `GraphEditorWindow` class (§5): canvas with pan/zoom (§5.3). `NodeRenderer.Render(node, selected)`. `LinkRenderer.Render(link, isLatent)`. `PinSocket.Render(pin, value)`. Node drag-and-drop, link drawing (§5.5). `NodePalette` sidebar (§5.6): grouped list of node types from `INodeRegistry`, drag to add.
- Graph commands: Add Node, Delete Node, Add Link, Delete Link, Group Select, Undo, Redo (§5.7 CommandHistory). `IGraphCommand` interface, `CommandHistory` (capacity 64).
- `SelectionState` (§5.4): `SelectedNodes: HashSet<Guid>`, `SelectedLinks: HashSet<Guid>`.
- Debug overlay rendering (§5.8): when `session.IsPaused`, highlight breakpoint-hit node; execution history trail (recent 10 nodes dimly highlighted).
- `INodeRegistry` integration: palette built from `INodeRegistry.GetAllNodeDescriptors()` (stub -- implementations from Compiler DD's catalog integration).
- Read debug map for overlay: `session.GetNodeHistory(entity)` -> highlight recent nodes.

**What is NOT included:**
- StructEdit-based inspector (ED-003).
- Watch Panel / Debug Panel windows (ED-004).

### Constraints

- Graph canvas is ImGui `InvisibleButton`-based click-detect plus `GetWindowDrawList()` custom rendering per §5.3.
- Undo/Redo must work for all graph commands; CommandHistory stores before/after snapshots.
- When `session.IsPaused`, the graph must not be editable -- all graph editing commands are disabled with a status overlay "Paused at breakpoint."

### Success Conditions

- SC1: `AssetBrowserWindow.DrawUI()` with empty catalog renders without ImGui errors.
- SC2: `NewAssetDialog` creates a `BlueprintAsset` in `EditorState`, marks it dirty in `DirtyTracker`.
- SC3: `CommandHistory.Undo()` after AddNode restores the node count.
- SC4: `SelectionState` cleared on graph switch.
- SC5: `dotnet build` zero errors.

---

## TASK-ED-003 -- Inspector Window and StructEdit Drawer Infrastructure

**Phase:** 6 -- Editor
**Design Reference:** [Editor DD §6](./Blueprint_Subsystem_Editor_Detailed_Design.md#6-inspector-window-structedit-driven), [§7](./Blueprint_Subsystem_Editor_Detailed_Design.md#7-structedit-drawer-infrastructure)
**Effort:** 4-5 days

### Scope

**What IS included:**
- `InspectorWindow` class (§6): renders selected node's properties, graph properties, asset-level properties. Three-tab layout: Node, Graph, Asset (§6.2).
- `IStructEditDrawer<T>` interface (§7.1): `bool Draw(string label, ref T value, DrawContext ctx)` -- returns `true` if value was modified.
- Drawer implementations (§7.2-7.7):
  - `PrimitiveDrawers`: `IntDrawer`, `FloatDrawer`, `BoolDrawer`, `StringDrawer`.
  - `EnumDrawer<TEnum>` generic: ImGui Combo box with reflection-based enum names.
  - `GuidDrawer`: display only (read-only Guid field).
  - `NodeListDrawer`: list-of-node-ids as ImGui ListBox.
  - `BlueprintTypeRefDrawer`: dropdown built from `ITypeRegistry.GetAllTypes()` + search filter.
  - `HostingsDrawer`: checkbox list for `AiPrimitiveHosting` flags.
  - `CallablePeersDrawer`: list of known peer asset names from catalog.
  - `DispatchKindDrawer`: `BlueprintDispatchKind` ComboBox.
- `DrawerRegistry` (§7.8): dictionary `Type -> object (IStructEditDrawer<T>)`. `Register<T>(IStructEditDrawer<T>)`. `TryGet<T>(out IStructEditDrawer<T> drawer)`.
- `DrawContext` record: `bool IsReadOnly`, `string IdPrefix`, optional `ITypeRegistry TypeRegistry`.
- Dirty notification on mutation: drawer returns `true` -> caller marks asset dirty in `DirtyTracker`.
- `PropertySheet.Draw(T value, DrawerRegistry registry, DrawContext ctx)`: reflection walk over public properties, dispatches per-property to registered drawer.

**What is NOT included:**
- Debug overlay inspector (that's in DBG-004/ED-004).
- Graph canvas rendering (ED-002).

### Constraints

- All drawer `Draw(...)` calls must not allocate on a no-mutation call (reflection walk allocates during `PropertySheet.Draw`; that's acceptable at draw-time but the draw should not re-register or re-build lookup structures per frame).
- Drawers must handle `IsReadOnly = true` by rendering read-only ImGui controls (no-op on mutation attempt).

### Success Conditions

- SC1: `FloatDrawer.Draw("Speed", ref speed, ctx)` renders an ImGui InputFloat and returns true when value changes.
- SC2: `DrawerRegistry.TryGet<float>(out var d)` returns the registered `FloatDrawer`.
- SC3: `InspectorWindow.DrawUI()` with a selected node renders Node tab without ImGui errors.
- SC4: Mutation in `FloatDrawer` -> `DirtyTracker.IsDirty(assetId) == true`.
- SC5: `dotnet build` zero errors.

---

## TASK-ED-004 -- Debug Panel, Watch Panel, Callstack Window, Hot Reload Log

**Phase:** 6 -- Editor
**Design Reference:** [Editor DD §8](./Blueprint_Subsystem_Editor_Detailed_Design.md#8-debug-panel--watch-panel--callstack-windows), [§9](./Blueprint_Subsystem_Editor_Detailed_Design.md#9-hot-reload-log-window)
**Effort:** 3-4 days

### Scope

**What IS included:**
- `DebugPanelWindow` class (§8.1): shows current pause state, breakpoint list, step buttons. "Continue" / "Step Over" / "Step Into" / "Step Out" buttons -> call `session.Continue()` etc. Breakpoint list (assetId, nodeId, hit count). "Set Breakpoint" flow: user clicks node in Graph Editor while debug mode active -> session.SetBreakpoint. "Remove Breakpoint" action.
- Pause indicator in title bar (§8.2): when `session.IsPaused`, debug panel title appended " [PAUSED]" or color-coded.
- `WatchPanelWindow` class (§8.3): table of watches (Name, Type, Value, Tick, Stale?). "Add Watch" button -> shows modal with assetId / graphId / pinId selector. Value column uses `MarshalFromBytes(watch.LastValueBytes, watch.ExpectedType)` for display. Stale watches shown with warning.
- "Value changed" highlight: row flashes for 1 second after last write (per §8.4 visual feedback requirement).
- Subscription to `session.OnPinValueChanged`: updates watch display data.
- `CallstackWindow` class (§8.5): shows `GetNodeHistory(focusedEntity)` as a list. Each entry: Node name, GraphId, SimTick. Click -> selects that node in Graph Editor.
- `HotReloadLogWindow` class (§9): scrollable log table with columns: Timestamp, Source (Quick/Full), Outcome (Success/Fail), AssetCount, DurationMs. Subscribes to `coordinator.OnReloadCompleted` and `coordinator.OnReloadFailed`. "Clear Log" button. Max 1000 entries (ring buffer).
- `ReloadLogEntry` record: Timestamp, Source, Outcome, Message, DurationMs.
- "Greyed-out" Quick Reload button while paused: per Debug Protocol DD §11.4 and Editor DD §12.5 -- when `session.IsPaused`, Quick Reload toolbar button is disabled with tooltip "Cannot reload while paused at breakpoint."

**What is NOT included:**
- Quick Reload / Full Rebuild trigger logic (ED-005).
- Watch expression storage (uses `IBlueprintDebugSession` from DBG-004).

### Constraints

- `WatchPanelWindow` subscribes to `session.OnPinValueChanged`; the subscription must be removed on `OnDeactivated()`.
- `HotReloadLogWindow.OnReloadCompleted` routes by `info.Source` (per Patch 2) to populate log entry correctly.
- All windows implement `OnActivated`/`OnDeactivated` to subscribe/unsubscribe events.

### Success Conditions

- SC1: `DebugPanelWindow.DrawUI()` while `session.IsPaused` shows pause indicator and enabled step buttons.
- SC2: `WatchPanelWindow` shows "Stale" indicator after `session.OnWatchStale` fires.
- SC3: `HotReloadLogWindow` shows last 1000 reload events, oldest evicted beyond cap.
- SC4: `CallstackWindow` row click fires `EditorSelectionStore.SelectNode(nodeId)`.
- SC5: `dotnet build` zero errors.

---

## TASK-ED-005 -- Quick Reload, Full Rebuild, Debug Session Lifecycle

**Phase:** 6 -- Editor
**Design Reference:** [Editor DD §10](./Blueprint_Subsystem_Editor_Detailed_Design.md#10-quick-reload-pipeline), [§11](./Blueprint_Subsystem_Editor_Detailed_Design.md#11-full-rebuild-pipeline), [§12](./Blueprint_Subsystem_Editor_Detailed_Design.md#12-editors-debug-session-lifecycle), [Editor DD Patches 1-3](./Blueprint_Subsystem_Editor_Detailed_Design_InlinePatches.md)
**Effort:** 4-6 days

### Scope

**What IS included:**
- `QuickReloadService` class (§10 + Patches 1-3):
  - `TriggerAsync(BlueprintAsset asset, CompilerMode mode)` -> `QuickReloadResult`.
  - `BuildSiblingSignatures(BlueprintAsset editedAsset)` per Patch 1: walk `IAssetCatalog.EnumerateAll()` via `BlueprintSignatureParser`, dirty-aware in-memory merge via `EditorState.GetInMemoryAsset`, add edited asset via `BlueprintSignatureBuilder.FromInMemoryAsset`.
  - Registrar invocation step (Patch 3): `HsmActionDispatcher.ClearAll()` BEFORE registrars; fresh `BehaviorRegistry stagingRegistry`; `coordinator.BeginStaging()` for `blueprintStaging`; reflection-based `InvokeAllRegistrars`.
  - Debug map registration step (Patch 2): `session.RegisterDebugMap(asset.AssetId, result.DebugMap)` BEFORE `ApplyQuickReload`; rollback via `session.UnregisterDebugMap` on apply failure.
  - Call `coordinator.ApplyQuickReload(alc, behaviorStaging, blueprintStaging)` per Patch 3 signature.
  - `QuickReloadResult` record: `Succeeded: bool`, `Diagnostics`, `DurationMs`.
  - `LastSignaturesUsedForTesting: IReadOnlyList<BlueprintSignature>` (internal test accessor).
- `BlueprintSignatureBuilder.FromInMemoryAsset(BlueprintAsset)` static helper (Patch 1).
- Updated `AiHotReloadCoordinator.ApplyQuickReload(AssemblyLoadContext, BehaviorRegistry behaviorStaging, BlueprintRegistryStaging blueprintStaging)` per Patch 3: atomic commit + merge + `_currentAlc` swap + fire `OnReloadCompleted(QuickReloadViaApi)`.
- Updated `AiHotReloadCoordinator.OnReloadCompleted` event type: `Action<ReloadCompletedInfo>?`. `ReloadCompletedInfo` record, `ReloadSource` enum.
- `FullRebuildService` class (§11): `TriggerAsync()` -> spawns MSBuild process (`dotnet build`) via `Process.Start`, streams output to `IOutputConsole`, awaits completion, returns `FullRebuildResult`. On success: `coordinator.DrainPendingCallbacks()` at next frame boundary (caller responsibility -- the service just sets a flag `PendingDrainAfterBuild`).
- `BlueprintEditorModule.OnReloadCompleted` handler per Patch 2: `QuickReloadViaApi` source -> no disk read (map already registered); `FullRebuildViaFileWatcher` -> walk DLL directory for `*.dbgmap.json` + `session.RegisterDebugMap`.
- Debug session lifecycle (§12): `BlueprintEditorModule` owns `IBlueprintDebugSession`. `InitializeSession()` creates `BlueprintDebugSession(registry, view, timeController)`. Session started lazily on first debug-mode compile. Session cleared on Full Rebuild (§12.2: `session.OnHotReloadBegin()` before reload, `OnHotReloadCompleted([reloadedIds])` after).
- `QuickReloadToolbarButton` -- disabled when `session.IsPaused` (per ED-004 §8 greyed-out rule).

**What is NOT included:**
- Window rendering (ED-002 through ED-004).
- StructEdit drawers (ED-003).

### Constraints

- `BuildSiblingSignatures` MUST NOT use `BlueprintRegistry.GetAll()` (Patch 1).
- `QuickReloadService` must call `HsmActionDispatcher.ClearAll()` BEFORE invoking any registrar (Patch 3).
- Debug map must be registered BEFORE `ApplyQuickReload` so `OnReloadCompleted` subscribers see consistent state (Patch 2).
- `ApplyQuickReload` fires `OnReloadCompleted` with `Source = QuickReloadViaApi` and `DllPath = null`.
- Full Rebuild triggers via `Process.Start("dotnet build ...")` for Slice 1 (Q-16.2 resolution).
- `coordinator.OnReloadCompleted` is now `Action<ReloadCompletedInfo>?` -- existing subscribers of `Action?` from HR-001 must be updated.

### Success Conditions

- SC1: `QuickReloadService.TriggerAsync` success case: `result.Succeeded == true`, `coordinator.OnReloadCompleted` fires with `Source == QuickReloadViaApi`.
- SC2: `QuickReloadService.TriggerAsync` with dirty sibling asset A: `LastSignaturesUsedForTesting` contains A's in-memory signature (from Patch 1 test in patches doc).
- SC3: Apply failure rollback: `session.GetDebugMap(assetId)` returns null after a failed `ApplyQuickReload`.
- SC4: `FullRebuildService.TriggerAsync` spawns a `dotnet build` process and returns its exit code.
- SC5: Full Rebuild's `OnReloadCompleted` handler reads `.dbgmap.json` from DLL directory; Quick Reload's handler does not attempt disk read.
- SC6: `dotnet build` zero errors.

---

## TASK-ED-006 -- Editor Preferences, Configuration, and Editor Test Suite

**Phase:** 6 -- Editor
**Design Reference:** [Editor DD §14](./Blueprint_Subsystem_Editor_Detailed_Design.md#14-editor-preferences-and-configuration), [§15](./Blueprint_Subsystem_Editor_Detailed_Design.md#15-editor-test-strategy)
**Effort:** 3-4 days

### Scope

**What IS included:**
- `BlueprintEditorPreferences` class (§14): `AutoReloadOnSave: bool`, `DefaultCompilerMode: CompilerMode`, `WatchPanelVisible: bool`, `GraphEditorGridSnap: float`, `NodeHistorySize: int (max 256)`, `HotReloadLogMaxEntries: int`. Serialized to JSON in `AppData` or engine config path.
- `PreferencesWindow` class (§14.2): ImGui form rendering all preference fields. "Save" button writes to disk. "Reset to Defaults" button.
- `BlueprintEditorConfiguration` record (§14.3): compile-time config (DebugMapsOutputDirectory, BehaviorsDllDirectory, BehaviorsBuildTarget). Read from `config.json` or engine config system.
- Editor test suite (§15): all test files and patterns from §15:
  - `Editor/AssetBrowser/AssetBrowserWindowTests.cs`: empty catalog, item listing, filter, open action.
  - `Editor/GraphEditor/CommandHistoryTests.cs`: AddNode/Undo/Redo round-trips.
  - `Editor/GraphEditor/NodePaletteTests.cs`: palette groups built from `INodeRegistry`.
  - `Editor/Inspector/DrawerRegistryTests.cs`: register + retrieve drawers, missing drawer fallback.
  - `Editor/QuickReload/QuickReloadServiceTests.cs`: success path, Roslyn failure, apply failure rollback (debug map unregistered), dirty sibling signature test (Patch 1), `OnReloadCompleted` source discrimination (Patch 2), `HsmActionDispatcher.ClearAll` called before registrars (Patch 3).
  - `Editor/QuickReload/SiblingSignatureTests.cs`: `BuildSiblingSignatures` uses catalog not registry; dirty-asset override.
  - `Editor/FullRebuild/FullRebuildServiceTests.cs`: process spawn, output streaming.
  - `Editor/DebugSession/DebugSessionLifecycleTests.cs`: init on first debug compile, clear on rebuild, `OnHotReloadBegin` called before reload.
  - `Editor/HotReloadLog/HotReloadLogWindowTests.cs`: log entries added, ring-buffer eviction at 1000.
  - `Editor/Preferences/PreferencesSerializationTests.cs`: save + reload round-trip, invalid JSON handled gracefully.
- `MockWindowRegistrar`, `MockOutputConsole`, `MockAiHotReloadCoordinator` helpers for editor tests.

**What is NOT included:**
- Window rendering integration tests (covered by manual testing + Roadmap demo QA per Q-16.5).
- Live editor frame-time benchmarks (manual QA).

### Constraints

- `BlueprintEditorPreferences` defaults: `AutoReloadOnSave = false`, `DefaultCompilerMode = CompilerMode.Debug`, `NodeHistorySize = 64`.
- Preferences save path must be deterministic (not GUID-based) so tests can predict the path.
- `QuickReloadServiceTests` must verify `HsmActionDispatcher.ClearAll()` was called BEFORE registrars executed (using a test spy on the static call -- either a thread-local toggle or a mock wrapper).

### Success Conditions

- SC1: `dotnet test --filter "FullyQualifiedName~Editor"` reports zero failures, zero skipped.
- SC2: Preferences round-trip test: save preferences, reload from JSON, all field values preserved.
- SC3: `QuickReloadServiceTests.QuickReload_WithDirtySiblingAsset_UsesDirtyInMemorySignature` passes.
- SC4: `QuickReloadServiceTests.ApplyFailure_RollsBackDebugMapRegistration` passes.
- SC5: `HotReloadLogWindow` evicts oldest entry when 1001st entry added.
- SC6: `dotnet build` zero errors.

---

## Phase 7 -- Demos

---

## TASK-DEMO-001 -- Demo: MathUtilsLib (Library Dispatch)

**Phase:** 7 -- Demos
**Design Reference:** [Roadmap �5](./Blueprint_Subsystem_Implementation_Roadmap_v1.1.md#5-slice-1-demo-scenarios), [Architecture �1.2](./Blueprint_Subsystem_Architecture_v1.2.md)
**Effort:** 1-2 days

### Scope

**What IS included:**
- `MathUtilsLib.bp.json` -- a Library Blueprint asset with at least two exported function graphs: `Lerp(a: float, b: float, t: float) -> float` and `Clamp(value: float, min: float, max: float) -> float`.
- Full compiler run through Stages 1-8 producing a generated `.g.cs` + PDB for this asset.
- Loading the compiled Library into a test fixture via `BlueprintTestFixture.CompileAndLoad`.
- C# call site test: call the Library's exported functions via the generated static class methods; verify return values.
- Hot reload test: modify the asset (change a default parameter), `SimulateReload`, verify new behavior without restarting the fixture.
- ALC leak check: `ForceGcReclaim()` after reload -- old ALC `WeakReference.IsAlive == false`.
- Snapshot check: emitted source matches `Snapshots/Demos/MathUtilsLib.cs.txt`.
- Editor authoring walkthrough script (manual test instructions in a comment block in the test file): steps to open Asset Browser, create new Library, add Lerp graph, verify Quick Reload works.

**What is NOT included:**
- AiPrimitive/Instance dispatch features.
- Multi-entity logic.

### Constraints

- Library blueprint must have `Dispatch = BlueprintDispatchKind.Library`.
- Call site in test calls the generated static method directly (not via the registry), proving the generated class shape is correct.
- Hot reload must swap the registrar correctly -- `BlueprintRegistry.TryGetById(id, out def)` returns updated definition after reload.

### Success Conditions

- SC1: `Lerp(0f, 1f, 0.5f)` returns `0.5f` via the compiled generated code.
- SC2: After hot reload with modified Lerp default-clamp behavior, new result differs from pre-reload.
- SC3: ALC `WeakReference.IsAlive == false` after `ForceGcReclaim()` post-reload.
- SC4: Generated source snapshot matches.
- SC5: `dotnet test --filter "DEMO-001|MathUtilsLib"` passes.

---

## TASK-DEMO-002 -- Demo: HealthRegen (Instance Dispatch)

**Phase:** 7 -- Demos
**Design Reference:** [Roadmap �5](./Blueprint_Subsystem_Implementation_Roadmap_v1.1.md#5-slice-1-demo-scenarios), [Architecture �1.2](./Blueprint_Subsystem_Architecture_v1.2.md)
**Effort:** 2-3 days

### Scope

**What IS included:**
- `HealthRegen.bp.json` -- an Instance Blueprint with: `CurrentHealth: float` variable, `MaxHealth: float` variable (default 100.0), `RegenRate: float` variable (default 5.0 per second). Event graph: on `Tick`, if `CurrentHealth < MaxHealth`, increment `CurrentHealth` by `RegenRate * deltaTime`, clamp to `MaxHealth`. Latent sequence: after health reaches MaxHealth, wait for a `HealthDepleted` channel command, then restart regen cycle.
- Full compile + load into fixture.
- Tick test: 10 ticks at `dt = 1.0f` starting from `CurrentHealth = 80f` -- `CurrentHealth` reaches 100f by tick 4.
- Debug protocol test: set breakpoint on the regen-increment node, tick -- `MockTimeController.PauseWasRequested == true`, `BreakpointHit.Self == entity`.
- Watch test: add a watch on `CurrentHealth` pin, tick, verify `watch.HasEverBeenWritten == true` and decoded float value matches expected.
- Soft reload test (soft = same StructureHash): `CurrentHealth` preserved across reload.
- Hard reload test (hard = StructureHash changed by adding a variable): `CurrentHealth` zeroed, `InstanceVersion` incremented.
- Latent cursor test: trigger `HealthDepleted` channel command, verify regen restarts.
- Editor authoring: manual walkthrough steps in test file comments.

**What is NOT included:**
- Multi-entity scenarios (DEMO-003).
- AiPrimitive hosting.

### Constraints

- Blueprint must use `Dispatch = BlueprintDispatchKind.Instance`.
- All Instance-tier variables must fit within the 16096-byte budget.
- Tick with latent wait must use the cursor switch pattern from CP-003.

### Success Conditions

- SC1: After 10 ticks at dt=1.0f from CurrentHealth=80f, `CurrentHealth >= 100f`.
- SC2: Breakpoint fires; `MockTimeController.PauseWasRequested == true`.
- SC3: Watch decoded value matches `CurrentHealth` after tick.
- SC4: Soft reload preserves `CurrentHealth` value.
- SC5: Hard reload zeroes `CurrentHealth`; `InstanceVersion` bumped.
- SC6: After `HealthDepleted` channel command, regen resumes within 2 ticks.
- SC7: `dotnet test --filter "DEMO-002|HealthRegen"` passes.

---

## TASK-DEMO-003 -- Demo: DoorActor + DoorSensor (Multi-Blueprint Peer Calls)

**Phase:** 7 -- Demos
**Design Reference:** [Roadmap �5](./Blueprint_Subsystem_Implementation_Roadmap_v1.1.md#5-slice-1-demo-scenarios), [Architecture �1.2](./Blueprint_Subsystem_Architecture_v1.2.md)
**Effort:** 2-3 days

### Scope

**What IS included:**
- `DoorActor.bp.json` -- Instance Blueprint: `IsOpen: bool` variable. Function graph `Open()` sets `IsOpen = true`. Function graph `Close()` sets `IsOpen = false`. Has `DoorSensor` as a declared callable peer (in `callablePeers`).
- `DoorSensor.bp.json` -- Instance Blueprint: `DetectedEntity: Entity` variable. Function graph `NotifyDoor(doorEntity: Entity)` -- calls `DoorActor.Open()` on the provided entity peer.
- Both assets compiled together (DoorActor and DoorSensor in the same compile batch, each as sibling of the other).
- Two entities created: `doorEntity` with `DoorActor` attached, `sensorEntity` with `DoorSensor` attached.
- Peer call test: call `DoorSensor.NotifyDoor(doorEntity)` -- verify `DoorActor.IsOpen == true` on `doorEntity`.
- `callablePeers` validation test: compile `DoorSensor` without `DoorActor` in the peer catalog -- validator emits BP1301.
- Hot reload test: reload both assets together, peer call still works after reload.
- ALC leak test: both assets share one ALC (compiled together); one reload -- old shared ALC reclaimed.
- Editor authoring: manual walkthrough in test file comments.

**What is NOT included:**
- AiPrimitive hosting.
- Partition allocator multi-slot (Slice 2 -- DEMO-003 uses one Blueprint per entity).

### Constraints

- Both assets compiled in a single `CompileAll(new[] { doorActor, doorSensor })` batch so each has the other as a sibling signature.
- Peer call uses the generated `NotifyDoor` method's `Call(entity, view, ecb)` dispatch (no raw delegate pointer manipulation in the test).

### Success Conditions

- SC1: After `NotifyDoor(doorEntity)` tick, `DoorActor` state on `doorEntity` has `IsOpen == true`.
- SC2: `Close()` call -- `IsOpen == false`.
- SC3: Compile without DoorActor in siblings -- BP1301 emitted.
- SC4: Reload both -- peer call continues working.
- SC5: Post-reload ALC reclaimed.
- SC6: `dotnet test --filter "DEMO-003|DoorActor"` passes.

---

## TASK-DEMO-004 -- Demo: HasVisibleTarget (AiPrimitive Multi-Hosting)

**Phase:** 7 -- Demos
**Design Reference:** [Roadmap �5](./Blueprint_Subsystem_Implementation_Roadmap_v1.1.md#5-slice-1-demo-scenarios), [Architecture �1.2](./Blueprint_Subsystem_Architecture_v1.2.md)
**Effort:** 2-3 days

### Scope

**What IS included:**
- `HasVisibleTarget.bp.json` -- AiPrimitive Blueprint: `Dispatch = AiPrimitive`, `Hostings = [BTreeCondition, HsmGuard]`, `Kind = Condition`. Parameters (in Params struct): `DetectionRange: float`. WorkingState: none (stateless condition). Graph: gets a `SightComponent` from the entity, checks if `DistanceTo(nearest enemy) <= DetectionRange`; returns `ReturnStatus.Success` if true, `ReturnStatus.Failure` otherwise.
- Full compile through all stages, including AiPrimitive phase-byte (none needed -- stateless), BTree thunk (`BTreeTick`), HSM thunk (`HsmGuard`).
- BTree thunk test: simulate a BTree evaluation call via fixture; `Params.DetectionRange = 10f`, entity within range -- returns `Success`; entity out of range -- `Failure`.
- HSM thunk test: simulate an HSM guard evaluation; same range-check logic, `Success`/`Failure`.
- AiPrimitive validator test: compile with `ReturnNode(Running)` in Condition graph -- BP1100; compile with `LatentDelayNode` in Condition graph -- BP1101.
- Hot reload test: change `DetectionRange` default, reload, verify new default is picked up.
- ALC leak test.
- Registrar shape verification: generated registrar has 2 parameters `(BlueprintRegistryStaging, BehaviorRegistry)` -- no `HsmActionDispatcher` parameter.
- Editor authoring: manual walkthrough in test file comments.

**What is NOT included:**
- Action-style latent AiPrimitive (that is DEMO-005).
- Instance dispatch.

### Constraints

- Condition graph must NOT have latent ops (per Compiler DD Validator rules, validated in test).
- `BTreeTick` and `HsmGuard` thunk generated correctly with no extra parameters.
- `HsmActionDispatcher.RegisterAction` called statically by generated registrar (no parameter injection).

### Success Conditions

- SC1: BTree eval with `DetectionRange = 10f`, entity at distance 5f -- `ReturnStatus.Success`.
- SC2: BTree eval with entity at distance 15f -- `ReturnStatus.Failure`.
- SC3: HSM guard eval -- same pass/fail as BTree (same underlying logic).
- SC4: Condition graph with `ReturnNode(Running)` -- BP1100 during compile.
- SC5: Generated registrar has exactly 2 `Register` parameters.
- SC6: Post-reload ALC reclaimed.
- SC7: `dotnet test --filter "DEMO-004|HasVisibleTarget"` passes.

---

## TASK-DEMO-005 -- Demo: MoveToAndFire (Headline AiPrimitive Action)

**Phase:** 7 -- Demos
**Design Reference:** [Roadmap �5](./Blueprint_Subsystem_Implementation_Roadmap_v1.1.md#5-slice-1-demo-scenarios), [Architecture �1.2](./Blueprint_Subsystem_Architecture_v1.2.md)
**Effort:** 3-4 days

### Scope

**What IS included:**
- `MoveToAndFire.bp.json` -- AiPrimitive Blueprint: `Dispatch = AiPrimitive`, `Hostings = [BTreeAction, HsmAction]`, `Kind = Action`. Parameters: `TargetEntity: Entity`, `StopDistance: float`. WorkingState: `__phase: byte` (synthesized by Stage 6 Lower), `TargetPosition: Vector3`. Graph: issue `MoveToCommand(entity, TargetPosition)` channel command, wait for `ArrivedAtDestination` channel event, issue `FireCommand(entity, TargetEntity)` channel command, wait for `WeaponFired` channel event, return `ReturnStatus.Success`. Uses dual hosting: BTree action tick thunk + HSM activity thunk.
- Full compile through all stages including AiPrimitive phase-byte state machine (2 Wait ops -- phases 0, 1, 2), ChannelCommandLowering, WaitForChannelNode.
- Phase-advance test: tick 1 (phase 0): `MoveToCommand` issued, phase advances to 1, returns Running. Tick 2 (phase 1, `ArrivedAtDestination` NOT yet fired): stays Running. Tick 3 (phase 1, `ArrivedAtDestination` fired): phase advances to 2, `FireCommand` issued, returns Running. Tick 4 (phase 2, `WeaponFired` fired): returns Success.
- BTree thunk test (`BTreeTick`): drive phase-advance via BTree evaluator mock.
- HSM thunk test (`HsmActivity`): drive same phase-advance via HSM mock.
- Hot reload test -- soft reload (same StructureHash): `__phase` and `TargetPosition` preserved -- continues from current phase.
- Hot reload test -- hard reload (StructureHash changed): WorkingState zeroed -- restarts from phase 0.
- Editor hot-reload end-to-end: manual walkthrough instructions -- edit `StopDistance` default in asset, Quick Reload, verify new default.
- ALC leak test (chained reloads: 3 Quick Reloads, no ALC leak).
- Registrar shape: `Register(BlueprintRegistryStaging staging, BehaviorRegistry behReg)` -- `HsmActionDispatcher.RegisterAction` static call present in generated code.
- Snapshot: full generated source snapshot for `MoveToAndFire` in `Snapshots/Demos/MoveToAndFire.cs.txt`.
- M16 acceptance gate (Roadmap �10, definition of Slice 1 Complete item 2): this demo constitutes the "headline demo" walkthrough.

**What is NOT included:**
- Real game engine AI integration (tests use mocked BTree/HSM evaluators via `BlueprintTestFixture`).
- Multi-target firing (Slice 2).

### Constraints

- Graph has exactly 2 latent wait ops -- 3 phases (0, 1, 2).
- Phase-0 block must issue `MoveToCommand` channel command (verified by checking `EntityCommandBuffer` mock calls).
- Phase-1 check block reads `ArrivedAtDestination` channel component; if not present, returns Running.
- Phase-2 check block reads `WeaponFired` channel component; if not present, returns Running; if present, returns Success.
- `StructureHash` stability test: compile same source twice -- identical `StructureHash` values.
- After 3 chained Quick Reloads: only one ALC live (the most recent), all previous reclaimed.

### Success Conditions

- SC1: Tick sequence (1: phase 0 -- Running; 2: phase 1, no event -- Running; 3: phase 1, `ArrivedAtDestination` -- phase 2, Running; 4: phase 2, `WeaponFired` -- Success).
- SC2: BTree tick and HSM tick produce identical phase-advance behavior (same EC mock calls).
- SC3: Soft reload preserves `__phase = 1` (in-progress wait state).
- SC4: Hard reload resets `__phase = 0` (restarts action).
- SC5: 3 chained Quick Reloads -- 2 previous ALCs reclaimed, only current ALC alive.
- SC6: Generated registrar has `Register(BlueprintRegistryStaging, BehaviorRegistry)` with static `HsmActionDispatcher.RegisterAction` call.
- SC7: `StructureHash` identical across two independent compiles of the same asset.
- SC8: `dotnet test --filter "DEMO-005|MoveToAndFire"` passes.
- SC9: Manual demo walkthrough checklist (in test file comments) covers all 6 Roadmap �10 items.
