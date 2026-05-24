# BATCH-05 Instructions

**Workstream:** stride-mock
**Tasks:** SM-009, SM-010
**Developer:** Claude Sonnet 4.6
**Prereqs:** BATCH-04 committed (762b40f)

---

## Pre-Existing Test Baselines (DO NOT REGRESS)

Run once before starting to confirm your environment matches:

```
Hrot.SimHost.Tests:       Passed: 566, Failed: 27 (pre-existing), Skipped: 3
Hrot.IG.Tests:            Passed: 313, Failed: 68 (pre-existing), Skipped: 0
Hrot.StrideMock.Tests:    Passed: 41
Hrot.FakeStrideApp.Tests: Passed: 3
```

The 27 SimHost failures and 68 IG failures are pre-existing and must remain the SAME set.
Do NOT introduce new failures. The 566 + 313 passing tests must stay green.

---

## Task References

- Full task specs: [TASK-DETAILS.md](../TASK-DETAILS.md) sections SM-009 and SM-010
- Architecture design: [DESIGN.md](../DESIGN.md) sections 10.1 and 10.2
- `SharedApplicationBootstrapper`: `Hrot\Engine\Hrot.Common\Infrastructure\SharedApplicationBootstrapper.cs`
- Reference implementation: `Hrot\Subsystems\Hrot.StrideMock\StrideNodeBootstrapper.cs` (study this!)

---

## SM-009 -- Refactor SimHostApp to Use SharedApplicationBootstrapper

### Goal

`SimHostApp` currently contains the 7-phase initialization monolith inline in `OnLoad()`.
Migrate the bootstrapping phases into a new `SimHostNodeBootstrapper : SharedApplicationBootstrapper`
so that `SimHostApp.OnLoad()` delegates to the base-class pipeline instead of duplicating it.

`SimHostApp` inherits `FdpApplication` and C# only allows single inheritance, so the pattern
is **composition**, not inheritance:
- NEW: `SimHostNodeBootstrapper : SharedApplicationBootstrapper` (analogous to `StrideNodeBootstrapper`)
- MODIFIED: `SimHostApp` HAS-A `SimHostNodeBootstrapper` (analogous to `FakeStrideApp`)

### Step 1: Create `SimHostNodeBootstrapper.cs`

**File:** `Hrot\Subsystems\Hrot.SimHost\SimHostNodeBootstrapper.cs`

The class extends `SharedApplicationBootstrapper` and implements all 6 abstract hooks.
Study `StrideNodeBootstrapper.cs` as the reference pattern.

**Constructor parameters:**
```csharp
public SimHostNodeBootstrapper(
    INetworkFactory? networkFactory,
    NodeRole role,
    string localTempRoot,
    FdpEventBus? eventHistoryService,
    HrotNodeConfig hrotConfig)
```

Store these as private fields. They are needed across multiple hooks.

**Internal state the class must own and expose:**
```csharp
public SimHostCoreLogicPack? CoreLogicPack { get; private set; }
public ISlaveOrchestrationTranslator? SlaveTranslator { get; private set; }
public CheckpointIOWorker? CheckpointWorker { get; private set; }
public PhysicsToolkitModule? PhysicsModule { get; private set; }
public CognitiveSpatialModule? PerceptionModule { get; private set; }
public BehaviorRegistry? BehaviorRegistry { get; private set; }
```

These are exposed as public properties so `SimHostApp` can access them after `BootstrapNode()`.

#### Hook 1: `GetBehaviorRegistry()`
```csharp
protected override BehaviorRegistry? GetBehaviorRegistry()
{
    BehaviorRegistry ??= new BehaviorRegistry();
    return BehaviorRegistry;
}
```

#### Hook 2: `RegisterDomainComponents(EntityRepository world)`
```csharp
protected override void RegisterDomainComponents(EntityRepository world)
    => SimHostComponentRegistry.RegisterAll(world);
```

#### Hook 3: `BuildSerializer(BehaviorRegistry? registry)`
```csharp
protected override ScenarioSerializer BuildSerializer(BehaviorRegistry? registry)
    => Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(registry);
```

#### Hook 4: `PopulateSystems(context, input, sim, postSim)`

This corresponds to the current "SimHostCoreLogicPack construction" section in `SimHostApp.OnLoad()`.

