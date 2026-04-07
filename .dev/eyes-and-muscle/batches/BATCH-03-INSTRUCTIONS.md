# BATCH-03 Instructions — Phase 4 Migrations

**Workstream:** eyes-and-muscle  
**Tasks covered:** Corrective-0, Pre-Migration Infrastructure, EAM-M001, EAM-M002, EAM-M003  
**Tests:** `Hrot.ClusterRunner.Integration.Tests`, `Hrot.SimHost.Tests`, `Hrot.IG.Tests`, `Hrot.ClusterRunner.Tests`

---

## Critical constraints

1. **Pure behavioural refactor** — no new features. All existing tests must pass unchanged after each task.
2. **Run full tests after every file change group.** Do not combine multiple tasks into one test run; test each independently.
3. **Project references are a real constraint.** `Hrot.SimHost` CANNOT reference `Hrot.ClusterRunner`. This is why the pre-migration infrastructure task must happen first.
4. **Test commands to use throughout:**
   ```powershell
   dotnet build d:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln --no-restore
   dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot.ClusterRunner.Tests --no-build
   dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot.ClusterRunner.Integration.Tests --no-build
   dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot.SimHost.Tests --no-build
   dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot.IG.Tests --no-build
   ```

---

## Corrective-0 — Formally scope out SimulationLogicModule P2 debt

**The debt item from BATCH-02:**
> `SimulationLogicModule` omitted from `EyesAndMuscleSubsystem` — old SystemGroup API incompatible with `kernel.RegisterModule(IEcsModule)`.

**Resolution:**
This is not a P1 blocking issue. `SimulationLogicModule` uses a `SystemGroup`-based API that pre-dates `IEcsModule.RegisterSystems`. EAM-M001 (SimHostApp migration) will continue to use `NodeBootstrapper.BuildSimulationLogic` + `_simLogicModule.RegisterSystems(_kernelGroup, ...)` unchanged — the EcsModule path is the PoC path only.

**Action required:** No code changes needed. Update `DEBT-TRACKER.md` to close this item with status "Accepted: EyesAndMuscleModule.Tick is PoC muscle path. EAM-M001 uses NodeBootstrapper.BuildSimulationLogic via existing legacy path."

---

## Pre-Migration — Move HrotNode* infrastructure to `Hrot.Common`

### Context
`HrotNodeBuilder` currently lives in `Hrot.ClusterRunner/Infrastructure/`. `Hrot.SimHost` **cannot** reference `Hrot.ClusterRunner` (circular dependency: ClusterRunner → SimHost). To use `HrotNodeBuilder` inside `SimHostApp.OnLoad`, the builder and its types must live in a shared project that both `Hrot.SimHost` and `Hrot.ClusterRunner` can reference. `Hrot.Common` is referenced by both.

### Step PM-1 — Add references to `Hrot.Common.csproj`

Open `d:\Work\IOS-IG-SimHost-FDP-2\Hrot.Common\Hrot.Common.csproj` and add these `ProjectReference` entries inside the existing `<ItemGroup>` block that has project references:

```xml
<ProjectReference Include="..\Hrot.Map.Common\Hrot.Map.Common.csproj" />
<ProjectReference Include="..\FDP\ModuleHost\ModuleHost.Core\ModuleHost.Core.csproj" />
<ProjectReference Include="..\FDP\ExtDeps\FastCycloneDds\src\CycloneDDS.Runtime\CycloneDDS.Runtime.csproj" />
<ProjectReference Include="..\FDP\ExtDeps\FastCycloneDds\src\CycloneDDS.Schema\CycloneDDS.Schema.csproj" />
<ProjectReference Include="..\FDP\ModuleHost\ModuleHost.Network.Cyclone\ModuleHost.Network.Cyclone.csproj" />
<ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Lifecycle\FDP.Toolkit.Lifecycle.csproj" />
<ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Replication\FDP.Toolkit.Replication.csproj" />
```

