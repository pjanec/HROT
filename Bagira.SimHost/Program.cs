using System;
using System.Collections.Generic;
using System.Threading;
using Bagira.SimHost.Configuration;
using Bagira.SimHost.Modules;
using Bagira.SimHost.Utilities;
using CycloneDDS.Runtime;
using Fdp.Interfaces; 
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Lifecycle; 
using FDP.Toolkit.NetworkSpawning.Systems; 
using FDP.Toolkit.Replication.Services; 
using FDP.Toolkit.Time.Controllers; 
using Fdp.Toolkit.Tkb; 
using ModuleHost.Core;
using ModuleHost.Core.Network; 
using ModuleHost.Core.Time;
using ModuleHost.Network.Cyclone.Modules; 
using ModuleHost.Network.Cyclone.Services; 

// Resolve Ambiguities
using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;
using IDescriptorTranslator = Fdp.Interfaces.IDescriptorTranslator;
using Bagira.SimHost;

Console.Title = "Bagira.SimHost";
Logger.Info("[SimHost] Starting...");

// ── S5.4: Graceful Ctrl+C shutdown ───────────────────────────────────────────
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true; // prevent immediate process kill
    Logger.Info("[SimHost] Shutdown requested...");
    cts.Cancel();
};

SystemGroup? kernelGroup = null;

