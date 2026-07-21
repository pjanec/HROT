# Hrot.Blueprints.Tests

> Hand-synced 2026-07-21 to match shipped state; regenerate to fully refresh.

- **Project file**: `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj`
- **Target framework**: net8.0
- **Test framework**: xUnit 2.x
- **Date documented**: 2026-05-30

---

## Executive Overview

`Hrot.Blueprints.Tests` is the unified test suite for the entire Blueprint subsystem.
It combines unit, integration, and end-to-end tests for every layer: compiler pipeline
stages, IR model, runtime systems (registry, blackboard, tick/maintenance), hot-reload
coordinator, debug protocol, editor infrastructure, visual attachment providers, and
five demo scenarios that exercise the complete stack from Blueprint JSON asset to
simulation execution.

The project is deliberately monolithic: one test project exercises all subsystem layers
so that inter-layer integration failures surface during the same test run that validates
individual components.

---

## Architecture

### Test Fixture Central Infrastructure

All tests that need a running simulation context use `BlueprintTestFixture`, which wires
all Blueprint infrastructure into a single disposable object:

```
BlueprintTestFixture
  |-- EntityRepository (World)       -- in-memory ECS world
  |-- MockSimulationView             -- ISimulationView backed by EntityRepository
  |-- MockEntityCommandBuffer        -- IEntityCommandBuffer backed by EntityRepository
  |-- BlueprintRegistry              -- runtime Blueprint type registry
  |-- BehaviorRegistry               -- AI behavior registration table
  |-- BlueprintTickSystem            -- per-frame Blueprint tick dispatcher
  |-- BlueprintMaintenanceSystem     -- cleanup / maintenance dispatcher
  |-- BlueprintCompiler              -- full AST compiler (Stages 1-7)
  |-- CapturingDebugSession          -- test double for IBlueprintDebugSession
  |-- AiHotReloadCoordinator         -- ALC lifecycle manager (in-process reload)
  |
  +-- CompileAndLoad(asset)          -- compile -> Roslyn -> ALC -> register
  +-- SimulateQuickReload(asset)     -- compile -> new ALC -> coordinator.ApplyQuickReload
  +-- TickFrame(entity, asset)       -- run tick + maintenance for one frame
  +-- GetCurrentAlc()                -- expose live ALC for lifecycle assertions
  +-- GetAlcWeakReferences()         -- expose WeakRefs to old ALCs for GC tests
```

Disposal calls `AiHotReloadCoordinator.Dispose()`, triggers GC, and (when
`VerifyAlcUnloadOnDispose = true`) asserts that all collectible ALCs were reclaimed
within `GcReclaimRetries` attempts spaced `GcReclaimDelayMs` apart.

### Options

```csharp
public sealed class BlueprintTestFixtureOptions
{
    public bool VerifyAlcUnloadOnDispose { get; init; } = true;
    public int  GcReclaimRetries        { get; init; } = 50;
    public int  GcReclaimDelayMs        { get; init; } = 50;
    public bool VerboseLeakDiagnostics  { get; init; } = false;
}
```

Tests that intentionally hold ALC references across multiple reloads set
`VerifyAlcUnloadOnDispose = false` and verify GC reclaim themselves.

### Mock Infrastructure

#### `MockSimulationView`

Implements `ISimulationView` and `ISimulationViewContext` by delegating component reads
and writes directly to an in-memory `EntityRepository`. Supports the full ECS component
API used by generated Blueprint code: `GetComponentRO`, `GetComponentRW`, `HasComponent`,
`TryGetComponent`, and the world query interface.

Registered test component types are defined in `MockTestTypes.cs`:

| Type | Size | Description |
|------|------|-------------|
| `TestHealthComponent` | 8 bytes | `float Current, float Max` |
| `TestPositionComponent` | 8 bytes | `float X, float Y` |
| `TestFlagComponent` | 4 bytes | `bool Active, int Counter` |
| `TestTargetComponent` | 4 bytes | `Entity Target` |

#### `MockEntityCommandBuffer`

Implements `IEntityCommandBuffer` by immediately applying structural ECS mutations
(create entity, add/remove components, set component data) to the backing
`EntityRepository`. Deferred semantics are not simulated; all writes take effect on
the next component read within the same test.

#### `MockDispatcherSystem`