> **Note:** Verify exact paths by inspecting what `Hrot.ClusterRunner.csproj` uses for the same packages.  
> Run `dotnet build Hrot.Common\Hrot.Common.csproj` after adding references before proceeding.

### Step PM-2 — Move `NodeRole` to `Hrot.Common`

Create `d:\Work\IOS-IG-SimHost-FDP-2\Hrot.Common\NodeRole.cs` with updated namespace:

```csharp
namespace Hrot.Common
{
    // (copy entire file content from Hrot.SimHost/NodeRole.cs, change namespace to Hrot.Common)
}
```

Then update `Hrot.SimHost/NodeRole.cs` to re-export via type alias so downstream code still compiles:

```csharp
// Hrot.SimHost/NodeRole.cs — backward-compat shim only
global using NodeRole = Hrot.Common.NodeRole;
```

> Or if a `global using` isn't suitable: remove `Hrot.SimHost/NodeRole.cs` entirely and update all `using Hrot.SimHost;` usages in files that only needed NodeRole.  
> Files that use `NodeRole` in Hrot.SimHost: search with grep for `NodeRole` and add `using Hrot.Common;` where needed.  
> Files in Hrot.ClusterRunner already reference Hrot.Common directly (since ClusterRunner also references Hrot.Common through its Hrot.SimHost reference chain) — update `using Hrot.SimHost;` to `using Hrot.Common;` wherever NodeRole is the only thing imported from Hrot.SimHost.

### Step PM-3 — Move `HrotNodeBuilder`, `HrotNodeContext`, `HrotNodeConfig`, `DdsIdAllocatorHelper` to `Hrot.Common`

1. Copy these 4 files verbatim from `Hrot.ClusterRunner/Infrastructure/` to `Hrot.Common/Infrastructure/` (create the folder if needed):
   - `HrotNodeBuilder.cs` → change namespace from `Hrot.ClusterRunner.Infrastructure` to `Hrot.Common.Infrastructure`
   - `HrotNodeContext.cs` → change namespace to `Hrot.Common.Infrastructure`
   - `HrotNodeConfig.cs` → change namespace to `Hrot.Common.Infrastructure`  
   - `DdsIdAllocatorHelper.cs` → change namespace to `Hrot.Common.Infrastructure`

2. In each moved file, update any usings of `Hrot.SimHost` (NodeRole) to `Hrot.Common`.

3. In `Hrot.Common.Infrastructure.HrotNodeBuilder`: the `WithRole` signature takes `Hrot.Common.NodeRole` — update accordingly.

4. Delete the originals from `Hrot.ClusterRunner/Infrastructure/` (all 4 files).

5. In `Hrot.ClusterRunner` files that used the old namespace, update:
   - `using Hrot.ClusterRunner.Infrastructure;` → `using Hrot.Common.Infrastructure;`
   - This affects: `NedReplicationModule.cs`, `EyesAndMuscleSubsystem.cs`, `HrotNodeBuilderTests.cs`, `EyesAndMuscleSubsystemTests.cs`, any integration test that imports the old namespace.

6. `Hrot.ClusterRunner.csproj` already references `Hrot.Common` → no new reference needed.

**After PM-3:** Run full build. Fix all namespace/reference compile errors before proceeding. The test suite should be fully green after this step (no logic changes).

---

## EAM-M001 — Migrate `SimHostApp.OnLoad` to `HrotNodeBuilder`

**File to modify:** `d:\Work\IOS-IG-SimHost-FDP-2\Hrot.SimHost\SimHostApp.cs`

### What to replace

`SimHostApp.OnLoad` currently has ~300 lines doing:
- Step 1: Load config / domainId / localNodeId 
- Step 2: Create EntityRepository + ModuleHostKernel
- Step 3: Create EventBus + TimeController
- Step 4: Create DdsParticipant + EnsureIdAllocatorRouting + entityMap
- Step 5: Geodetic config (wgs84)
- Step 5a/5b: Attribute compilers
- Step 6: Doctrine registry
- Step 7: Road network
- Step 8: NodeBootstrapper.BuildSimulationLogic (creates _simLogicModule)
- Step 8a: ClusterSlave + BuildOrchestration
- Steps 9–10: Toolkit modules + CycloneNetworkModule

