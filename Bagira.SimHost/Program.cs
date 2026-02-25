using System;
using System.Collections.Generic;
using System.Threading;
using Bagira.SimHost.Configuration;
using Bagira.SimHost.Modules;
using CycloneDDS.Runtime;
using Fdp.Interfaces; 
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using FDP.Toolkit.Lifecycle; 
using FDP.Toolkit.NetworkSpawning.Systems; 
using FDP.Toolkit.Replication.Services; 
using FDP.Toolkit.Time.Controllers; 
using Fdp.Toolkit.Tkb; 
using ModuleHost.Core;
using ModuleHost.Core.Network; 
using ModuleHost.Core.Time; // For TimeRole, TimeMode
using ModuleHost.Network.Cyclone.Modules; 
using ModuleHost.Network.Cyclone.Services; 

// Resolve Ambiguities
using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;
using IDescriptorTranslator = Fdp.Interfaces.IDescriptorTranslator;

Console.Title = "Bagira.SimHost";
Console.WriteLine("[SimHost] Starting...");

try
{
    // 1. Initialize Kernel
    var world = new EntityRepository();
    var eventAccumulator = new EventAccumulator();
    var kernel = new ModuleHostKernel(world, eventAccumulator);

    // Set Time Controller (Real-time)
    var eventBus = new FdpEventBus(); 
    var timeConfig = new TimeControllerConfig 
    { 
        Mode = TimeMode.Continuous, 
        Role = TimeRole.Master 
    };
    var timeCtrl = TimeControllerFactory.Create(eventBus, timeConfig);
    timeCtrl.SetTimeScale(1.0f);
    kernel.SetTimeController(timeCtrl);

    // 2. Data Model Services
    var ddsParticipant = new DdsParticipant(); 

    var tkbDb = new TkbDatabase();
    var entityMap = new NetworkEntityMap();
    var idAllocator = new DdsIdAllocator(ddsParticipant, "SimHostAllocator");
    // Removed .Start()

    // 3. Geodetic Configuration
    var wgs84 = new WGS84Transform();
    wgs84.SetOrigin(
        SimHostConfig.OriginLatitude,
        SimHostConfig.OriginLongitude,
        SimHostConfig.OriginAltitude
    );
    Console.WriteLine($"[SimHost] Origin set to: {SimHostConfig.OriginLatitude}, {SimHostConfig.OriginLongitude}");

    // 4. Toolkit Modules

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

    // 5. Network Module (Cyclone)
    
    // Prepare Translators
    var translators = new List<IDescriptorTranslator>();
    if (simHostMod.GeoEgressTranslator != null)
    {
        translators.Add(simHostMod.GeoEgressTranslator);
    }

    // Network Setup
    var localNodeId = 1;
    var nodeMapper = new NodeIdMapper(0, localNodeId);
    
    // Topology: StaticNetworkTopology(localNodeId, allNodes)
    var topology = new StaticNetworkTopology(localNodeId, new[] { localNodeId });

    var cycloneModule = new CycloneNetworkModule(
        ddsParticipant,
        nodeMapper,
        idAllocator,
        topology,
        elm,
        customTranslators: translators,
        sharedEntityMap: entityMap
    );
    kernel.RegisterModule(cycloneModule);

    // 6. Initialize Kernel
    Console.WriteLine("[SimHost] Kernel Initializing...");
    kernel.Initialize();

    Console.WriteLine("[SimHost] Running. Press Ctrl+C to exit.");

    // 7. Loop
    var running = true;
    Console.CancelKeyPress += (s, e) => {
        e.Cancel = true;
        running = false;
        Console.WriteLine("[SimHost] Stopping...");
    };

    while (running)
    {
        kernel.Update();
        Thread.Sleep(1); 
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[SimHost] Fatal Error: {ex}");
}

Console.WriteLine("[SimHost] Exited.");