Implements `IEcsModuleSystem` and intercepts channel command and event dispatch calls
in generated Blueprint code. Exposes counters and last-seen payloads for assertion.
Contains three sub-dispatchers:

- `MockLocomotionDispatcher` -- captures `MoveTo`, `FollowRoute` commands
- `MockWeaponDispatcher` -- captures `AimAndFire` commands
- `MockInteractionDispatcher` -- captures `OpenDoor`, `EjectPassengers` commands

### Builder API

`BlueprintAssetBuilder` is a fluent API for constructing `BlueprintAsset` objects
programmatically in tests. Located at `Builders/BlueprintAssetBuilder.cs`.

```csharp
var asset = new BlueprintAssetBuilder("PatrolAction")
    .AsAiPrimitive(AiPrimitiveIntent.Action, AiPrimitiveHosting.BTreeAction)
    .WithParameter("Speed", "float", "5.0")
    .WithWorkingState("Phase", "int")
    .AddGraph(gBuilder => gBuilder
        .AiPrimitiveMain()
        .AddLiteralNode("lit01", "float", "1.0f")
        .AddReturnNode("ret01", NodeStatus.Success)
        .Link("lit01", "Value", "ret01", "Exec"))
    .Build();
```

Key builder methods:

| Method | Description |
|--------|-------------|
| `AsLibrary()` | Sets dispatch to Library |
| `AsAiPrimitive(intent, hosting)` | Sets dispatch to AiPrimitive with given intent/hosting |
| `AsInstance()` | Sets dispatch to Instance |
| `WithParameter(name, typeId, defaultJson?)` | Adds a `ParameterDecl` |
| `WithWorkingState(name, typeId, defaultJson?)` | Adds a `VariableDecl` to WorkingState |
| `WithVariable(name, typeId, defaultJson?)` | Adds a `VariableDecl` to Variables (Instance) |
| `WithCustomEvent(name)` | Adds a `CustomEventDecl` (Instance) |
| `WithCallablePeer(assetId)` | Adds a peer GUID to `CallablePeers` |
| `AddGraph(gBuilder => ...)` | Adds a `Graph` via a nested `GraphBuilder` |
| `Build()` | Returns the completed `BlueprintAsset` |

### CapturingDebugSession

`CapturingDebugSession` (in `Debug/`) is a test double that implements both
`IBlueprintProbeSink` and `IBlueprintDebugSession`. It records every probe notification
for assertion and provides a lightweight string-keyed breakpoint set for integration
tests that do not need the full GUID-based production session.

Key recording API:

```csharp
// Records
IReadOnlyList<NodeEnterRecord>  NodeEntries { get; }
IReadOnlyList<PinValueRecord>   PinValues   { get; }

// Simple breakpoints (string node-id key)
void SetBreakpoint(string nodeId);
void ClearBreakpoint(string nodeId);

// Events forwarded from the probe
event Action<BreakpointHit>? OnBreakpointHit;
```

`NodeEnterRecord` and `PinValueRecord` are lightweight value types that capture the
`Entity`, the string identifier, the value (for pins), and the simulation tick.

### TestData

`TestData` (in `TestData.cs`) provides helpers to load named `.bp.json` files from the
`TestAssets/` directory and to read/regenerate snapshot files.

Named assets available via `TestData.SampleAssets`:

| Constant | File | Dispatch |
|----------|------|----------|
| `LibraryMath` | `LibraryMath.bp.json` | Library |
| `InstanceCounter` | `InstanceCounter.bp.json` | Instance |
| `InstanceCounterV1ModifiedBody` | `InstanceCounterV1ModifiedBody.bp.json` | Instance |
| `InstanceCounterV2WithBonus` | `InstanceCounterV2WithBonus.bp.json` | Instance |
| `HealthRegen` | `HealthRegen.bp.json` | Instance |
| `HasVisibleTarget` | `HasVisibleTarget.bp.json` | AiPrimitive |
| `MoveToAndFire` | `MoveToAndFire.bp.json` | AiPrimitive |
| `DoorActor` | `DoorActor.bp.json` | Instance |
| `DoorSensor` | `DoorSensor.bp.json` | Instance |

Additional anonymous assets in `TestAssets/`:
`empty-library.bp.json`, `instance-blueprint.bp.json`, `simple-action.bp.json`,
`simple-condition.bp.json`, `with-branch.bp.json`, `with-callable-peer.bp.json`,
`with-custom-event.bp.json`, `with-delay.bp.json`, `with-sequence.bp.json`.