**Steps 1–end remain**, but steps 2–4 and 8a are replaced by `HrotNodeBuilder.Build()`.

### New `OnLoad` structure

```csharp
protected override void OnLoad()
{
    Console.Title = "Hrot.SimHost";
    Logger.Info("[SimHost] Starting graphical application...");

    // ── Step 1 — Load configuration ──────────────────────────────────────────
    _nodeConfig?.ApplyEnvironment();
    Logger.Info($"[SimHost] Node role: {_role}");

    var nodeConfig   = _nodeConfig ?? NodeConfiguration.LoadFrom("config.json");
    if (_nodeConfig == null) nodeConfig.ApplyEnvironment();
    var domainId     = _domainOverride ?? (int)nodeConfig.DdsDomainId;
    var localNodeId  = _nodeIdOverride != 0 ? _nodeIdOverride : SimHostNetworkConstants.LocalNodeId;
    Logger.Info($"[SimHost] Domain ID:       {domainId}");
    Logger.Info($"[SimHost] Node ID:         {localNodeId}");
    Logger.Info($"[SimHost] Simulation Rate: {nodeConfig.SimulationRateHz} Hz");

    // ── Steps 2–4 + 8a: Build Hrot node infrastructure ───────────────────────
    var hrotConfig = new HrotNodeConfig
    {
        DomainId      = domainId,
        NodeId        = localNodeId,
        Headless      = _headless,
        LocalTempRoot = nodeConfig.LocalTempRoot,
        SubsystemName = "SimHost",
    };
    _context = new HrotNodeBuilder(hrotConfig)
        .WithRole("SimHost", NodeRole.AllInOne)
        .Build();

    _world       = _context.World;
    _kernel      = _context.Kernel;
    _eventBus    = _context.EventBus;
    _entityMap   = _context.EntityMap;
    _clusterSlave    = _context.ClusterSlave;
    _slaveTranslator = _context.SlaveTranslator;

    base.World  = _world;
    base.Kernel = _kernel;

    RegisterSimComponents(_world);

    // ── Step 5 — Geodetic configuration ──────────────────────────────────────
    var tkbDb     = HrotEnvironment.CreateTkb();   // still needed here for spawning
    var wgs84     = HrotEnvironment.CreateGeoTransform();
    _geoTransform = wgs84;

    // ── Step 5a/5b — Attribute compilers ─────────────────────────────────────
    var jsonAttributeCompiler = AttributeCompilerFactory.Build(wgs84);
    var binaryInterpreter     = AttributeCompilerFactory.BuildBinaryInterpreter(wgs84);

    // ── Step 6 — Doctrine registry (SimHost-specific; not in HrotNodeBuilder) ─
    // ... (keep doctrine setup unchanged from original OnLoad)

    // ── Step 7 — Road network ─────────────────────────────────────────────────
    var roadNetwork = LoadRoadNetwork(nodeConfig.RoadNetworkBlobPath);

    // ── Step 8 — SimulationLogicModule (NodeBootstrapper; unchanged) ──────────
    var bootstrapper = new NodeBootstrapper();
    _simLogicModule  = bootstrapper.BuildSimulationLogic(
        _role, _doctrineRegistry, _entityMap, vehicleApi: null, roadNetwork);

    // ── BuildOrchestration: pass HrotNodeBuilder-provided ghost/lifecycle objs ─
    var ghostCreationSystem   = _context.GhostCreationSystem
                                 ?? new GhostCreationSystem(_entityMap);
    var simulationSystemGroup = new SimulationSystemGroup();
    var networkLifecycleGroup = new NetworkLifecycleSystemGroup(ghostCreationSystem);

    _clusterSlave = bootstrapper.BuildOrchestration(
        _role, _kernel, _world, localNodeId,
        participant:        _context.Participant,
        subsystemName:      "SimHost",
        eventBus:           _eventBus,
        scenarioSerializer: scenarioSerializer,
        localTempRoot:      nodeConfig.LocalTempRoot,
        checkpointWorker:   _checkpointWorker,
        simGroup:           simulationSystemGroup,
        lifecycleGroup:     networkLifecycleGroup,
        ghostCreationSystem: ghostCreationSystem);
    _slaveTranslator = bootstrapper.SlaveTranslator;

    // ── _kernelGroup systems (unchanged) ────────────────────────────────────────
    _kernelGroup = new SystemGroup();
    _kernelGroup.Create(_world);
    _kernelGroup.AddSystem(new MissionControlExecutionSystem(_entityMap, _doctrineRegistry));
    // ... (keep same as original)
    _simLogicModule.RegisterSystems(_kernelGroup, _kernelGroup, _kernelGroup);

    // ── Step 8b — NedReplicationModule ────────────────────────────────────────
    _nedReplicationModule = new NedReplicationModule(
        participant:  _context.Participant,
        role:         _role,
        entityMap:    _entityMap,
        geoTransform: wgs84,
        eventBus:     _eventBus,
        localNodeId:  localNodeId,
        domainId:     domainId);
    _kernel.RegisterModule(_nedReplicationModule);

    // ── Step 9 — Toolkit modules (unchanged from original) ───────────────────
    // ... keep PhysicsToolkitModule, GeographicModule, EntityLifecycleModule etc.
    // NOTE: EntityLifecycleModule creation stays here because it's needed before
    //       spawning and SimHostModule construction below.
    //       (HrotNodeBuilder creates one too in BaseModules — register it via _context.BaseModules)
    foreach (var m in _context.BaseModules)
        _kernel.RegisterModule(m);

    // ── Steps 10 + onwards: DDS adapters, SimHostModule, CycloneNetworkModule ──
    // Keep exactly as in the current OnLoad — nothing changes for these steps.
    // ...

    // ── Step 11 — Kernel init ─────────────────────────────────────────────────
    _kernel.Initialize();
    Logger.Info("[SimHost] Kernel initialized.");

    // ... (visualization, etc.)
    _initialized = true;
}
```