```csharp
protected override void PopulateSystems(
    HrotNodeContext context,
    List<IEcsModuleSystem> input,
    List<IEcsModuleSystem> sim,
    List<IEcsModuleSystem> postSim)
{
    // Load road network (needs localTempRoot / config path)
    // ** see SimHostApp.LoadRoadNetwork() -- it's an internal static method **
    // ** You can call SimHostApp.LoadRoadNetwork(path, nodeId: context.NodeId) **
    // ** or move the road-network loading here inline **
    // NOTE: road network path comes from NodeConfiguration but SimHostNodeBootstrapper
    // does not own a NodeConfiguration -- pass the road network blob in the constructor
    // if needed, or load it from a path stored as a field.

    CoreLogicPack = new SimHostCoreLogicPack(context.EntityMap, _roadNetwork);

    // DDS attribute update systems from factory (NOP in offline mode)
    var nodeFactory = context.ConfiguredFactory;  // see note below
    // NOTE: context.ConfiguredFactory is not available directly. Use the field:
    // The configured factory is the one stored after ConfigureForNode() in base class Phase 1.
    // Simplest approach: store it as a field in override of BootstrapNode or pass it
    // through the constructor. However, the cleanest approach is to use the factory
    // as a parameter to PopulateSystems. Since the base class signature does not include
    // the factory, store the configured factory as a private field populated during an
    // override of GetAdditionalModules or by storing it before PopulateSystems is called.
    //
    // ** ACTUAL SOLUTION: ** The base class stores configuredFactory as a local inside
    // BootstrapNode() and passes it to RegisterNetworkTranslators(). You cannot access it
    // from PopulateSystems. Instead, store the raw _networkFactory passed to the constructor
    // and use it to call CreateSimHostAttributeUpdateSystems() in PopulateSystems, THEN
    // call ConfigureForNode yourself:
    //    var nodeFactory = _networkFactory?.ConfigureForNode(context, _role, GetBehaviorRegistry());
    // This is safe because ConfigureForNode is idempotent for the same context.

    foreach (var sys in nodeFactory?.CreateSimHostAttributeUpdateSystems()
                         ?? System.Linq.Enumerable.Empty<IEcsModuleSystem>())
        input.Add(sys);

    foreach (var s in CoreLogicPack.InputSystems)          input.Add(s);
    foreach (var s in CoreLogicPack.SimulationSystems)     sim.Add(s);
    foreach (var s in CoreLogicPack.PostSimulationSystems) postSim.Add(s);
}
```

**IMPORTANT about road network:** `SimHostApp.LoadRoadNetwork()` is an `internal static` method.
You need either:
a) Pass the road network path (or blob) in the constructor, OR
b) Make `SimHostNodeBootstrapper` load it internally

**Recommended:** Add `string roadNetworkBlobPath` to the constructor. Store it. In `PopulateSystems`,
call `SimHostApp.LoadRoadNetwork(roadNetworkBlobPath, localNodeId: context.NodeId)`.

#### Hook 5: `BuildOrchestration(context, simGroup, postSimGroup, serializer)`

This corresponds to the `NodeBootstrapper.BuildOrchestration(...)` call and diagnostics setup
currently in `SimHostApp.OnLoad()` around lines 405-460.

```csharp
protected override ClusterSlave BuildOrchestration(
    HrotNodeContext context,
    TogglableSimulationGroup simGroup,
    TogglablePostSimulationGroup postSimGroup,
    ScenarioSerializer serializer)
{
    // Create services needed by diagnostics handler
    var archService    = new Fdp.ModuleHost.Diagnostics.ArchitectureDiagnosticsService(context.Kernel);
    var entityService  = new Fdp.Toolkit.Diagnostics.EntityStateExtractionService(context.World, context.EntityMap);
    var logService     = new Hrot.Core.Diagnostics.LogArchiveExtractionService(
        string.IsNullOrWhiteSpace(_hrotConfig.LogDirectory)
            ? System.IO.Path.Combine(System.AppContext.BaseDirectory, "logs")
            : _hrotConfig.LogDirectory,
        _hrotConfig.SubsystemName,
        context.NodeId);
    var diagHandler = new Hrot.Common.Diagnostics.DiagnosticsDumpClusterOpHandler(
        _eventHistoryService, archService, entityService, logService, _hrotConfig);

    var checkpointPath = System.IO.Path.Combine(_localTempRoot, "checkpoints");
    CheckpointWorker = new CheckpointIOWorker(checkpointPath, context.NodeId);

    var nodeBootstrapper = new NodeBootstrapper(_networkFactory);
    var slave = nodeBootstrapper.BuildOrchestration(
        _role, context.Kernel, context.World, context.NodeId,
        participant:          context.Participant,
        subsystemName:        "SimHost",
        eventBus:             context.EventBus,
        scenarioSerializer:   null,   // SimHost does not load/save scenarios; CGF does
        localTempRoot:        _localTempRoot,
        checkpointWorker:     CheckpointWorker,
        simGroup:             simGroup,
        lifecycleGroup:       context.NedReplication?.NetworkLifecycleGroup,
        ghostCreationSystem:  context.GhostCreationSystem,
        eventAccumulator:     context.EventAccumulator,
        afterSeek:            (context.NedReplication as INedReplicationModule)?.AfterSeekCallback,
        diagnosticsDumpHandler: diagHandler);

    SlaveTranslator = nodeBootstrapper.SlaveTranslator;
    return slave;
}
```