#### Snapshot testing

`TestData.ReadOrRegenerateSnapshot(relativePath, actual)` compares the `actual` string
against the stored snapshot under `TestAssets/Snapshots/`. When the environment variable
`BLUEPRINT_REGENERATE_SNAPSHOTS=1` is set, the snapshot is written rather than compared.
This pattern is used extensively by Stage 7 Emit tests to pin the exact generated C#
output.

---

## Source Structure

```
Hrot.Blueprints.Tests/
|
|-- BlueprintTestFixture.cs         -- central per-test fixture
|-- BlueprintTestFixtureOptions.cs  -- fixture configuration
|-- BlueprintTestFixtureTests.cs    -- tests for the fixture itself
|-- CapturingDebugSession.cs        -- IBlueprintDebugSession test double
|-- CapturingDebugSessionTests.cs   -- tests for the test double
|-- TestData.cs                     -- asset loader and snapshot helpers
|-- TestEventDefinitions.cs         -- engine event types for use in tests
|-- GlobalAliases.cs                -- global using aliases
|-- PlaceholderTests.cs             -- always-passing smoke test
|-- AlcUnloadTests.cs               -- standalone ALC GC reclaim tests
|-- SampleAssetLoadTests.cs         -- JSON round-trip on all named assets
|-- SchemaReflectionTests.cs        -- reflection-based schema consistency tests
|-- MockDispatcherSystemTests.cs    -- contract tests for mock dispatcher
|
|-- Builders/
|   |-- BlueprintAssetBuilder.cs         -- fluent asset builder
|   |-- BlueprintAssetBuilderTests.cs    -- builder API tests
|
|-- Mocks/
|   |-- MockSimulationView.cs            -- ISimulationView mock
|   |-- MockEntityCommandBuffer.cs       -- IEntityCommandBuffer mock
|   |-- MockTestTypes.cs                 -- test ECS component types
|   |-- MockContractTests.cs             -- mock behavior contract tests
|   |-- MockSimulationViewContractTests.cs
|   |-- MockEntityCommandBufferContractTests.cs
|
|-- MockSystems/
|   |-- MockDispatcherSystem.cs          -- channel command / event interceptor
|   |-- MockLocomotionDispatcher.cs      -- captures MoveTo / FollowRoute
|   |-- MockWeaponDispatcher.cs          -- captures AimAndFire
|   |-- MockInteractionDispatcher.cs     -- captures OpenDoor / EjectPassengers
|
|-- Compiler/
|   |-- Stage1_ParseTests.cs             -- Stage 1 JSON parse tests
|   |-- Stage0_RehydrateTests.cs         -- reflection-free pin/link reconstruction tests
|   |-- Stage2_ValidationTests/          -- per-validator tests
|   |   |-- V_FlowForEachValidatorTests.cs -- FlowForEach latent-node-forbidden-in-body rules
|   |-- MakeBreakStructTests.cs          -- MakeStruct/BreakStruct/SetMembers node tests
|   |-- StructTypedVariableTests.cs      -- struct-typed Instance Variables tests
|   |-- MultiPinSetSharedTests.cs        -- multi-pin per-field SetShared/GetShared/PublishEvent tests
|   |-- CustomEventPubSubCapstoneTests.cs -- end-to-end custom-event publish/subscribe capstone
|   |-- Stage3_NormalizationTests/       -- normalization pass tests
|   |-- Stage4_TypeResolveTests.cs       -- type resolution tests
|   |-- Stage5_ScheduleTests/            -- IR schedule tests
|   |-- Stage6_LoweringTests/            -- lowering tests (AiPrimitive, Instance, WaitLowering)
|   |-- Stage7_EmitTests/                -- emit tests with snapshot comparison
|   |-- Stage8_RoslynTests/              -- Roslyn compilation + ALC load tests
|   |-- EndToEnd/                        -- full pipeline end-to-end tests
|   |-- Determinism/                     -- FNV hash and ordering determinism tests
|   |-- CatalogTests.cs                  -- catalog lookup tests
|   |-- WhenNodeValidatorTests.cs        -- WhenNode-specific validation tests
|   |-- ReadEqsResultValidatorTests.cs   -- ReadEqsResultNode validation tests
|   |-- SpawnEqsSensorValidatorTests.cs  -- SpawnEqsSensorNode validation tests
|   |-- RecipeIntegrityTests.cs          -- recipe asset consistency tests
|   |-- TestDiagnosticInventory.cs       -- ensures all diagnostic codes are tested
|   |-- CoversDiagnosticCodeAttribute.cs -- custom xUnit attribute for diagnostic coverage
|
|-- Runtime/
|   |-- FakeBlueprints.cs                -- hand-authored fake generated classes for early runtime tests
|   |-- BlueprintDefinitionTests.cs      -- delegate type and definition tests
|   |-- BlueprintRegistryTests.cs        -- registry add/lookup/staging tests
|   |-- BlackboardLayoutTests.cs         -- component size and offset validation
|   |-- AllocationFreeTests.cs           -- allocation checks via GC count comparison
|   |-- WhenNodeRuntimeTests.cs          -- WhenNode edge-detection runtime tests
|   |-- WhenNodeEqsInlineArrayTests.cs   -- EQS trigger inline-array runtime tests
|   |-- ReadEqsResultNodeRuntimeTests.cs -- ReadEqsResultNode integration tests
|   |-- SpawnEqsSensorRuntimeTests.cs    -- SpawnEqsSensorNode integration tests
|   |-- UtilityNodeRuntimeTests.cs       -- ScoreDecisionNode / ReadRankedResultNode tests
|   |-- PartitionAllocator/              -- blackboard partition allocator tests
|   |-- BlueprintTickSystem/             -- tick dispatch and world-singleton tests
|   |-- BlueprintMaintenanceSystem/      -- cleanup and PendingDestroy tests
|
|-- HotReload/
|   |-- WhenNodeHotReloadTests.cs        -- WhenNode prev-state continuity through hot reload
|   |-- Coordinator/
|   |   |-- QuickReloadTests.cs          -- ALC swap and GC reclaim under quick reload
|   |   |-- AlcLifecycleTests.cs         -- ALC load, unload, reclaim lifecycle
|   |   |-- FailureRollbackTests.cs      -- coordinator rollback on registrar failure
|   |   |-- RegistrarInjectionTests.cs   -- registrar parameter injection tests
|   |-- PdbLoading/                      -- PDB loading and symbol resolution tests
|   |-- RuntimeIntegration/              -- hot-reload + live-tick interaction tests
|
|-- Debug/
|   |-- ProbeDispatchTests.cs            -- DebugProbe.Sink dispatch tests
|   |-- BreakpointTests.cs               -- breakpoint set/clear/hit tests
|   |-- StepTests.cs                     -- StepInto/Over/Out semantics
|   |-- WatchTests.cs                    -- watch add/write/query tests
|   |-- NodeHistoryTests.cs              -- ExecutionHistory ring-buffer tests
|   |-- DebugMapTests.cs                 -- DebugMap serialization and index tests
|   |-- DebugSessionInterfaceTests.cs    -- full IBlueprintDebugSession contract tests
|   |-- StateInspectorTests.cs           -- BlueprintStateSnapshot tests
|   |-- MultiEntityTests.cs              -- entity-filter and per-entity history tests
|   |-- HotReloadInteractionTests.cs     -- debug session + hot reload interaction
|   |-- MockTimeController.cs            -- IEngineDebugTimeController mock
|
|-- Editor/
|   |-- EditorInfrastructureTests.cs     -- module, window lifecycle, DI wiring tests
|   |-- EditorWindowTests.cs             -- window activate/deactivate/draw tests
|   |-- AssetBrowserWindowTests.cs       -- asset browser render and filter tests
|   |-- CommandHistoryTests.cs           -- undo/redo ring-buffer tests
|   |-- Host/                           -- NodeEdit Host layer tests
|   |   |-- BlueprintNodeModelTests.cs    -- node model / BuildTitle tests
|   |   |-- NodePinSchemaTests.cs         -- canonical pin projection tests
|   |   |-- BlueprintCommandSinkTests.cs  -- wire-drop add/remove -> CommandHistory undo tests
|   |-- DrawerRegistryTests.cs           -- drawer registration and lookup tests
|   |-- HotReloadLogModelTests.cs        -- log ring-buffer cap/clear tests
|   |-- PreferencesTests.cs              -- save/load/defaults round-trip tests
|   |-- QuickReloadServiceTests.cs       -- quick reload pipeline with mock compiler
|   |-- WhenNodeDrawerTests.cs           -- WhenNode inspector UI mode/payload tests
|   |-- ReadEqsResultNodeDrawerTests.cs  -- EQS result drawer variable combo tests
|   |-- SpawnEqsSensorNodeDrawerTests.cs -- EQS sensor drawer template combo tests
|   |-- PlayMontageChainNodeDrawerTests.cs -- montage-chain drawer tests
|   |-- WhenFiringPulseRendererTests.cs  -- pulse renderer active/inactive tests
|   |-- ConditionSummaryAttachmentTests.cs -- WhenNode attachment pill label tests
|   |-- EqsVisualAttachmentTests.cs      -- EQS attachment label and state tests
|   |-- CrossAssetDependencyAttachmentTests.cs -- peer-arrow attachment tests
|   |-- DebugWindowsTests.cs             -- debug panel / watch / callstack windows
|   |-- NewFromRecipeServiceTests.cs     -- recipe-based asset creation tests
|   |-- MockDebugSession.cs              -- IBlueprintDebugSession mock for editor tests
|   |-- MockOutputConsole.cs             -- IOutputConsole mock
|   |-- MockWindowRegistrar.cs           -- IWindowRegistrar mock
|   |-- CountingWindow.cs                -- IBlueprintEditorWindow counting stub
|
|-- Demos/
|   |-- LibraryMathDemoTests.cs          -- Library dispatch: MathUtils static methods
|   |-- HealthRegenDemoTests.cs          -- Instance dispatch: HealthRegen tick + event
|   |-- DoorActorDoorSensorDemoTests.cs  -- Instance peer calls: DoorActor <-> DoorSensor
|   |-- HasVisibleTargetDemoTests.cs     -- AiPrimitive multi-hosting: BTree + HSM condition
|   |-- MoveToAndFireDemoTests.cs        -- AiPrimitive headline action: latent MoveTo + AimAndFire
|
|-- Integration/
|   |-- CoverAwarePatrolEndToEndTest.cs  -- cover-aware patrol full stack integration
|   |-- WhenNodeEditorSmokeTest.cs       -- WhenNode editor round-trip smoke test
|   |-- WhenNodeEditorWiringTests.cs     -- WhenNode editor wiring validation
|
|-- Squad/
|   |-- SquadPrimitiveNodeTests.cs       -- squad-level AiPrimitive node tests
|
|-- Stage1To5Tests.cs                    -- condensed Stages 1-5 regression tests
|-- Stage6Tests.cs                       -- Stage 6 lowering regression tests
|-- Stage7Tests.cs                       -- Stage 7 emit regression tests
|-- Stage8Tests.cs                       -- Stage 8 Roslyn regression tests
```