> **IMPORTANT implementation notes:**
> 1. Delete the private `EnsureIdAllocatorRouting` method — `HrotNodeBuilder` calls the shared helper.
> 2. Add `private HrotNodeContext? _context;` and `private IEcsModule? _nedReplicationModule;` fields.
> 3. `NedReplicationModule` requires a `using Hrot.ClusterRunner.Replication;` — add to usings.
> 4. `using Hrot.Common.Infrastructure;` — add for HrotNodeBuilder/Context/Config.
> 5. Keep `_idAllocator` field assignment: `_idAllocator = _context.???` — but wait, `HrotNodeContext` doesn't expose `DdsIdAllocator`. Either expose it via a new property on `HrotNodeContext` or keep creating it inline before calling `HrotNodeBuilder` in cases where SimHostModule needs it.  
>    **CHECK:** `SimHostModule` needs `_idAllocator` for spawning. If `HrotNodeBuilder` creates it internally, expose `DdsIdAllocator? IdAllocator { get; init; }` on `HrotNodeContext` and add the assignment in the builder. This is a small extension to the existing `HrotNodeContext` type.
> 6. `Shutdown()` must pass `_nedReplicationModule` to `UninstallModulesAsync` if that method exists, or simply `Dispose()`.
> 7. `tokenBuilder.BuildOrchestration` receives a `ClusterSlave`. After HrotNodeBuilder.Build(), the pre-built ClusterSlave in `_context.ClusterSlave` already has Reference handlers. However, `BuildOrchestration` internally may create a NEW ClusterSlave and return it. In that case, use the returned one (`_clusterSlave = bootstrapper.BuildOrchestration(...)`). The `_context.ClusterSlave` can be ignored for SimHost because BuildOrchestration replaces it. That is acceptable for this migration.