Also here, after BuildOrchestration, seed the GlobalTime singleton:
```csharp
context.World.SetSingletonUnmanaged(new GlobalTime
{
    DeltaTime = 1.0f / _simulationRateHz,
    TimeScale = 1.0f,
});
```
(Add `float simulationRateHz` to the constructor.)

#### Hook 6: `RegisterSpawningPipeline(HrotNodeContext context)`

This corresponds to the section in `SimHostApp.OnLoad()` that registers `spawningSystem`,
`SimHostModule`, `SimHostCoreLogicPack`, perception module, area query system, and physics.

```csharp
protected override void RegisterSpawningPipeline(HrotNodeContext context)
{
    // Toolkit modules -- Physics
    PhysicsModule = new PhysicsToolkitModule();
    PhysicsModule.Initialize(context.World);

    // elm reference for spawning (BaseModules[0] == EntityLifecycleModule)
    var elm = (Fdp.Toolkit.Lifecycle.EntityLifecycleModule)context.BaseModules[0];

    var spawningSystem = new NetworkSpawningSystem(
        context.TkbDb!,
        elm,
        context.EntityMap,
        context.IdAllocator!,
        context.NodeId,
        onEntitySpawned: (world, entity, isLocalAuthority) =>
        {
            if (isLocalAuthority && world.HasComponent<SimTransform>(entity))
            {
                world.SetAuthority<SimTransform>(entity, true);
                if (world.HasComponent<NetworkTransform>(entity))
                    world.SetAuthority<NetworkTransform>(entity, true);
                if (world.HasComponent<NetworkVelocity>(entity))
                    world.SetAuthority<NetworkVelocity>(entity, true);
            }
        });

    context.Kernel.RegisterModule(new SimHostModule(spawnSystem: spawningSystem));
    context.Kernel.RegisterModule(CoreLogicPack!);
    context.Kernel.RegisterGlobalSystem(new Hrot.SimHost.Systems.AreaQueryResultMaterializationSystem());

    PerceptionModule = new CognitiveSpatialModule(
        context.World,
        colliderRadiusReader: static (view, e) => view.HasComponent<PhysicsCollider>(e)
            ? view.GetComponentRO<PhysicsCollider>(e).Radius
            : 0f);
    context.Kernel.RegisterModule(PerceptionModule);

    // GenesisMaterializationSystem -- Input phase, registered after togglable groups
    context.Kernel.RegisterGlobalSystem(
        new Hrot.SimHost.Systems.GenesisMaterializationSystem(context.EntityMap));
}
```

#### Hook 7: `RegisterNetworkTranslators(HrotNodeContext context, INetworkFactory configuredFactory)`

This corresponds to lines ~520-540 of `SimHostApp.OnLoad()`.

```csharp
protected override void RegisterNetworkTranslators(
    HrotNodeContext context,
    INetworkFactory configuredFactory)
{
    if (context.Participant == null) return;

    configuredFactory.CreateSimHostAuxiliaryTranslators().RegisterOn(context.Kernel);
    configuredFactory.CreateSimHostPerceptionTranslators(context.GhostCreationSystem).RegisterOn(context.Kernel);
    configuredFactory.CreateSimHostPathfindingTranslators(CoreLogicPack!.TrajectoryPool).RegisterOn(context.Kernel);
}
```

### Step 2: Modify `SimHostApp.OnLoad()` to use SimHostNodeBootstrapper

`SimHostApp.OnLoad()` is currently ~420 lines. After refactoring, it should be much shorter.
The structure becomes:

```
OnLoad():
  1. Config setup (same as now: load config, apply environment, compute node IDs)
  2. Create/obtain DDS participant (same as now)
  3. Build HrotNodeConfig (same as now)
  4. Create SimHostNodeBootstrapper(_networkFactory, _role, localTempRoot, _eventHistoryService, hrotConfig, roadNetworkBlobPath, simulationRateHz)
  5. Call _bootstrapper.BootstrapNode(hrotConfig, _role, _networkFactory ?? new OfflineNetworkFactory())
     --> returns HrotNodeContext
  6. Extract fields: _context, _world, _kernel, _eventBus, _entityMap, _idAllocator, _geoTransform, _clusterSlave
  7. Set base.World and base.Kernel
  8. Set GlobalTime singleton (if not done in hook)
  9. ALL visualization/gizmo/ImGui code stays in SimHostApp (unchanged)
  10. _kernel.Initialize() is called by the base class -- do NOT call it again!
```

**Critical:** The base class calls `context.Kernel.Initialize()` in Phase 7.
`SimHostApp.OnLoad()` must NOT call `_kernel.Initialize()` again.

Remove from SimHostApp after refactoring:
- `_timeModeTranslator` field and all its usage
- `_lockstepTranslator` field and all its usage
- The `if (ddsParticipant != null) { _timeModeTranslator = ...; _lockstepTranslator = ...; }` block
- From `OnUpdate()`: `_timeModeTranslator?.ScanAndPublish()`, `_timeModeTranslator?.PollIngress()`,
  `_lockstepTranslator?.ScanAndPublish()`, `_lockstepTranslator?.PollIngress()`

After refactoring, `SimHostApp` accesses the bootstrapper for things it needs:
```csharp
_bootstrapper = new SimHostNodeBootstrapper(...);
_context = _bootstrapper.BootstrapNode(hrotConfig, _role, _networkFactory ?? new OfflineNetworkFactory());
_clusterSlave   = _context.ClusterSlave;
_slaveTranslator = _bootstrapper.SlaveTranslator;  // still needed in OnUpdate()
_checkpointWorker = _bootstrapper.CheckpointWorker;
_simCorePack    = _bootstrapper.CoreLogicPack;
_physicsModule  = _bootstrapper.PhysicsModule;
_perceptionMod  = _bootstrapper.PerceptionModule;
_behaviorRegistry = _bootstrapper.BehaviorRegistry;
// ... _world, _kernel, _entityMap from _context
```

Store `SimHostNodeBootstrapper _bootstrapper` as a private field on SimHostApp (for lifecycle access).

### Step 3: Verify Shutdown() is correct

`SimHostApp.Shutdown()` must dispose `_bootstrapper.CheckpointWorker` instead of
`_checkpointWorker` directly (or keep `_checkpointWorker` as a field pointing to the same instance).
Either approach is fine as long as disposal happens exactly once.

### Step 4: Verify NodeBootstrapper.BuildOrchestration has NOT changed

Do NOT modify `NodeBootstrapper.cs` (existing orchestration class). Only create the new
`SimHostNodeBootstrapper.cs` file.

### SM-009 Success Conditions

From [TASK-DETAILS.md](../TASK-DETAILS.md):
- SC_SM009_1: All 566 currently-passing Hrot.SimHost.Tests tests still pass (27 pre-existing failures remain same set)
- SC_SM009_2: Hrot.SimHost.Integration.Tests passes (if exists)
- SC_SM009_3: 7-phase order preserved (code review)
- SC_SM009_4: No initialization duplicated between SimHostApp and SimHostNodeBootstrapper
- SC_SM009_5: SimHostApp.OnLoad() no longer contains TogglableGroup construction or orchestration handler setup directly
- SC_SM009_6: No inline `TimeNetworkModule` calls remain in `SimHostApp.cs`.
  `grep TimeNetworkModule Hrot\Subsystems\Hrot.SimHost\SimHostApp.cs` returns zero results.
  The three time-sync translators are registered exactly once (by base class Phase 6c).

---

## SM-010 -- Refactor IgApplication to Use SharedApplicationBootstrapper

### Goal

`IgApplication` (4164 lines) is NOT a `FdpApplication` -- it is an `IDisposable` class with
its own `InitializeEcs()` and `InitializeNetwork()` private methods. The goal mirrors SM-009:
create `IgNodeBootstrapper : SharedApplicationBootstrapper` and delegate initialization to it.

Full specs: [TASK-DETAILS.md](../TASK-DETAILS.md) SM-010
Architecture: [DESIGN.md](../DESIGN.md) section 10.2

### Architecture: IgNodeBootstrapper

**File:** `Hrot\Subsystems\Hrot.IG\IgNodeBootstrapper.cs`

Pattern: `IgApplication` HAS-A `IgNodeBootstrapper`. The bootstrapper implements all hooks.

**Key hook implementations:**

