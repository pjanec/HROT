# Task Detail — EyesAndMuscle Workstream

**Reference:** See [DESIGN.md](./DESIGN.md) for architectural rationale and phase descriptions.

---

## Phase 1 — DRY Initialization Infrastructure

---

### EAM-I001 — FdpKernelBuilder and HrotNodeContext

**Design reference:** [Phase 1 — DRY Initialization Infrastructure](./DESIGN.md#phase-1--dry-initialization-infrastructure)

**Scope**

Create two new building-block types:

1. `FdpKernelBuilder` — a generic, DDS-agnostic builder that initializes the minimum FDP engine stack.
2. `HrotNodeContext` — an immutable record that carries all the initialized state produced by `HrotNodeBuilder`.
3. `HrotNodeBuilder` — Hrot-specific builder wrapping `FdpKernelBuilder` with DDS and cluster wiring.

`HrotNodeBuilder` is the primary entry point; `FdpKernelBuilder` is an internal helper it uses.

**Constraints**

- `FdpKernelBuilder` must NOT reference `CycloneDDS`, `Hrot.NED`, `Hrot.SimHost`, or `Hrot.IG`. It belongs in a namespace that is accessible from all of those projects (e.g., `Hrot.Common` or `FDP.Framework.Runner`). If it goes in `Hrot.ClusterRunner`, it is internal to that project, which is acceptable since EyesAndMuscle lives there.
- `HrotNodeBuilder` must NOT reference `Hrot.SimHost` internal domain types (doctrines, SimHost components, road network). It only knows about generic Hrot infrastructure (`HrotEnvironment`, NED orchestration message types, `ClusterSlave`).
- Builders are single-use; `Build()` must throw `InvalidOperationException` if called more than once.
- `HrotNodeContext` is a positional record; all fields are `init`-only.

**`HrotNodeContext` record definition**

```csharp
public record HrotNodeContext(
    EntityRepository World,
    ModuleHostKernel Kernel,
    DdsParticipant Participant,
    FdpEventBus EventBus,
    NetworkEntityMap EntityMap,
    ClusterSlave ClusterSlave,
    NodeOpSlaveTranslator? SlaveTranslator,         // null when no DDS available (tests)
    IReadOnlyList<IEcsModule> BaseModules           // infrastructure modules created by the builder
);
```

**`HrotNodeBuilder` fluent interface**

```csharp
_context = new HrotNodeBuilder(config)
    .WithRole("EyesAndMuscle", NodeRole.MuscleGround | NodeRole.ImageGenerator)
    .Build();
```

`WithRole` sets the subsystem name and role; `Build()` runs the full initialization sequence and returns `HrotNodeContext`.

**Initialization sequence inside `Build()`**

In order:
1. `EntityRepository` construction
2. `ModuleHostKernel` + `EventAccumulator`
3. `FdpEventBus` (slave time bus)
4. `TimeControllerFactory.Create(eventBus, new TimeControllerConfig { Role = TimeRole.Slave, LocalNodeId = config.NodeId, ... })`
5. `kernel.SetTimeController(timeCtrl)`
6. `HrotEnvironment.CreateParticipant(config.DomainId)` + `EnableSenderTracking`
7. `new NetworkEntityMap()`
8. `new DdsIdAllocator(participant, subsystemName + "Allocator")`
9. `ClusterSlave` + `NodeOpSlaveTranslator` — wired **inline** (do NOT call `NodeBootstrapper.BuildOrchestration`; see note below)
10. Register generic handlers only: `ReferencePreviewHandler`, `ReferencePrefetchHandler`, `ReferenceArchiveHandler`, `ReferenceLiveLoadHandler`
11. Collect any infrastructure `IEcsModule` instances the builder created into `IReadOnlyList<IEcsModule> baseModules`
12. Return `new HrotNodeContext(..., BaseModules: baseModules)`

**Why inline, not `NodeBootstrapper.BuildOrchestration`:** `NodeBootstrapper.BuildOrchestration` is hardcoded to register domain-specific handlers (e.g., `ReferenceScenarioLoadHandler`, `ReferenceEditLoadHandler`) that depend on the SimHost scenario serializer. Calling it from `HrotNodeBuilder` would drag domain-specific serialization logic into the generic builder, violating the "Separation of concerns" constraint. The builder registers only the four generic handlers above; each subsystem registers its own domain-specific handlers after `Build()` returns.

**Files to create**

- `Hrot.ClusterRunner/Infrastructure/HrotNodeContext.cs` — the result record (no logic, no dependencies)
- `Hrot.ClusterRunner/Infrastructure/HrotNodeBuilder.cs` — the Hrot-specific builder; may internally use a private `FdpKernelBuilder` helper class
- Optionally: a private `FdpKernelBuilder` inner class or nested static class within `HrotNodeBuilder.cs` to isolate steps 1-5 (generic engine) from steps 6-10 (Hrot/DDS-specific). This is the "two-layer" split described in DESIGN.md. Whether it is a public class placed in `FDP.Framework.Runner` or `Hrot.Common`, or a private helper, is an implementation decision — but the separation of concerns between the generic engine setup and the DDS/NED wiring MUST be visible in the code (e.g., clearly separate regions or methods).

**Success conditions**

*SC1 — Builder produces valid context:*
> Setup: call `new HrotNodeBuilder(new SubsystemConfig { DomainId = testDomainId, NodeId = 200, Headless = true }).WithRole("Test", NodeRole.MuscleGround).Build()`.
> Assert: returned `HrotNodeContext` is non-null; `World`, `Kernel`, `EventBus`, `EntityMap`, `ClusterSlave` are all non-null; `BaseModules` is non-null and non-empty.

*SC2 — Kernel has a time controller:*
> Setup: same as SC1.
> Assert: calling `context.Kernel.GetCurrentTimeMode()` (or equivalent) does not throw and returns a valid mode; `SlaveSyncController` is active.

*SC3 — Double-build throws:*
> Setup: create a builder, call `Build()` once (succeeds), call `Build()` again.
> Assert: second call throws `InvalidOperationException`.

*SC4 — SlaveTranslator is null in headless tests without DDS participant:*
> Setup: configure SubsystemConfig such that DDS is not available (e.g., `Headless = true` with no DDS daemon running, or use a test domain that rejects connections).
> Assert: `HrotNodeContext.SlaveTranslator` is `null` and no exception is thrown (the builder degrades gracefully).

*SC5 — `NodeBootstrapper.BuildOrchestration` is NOT called:*
> Code review: `HrotNodeBuilder.cs` must NOT call `NodeBootstrapper.BuildOrchestration`. The `ClusterSlave` and `NodeOpSlaveTranslator` wiring must be inlined; only the four generic handlers are registered inside the builder. `HrotNodeBuilder.cs` must not import `Hrot.SimHost`.

---

### EAM-I002 — EnsureIdAllocatorRouting helper

**Design reference:** [Phase 1 — DRY Initialization Infrastructure](./DESIGN.md#phase-1--dry-initialization-infrastructure)

**Scope**

Extract the `EnsureIdAllocatorRouting(DdsParticipant)` method that currently exists only in `SimHostApp` into a shared static helper accessible from `HrotNodeBuilder`. This method wires the DDS ID allocator round-trip (request/response routing).

**Constraints**

- Must be a static method or an extension method on `DdsParticipant`.
- Must be accessible from `HrotNodeBuilder` without referencing `Hrot.SimHost`.
- Do NOT change the routing logic; only move it.

**Files to modify or create**

- Create `Hrot.Common/Networking/DdsIdAllocatorHelper.cs` OR inline into `HrotNodeBuilder.cs`.
- Modify `Hrot.SimHost/SimHostApp.cs` — replace the inline `EnsureIdAllocatorRouting` with a call to the shared helper.

**Success conditions**

*SC1 — Existing SimHost tests still pass:*
> All tests in `Hrot.SimHost.Tests` and `Hrot.ClusterRunner.Integration.Tests` that involve DDS still pass after the refactor.

*SC2 — No duplicate code:*
> Code review: there is exactly one implementation of the allocator routing logic. `SimHostApp` calls the shared helper.

---

## Phase 2 — NedReplicationModule

---

### EAM-N001 — NedReplicationModule core

**Design reference:** [Phase 2 — NedReplicationModule](./DESIGN.md#phase-2--nedreplicationmodule)

**Scope**

Create `NedReplicationModule : IEcsModule` in `Hrot.ClusterRunner/Replication/NedReplicationModule.cs`.

This module, when registered with the kernel, handles the complete NED ↔ ECS Anti-Corruption Layer for a given `NodeRole`. It:
- Selects the correct translator packs based on role.
- Registers the ECS systems that depend on NED-specific ECS components.

**What is NOT in scope:**
- Runtime hot-swap between NED and BDC — not needed in this workstream.

**Constructor**

```csharp
public NedReplicationModule(
    DdsParticipant participant,
    NodeRole role,
    NetworkEntityMap entityMap,
    IGeographicTransform geoTransform,
    FdpEventBus eventBus,
    int localNodeId,
    int domainId)
```

**`RegisterSystems(ISystemRegistry registry)` must register:**

For **all** roles:
- `GhostCreationSystem(entityMap)` — entity replica materialization on ingress
- `NetworkLifecycleSystemGroup(ghostCreationSystem)` — wraps lifecycle systems for replay gating
- `CycloneNetworkCleanupSystem(translators)` — fires DDS `Dispose` signal when an entity is destroyed locally; must receive the list of all registered translator instances
- `DisposalMonitoringSystem(entityMap)` — cleans up `NetworkEntityMap` entries when entities are removed from the ECS world
- `SharedTranslatorPack.Build(participant, entityMap, geoTransform, eventBus, localNodeId, domainId)`
- Register CycloneNetworkModule or equivalent that routes translator ticks

For `NodeRole.MuscleGround` (or `AllInOne`):
- Translators from `KinematicTranslatorPack.Build(participant, entityMap, geoTransform, eventBus, localNodeId)`
- `SmartEgressSystem` — suppresses duplicate egress packets

For `NodeRole.ImageGenerator` (or `AllInOne`):
- Translators from `EntityStatesIngressPack.Build(participant, entityMap, geoTransform, eventBus)`
- `DeadReckoningSyncSystem` — NED-specific DR interpolation

For `NodeRole.Brain` (or `AllInOne`):
- Translators from `CognitiveTranslatorPack.Build(participant, entityMap, geoTransform, eventBus, localNodeId)`
- `SmartEgressSystem` — suppresses duplicate egress packets (if already registered for `MuscleGround`, reuse the same instance; do not register twice)

**Constraints**

- `DeadReckoningSyncSystem` is currently in `Hrot.IG/Systems/`. It must be accessible from `NedReplicationModule` (in `Hrot.ClusterRunner`). Since `Hrot.ClusterRunner` already references `Hrot.IG`, this is legal. If at a later stage `NedReplicationModule` is moved to a shared project, `DeadReckoningSyncSystem` would need to move too — note this in a `// TODO: move to shared if NedReplicationModule is extracted` comment.
- The module's `ExecutionPolicy` should be `Synchronous` (it wraps synchronous translator ticks).
- The `Tick(ISimulationView view, float dt)` method is a no-op; all work is done through the registered systems.
- Role check: if `role` has none of `MuscleGround`, `ImageGenerator`, or `Brain` flags set (e.g., `NodeRole.Perception` or `NodeRole.NavigationSolver` alone), throw `ArgumentException` in the constructor with a descriptive message listing which roles are supported.

**Files to create**

- `Hrot.ClusterRunner/Replication/NedReplicationModule.cs`

**Success conditions**

*SC1 — Module registers correct systems for MuscleGround role:*
> Setup: construct `NedReplicationModule(participant, NodeRole.MuscleGround, entityMap, geo, bus, nodeId, domainId)`.
> Assert: after registering with a test kernel, `GhostCreationSystem`, `SmartEgressSystem`, `CycloneNetworkCleanupSystem`, and `DisposalMonitoringSystem` are registered; `DeadReckoningSyncSystem` is NOT registered.

*SC2 — Module registers correct systems for ImageGenerator role:*
> Setup: construct with `NodeRole.ImageGenerator`.
> Assert: `GhostCreationSystem`, `DeadReckoningSyncSystem`, `CycloneNetworkCleanupSystem`, and `DisposalMonitoringSystem` are registered; `SmartEgressSystem` is NOT registered.

*SC3 — Module registers all systems for combined role with driveFromNetwork: false:*
> Setup: construct with `NodeRole.MuscleGround | NodeRole.ImageGenerator`.
> Assert: `GhostCreationSystem`, `SmartEgressSystem`, and `DeadReckoningSyncSystem` are all registered.
> Assert: the `DeadReckoningSyncSystem` instance was constructed with `driveFromNetwork: false` (verify via constructor argument capture or a test-seam property on the module). This ensures DR processes only ghost entities (`IsGhost == true`) and does not fight `GroundKinematicsModule` on locally-owned entities.

*SC4 — Invalid role throws:*
> Setup: construct with a role flag that has none of `MuscleGround`, `ImageGenerator`, or `Brain` set (e.g., `NodeRole.Perception`).
> Assert: constructor throws `ArgumentException`.

*SC5 — No duplicate DeadReckoningSyncSystem registration:*
> Construct module, register with kernel, register manually again elsewhere (simulate mistake).
> Assert: kernel throws on the second registration OR `NedReplicationModule` protects against double-registration via a guard in its registration sequence.

*SC6 — Module registers correct systems for Brain role:*
> Setup: construct with `NodeRole.Brain`.
> Assert: `GhostCreationSystem`, `SmartEgressSystem`, `CycloneNetworkCleanupSystem`, and `DisposalMonitoringSystem` are registered; `DeadReckoningSyncSystem` is NOT registered (Brain has no DR interpolation requirement).

---

### EAM-N002 — Shared translator pack accessibility

**Design reference:** [Phase 2 — NedReplicationModule](./DESIGN.md#phase-2--nedreplicationmodule)

**Scope**

Ensure that the translator pack factory methods used by `NedReplicationModule` are accessible from `Hrot.ClusterRunner`. Currently:
- `KinematicTranslatorPack.Build(...)` is in `Hrot.SimHost.Network` — accessible since `Hrot.ClusterRunner` references `Hrot.SimHost`.
- `SharedTranslatorPack.Build(...)` — same.
- `EntityStatesIngressPack` — currently in `Hrot.IG`. Accessible since `Hrot.ClusterRunner` references `Hrot.IG`.

**Tasks:**

1. Verify that `KinematicTranslatorPack` has a `public static Build(...)` factory. If it is declared `internal`, change visibility to `public`.
2. Verify that `SharedTranslatorPack` has a `public static Build(...)` factory. Same.
3. Verify that `EntityStatesIngressPack` (or its equivalent in `Hrot.IG/Translators/`) has a `public static Build(...)` factory.
4. Verify that `CognitiveTranslatorPack` has a `public static Build(...)` factory accessible from `Hrot.ClusterRunner`. Since the Brain role is now fully in scope for `NedReplicationModule`, this pack must be reachable from `Hrot.ClusterRunner/Replication/NedReplicationModule.cs`.
5. If any of these classes use a constructor-based pattern rather than a factory, create a `Build` static method or change the access modifier.

**Constraints**

- Make only the minimum visibility changes needed — do not refactor the class internals.
- Do not change method signatures; only add `public` or add a factory-delegate wrapper.

**Files to potentially modify**

- `Hrot.SimHost/Network/KinematicTranslatorPack.cs`
- `Hrot.SimHost/Network/SharedTranslatorPack.cs`
- `Hrot.IG/Translators/EntityStatesIngressPack.cs` (or equivalent file)
- `Hrot.CGF/` or `Hrot.Common/` — wherever `CognitiveTranslatorPack` is defined; make `Build(...)` public if needed

**Success conditions**

*SC1 — `NedReplicationModule.cs` compiles cleanly with no `// HACK: internal access` workaround.*

*SC2 — No behavioral change in existing tests:*
> All tests in `Hrot.SimHost.Tests`, `Hrot.IG.Tests`, and `Hrot.ClusterRunner.Integration.Tests` still pass after visibility changes.

---

## Phase 3 — EyesAndMuscle Subsystem

---

### EAM-E001 — EyesAndMuscleSubsystem shell

**Design reference:** [Phase 3 — EyesAndMuscle Subsystem](./DESIGN.md#phase-3--eyesandmuscle-subsystem)

**Scope**

Create `EyesAndMuscleSubsystem : ISubsystem, IMapCameraProvider, IWindowRegistrar` in `Hrot.ClusterRunner/Services/EyesAndMuscleSubsystem.cs`.

This task covers:
1. The subsystem class itself (all lifecycle methods).
2. Its use of `HrotNodeBuilder` (Phase 1) for initialization.
3. Registration of `NedReplicationModule` (Phase 2) for networking.
4. Registration of the existing `SimHostCoreLogicPack` (muscle) and IG presentation modules (eyes).
5. MapCanvas + 2D visualization wiring (headless-safe).

This task does NOT cover the `EyesAndMuscleModule` (the SoD async PoC module) — that is EAM-E002.

**Initialization sequence inside `Initialize(SubsystemConfig config)`**

```
1. HrotNodeBuilder → HrotNodeContext (world, kernel, bus, entityMap, participant, clusterSlave)
2. Register component types:
   a. SimHostComponentRegistry.RegisterAll(context.World)
   b. IgComponentRegistry.RegisterAll(context.World)  (or equivalent combined set)
3. NedReplicationModule (NodeRole.MuscleGround | NodeRole.ImageGenerator)
4. SimHostCoreLogicPack (role = MuscleGround subset: ActionDispatch, GroundKinematics, Combat, DamageAssessment)
5. IG presentation modules: StyleResolutionModule, MapLayerModule, MapCullingModule, EventEffectModule, HistoryTrailModule
6. EyesAndMuscleModule (created in EAM-E002, registered here)
7. SpawningModule / NetworkSpawning systems
8. kernel.Initialize()
9. If not headless: create MapCanvas, wire visualization
```

**`Update(float deltaTime)` sequence**

```
1. _slaveTranslator?.Tick()      // DDS → bus
2. _clusterSlave.Tick()          // cluster state machine
3. _kernel.Update()              // ECS + all modules
```

**`DrawWorld()`** — calls `_canvas?.Draw()` (no-op if headless).

**`DrawUI()`** — renders subsystem ImGui panels (status, entity count, module state).

**`Shutdown()`** — disposes kernel, participant, canvas in reverse order.

**Headless support**

When `config.Headless = true`, skip Raylib/MapCanvas creation entirely. All module registrations still occur.

**Constraints**

- Do not copy-paste large blocks from `SimHostApp.OnLoad`; use `HrotNodeBuilder`.
- The `doctrineRegistry` is constructed here (not inside the builder), because doctrines are domain-specific.
- Road network loading: load from `config.json` if available; use `default` (empty) blob if not found.
- The subsystem must be usable without an `OrchestratorSubsystem` (i.e., ClusterSlave starts in standalone-friendly state).

**Files to create**

- `Hrot.ClusterRunner/Services/EyesAndMuscleSubsystem.cs`

**Success conditions**

*SC1 — Subsystem boots without exception (headless, no DDS):*
> Setup: create `EyesAndMuscleSubsystem`, call `Initialize(new SubsystemConfig { Headless = true, DomainId = testDomain, NodeId = 55 })`.
> Assert: no exception; `World` property is non-null.

*SC2 — Kernel has expected modules registered:*
> After `Initialize`, use reflection or test-hook to enumerate registered module names.
> Assert: names include `"NedReplication"`, `"SimulationLogic"` (or the SimHostCoreLogicPack name), `"StyleResolution"`, `"EyesAndMuscle"`.

*SC3 — `Update` does not throw on empty world:*
> Call `Initialize(headless)` then `Update(0.016f)` ten times.
> Assert: no exception.

*SC4 — `Shutdown` disposes kernel cleanly:*
> Call `Initialize(headless)`, then `Shutdown()`.
> Assert: no exception; a second `Update()` call after shutdown throws `ObjectDisposedException` or similar defensive error.

*SC5 — Headless mode skips visualization:*
> Call `Initialize(headless: true)`, then `DrawWorld()`.
> Assert: no exception (canvas is null-guarded); no Raylib API called.

---

### EAM-E002 — EyesAndMuscleModule (SoD async PoC)

**Design reference:** [Phase 3 — EyesAndMuscle Subsystem — EyesAndMuscleModule](./DESIGN.md#eyesandmusclemodule--sod-async-design)

**Scope**

Create `EyesAndMuscleModule : IEcsModule` in `Hrot.ClusterRunner/Services/EyesAndMuscleModule.cs`.

This module is the architectural centerpiece of the PoC. It runs **asynchronously** on a background thread with a **Snapshot-on-Demand (SoD)** execution policy.

**Properties**

```csharp
public string Name => "EyesAndMuscle";

public IModuleExecutionPolicy Policy => ModuleExecutionPolicy.Asynchronous(
    dataStrategy: DataStrategy.SoD,
    targetHz: 60);

public IEnumerable<Type>? GetRequiredComponents() => new[]
{
    typeof(SimTransform),
    typeof(NavigationIntent),
    typeof(NetworkIdentity),
};

public void RegisterSystems(ISystemRegistry registry) { } // no-op; uses Direct Execution pattern
```

**`Tick(ISimulationView view, float deltaTime)` logic**

```
1. THE EYES (always runs for MuscleGround | ImageGenerator role):
   - Query entities with SimTransform + NetworkIdentity
   - For each entity: read tf = view.GetComponentRO<SimTransform>(entity)
   - In PoC: increment an internal counter + optionally log position
   - In Stride: push tf.Position/Rotation to StrideDataBridge

2. THE MUSCLE (runs when role includes MuscleGround):
   - Query entities with NavigationIntent + SimTransform
   - For each entity: read intent, read currentTf
   - If NavigationMode.DirectPoint: compute simplified step toward destination
   - Write new SimTransform via cmd.SetComponent(entity, newTf)
   - The command buffer is auto-flushed by the kernel after Tick returns

3. DO NOT call view.ReleaseView() — the kernel handles this automatically.
```

**Constructor**

```csharp
public EyesAndMuscleModule(NodeRole role, DataStrategy dataStrategy = DataStrategy.SoD)
```

The `role` parameter determines whether the Muscle write path is active.
The `dataStrategy` parameter selects `RunMode.Asynchronous + DataStrategy.SoD` (production default) or `RunMode.Synchronous + DataStrategy.Direct` (useful for debugging or test isolation). The SoD asynchronous mode is the primary goal of the PoC; the Direct mode is available as a test shortcut.

**Test seams**

Expose `int EyesTicks` and `int MuscleTicks` counters (incremented in respective paths) so integration tests can assert the module ran.

**Constraints**

- Must NOT hold a reference to `view` beyond the `Tick` call.
- Must NOT call `Dispose` on anything from `view`.
- The Muscle path runs on the background thread; command-buffer writes are the only legal mutation.
- If an entity has `NavigationIntent` but no valid destination, the Muscle path skips that entity without throwing.
- This module is intentionally lightweight (a PoC). It does NOT implement full physics — that is handled by `SimHostCoreLogicPack.GroundKinematicsModule`. Its purpose is to prove the SoD+CommandBuffer plumbing.

**Files to create**

- `Hrot.ClusterRunner/Services/EyesAndMuscleModule.cs`

**Success conditions**

*SC1 — Module ticks execute on non-main thread:*
> Setup: register module in a test kernel; pump 10 frames.
> Assert: `EyesTicks >= 1` and the thread ID recorded in `Tick` differs from `Thread.CurrentThread.ManagedThreadId` of the `Update` caller.

*SC2 — Tick reads SimTransform from snapshot:*
> Setup: spawn entity with `SimTransform { Position = Vector3(1, 0, 0) }` in the world; pump frames until `EyesTicks >= 1`.
> Assert: position observed inside `Tick` equals `Vector3(1, 0, 0)` (or within tolerance).

*SC3 — Muscle write path applies position update:*
> Setup: spawn entity with `NavigationIntent { Mode = DirectPoint, FinalDestination = Vector2(10, 10) }` and `SimTransform { Position = Vector3(0, 0, 0) }`; pump frames until `MuscleTicks >= 5`.
> Assert: `SimTransform.Position` has moved away from `Vector3(0, 0, 0)` in the direction of the destination.

*SC4 — View is not held after Tick:*
> Code review: no field on `EyesAndMuscleModule` stores the `ISimulationView` argument.

*SC5 — Role suppression works:*
> Setup: construct module with `NodeRole.ImageGenerator` only; pump frames with an entity that has `NavigationIntent`.
> Assert: `MuscleTicks == 0`.

---

### EAM-E003 — EyesAndMuscle integration test

**Design reference:** [Phase 3 — EyesAndMuscle Subsystem](./DESIGN.md#integration-test)

**Scope**

Create `EyesAndMuscleIntegrationTests` in `Hrot.ClusterRunner.Integration.Tests/`.

These tests use the existing `HrotRunnerHarness` pattern (headless, deterministic, isolated DDS domain).

**Test scenarios**

**Test 1 — Subsystem boots and runs:**
> Setup: create Harness with `EyesAndMuscleSubsystem` alongside `OrchestratorSubsystem`.
> Action: `PumpFrames(50)`.
> Assert: no exception; `EyesAndMuscleSubsystem.World` is non-null; entity count starts at 0.

**Test 2 — Entity spawn propagates through muscle and eyes:**
> Setup: boot harness; transition to OperatingLive.
> Action: spawn an entity with `SimTransform`, `NavigationIntent(DirectPoint, dest=(100, 0))`.
> Action: `PumpFrames(100)`.
> Assert: `SimTransform.Position.X > 0` (entity moved); `EyesAndMuscleModule.EyesTicks > 0`; `EyesAndMuscleModule.MuscleTicks > 0`.

**Test 3 — Module runs asynchronously (does not block main loop):**
> Setup: inject a spy that records timestamps of main-thread `Update` calls and `Tick` calls in `EyesAndMuscleModule`.
> Action: `PumpFrames(30)`.
> Assert: at least one `Tick` timestamp falls between two consecutive `Update` timestamps (confirming async execution).

**Constraints**

- All tests must be headless.
- Use per-test domain ID counter (as in existing integration test harness).
- Tests must not rely on wall-clock timing (use frame counters or event counters instead).
- Constructor-injection of test spies via `EyesAndMuscleModule` constructor or a factory delegate; do not use `Thread.Sleep`.

**Files to create**

- `Hrot.ClusterRunner.Integration.Tests/EyesAndMuscleIntegrationTests.cs`

**Success conditions**

See test scenarios above. CI: all three tests pass under `dotnet test` with `--no-build`.

---

## Phase 4 — Migrate Existing Subsystems

---

### EAM-M001 — Migrate SimHostApp to HrotNodeBuilder + NedReplicationModule

**Design reference:** [Phase 4 — Migrate Existing Subsystems](./DESIGN.md#phase-4--migrate-existing-subsystems)

**Scope**

Replace the ~300-line manual initialisation sequence in `SimHostApp.OnLoad` with a call to `HrotNodeBuilder.Build()`. Register a `NedReplicationModule` for the `AllInOne` combined role (or the exact `NodeRole` flags that `SimHostApp` uses today) and store it as `_nedReplicationModule : IEcsModule` private field.

**Constraints**

- Pure behavioural refactor — no logic changes, no new features.
- `_simLogicModule` and `_nedReplicationModule` must be stored as private fields on `SimHostApp` so they can be passed to `_kernel.UninstallModulesAsync(new[] { _nedReplicationModule, _simLogicModule })` during teardown.
- `EnsureIdAllocatorRouting` must call the shared helper created in EAM-I002.
- All existing tests in `Hrot.SimHost.Tests` and `Hrot.ClusterRunner.Integration.Tests` must pass without modification.

**Files to modify**

- `Hrot.SimHost/SimHostApp.cs` — replace `OnLoad` body; remove duplicated wiring that now lives in `HrotNodeBuilder`.

**Success conditions**

*SC1 — All integration tests pass:*
> Run `dotnet test Hrot.ClusterRunner.Integration.Tests` and `dotnet test Hrot.SimHost.Tests`.
> Assert: 0 failures, 0 regressions.

*SC2 — SimHostApp.OnLoad body is ≤ 60 lines:*
> Code review: the method body is dominated by calls to `HrotNodeBuilder` and module registration; no more than one screen of inline initialisation remains.

*SC3 — No manual DDS participant creation in OnLoad:*
> Code review: `HrotEnvironment.CreateParticipant` is called only inside `HrotNodeBuilder`; `SimHostApp.OnLoad` does not call it directly.

*SC4 — Module fields are retained:*
> Code review: `SimHostApp` declares `private IEcsModule _nedReplicationModule` and `private IEcsModule _simLogicModule`; `Shutdown()` passes them to `UninstallModulesAsync`.

---

### EAM-M002 — Migrate IgApplication to HrotNodeBuilder + NedReplicationModule

**Design reference:** [Phase 4 — Migrate Existing Subsystems](./DESIGN.md#phase-4--migrate-existing-subsystems)

**Scope**

Replace the manual initialisation inside `IgApplication.InitializeEmbedded` (and any companion setup methods) with `HrotNodeBuilder.Build()`. Register `NedReplicationModule(role: NodeRole.ImageGenerator)`. Because this is a pure IG node, `DeadReckoningSyncSystem` must be configured with `driveFromNetwork: true` (smooth all entities — there is no local physics to protect).

**Constraints**

- Pure behavioural refactor.
- `driveFromNetwork: true` must be explicit at the `NedReplicationModule` registration site; a comment must explain why (`// Pure IG — no local physics; smooth all entities`).
- `_nedReplicationModule` stored as a private field; passed to `UninstallModulesAsync` in teardown.
- All existing tests in `Hrot.IG.Tests` and integration tests that exercise `IgSubsystem` must pass.

**Files to modify**

- `Hrot.IG/IgApplication.cs` (or equivalent entry-point file for the IG application)

**Success conditions**

*SC1 — All IG tests pass:*
> Run `dotnet test Hrot.IG.Tests` and any integration tests that boot `IgSubsystem`.
> Assert: 0 failures.

*SC2 — DeadReckoningSyncSystem configured with driveFromNetwork: true:*
> Code review: the only registration of `DeadReckoningSyncSystem` in `IgApplication` is via `NedReplicationModule` with `driveFromNetwork: true`.

*SC3 — No manual DDS participant creation in InitializeEmbedded:*
> Code review: `HrotEnvironment.CreateParticipant` not called directly in `IgApplication`; called only inside `HrotNodeBuilder`.

---

### EAM-M003 — Migrate CgfSubsystem to HrotNodeBuilder

**Design reference:** [Phase 4 — Migrate Existing Subsystems](./DESIGN.md#phase-4--migrate-existing-subsystems)

**Scope**

Replace the manual kernel / world / DDS setup inside `CgfSubsystem.Initialize` with `HrotNodeBuilder.Build()` and register `NedReplicationModule(role: NodeRole.Brain)`. The `Brain` role is fully supported by `NedReplicationModule` (implemented in EAM-N001), which wires `CognitiveTranslatorPack`, `SmartEgressSystem`, `GhostCreationSystem`, `CycloneNetworkCleanupSystem`, and `DisposalMonitoringSystem`. This is a pure behavioural refactor — `CgfSubsystem` no longer registers any translator packs manually.

**Constraints**

- Pure behavioural refactor — no logic changes.
- `_nedReplicationModule` must be stored as a private field on `CgfSubsystem` and passed to `_kernel.UninstallModulesAsync(new[] { _nedReplicationModule })` during teardown.
- All CGF tests in `Hrot.CGF` and `Hrot.ClusterRunner.Integration.Tests` that exercise `CgfSubsystem` must pass.

**Files to modify**

- `Hrot.CGF/CgfSubsystem.cs` (or wherever `CgfSubsystem.Initialize` resides)

**Success conditions**

*SC1 — All CGF integration tests pass:*
> Run integration tests that boot `CgfSubsystem`.
> Assert: 0 failures.

*SC2 — HrotNodeBuilder and NedReplicationModule are used:*
> Code review: `CgfSubsystem.Initialize` calls `new HrotNodeBuilder(config).WithRole("CgfNode", NodeRole.Brain).Build()`, then registers `NedReplicationModule(role: NodeRole.Brain, ...)`. No manual `CognitiveTranslatorPack` registration remains in `CgfSubsystem`.

*SC3 — NedReplicationModule field is retained:*
> Code review: `CgfSubsystem` declares `private IEcsModule _nedReplicationModule`; the teardown path passes it to `_kernel.UninstallModulesAsync`.

---

## Summary of all tasks

| ID | Phase | Name | Project |
|---|---|---|---|
| EAM-I001 | 1 | HrotNodeBuilder and HrotNodeContext | Hrot.ClusterRunner |
| EAM-I002 | 1 | EnsureIdAllocatorRouting helper | Hrot.Common or Hrot.ClusterRunner |
| EAM-N001 | 2 | NedReplicationModule core | Hrot.ClusterRunner |
| EAM-N002 | 2 | Shared translator pack accessibility | Hrot.SimHost, Hrot.IG |
| EAM-E001 | 3 | EyesAndMuscleSubsystem shell | Hrot.ClusterRunner |
| EAM-E002 | 3 | EyesAndMuscleModule (SoD async PoC) | Hrot.ClusterRunner |
| EAM-E003 | 3 | EyesAndMuscle integration test | Hrot.ClusterRunner.Integration.Tests |
| EAM-M001 | 4 | Migrate SimHostApp | Hrot.SimHost |
| EAM-M002 | 4 | Migrate IgApplication | Hrot.IG |
| EAM-M003 | 4 | Migrate CgfSubsystem | Hrot.CGF |