---

## Test Categories and xUnit Trait Usage

The test suite uses the `[Collection("DebugProbe")]` xUnit collection attribute on
tests that write to the global `DebugProbe.Sink`. This ensures those tests do not
run in parallel with each other, preventing probe-sink data races.

Most other test classes are uncollected and run in parallel by default.

---

## Key Test Patterns

### ALC GC Reclaim Pattern

Hot-reload tests that verify an old ALC is garbage-collected use the following pattern
to avoid keeping stack roots alive during the GC loop:

```csharp
[Fact]
public void QuickReload_UpdatesCurrentAlc()
{
    WeakReference<AssemblyLoadContext>[] alcWeakRefs;
    // [NoInlining] confines all ALC-touching locals to this frame.
    QuickReload_Body(out alcWeakRefs);
    for (int i = 0; i < 50; i++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        if (alcWeakRefs.All(w => !w.TryGetTarget(out _))) return;
        Thread.Sleep(50);
    }
    Assert.True(false, "ALC not GC-reclaimed.");
}

[MethodImpl(MethodImplOptions.NoInlining)]
private static void QuickReload_Body(out WeakReference<AssemblyLoadContext>[] refs)
{
    using var fixture = new BlueprintTestFixture(
        new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
    // ... compile, reload, collect weak refs ...
}
```

