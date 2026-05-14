# Stride Mock — Task Details

**Reference:** See [DESIGN.md](./DESIGN.md) for the full design context behind each task.

---

## Phase 1 — Foundation: Project Scaffolding

---

### SM-001 — Create Project Scaffolding

**Goal:** Create the two new C# projects and wire them into the solution so subsequent tasks have a build target.

**Locations:**
- `Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.csproj` — Class library
- `Hrot\Runner\Hrot.FakeStrideApp\Hrot.FakeStrideApp.csproj` — Executable (`OutputType = Exe`)

**Steps:**
1. Create `Hrot.StrideMock.csproj` targeting `net8.0`. Reference: `Hrot.Common` (for `SharedApplicationBootstrapper`, `HrotNodeBuilder`, `MapCamera`), `Hrot.SimHost` (for domain logic only: `SimHostComponentRegistry`, `KinematicComponentRegistry`, `GroundKinematicsModule`, `CombatModule`, `CognitiveSpatialModule`, `NavigationSolverModule`), `Hrot.Core`, `Fdp.Toolkit`, `Fdp.Presentation`. **Note:** `NodeBootstrapper` will be accessed through `SharedApplicationBootstrapper` in `Hrot.Common` — it is not a direct dependency of `Hrot.StrideMock`.
2. Create `Hrot.FakeStrideApp.csproj` targeting `net8.0` with `OutputType = Exe`. Reference: `Hrot.StrideMock`, `Fdp.Presentation` (Raylib/ImGui shell), `Hrot.Network.NED`.
3. Add `Hrot.StrideMock` as a `<ProjectReference>` in `Hrot.ClusterRunner.csproj`.
4. Add both projects to the solution file (`IOS-IG-SimHost-FDP-2.sln`).

**Success Conditions:**
- `dotnet build Hrot.StrideMock.csproj` compiles with no errors (empty stubs acceptable).
- `dotnet build Hrot.FakeStrideApp.csproj` compiles with no errors.
- `Hrot.ClusterRunner.csproj` can reference types from `Hrot.StrideMock` without build errors.

---

## Phase 2 — SharedApplicationBootstrapper

---

### SM-002 — Implement SharedApplicationBootstrapper

**Goal:** Create the abstract Template Method bootstrapper in `Hrot.Common.Infrastructure` that locks the 7-phase initialization order, eliminating duplication across SimHost, IG, and StrideMock.

**Location:** `Hrot\Engine\Hrot.Common\Infrastructure\SharedApplicationBootstrapper.cs`