`RegisterDomainComponents(world)`:
- Move all `_world.RegisterComponent<X>()` calls from `IgApplication.InitializeEcs()` into this hook.
  The full component registration block is around lines 680-790 of `IgApplication.cs`.
  Use an `IgComponentRegistry` helper class (analogous to `SimHostComponentRegistry`) OR inline.

`PopulateSystems(context, input, sim, postSim)`:
- IG runs in read-only mode for most simulation. Any input/sim/postSim systems that IG uses
  (e.g. map overlay systems) go here.

`GetAdditionalModules()`:
- THIS IS THE CRITICAL CHANGE FOR SM-010. IG presentation modules must be registered here:
  ```csharp
  protected override IEnumerable<IEcsModule> GetAdditionalModules()
  {
      yield return new MapLayerModule(...);
      yield return new MapCullingModule(...);
      yield return new StyleResolutionModule(...);
      yield return new EventEffectModule();
  }
  ```
  See DESIGN.md section 10.2 for the rationale (internal phase ordering must not be flattened).

`BuildOrchestration(context, simGroup, postSimGroup, serializer)`:
- Create IG's ClusterSlave. Study how `IgApplication.InitializeNetwork()` currently wires
  its orchestration handlers and replicate in the hook.

`RegisterNetworkTranslators(context, configuredFactory)`:
- Move all translator registrations from `IgApplication.InitializeNetwork()` here EXCEPT
  the `TimeNetworkModule` calls (which are now handled by base class Phase 6c).

### Critical: Remove TimeNetworkModule calls from InitializeNetwork()

In `IgApplication.InitializeNetwork()` around lines 877-886:
```csharp
// REMOVE these three lines -- base class Phase 6c handles them:
customTranslators.Add(TimeNetworkModule.CreateDescriptorTranslator(participant, igTimeBus));
customTranslators.Add(TimeNetworkModule.CreateSlaveTimeSyncTranslator(participant, igTimeBus, _effectiveInstanceId));
customTranslators.Add(TimeNetworkModule.CreateSlaveLockstepTranslator(participant, igTimeBus, _effectiveInstanceId));
```

After migration to `IgNodeBootstrapper.BootstrapNode()`, the base class Phase 6c registers
these translators via `CycloneNetworkIngressSystem` and `CycloneEgressSystem` in the kernel.
Leaving them in `IgApplication.InitializeNetwork()` would double-register them.

### SM-010 Success Conditions

From [TASK-DETAILS.md](../TASK-DETAILS.md):
- SC_SM010_1: All 313 currently-passing Hrot.IG.Tests tests still pass (68 pre-existing failures remain same set)
- SC_SM010_2: IG presentation modules registered via `GetAdditionalModules()` hook
- SC_SM010_3: Phase ordering preserved  
- SC_SM010_4: No orchestration setup duplicated

---

## Implementation Order

1. Implement SM-009 first (SimHostNodeBootstrapper + SimHostApp refactor)
2. Verify SimHost tests: 566 pass / 27 fail (same set as baseline)
3. Implement SM-010 (IgNodeBootstrapper + IgApplication refactor)
4. Verify IG tests: 313 pass / 68 fail (same set as baseline)
5. Run full build to confirm zero compile errors

---

## Build Verification

```bash
dotnet build IOS-IG-SimHost.sln -c Debug --no-incremental
dotnet test Hrot\Subsystems\Hrot.SimHost.Tests\Hrot.SimHost.Tests.csproj --no-build
dotnet test Hrot\Subsystems\Hrot.IG.Tests\Hrot.IG.Tests.csproj --no-build
dotnet test Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.Tests\Hrot.StrideMock.Tests.csproj --no-build
dotnet test Hrot\Runner\Hrot.FakeStrideApp\Hrot.FakeStrideApp.Tests\Hrot.FakeStrideApp.Tests.csproj --no-build
```

---

## Report Format

Submit a `BATCH-05-REPORT.md` containing:
1. Files created/modified with brief summary
2. Test results (pass/fail counts for each project)
3. Confirmation that SM-009 `grep TimeNetworkModule Hrot\Subsystems\Hrot.SimHost\SimHostApp.cs` returns empty
4. Confirmation that SM-010 TimeNetworkModule calls removed from IgApplication.InitializeNetwork()
5. Any issues encountered (deviations from spec with reasoning)

---

## AGENTS.md Reminders

- No Unicode characters in comments or string literals
- Preserve existing comments exactly unless wrong
- Minimize textual diffs (only change lines required for the fix)
- Solution must compile before finishing