**Add to `HrotNodeContext`:**
```csharp
/// <summary>The DDS ID allocator for entity ID allocation. Null in headless contexts.</summary>
public DdsIdAllocator? IdAllocator { get; init; }
```

**Update `HrotNodeBuilder.Build()` step 7 to assign it:**
```csharp
idAllocator = new DdsIdAllocator(participant, (_config.SubsystemName ?? _subsystemName) + "Allocator");
DdsIdAllocatorHelper.EnsureRouting(participant, idAllocator);
```
And in the return context:
```csharp
IdAllocator = idAllocator,
```

### Success conditions

*SC1 — All SimHost integration tests pass with zero regressions:*
```powershell
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot.ClusterRunner.Integration.Tests --no-build
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot.SimHost.Tests --no-build
```

*SC2 — SimHostApp.OnLoad body is ≤ 60 meaningful lines:*
> Review: the method body is dominated by calls to `HrotNodeBuilder`, module registration, and SimHost-specific doctrine/road-network setup. The private `EnsureIdAllocatorRouting` method is deleted.

*SC3 — No manual `HrotEnvironment.CreateParticipant` in OnLoad:*
> Review: `HrotEnvironment.CreateParticipant` not called directly in `SimHostApp.OnLoad`.

*SC4 — Module fields stored:*
> Review: `SimHostApp` declares `private HrotNodeContext? _context`, `private IEcsModule? _nedReplicationModule`. Teardown uses them.

---

## EAM-M002 — Migrate `IgApplication` to `HrotNodeBuilder`

**File to modify:** `d:\Work\IOS-IG-SimHost-FDP-2\Hrot.IG\IgApplication.cs`

### What to replace

`InitializeEcs` and `InitializeNetwork` together create:
- `EntityRepository`, `ModuleHostKernel`, `NetworkEntityMap` in `InitializeEcs`
- `DdsParticipant` + `EntityLifecycleModule` + `ReplicationLogicModule` + its `GhostCreationSystem` in `InitializeNetwork`

All of these are replaced by `HrotNodeBuilder.Build()`. The 40+ IG-specific translators (EntityMaster, GeoSpatial, etc.) remain manual.

### Pre-requisite: add usings to IgApplication.cs

```csharp
using Hrot.Common.Infrastructure; // HrotNodeBuilder, HrotNodeContext, HrotNodeConfig
using Hrot.ClusterRunner.Replication; // NedReplicationModule
using Hrot.Common; // NodeRole
```

> Add `<ProjectReference Include="..\Hrot.ClusterRunner\Hrot.ClusterRunner.csproj" />` to `Hrot.IG.csproj` if it is NOT already present. (Check first — it may already be there since Hrot.IG is referenced by Hrot.ClusterRunner... wait, that would be circular: Hrot.ClusterRunner → Hrot.IG and Hrot.IG → Hrot.ClusterRunner is CIRCULAR.)  
> **Therefore: NedReplicationModule must come from Hrot.Common.Infrastructure too if IgApplication is to use it.**  
> Check if there's a circular dep issue: Hrot.ClusterRunner → Hrot.IG. If yes, IgApplication CANNOT use NedReplicationModule directly.  
>  
> **Alternative for EAM-M002**: If the circular dep is confirmed, IgApplication can use HrotNodeBuilder (from Hrot.Common) + manually register the translator packs (EntityLifecycleModule + ReplicationLogicModule replaced by NedReplicationModule is NOT available). In this case, move NedReplicationModule to Hrot.Common too, or keep the manual pack registration but use HrotNodeBuilder for the core infrastructure.

Because of the likely circular dependency between Hrot.IG and Hrot.ClusterRunner, take this approach for EAM-M002:

1. **Use HrotNodeBuilder for core infra** (world, kernel, participant, entityMap, clusterSlave)
2. **Do NOT use NedReplicationModule** (it's in Hrot.ClusterRunner which Hrot.IG cannot reference)
3. **Replace ReplicationLogicModule** with explicit `EntityLifecycleModule` (already from HrotNodeBuilder's BaseModules) + manually wire `GhostCreationSystem` from context
4. **DeadReckoningSyncSystem with driveFromNetwork:true** — register it explicitly: `registry.RegisterSystem(new DeadReckoningSyncSystem(driveFromNetwork: true))`

### New `InitializeEcs` structure

Replace `InitializeEcs()` body with:

```csharp
private void InitializeEcs()
{
    var igConfig = new HrotNodeConfig
    {
        DomainId  = _domainOverride ?? IgNetworkConstants.DdsDomain,
        NodeId    = _effectiveInstanceId,
        Headless  = false, // DDS is always initialised in InitializeNetwork; headless=false here
    };
    _context = new HrotNodeBuilder(igConfig)
        .WithRole("IgApplication", NodeRole.ImageGenerator)
        .Build();

    _world       = _context.World;
    _entityMap   = _context.EntityMap;
    _kernel      = _context.Kernel;

    // IG-specific component registration (unchanged)
    HrotSharedComponentRegistry.RegisterAll(_world);
    _world.RegisterComponent<ResolvedStyle>();
    // ... (keep all IG-specific RegisterComponent calls)

    // UI panels, camera, viewports (unchanged)
    // ...
}
```

### New `InitializeNetwork` structure

Replace the DDS + module-setup block inside `InitializeNetwork`:

```csharp
private void InitializeNetwork(bool enableNetwork, int? domainIdOverride)
{
    _networkEnabled = enableNetwork;
    _geoTransform   = HrotEnvironment.CreateGeoTransform();

    // Register BaseModules from builder
    foreach (var m in _context.BaseModules)
        _kernel.RegisterModule(m);

    // Re-use ghostCreationSystem from context (null in headless; create fallback)
    _ghostCreationSystem = _context.GhostCreationSystem
        ?? new GhostCreationSystem(_entityMap);

    // Replace ReplicationLogicModule with explicit DR sync (Pure IG — driveFromNetwork: true)
    // Note: NedReplicationModule is not used here because Hrot.IG cannot reference Hrot.ClusterRunner.
    // DeadReckoningSyncSystem handles ghost entity smoothing; SharedTranslatorPack is registered below.
    // TODO (P3 debt): Move NedReplicationModule to Hrot.Common so IgApplication can use it directly.

    DdsParticipant? participant = null;
    // ... (keep existing IG translator setup code, but use _context.Participant
    //      instead of locally created participant, and _context.ClusterSlave, etc.)

    if (enableNetwork)
    {
        participant = _context.Participant; // HrotNodeBuilder already created this
        // ... rest of IG-specific translators (EntityMasterIngressTranslator, etc.)
        //     use `participant` and `_ghostCreationSystem` as before — nothing else changes    
    }

    // Cluster slave wiring — use context's ClusterSlave + SlaveTranslator
    _clusterSlave     = _context.ClusterSlave;
    _igSlaveTranslator = _context.SlaveTranslator;

    // ... register CycloneNetworkModule etc. (unchanged)

    _kernel.Initialize();
}
```

> **Key changes in InitializeNetwork:**
> 1. `participant = _context.Participant` — remove `HrotEnvironment.CreateParticipant(domainId)` call
> 2. `_ghostCreationSystem = _context.GhostCreationSystem ?? new GhostCreationSystem(...)` — remove old `replicationModule.GhostCreationSystem` line
> 3. Remove `elm = new EntityLifecycleModule(...)` + `_kernel.RegisterModule(elm)` — handled by HrotNodeBuilder's BaseModules
> 4. Remove `var replicationModule = new ReplicationLogicModule(...)` + `_kernel.RegisterModule(replicationModule)` — replaced by explicit DR sync
> 5. Keep `_networkEnabled = false;` guard logic — the non-network path still creates no participant (context.Participant is null when headless)

### Success conditions

*SC1 — All IG tests pass:*
```powershell
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot.IG.Tests --no-build
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot.ClusterRunner.Integration.Tests --filter "Ig" --no-build
```

*SC2 — HrotEnvironment.CreateParticipant not called directly in InitializeEmbedded or InitializeNetwork:*
> Code review: only called inside HrotNodeBuilder; `_context.Participant` is reused.

*SC3 — DeadReckoningSyncSystem registered with `driveFromNetwork: true`:*
> Code review: `new DeadReckoningSyncSystem(driveFromNetwork: true)` registered explicitly as IG has no local physics.

---

## EAM-M003 — Migrate `CgfSubsystem.Initialize` to `HrotNodeBuilder`

**File to modify:** `d:\Work\IOS-IG-SimHost-FDP-2\Hrot.ClusterRunner\Services\CgfSubsystem.cs`

### What to replace

Currently `CgfSubsystem.Initialize` creates a `CgfApplication` and calls `_app.Install(...)` for each pack. `CgfApplication`'s constructor (which you should read before implementing) creates the DDS participant, ClusterSlave, NodeOpSlaveTranslator, EventBus, and time controller — exactly what `HrotNodeBuilder.Build()` provides.

**Read these before implementing:**
- `d:\Work\IOS-IG-SimHost-FDP-2\Hrot.CGF\CgfApplication.cs` — full file, especially the constructor
- `d:\Work\IOS-IG-SimHost-FDP-2\Hrot.ClusterRunner\Services\CgfSubsystem.cs` — full file

### New `CgfSubsystem.Initialize`

```csharp
public void Initialize(SubsystemConfig config)
{
    // ── Build common infrastructure ────────────────────────────────────────────
    var nodeConfig = new HrotNodeConfig
    {
        DomainId      = config.DomainId,
        NodeId        = config.NodeId != 0 ? config.NodeId : 400,
        Headless      = config.Headless,
        SubsystemName = "CGF",
    };
    _context = new HrotNodeBuilder(nodeConfig)
        .WithRole("CgfNode", NodeRole.Brain)
        .Build();

    CgfComponentRegistry.RegisterAll(_context.World);

    // ── Register ClusterSlave handlers specific to CGF ─────────────────────
    // HrotNodeBuilder registers the 4 generic handlers (Preview, Prefetch, Archive, LiveLoad).
    // CGF additionally needs replay and scenario handlers:
    var rrController = new CgfRecordReplayController();
    _context.ClusterSlave.RegisterHandler(new ReferenceReplayLoadHandler(
        rrController,
        simGroup:              null,
        lifecycleGroup:        null,
        bypassLifecycleToggle: null,
        storageDirectory:      nodeConfig.LocalTempRoot ?? @"C:\FDP_Temp"));
    // (Register ReferenceScenarioLoadHandler and ReferenceEpisodeLoadHandler if a ScenarioSerializer
    //  is provided via SubsystemConfig — add a ScenarioSerializer? property to SubsystemConfig
    //  or pass null initially.)

    // ── Register base infrastructure modules ─────────────────────────────────
    foreach (var m in _context.BaseModules)
        _context.Kernel.RegisterModule(m);

    // ── Register NedReplicationModule (Brain role) ────────────────────────────
    // Replaces: EntityStatesIngressPack + ActuatorIntentsEgressPack + GhostCleanupModule
    _nedReplicationModule = new NedReplicationModule(
        participant:  _context.Participant,
        role:         NodeRole.Brain,
        entityMap:    _entityMap!,
        geoTransform: HrotEnvironment.CreateGeoTransform(),
        eventBus:     _context.EventBus,
        localNodeId:  nodeConfig.NodeId,
        domainId:     config.DomainId);
    _context.Kernel.RegisterModule(_nedReplicationModule);

    // ── Register CGF simulation logic (Brain-specific) ────────────────────────
    var doctrineRegistry = new DoctrineRegistry();
    _entityMap           = _nedReplicationModule.GhostCreationSystem.EntityMap; // re-use from module
    _context.Kernel.RegisterModule(new CgfLogicPack(doctrineRegistry, _entityMap));

    // ── Initialize ────────────────────────────────────────────────────────────
    _context.Kernel.Initialize();
}
```

> **Implementation notes:**
> 1. Add `private HrotNodeContext? _context;` and `private IEcsModule? _nedReplicationModule;` fields.
> 2. `_entityMap` must be created BEFORE `NedReplicationModule` (pass it in), OR use `_nedReplicationModule.GhostCreationSystem` to access the entity map afterwards. Best: create `_entityMap = new NetworkEntityMap();` before calling `NedReplicationModule` ctor and pass it in.
> 3. `CgfLogicPack(doctrineRegistry, _entityMap)` — CgfLogicPack is `IEcsModule`, so use `RegisterModule`.
> 4. Do NOT call `CgfApplication` at all — it's entirely replaced by `HrotNodeBuilder`.
> 5. Drop `GhostCleanupModule` — `NedReplicationModule` registers `DisposalMonitoringSystem` which handles cleanup.
> 6. `EntityStatesIngressPack` (ingress physical states) and `ActuatorIntentsEgressPack` (egress actuator intents) are REPLACED by `NedReplicationModule(Brain)` which uses `CognitiveTranslatorPack` (NavigationIntent + GeoSpatial + Mission). This is a translator set change — run all CGF integration tests carefully to verify no regressions.

### Update `Update` and `Shutdown`

```csharp
public void Update(float deltaTime)
{
    _context?.SlaveTranslator?.Tick();
    _context?.ClusterSlave.Tick();
    _context?.Kernel.Update(deltaTime);
    _context?.EventBus.SwapBuffers();
}

public void Shutdown()
{
    _context?.Kernel.Dispose();
    _context?.Participant?.Dispose();
    _context = null;
}
```

(Previously called `_app?.Tick()` — `CgfApplication.Tick()` does exactly these same steps internally.)

### Success conditions

*SC1 — All CGF integration tests pass:*
```powershell
dotnet test d:\Work\IOS-IG-SimHost-FDP-2\Hrot.ClusterRunner.Integration.Tests --filter "Cgf" --no-build
```

*SC2 — HrotNodeBuilder and NedReplicationModule used in Initialize:*
> Code review: no `new CgfApplication(...)`, no `_app.Install(...)`.

*SC3 — NedReplicationModule field retained:*
> Code review: `private IEcsModule? _nedReplicationModule;` present; passed to disposal in Shutdown.

---

## Test policy

Run full test suites after EACH task is complete:

| After completing | Test command |
|---|---|
| Pre-Migration | `dotnet build` + `dotnet test Hrot.ClusterRunner.Tests` + `dotnet test Hrot.ClusterRunner.Integration.Tests` |
| EAM-M001 | Above + `dotnet test Hrot.SimHost.Tests` |
| EAM-M002 | Above + `dotnet test Hrot.IG.Tests` |
| EAM-M003 | All of the above |

**Expected outcome:** 0 failing tests across all suites.

---

## Report format

Write report to: `.dev/eyes-and-muscle/reports/BATCH-03-REPORT.md`

For each task report:
1. Files created/modified  
2. Tests run + pass/fail count
3. Deviations from spec with justification
4. Any new debt discovered

---

## Known risks

| Risk | Mitigation |
|---|---|
| Hrot.Common.csproj expanded deps may cause assembly resolution issues | Build frequently; check output of `dotnet build --verbosity diagnostic` |
| NodeRole move may break more files than expected | Grep for `Hrot.SimHost.NodeRole` and `NodeRole.` across solution before moving |
| EAM-M003 translator pack change (EntityStates→Cognitive) may cause test regressions | Expected — check CGF integration tests carefully; if tests fail due to translator change, investigate whether the old EntityStatesIngressPack should be KEPT alongside NedReplicationModule |
| CgfApplication uniquely creates ReferenceReplayLoadHandler with CgfRecordReplayController | HrotNodeBuilder's ReferenceLiveLoadHandler is generic; CGF-specific handlers must be re-registered manually on `_context.ClusterSlave` after Build() |