**Design Reference:** [DESIGN.md §4](./DESIGN.md#4-sharedapplicationbootstrapper-hrotcommoninfrastructure)

**Key Constraints (preserve these exactly):**
- Phase 2 (`RegisterDomainComponents`) must run before Phase 3 (serializer build).
- Phase 4 (`PopulateSystems` + group creation) must run before Phase 5 (`BuildOrchestration`).
- Phase 6 (spawning + network translators) must run after Phase 5.
- Phase 7 (`Kernel.Initialize()`) is always last.

**Success Conditions:**
- `SC_SM002_1`: `SharedApplicationBootstrapper` compiles; concrete test subclass implementing all abstract hooks can call `BootstrapNode()` without throwing.
- `SC_SM002_2`: If a subclass registers a component in `RegisterDomainComponents`, it is present in the world before `HrotScenarioSerializerFactory.Build()` is invoked (serializer includes the component).
- `SC_SM002_3`: If `PopulateSystems` registers a system, the `TogglableSimulationGroup` passed into `BuildOrchestration` contains it (verify via group inspection in a unit test).
- `SC_SM002_4`: `Kernel.Initialize()` is called exactly once, after all translator registrations.
- `SC_SM002_5`: The class exposes exactly these abstract hooks: `RegisterDomainComponents`, `BuildSerializer`, `PopulateSystems`, `BuildOrchestration`, `RegisterSpawningPipeline`, `RegisterNetworkTranslators`; and these virtual hooks: `GetAdditionalModules`, `GetBehaviorRegistry`. `BuildSerializer` and `BuildOrchestration` are abstract (not base-class concrete) because `HrotScenarioSerializerFactory` and `NodeBootstrapper` live in `Hrot.SimHost`, above `Hrot.Common` in the dependency hierarchy — referencing them from the base class would create an illegal circular dependency and prevent compilation. A test subclass that implements only the abstract hooks and leaves virtuals at defaults must compile and `BootstrapNode()` must complete without throwing.
- `SC_SM002_6`: `SharedApplicationBootstrapper.TimeControl` (type `ITimeControlGateway?`) is non-null after `BootstrapNode()` when a non-null `INetworkFactory` with a live DDS participant is provided. The base class Phase 6c sets it by calling `nodeFactory.CreateTimeControlGateway()` on the **configured** factory returned by `networkFactory.ConfigureForNode(context...)` — not the raw input `networkFactory`, whose event bus is an unbound shell disconnected from the kernel (a gateway built from the raw factory publishes `ClusterOpRequest` into the void and the cluster clock ignores all UI commands). This is the only place `TimeControl` is assigned; no constructor or subclass hook does it. Both `StrideMockSubsystem` and `FakeStrideApp` access time control via `_bootstrapper.TimeControl` (the base class property), not a duplicate field.
- `SC_SM002_7`: After `BootstrapNode()`, the kernel's registered ingress systems include translators for `SwitchTimeModeEvent`, `FrameOrderDescriptor`, and the NTP handshake (i.e., `TimeNetworkModule.CreateDescriptorTranslator`, `CreateSlaveLockstepTranslator`, `CreateSlaveTimeSyncTranslator` have been called and registered). These are wired by the base class in Phase 6c — **no subclass hook may register them again**. An integration test confirms that sending a simulated `SwitchTimeModeEvent` over the event bus causes the `SlaveSyncController` to transition state.
- `SC_SM002_8`: When `context.NedReplication` is non-null, `context.Kernel.RegisterModule(context.NedReplication)` is called by the base class in Phase 6a+, before any domain translator registration. Verify that `GhostCreationSystem` is present in the kernel after `BootstrapNode()`. Subclasses must **not** call `RegisterModule(context.NedReplication)` — a test using a subclass that does so must detect the double-registration (assertion on system count, or `ModuleHostKernel` throwing `InvalidOperationException`).
- `SC_SM002_9`: `HrotNodeBuilder` is chained with `.WithReplication(role)` before `.Build()` in Phase 1. Without this chain, `context.NedReplication` is permanently null, Phase 6a+ is silently skipped (no exception thrown), `GhostCreationSystem` is absent from the kernel, and all dead-reckoning and ghost-egress systems fail to register. Verify by asserting `context.NedReplication != null` immediately after `BootstrapNode()` in an integration test with a live participant.
- `SC_SM002_10`: `BuildOrchestration()` receives `lifecycleGroup: context.NedReplication?.NetworkLifecycleGroup`. Without this parameter the orchestration handler has no handle to disable ghost lifecycle processing during `PrepareReplay`; `GhostDestructionSystem` fires against the flight recorder's memory writes and corrupts replay state. Verify in a replay-load integration test that no ghost-destruction events fire while `LoadingReplay` is the cluster state.

---

## Phase 3 — Core Integration Library

---

### SM-003 — Implement StrideNodeBootstrapper

**Goal:** Concrete `SharedApplicationBootstrapper` that sets up the full SimHost-equivalent module set for the Stride node, plus the dual-buffer gizmo terminal, slave time sync, and `ITimeControlGateway`.

**Location:** `Hrot\Subsystems\Hrot.StrideMock\StrideNodeBootstrapper.cs`

**Design Reference:** [DESIGN.md §5](./DESIGN.md#5-stridenodebootstrapper-hrotstrideMock)

**Module injection (Stage 1 defaults):**
```csharp
var roadNetwork = SimHostApp.LoadRoadNetwork(null, localNodeId: nodeId);
var trajectoryPool = new TrajectoryPoolManager();

var bootstrapper = new StrideNodeBootstrapper(
    kinematicsModule:  new GroundKinematicsModule(roadNetwork, trajectoryPool),
    perceptionModule:  new CognitiveSpatialModule(context.World, ...),
    combatModule:      new CombatModule(),
    navigationModule:  new NavigationSolverModule(roadNetwork, trajectoryPool));
```

**Success Conditions:**
- `SC_SM003_1`: `BootstrapNode()` completes without throwing against a live DDS participant (integration test).
- `SC_SM003_2`: `Context.ClusterSlave` is non-null after `BootstrapNode()`.
- `SC_SM003_3`: `ProducerBuffer` and `ConsumerBuffer` are distinct instances (not the same object).
- `SC_SM003_4`: `Camera` is non-null and has default `Zoom = 1f`.
- `SC_SM003_5`: `TimeControl` is accessible via the inherited `SharedApplicationBootstrapper.TimeControl` property and is non-null after `BootstrapNode()` with a live participant. **Do not** add a duplicate `TimeControl` field on `StrideNodeBootstrapper` itself.
- `SC_SM003_6`: `KinematicComponentRegistry` components (e.g. `VehicleState`) are registered in `Context.World`.
- `SC_SM003_7`: `CognitiveComponentRegistry` components (e.g. `BrainHsm128`) are **not** registered in `Context.World` (lean registry check).
- `SC_SM003_10`: `VisualEffectState` is explicitly registered as an ECS component (`world.RegisterComponent<VisualEffectState>()`). Without this, `SyncFdpToStrideScript` will throw when querying for effect entities because no component table exists. Verify via `repo.IsComponentTypeRegistered<VisualEffectState>()`.
- `SC_SM003_11`: `TracerTarget` is explicitly registered as an ECS component (`world.RegisterComponent<TracerTarget>()`). Verify the same way.
- `SC_SM003_8`: `Tick()` can be called repeatedly without throwing; `ConsumerBuffer` is cleared each frame.
- **Forbidden:** Do NOT manually register `DeadReckoningSyncSystem`. `NedReplicationModule` auto-registers it when it detects `NodeRole.ImageGenerator`. Manual registration causes double-tick interpolation corruption. Verify the system is present in the kernel exactly once (grep or reflection check in a test).

---

### SM-004 — Implement SyncFdpToStrideScript

**Goal:** ECS→engine sync script with 2-pass differential synchronisation, cluster state gating, and visual effect tracking.

**Location:** `Hrot\Subsystems\Hrot.StrideMock\SyncFdpToStrideScript.cs`  
Also define: `FakeStrideEntity.cs`, `FakeStrideEffect.cs`, `FakeStrideScript.cs` (abstract base)

**Design Reference:** [DESIGN.md §6](./DESIGN.md#6-syncfdptostridesscript-hrotstrideMock)

**Success Conditions:**
- `SC_SM004_1`: When an ECS entity with `SimTransform` is spawned, `ActiveEntities` gains a corresponding `FakeStrideEntity` after the next `Update()`.
- `SC_SM004_2`: When an ECS entity is destroyed (via `EntityRepository.DestroyEntity()`), `ActiveEntities` loses the entry after the next `Update()`.
- `SC_SM004_3`: When ECS index is recycled with a new generation (destroy + spawn at same index), the old `FakeStrideEntity` is removed and a new one created (generational safety).
- `SC_SM004_4`: When `currentClusterState` is `LoadingLive`, `SyncStrideEntities()` is **not** called; `CurrentStateMessage` is non-empty.
- `SC_SM004_5`: When state transitions to `OperatingLive`, `SyncStrideEntities()` resumes; `CurrentStateMessage` is empty.
- `SC_SM004_6`: A `WeaponFireNotification` on the event bus results in a `FakeStrideEffect` with `Type == EffectType.Explosion` appearing in `ActiveEffects`.
- `SC_SM004_7`: After the effect's lifetime expires (driven by `VisualEffectCleanupSystem`), the effect is removed from `ActiveEffects`.
- `SC_SM004_8`: `_staleEntities` list is reused across frames (no per-frame allocation — verify with GC pressure test if available).

---

### SM-005 — Visual Effects Wiring

**Goal:** Register `EventToEffectSystem` and `VisualEffectCleanupSystem` in `StrideNodeBootstrapper`; confirm effect entities appear in `SyncFdpToStrideScript.ActiveEffects` correctly.

**Location:** System registration inside `StrideNodeBootstrapper`; rendering in `StrideMockSubsystem.DrawWorld()` and `FakeStrideApp.OnDrawWorld()`.

**Design Reference:** [DESIGN.md §6.4](./DESIGN.md#64-visual-effects)

**Success Conditions:**
- `SC_SM005_1`: `EventToEffectSystem` is registered in the kernel's simulation phase; `VisualEffectCleanupSystem` in the post-simulation phase.
- `SC_SM005_2`: `EventToEffectSystem` is wrapped in the `TogglableSimulationGroup` (simulation phase, disabled during replay). `VisualEffectCleanupSystem` is wrapped in the `TogglablePostSimulationGroup` (post-simulation phase — a post-sim system cannot be placed in a simulation group). Both groups are passed to `BuildOrchestration()` so the orchestration handler disables both during `LoadingReplay`.
- `SC_SM005_3`: Explosions render as orange circles (alpha-faded) in `DrawWorld()`; tracers render as yellow lines. (Visual verification — run the app.)
- `SC_SM005_4`: No explosion/tracer `FakeStrideEffect` entries survive beyond their ECS entity lifetime.

---

## Phase 4 — ClusterRunner Integration

---

### SM-006 — Implement StrideMockSubsystem

**Goal:** Thin `ISubsystem` + `IMapCameraProvider` wrapper. Connects the `StrideNodeBootstrapper` core to the `ClusterRunner` lifecycle.

**Location:** `Hrot\Subsystems\Hrot.StrideMock\StrideMockSubsystem.cs`

**Design Reference:** [DESIGN.md §7](./DESIGN.md#7-stridemocksubsystem-hrotstrideMock)

**Success Conditions:**
- `SC_SM006_1`: `Name` == `"StrideMock"`.
- `SC_SM006_2`: `TitleBarColor` is orange (`(0.8f, 0.4f, 0.1f, 1f)`).
- `SC_SM006_3`: `StrideMockSubsystem` accepts `INetworkFactory` via constructor injection (matching the `SimHostSubsystem(INetworkFactory)` pattern). `Initialize(config)` passes the factory into `StrideNodeBootstrapper.BootstrapNode()` without throwing. A test that constructs the subsystem without a factory must fail at compile-time or throw a clear `ArgumentNullException`.
- `SC_SM006_3a`: `Initialize(config)` calls `BootstrapNode` **first**, then extracts `_core.Context.TkbDb` and calls `DemoTkbSetup.RegisterAll(tkb)` **and** `Fdp.Examples.Scenarios.UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(tkb)` against that instance **before the first `Update()` tick**. Populating a standalone `ITkbDatabase` before `BootstrapNode` is silently orphaned — `HrotNodeBuilder` provisions its own database internally and wires it into the genesis pipeline; any external instance is unreachable. `DemoTkbSetup` alone only registers CommandTank (ID 100); UrbanCombat entities (IDs 1001–2003) require the second call. Omitting the post-bootstrap population causes every spawned entity to stall permanently in `Constructing`. Verify by connecting to a running CGF Brain and confirming at least one entity is promoted from ghost to live within 2 seconds of scenario load.
- `SC_SM006_4`: `GetCameraView()` returns a non-null `MapCameraView` after `Initialize`.
- `SC_SM006_5`: `ApplyCameraView(view)` changes `Camera.Target` and `Camera.Zoom` to match the provided view.
- `SC_SM006_6`: `Update(dt)` only calls `Camera.HandleInput` when `IsActiveMapOwner()` returns true; always calls `Camera.Update(dt)`.
- `SC_SM006_7`: `DrawWorld()` does not throw; renders gizmos and entities without Raylib error (visual verification).
- `SC_SM006_8`: `DrawUI()` shows splash ImGui window when `CurrentStateMessage` is non-empty.
- `SC_SM006_9`: `Shutdown()` calls `StrideNodeBootstrapper.Dispose()` without throwing; no DDS participant hang.

---

### SM-007 — Wire StrideMockSubsystem into ClusterRunner

**Goal:** Make `stridemock` a valid CLI mode; assign NodeId offset 700; update subsystem discovery chain.

**Locations:**
- `Hrot\Runner\Hrot.ClusterRunner\Configuration\HrotRunnerConfiguration.cs` — add `"stridemock"` to valid names
- `Hrot\Runner\Hrot.ClusterRunner\Program.cs` — add `"STRIDEMOCK" => 700` to `ResolveAppNodeId`
- `Hrot\Runner\Hrot.ClusterRunner\Hrot.ClusterRunner.csproj` — `<ProjectReference>` to `Hrot.StrideMock`

**Design Reference:** [DESIGN.md §9](./DESIGN.md#9-clusterrunner-integration)

**Success Conditions:**
- `SC_SM007_1`: `HrotRunnerConfiguration.Validate()` does **not** throw for `--mode stridemock`.
- `SC_SM007_2`: `HrotRunnerConfiguration.Validate()` does **not** throw for `--mode orchestrator,cgf,stridemock`.
- `SC_SM007_3`: Existing valid modes (`simhost`, `ig`, etc.) still parse without error (no regression).
- `SC_SM007_4`: `ResolveAppNodeId("StrideMock", 0)` returns `700`.
- `SC_SM007_5`: `ScanForSubsystems()` returns a type list containing `StrideMockSubsystem`.
- `SC_SM007_6` (Integration): `Hrot.ClusterRunner.exe -m stridemock --no-wait` starts, logs "StrideMock initialised", and does not crash on startup.
- `SC_SM007_7` (Integration): `Hrot.ClusterRunner.exe -m orchestrator,cgf,stridemock` boots all three subsystems and the `[StrideMock]` tab appears in the main menu.

---

## Phase 5 — Standalone App

---

### SM-008 — Implement FakeStrideApp

**Goal:** Standalone Raylib/ImGui application that runs the Stride integration architecture independently of `ClusterRunner`.

**Location:** `Hrot\Runner\Hrot.FakeStrideApp\FakeStrideApp.cs` + `Program.cs`

**Design Reference:** [DESIGN.md §8](./DESIGN.md#8-fakestrideapp-hrotfakestrideapp)

**Steps:**
`OnLoad()` must execute in this order:
  1. Instantiate a `DdsParticipant` and `NedNetworkFactory` (mandatory before `BootstrapNode` — missing these stalls the clock permanently).
  2. Set `HrotNodeConfig.LocalTempRoot = Path.Combine(OrchestrationConstants.DefaultStagingDirectory, "nodes", $"node-{nodeId}")` to avoid staging-directory collisions with a co-located `ClusterRunner` process (must be set before `BootstrapNode`).
  3. Call `BootstrapNode(config, role, networkFactory)`. `HrotNodeBuilder` (Phase 1 inside the bootstrapper) provisions its own `ITkbDatabase` and wires it into the genesis and ghost-promotion pipelines.
  4. Extract and populate the active TKB **after** `BootstrapNode`, before the first `Tick()`:
     ```csharp
     var tkb = _core.Context.TkbDb;
     DemoTkbSetup.RegisterAll(tkb);  // CommandTank (ID 100)
     Fdp.Examples.Scenarios.UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates(tkb);  // IDs 1001–2003
     ```
     Populating any `ITkbDatabase` instance other than `_core.Context.TkbDb` is silently orphaned — the genesis pipeline holds a reference to the builder-provisioned database. Both calls are required; `DemoTkbSetup` alone only registers CommandTank (ID 100) and UrbanCombat entities (IDs 1001–2003) will permanently stall in `Constructing` without the second call.

**Success Conditions:**
- `SC_SM008_1`: `Hrot.FakeStrideApp.exe` launches a 1280×720 Raylib window without crashing.
- `SC_SM008_2`: Red circles appear for each active ECS entity with a `SimTransform`.
- `SC_SM008_3`: Right-click drag pans the map; scroll wheel zooms in/out smoothly.
- `SC_SM008_4`: Gizmo primitives from the cluster appear in the consumer buffer and are drawn in the world view.
- `SC_SM008_5`: Explosions and tracers render correctly when triggered by the cluster.
- `SC_SM008_6`: During `LoadingLive` or `LoadingReplay`, the splash screen is shown and the map is blank (no stale entities visible).
- `SC_SM008_7`: Closing the window calls `_core.Dispose()` cleanly; the process exits with code 0.
- `SC_SM008_8`: `GetMapCamera()` returns the same `MapCamera` instance as `_core.Camera` (follows SimHostApp pattern).
- `SC_SM008_9`: A network-spawned entity (received via DDS from a co-running SimHost or ClusterRunner CGF) appears as a red circle within 2 seconds of scenario load. This verifies TKB resolution, ghost promotion, and `SimTransform` replication are all working end-to-end. (Integration test — requires live cluster.)
- `SC_SM008_10`: Running `FakeStrideApp` alongside `Hrot.ClusterRunner.exe -m orchestrator` on the same machine produces no file-lock errors; the node's staging directory is isolated at `<DefaultStagingDirectory>\nodes\node-700\`.

---

## Phase 6 — DRY Refactoring of Existing Nodes

---

### SM-009 — Refactor SimHostApp to Use SharedApplicationBootstrapper

**Goal:** Migrate `SimHostApp.OnLoad()` to inherit `SharedApplicationBootstrapper` and use the hook overrides, eliminating the duplicate initialization block. All existing SimHost tests must remain green.

**Location:** `Hrot\Subsystems\Hrot.SimHost\SimHostApp.cs` (and `NodeBootstrapper.cs` if needed)

**Design Reference:** [DESIGN.md §10.1](./DESIGN.md#101-simhostapp)

**Additional Steps:**
- Remove time-synchronization translator wiring from `SimHostAuxiliaryTranslatorPack.Create()` (i.e., calls to `TimeNetworkModule.CreateDescriptorTranslator`, `CreateSlaveLockstepTranslator`, and `CreateSlaveTimeSyncTranslator`). After migrating to `SharedApplicationBootstrapper`, the base class Phase 6c registers these unconditionally. Leaving them in the auxiliary pack causes double-registration: each DDS time event is processed twice, producing duplicated `SwitchTimeModeEvent` dispatch and DDS reader contention. The auxiliary pack must only contain domain translators (combat, kinematics, perception, pathfinding).

**Success Conditions:**
- `SC_SM009_1`: All tests in `Hrot.SimHost.Tests` pass after the refactor.
- `SC_SM009_2`: All tests in `Hrot.SimHost.Integration.Tests` pass after the refactor.
- `SC_SM009_3`: The 7-phase ordering is identical to pre-refactor (verified by a sequenced init log/assertion test or code review).
- `SC_SM009_4`: No initialization code duplicated between `SimHostApp` and `StrideNodeBootstrapper` — both use `SharedApplicationBootstrapper` hooks.
- `SC_SM009_5`: `SimHostApp.OnLoad()` no longer contains the orchestration handler setup or `TogglableGroup` construction directly — all delegated to the bootstrapper pipeline.
- `SC_SM009_6`: `SimHostAuxiliaryTranslatorPack.Create()` no longer contains any calls to `TimeNetworkModule` methods. A grep for `TimeNetworkModule` inside `Hrot.SimHost` returns zero results. The three time-sync translators are present in the kernel exactly once (registered by `SharedApplicationBootstrapper` Phase 6c).

---

### SM-010 — Refactor IgApplication to Use SharedApplicationBootstrapper

**Goal:** Migrate `IgApplication`'s overlapping initialization code to `SharedApplicationBootstrapper`, injecting IG-specific modules via hooks. All existing IG tests must remain green.

**Location:** `Hrot\Subsystems\Hrot.IG\IgApplication.cs`

**Design Reference:** [DESIGN.md §10.2](./DESIGN.md#102-igapplication)

**Success Conditions:**
- `SC_SM010_1`: All tests in `Hrot.IG.Tests` pass after the refactor.
- `SC_SM010_2`: IG presentation modules (`MapLayerModule`, `MapCullingModule`, `StyleResolutionModule`, `EventEffectModule`) are registered via the `GetAdditionalModules()` hook, **not** via `PopulateSystems`. They must not be flattened into raw system lists because their internal execution phases are controlled by the module itself. A test verifies these modules appear in the kernel and retain their phase ordering.
- `SC_SM010_3`: `SharedApplicationBootstrapper` phase ordering applies to IG init (Trap #1–#5 safe).
- `SC_SM010_4`: No orchestration setup duplicated between `IgApplication` and `StrideNodeBootstrapper`.

---

## Phase 7 — Integration Gate

---

### SM-011 — Full Integration Validation (GATE)

**Goal:** Verify all success conditions from [DESIGN.md §12](./DESIGN.md#12-success-conditions) are met before considering the workstream complete.

**Checklist:**
- [ ] `SC_SM006_7 / SC_SM007_7`: `[StrideMock]` tab visible; camera sync on tab switch works.
- [ ] `SC_SM007_6`: Standalone mode boots cleanly.
- [ ] `SC_SM008_1–SC_SM008_7`: FakeStrideApp visual + lifecycle verification.
- [ ] Replay safety: load a recording, seek backward — no ghost entities, scene cleanly restores.
- [ ] Recording: `OperatingLive` session produces `node_700.fdp` in the staging directory.
- [ ] 2PC: `SerializeLocal` and `PrefetchFiles` commands ACKed correctly (verify in Orchestrator logs).
- [ ] Diagnostics: `CollectDiagnostics` produces a valid dump from node 700.
- [ ] Time: Orchestrator Pause command halts all nodes on same tick (StrideMock included).
- [ ] DRY: `StrideNodeBootstrapper` has 0 references to `Raylib`, `ImGui`, or `IMapCameraProvider` (static analysis or grep).
- [ ] SM-009 + SM-010: All SimHost and IG tests green.