The `[NoInlining]` attribute ensures all strong references to the ALC are scoped
to the helper frame and are eligible for collection when the loop runs.

### Snapshot-Based Emit Testing

Stage 7 Emit tests pin generated C# output using `TestData.ReadOrRegenerateSnapshot`:

```csharp
[Fact]
public void Emit_LibraryMath_MatchesSnapshot()
{
    var asset = TestData.LoadAsset(TestData.SampleAssets.LibraryMath);
    var result = Compile(asset, CompilerMode.Release);
    Assert.True(result.Succeeded);
    TestData.ReadOrRegenerateSnapshot(
        "Stage7/LibraryMath_Release.g.cs.txt",
        result.GeneratedSource!);
}
```

To regenerate all snapshots after intentional code-generation changes, run tests
with `BLUEPRINT_REGENERATE_SNAPSHOTS=1` set in the environment.

### Diagnostic Coverage Attribute

`[CoversDiagnosticCode("BP1XXX")]` is a custom xUnit attribute applied to test methods
that exercise a specific diagnostic code. `TestDiagnosticInventory` collects all
`[CoversDiagnosticCode]` usages and compares them against `DiagnosticCodes` constants
to detect diagnostic codes that have no test coverage.

---

## Demo Tests

The five demo tests under `Demos/` exercise the complete stack from `.bp.json` asset
through the compiler, Roslyn, ALC load, ECS wiring, and simulation execution. They serve
as living specification for each dispatch kind.