try
{
    // ── S5.2: Load configuration ──────────────────────────────────────────────
    var config = SimHostConfig.Load("config.json");
    Logger.Info($"[SimHost] Domain ID:        {config.DomainId}");
    Logger.Info($"[SimHost] Simulation Rate:  {config.SimulationRateHz} Hz");
    Logger.Info($"[SimHost] Origin:           {config.GeodeticOrigin.Latitude}, {config.GeodeticOrigin.Longitude}");

    // ── 1. Initialize Kernel ──────────────────────────────────────────────────
    var world            = new EntityRepository();
    var eventAccumulator = new EventAccumulator();
    var kernel           = new ModuleHostKernel(world, eventAccumulator);

    // Set Time Controller (real-time)
    var eventBus  = new FdpEventBus(); 
    var timeConfig = new TimeControllerConfig 
    { 
        Mode = TimeMode.Continuous, 
        Role = TimeRole.Master 
    };
    var timeCtrl = TimeControllerFactory.Create(eventBus, timeConfig);
    timeCtrl.SetTimeScale(1.0f);
    kernel.SetTimeController(timeCtrl);

    // ── 2. Data Model Services ────────────────────────────────────────────────
    var ddsParticipant = new DdsParticipant(); 
    var tkbDb          = new TkbDatabase();
    var entityMap      = new NetworkEntityMap();
    var idAllocator    = new DdsIdAllocator(ddsParticipant, "SimHostAllocator");

    // ── 3. Geodetic Configuration ─────────────────────────────────────────────
    var wgs84 = new WGS84Transform();
    wgs84.SetOrigin(
        config.GeodeticOrigin.Latitude,
        config.GeodeticOrigin.Longitude,
        config.GeodeticOrigin.Altitude
    );
    Logger.Info($"[SimHost] WGS84 origin set to: {config.GeodeticOrigin.Latitude:F4}, {config.GeodeticOrigin.Longitude:F4}");

    // ── S5.1: Doctrine Registry ───────────────────────────────────────────────
    // Register all four SimHost doctrines with stable compile-time IDs
    // (DEBT-006: IDs are integer constants, never string.GetHashCode()).
    var doctrineRegistry = new DoctrineRegistry();
    doctrineRegistry.Register(SimHostDoctrineIds.MoveTo_BT, "MoveToLocation",
        new DoctrineDefinition { Name = "MoveToLocation", BrainTier = BehaviorConstants.BrainTierBTree });
    doctrineRegistry.Register(SimHostDoctrineIds.FollowRoute_BT, "FollowRoute",
        new DoctrineDefinition { Name = "FollowRoute",    BrainTier = BehaviorConstants.BrainTierBTree });
    doctrineRegistry.Register(SimHostDoctrineIds.JoinFormation_BT, "JoinFormation",
        new DoctrineDefinition { Name = "JoinFormation",  BrainTier = BehaviorConstants.BrainTierBTree });
    doctrineRegistry.Register(SimHostDoctrineIds.Idle_HSM, "Idle",
        new DoctrineDefinition { Name = "Idle",           BrainTier = BehaviorConstants.BrainTierHsm });
    Logger.Info("[SimHost] Doctrine registry initialised (4 doctrines: MoveTo, FollowRoute, JoinFormation, Idle)");

    // ── S5.1: SimulationLogicModule ───────────────────────────────────────────
    // Wire all behavior / navigation / physics systems into a dedicated SystemGroup.
    var simLogicModule = new SimulationLogicModule(
        doctrineRegistry,
        entityMap,
        vehicleAPI: null            // dummy – full VehicleAPI wired up in a later phase
    );

    kernelGroup = new SystemGroup();
    kernelGroup.Create(world);
    simLogicModule.RegisterSystems(kernelGroup);
    Logger.Info("[SimHost] SimulationLogicModule registered (9 systems)");

    // Seed the GlobalTime singleton so ComponentSystem.DeltaTime is valid on the
    // first frame (the time controller will overwrite it every subsequent frame).
    world.SetSingletonUnmanaged(new GlobalTime
    {
        DeltaTime = 1.0f / config.SimulationRateHz,
        TimeScale = 1.0f
    });

    // ── 4. Toolkit Modules ────────────────────────────────────────────────────

    // Geographic Module
    var geoModule = new GeographicModule(wgs84);
    kernel.RegisterModule(geoModule);

    // Entity Lifecycle Module
    var elm = new EntityLifecycleModule(tkbDb, new List<int>()); 
    kernel.RegisterModule(elm);

    // Network Spawning System
    var spawningSystem = new NetworkSpawningSystem(
        tkbDb,
        elm,
        entityMap,
        idAllocator,
        localNodeId: 1 
    );

    // SimHost Application Module
    var simHostMod = new SimHostModule(
        ddsParticipant,
        tkbDb,
        idAllocator,
        1, // localNodeId
        spawningSystem,
        entityMap,
        wgs84
    );
    kernel.RegisterModule(simHostMod);

    // ── 5. Network Module (Cyclone) ───────────────────────────────────────────
    var translators = new List<IDescriptorTranslator>();
    if (simHostMod.GeoEgressTranslator != null)
        translators.Add(simHostMod.GeoEgressTranslator);
    translators.Add(simHostMod.MissionIngressTranslator);
    translators.Add(simHostMod.MissionEgressTranslator);

    var localNodeId = 1;
    var nodeMapper  = new NodeIdMapper(0, localNodeId);
    var topology    = new StaticNetworkTopology(localNodeId, new[] { localNodeId });

    var cycloneModule = new CycloneNetworkModule(
        ddsParticipant,
        nodeMapper,
        idAllocator,
        topology,
        elm,
        customTranslators: translators,
        sharedEntityMap:   entityMap
    );
    kernel.RegisterModule(cycloneModule);

    // ── 6. Initialise Kernel ──────────────────────────────────────────────────
    Logger.Info("[SimHost] Kernel initialising...");
    kernel.Initialize();

    Logger.Info("[SimHost] Running. Press Ctrl+C to exit.");

    // ── 7. Main simulation loop ───────────────────────────────────────────────
    RunSimulationLoop(kernel, kernelGroup, cts.Token);

    // ── S5.4: Cleanup ─────────────────────────────────────────────────────────
    Logger.Info("[SimHost] Shutting down...");
    idAllocator.Dispose();
    Logger.Info("[SimHost] Shutdown complete.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[SimHost] Fatal Error: {ex}");
}
finally
{
    kernelGroup?.Dispose();
}

Logger.Info("[SimHost] Exited.");

// ── RunSimulationLoop (S5.4) ──────────────────────────────────────────────────
/// <summary>
/// Drives the kernel + simulation-logic group at ~1 ms resolution until
/// <paramref name="cancellationToken"/> is cancelled (Ctrl+C).
/// </summary>
static void RunSimulationLoop(
    ModuleHostKernel kernel,
    SystemGroup      group,
    CancellationToken cancellationToken)
{
    ulong frame = 0;

    while (!cancellationToken.IsCancellationRequested)
    {
        kernel.Update();   // time controller, modules, network
        group.Run();       // SimulationLogicModule systems

        frame++;
        Thread.Sleep(1);   // yield to OS; time controller handles dt internally
    }

    Logger.Info($"[SimHost] Simulation loop terminated at frame {frame}.");
}