### LibraryMathDemoTests

Exercises the `LibraryMath` Library Blueprint. Verifies that the generated static methods
`Clamp`, `Lerp`, and `Add` produce correct outputs when called directly from test code.
No ECS world or tick system required.

### HealthRegenDemoTests

Exercises the `HealthRegen` Instance Blueprint. Creates an entity with a
`BlueprintBlackboard1024` component, attaches the Blueprint, runs several tick frames,
and asserts that the entity's health increases per-tick toward the `MaxHealth` variable
and that an `OnLowHealth` custom event handler fires at the health threshold.

### DoorActorDoorSensorDemoTests

Exercises two-Blueprint peer call semantics. `DoorActor` calls `DoorSensor.IsTriggered()`
via a `CallPeerBlueprintNode`. Creates both entities, wires peer references in the
registry, and asserts that the `DoorActor` tick correctly reads the `DoorSensor` state.

### HasVisibleTargetDemoTests

Exercises `HasVisibleTarget`, an AiPrimitive Condition with two hostings
(`BTreeCondition` and `HsmCondition`). Invokes the generated `TickCore` via both the
B-Tree dispatcher thunk and the HSM dispatcher thunk and asserts that the condition
returns `NodeStatus.Success` when a visible target entity exists in the mock view.

### MoveToAndFireDemoTests

Exercises `MoveToAndFire`, an AiPrimitive Action that contains latent `WaitForChannel`
nodes. Runs multiple tick frames and asserts the phase-byte state machine progresses
correctly through the `MoveTo` channel command, the wait, and the `AimAndFire` command.

---

## Dependencies

### Project References

| Reference | Purpose |
|-----------|---------|
| `Hrot.Blueprints.Core` | Full runtime: asset model, compiler, debug probe, debug session interface, ALC coordinator |
| `Hrot.Blueprints.Editor` | Editor windows, reload services, node drawers, visual attachments |
| `Fdp.Core` | Entity type and ECS interfaces |
| `Fdp.Toolkits` | Blueprint runtime toolkit (registry, blackboard, tick systems, behavior registry) |

### NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `xunit` | 2.x | Test framework |
| `xunit.runner.visualstudio` | 2.x | VS test runner integration |
| `Microsoft.NET.Test.Sdk` | latest | `dotnet test` host |

### InternalsVisibleTo

`Hrot.Blueprints.Core` and `Hrot.Blueprints.Compiler` both grant
`InternalsVisibleTo("Hrot.Blueprints.Tests")`, giving the test project access to
all `internal` compiler types including validators, IR printer, stage implementations,
and emit internals.

---

## Running the Tests

```powershell
# Run the full test suite
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj

# Run only compiler stage tests
dotnet test ... --filter "FullyQualifiedName~Compiler"

# Run only demo tests
dotnet test ... --filter "FullyQualifiedName~Demo"

# Run only hot-reload tests
dotnet test ... --filter "FullyQualifiedName~HotReload"

# Run only debug protocol tests
dotnet test ... --filter "FullyQualifiedName~Debug"

# Regenerate emit snapshots
$env:BLUEPRINT_REGENERATE_SNAPSHOTS=1
dotnet test ... --filter "FullyQualifiedName~Stage7"
Remove-Item Env:BLUEPRINT_REGENERATE_SNAPSHOTS
```
