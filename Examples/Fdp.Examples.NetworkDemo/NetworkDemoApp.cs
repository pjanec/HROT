using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using System.Numerics;
using System.Collections.Generic;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;
using Fdp.Kernel.FlightRecorder.Metadata;
using Fdp.Interfaces;
using Fdp.Examples.NetworkDemo.Components;
using Fdp.Examples.NetworkDemo.Events;
using Fdp.Examples.NetworkDemo.Configuration;
using Fdp.Examples.NetworkDemo.Descriptors;
using Fdp.Examples.NetworkDemo.Systems;
using Fdp.Examples.NetworkDemo.Modules;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using ModuleHost.Core;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Network;
using ModuleHost.Core.Network.Interfaces;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Lifecycle.Systems;
using FDP.Toolkit.Lifecycle.Events;
using Fdp.Toolkit.Tkb;
using FDP.Toolkit.Replication;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Systems;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Time.Controllers;
using ModuleHost.Network.Cyclone;
using ModuleHost.Network.Cyclone.Services;
using ModuleHost.Network.Cyclone.Modules;
using ModuleHost.Network.Cyclone.Providers;
using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tracking;
using NLog;
using FDP.Kernel.Logging;

namespace Fdp.Examples.NetworkDemo
{
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class ComponentSystemWrapper : IEcsModuleSystem
    {
        private readonly ComponentSystem _sys;
        public ComponentSystemWrapper(ComponentSystem sys) => _sys = sys;
        public void Execute(ISimulationView view, float dt) => _sys.Run();
    }

    public class NetworkDemoApp : IDisposable
    {
        public EntityRepository World { get; private set; } = default!;
        public ModuleHostKernel Kernel { get; private set; } = default!;

        public int InstanceId => instanceId;
        public int LocalNodeId => localInternalId;
        public Fdp.Interfaces.ITkbDatabase Tkb => tkb;
        public FDP.Toolkit.Replication.Services.NetworkEntityMap EntityMap { get; private set; } = default!;
        public Fdp.Kernel.FdpEventBus EventBus { get; private set; } = default!; // For testing
        
        private DdsParticipant participant = default!;
        private AsyncRecorder? recorder;
        private ReplayBridgeSystem? replaySystem;
        private bool isReplay;
        private int instanceId;
        private string recordingPath = default!;
        private int localInternalId;
        private NodeIdMapper nodeMapper = default!;
        private TkbDatabase tkb = default!;
        private FDP.Toolkit.Time.Controllers.DistributedTimeCoordinator? _timeCoordinator;
        private FDP.Toolkit.Time.Controllers.SlaveTimeModeListener? _slaveListener;
        private bool _testMode;
        
        private readonly ConcurrentQueue<Action<EntityRepository>> _actionQueue = new();
        public void EnqueueAction(Action<EntityRepository> action) => _actionQueue.Enqueue(action);

        public async Task InitializeAsync(int nodeId, bool replayMode, string? recPath = null, bool autoSpawn = true, bool enableNetwork = true, bool testMode = false)
        {
            using (ScopeContext.PushProperty("NodeId", nodeId))
            {
            FdpLog<NetworkDemoApp>.Info("Starting Node {0}...", nodeId);
            instanceId = nodeId;
            isReplay = replayMode;
            recordingPath = recPath ?? $"node_{instanceId}.fdp";
            _testMode = testMode;
            
            string nodeName = instanceId == 100 ? "Alpha" : "Bravo";
            
            FdpLog<NetworkDemoApp>.Info("==========================================");
            var modeLabel = isReplay ? "REPLAY" : "LIVE";
            FdpLog<NetworkDemoApp>.Info(
                "  Network Demo - {0} (ID: {1}) [{2}]",
                nodeName,
                instanceId,
                modeLabel);
            FdpLog<NetworkDemoApp>.Info("==========================================");
            
            // Common Setup
            World = new EntityRepository();
            EntityMap = new FDP.Toolkit.Replication.Services.NetworkEntityMap();
            DemoComponentRegistry.Register(World);
            // Ensure Combat events are registered (Fix for CombatSystemTests)
            World.RegisterEvent<FireInteractionEvent>();

            var accumulator = new EventAccumulator();
            Kernel = new ModuleHostKernel(World, accumulator);
            var eventBus = new FdpEventBus(); // Shared event bus
            EventBus = eventBus;
            
            // --- 1. Network & Topology ---
            if (enableNetwork)
            {
                participant = new DdsParticipant(domainId: 0);
            }
            // Force simple ID mapping for Demo/Test to ensure uniqueness in shared process
            nodeMapper = new NodeIdMapper(localDomain: 0, localInstance: instanceId);
            // Use Mapper to get consistent internal IDs (Local matches 1, Peers get 2, 3...)
            localInternalId = nodeMapper.GetOrRegisterInternalId(new ModuleHost.Network.Cyclone.Topics.NetworkAppId { AppDomainId = 0, AppInstanceId = instanceId });
            
            INetworkIdAllocator? idAllocator = null;
            if (enableNetwork && participant != null)
            {
                idAllocator = new DdsIdAllocator(participant, $"Node_{instanceId}");
            }
            
            var peerInstances = new int[] { 100, 200 }.Where(x => x != instanceId).ToArray();
            var peerInternalIds = peerInstances.Select(p => nodeMapper.GetOrRegisterInternalId(new ModuleHost.Network.Cyclone.Topics.NetworkAppId { AppDomainId = 0, AppInstanceId = p })).ToArray();
            var topology = new StaticNetworkTopology(localNodeId: localInternalId, peerInternalIds);

            // --- 2. TKB & Serialization ---
            tkb = new TkbDatabase();
            World.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(tkb);
            
            // Configuration
            TankTemplate.Register(tkb);
            
            // var setup = new DemoTkbSetup();
            // setup.Load(tkb); // CONFLICT: Type 100 already registered by TankTemplate

            // --- 3. Modules Registration ---

            // Hoist WGS84 for Shared Use
            var wgs84 = new WGS84Transform();
            wgs84.SetOrigin(52.52, 13.405, 0);

            // A. Infrastructure (Toolkit)
            // ELM timeout must exceed the NetworkGateway reliable-init timeout (300 frames)
            // so that the Gateway's fallback ACK is always processed before ELM destroys the entity.
            // In test mode, use 50 frames to allow the gateway fallback (30 + margin)
            int lifecycleTimeout = testMode ? 50 : (ModuleHost.Core.Network.NetworkConstants.RELIABLE_INIT_TIMEOUT_FRAMES * 2 + 50);
            var elm = new EntityLifecycleModule(tkb, Array.Empty<int>(),
                        timeoutFrames: lifecycleTimeout); 
            Kernel.RegisterModule(elm);

            ReplicationLogicModule? replicationLogicModule = null;
            if (!isReplay)
            {
                replicationLogicModule = new ReplicationLogicModule(EntityMap, tkb, elm);
                Kernel.RegisterModule(replicationLogicModule);
            }

            // Register NetworkSpawningSystem from FDP.Toolkit.NetworkSpawning.
            // Must be after ELM and before modules that publish SpawnEntityCommand events.
            if (!isReplay)
            {
                INetworkIdAllocator effectiveAllocator = idAllocator ?? new SequentialIdAllocator();
                var spawningSystem = new NetworkSpawningSystem(tkb, elm, EntityMap, effectiveAllocator, localInternalId);
                Kernel.RegisterModule(new SpawningModule(spawningSystem));
            }

            // B. Network (Cyclone)
            if (enableNetwork && participant != null && idAllocator != null)
            {
                var allTranslators = new List<Fdp.Interfaces.IDescriptorTranslator>();
                
                // 1. Geodetic
                allTranslators.Add(new Fdp.Examples.NetworkDemo.Translators.FastGeodeticTranslator(participant, wgs84, EntityMap));
                
                // 1.1 Ownership
                allTranslators.Add(new Fdp.Examples.NetworkDemo.Translators.OwnershipUpdateTranslator(nodeMapper, participant));
                
                // Fire Event
                allTranslators.Add(new Fdp.Examples.NetworkDemo.Translators.FireEventTranslator(participant, EntityMap));

                // EntityMaster – cross-node entity announcement (spawn/despawn replication)
                if (!isReplay && replicationLogicModule != null)
                {
                    var masterEgress = new Fdp.Examples.NetworkDemo.Translators.EntityMasterEgressTranslator(participant, nodeMapper, localInternalId);
                    allTranslators.Add(masterEgress);
                    allTranslators.Add(new Fdp.Examples.NetworkDemo.Translators.EntityMasterIngressTranslator(participant, EntityMap, replicationLogicModule.GhostCreationSystem, nodeMapper, localInternalId));
                    // Register the cleanup system that watches locally-owned entities and
                    // publishes DDS dispose signals when they are destroyed.
                    Kernel.RegisterModule(new NetworkCleanupModule(masterEgress));
                }

                // 2. Auto-generated
                var ghostSystemForAuto = replicationLogicModule?.GhostCreationSystem ?? new GhostCreationSystem(EntityMap);
                var (autoTranslators, _) = ReplicationBootstrap.CreateAutoTranslators(
                    participant,
                    typeof(NetworkDemoApp).Assembly,
                    EntityMap,
                    ghostSystemForAuto
                );
                allTranslators.AddRange(autoTranslators);

                var networkModule = new CycloneNetworkModule(
                    participant, nodeMapper, idAllocator, topology, elm,
                    customTranslators: allTranslators,
                    sharedEntityMap: EntityMap,
                    reliableInitTimeoutFrames: testMode ? 30 : -1  // Use 30 frame timeout in testMode instead of 300
                );
                Kernel.RegisterModule(networkModule);
            }
            
            // C. Application Modules
            if (!isReplay)
            {
                // Input
                IInputSource inputSource;
                try {
                    inputSource = Console.IsInputRedirected ? new NullInputSource() : new ConsoleInputSource();
                } catch {
                    inputSource = new NullInputSource();
                }
                
                Kernel.RegisterModule(new GameInputModule(inputSource, eventBus, localInternalId));
                Kernel.RegisterModule(new GameLogicModule(localInternalId, eventBus));
                Kernel.RegisterModule(new RadarModule(eventBus));
                
                // Recorder
                recorder = new AsyncRecorder(recordingPath);
                Kernel.RegisterModule(new RecordingModule(recorder, World));
            }
            else
            {
                // Replay Mode: Just smoothing, no recording
                Kernel.RegisterModule(new RecordingModule(null!, World));
            }

            // D. Bridge (Control <-> Simulation)
            replaySystem = null;

            if (isReplay)
            {
                string metaPath = recordingPath + ".meta";
                Fdp.Examples.NetworkDemo.Configuration.RecordingMetadata meta;
                try 
                {
                   meta = MetadataManager.Load(metaPath);
                   FdpLog<NetworkDemoApp>.Info(
                       "[Replay] Loaded metadata (MaxID: {0})",
                       meta.MaxEntityId);
                } 
                catch (Exception ex)
                {
                    FdpLog<NetworkDemoApp>.Warn(
                        "[Replay] Metadata load failed ({0}). Using default range.",
                        ex.Message);
                    meta = new Fdp.Examples.NetworkDemo.Configuration.RecordingMetadata { MaxEntityId = 1_000_000 };
                }

                World.ReserveIdRange((int)meta.MaxEntityId);
                
                replaySystem = new ReplayBridgeSystem(recordingPath, -1);
                replaySystem.RegisterDynamicType<SquadChat>(DemoDescriptors.SquadChat);
                
                FdpLog<NetworkDemoApp>.Info("[Mode] REPLAY - Playback active (Physics Disabled)");
            }
            else
            {
                World.ReserveIdRange(FdpConfig.SYSTEM_ID_RANGE);
                FdpLog<NetworkDemoApp>.Info(
                    "[Init] Reserved ID range 0-{0}",
                    FdpConfig.SYSTEM_ID_RANGE);
            }

            Kernel.RegisterModule(new BridgeModule(eventBus, replaySystem, localInternalId, instanceId == 100));

            // Time Controller setup
            ModuleHost.Core.Time.ITimeController timeController;
            if (isReplay)
            {
                timeController = new SteppingTimeController(new GlobalTime { TimeScale = 1 });
            }
            else
            {
                timeController = new MasterTimeController(eventBus, null);

                var timeConfig = new FDP.Toolkit.Time.Controllers.TimeControllerConfig { LocalNodeId = localInternalId };
                timeConfig.SyncConfig.PauseBarrierFrames = 10;

                if (instanceId == 100)
                {
                     var slaveSet = new System.Collections.Generic.HashSet<int>(peerInternalIds);
                     _timeCoordinator = new FDP.Toolkit.Time.Controllers.DistributedTimeCoordinator(
                        eventBus, Kernel, timeConfig, slaveSet);
                }
                else
                {
                     _slaveListener = new FDP.Toolkit.Time.Controllers.SlaveTimeModeListener(eventBus, Kernel, timeConfig);
                }
            }
            Kernel.SetTimeController(timeController);

            // --- 5. Initialization ---

            Kernel.Initialize();
            
            if (!isReplay)
            {
                 var entity = World.CreateEntity();
                 World.AddComponent(entity, new NetworkIdentity { Value = 999 });
                 World.AddComponent(entity, new FDP.Toolkit.Replication.Components.NetworkAuthority { 
                     PrimaryOwnerId = nodeMapper.GetOrRegisterInternalId(new ModuleHost.Network.Cyclone.Topics.NetworkAppId { AppDomainId = 0, AppInstanceId = 100 }), 
                     LocalNodeId = localInternalId 
                 });
                 World.AddComponent(entity, new Fdp.Examples.NetworkDemo.Components.TimeModeComponent());
                 EntityMap.Register(999, entity);

                 if (instanceId == 100)
                 {
                     World.SetAuthority<Fdp.Examples.NetworkDemo.Components.TimeModeComponent>(entity, true);
                 }

                 FdpLog<NetworkDemoApp>.Info(
                     "[INIT] Time Sync Entity Created (ID 999) [Local:{0}]",
                     localInternalId);
            }
            
            FdpLog<NetworkDemoApp>.Info("[INIT] Kernel initialized");
            FdpLog<NetworkDemoApp>.Info("[INIT] Waiting for peer discovery...");
            
            // Simple delay for discovery
            int delay = testMode ? 10 : (enableNetwork ? 200 : 10);
            await Task.Delay(delay); // Allow DDS to settle
            
            if (!isReplay)
            {
                 if (autoSpawn)
                 {
                     SpawnLocalEntities(World, tkb, instanceId, localInternalId);
                     FdpLog<NetworkDemoApp>.Info(
                         "[SPAWN] Auto-spawn queued for {0} (processed next frame)",
                         instanceId);
                 }
                 else
                 {
                     FdpLog<NetworkDemoApp>.Info("[SPAWN] Auto-spawn disabled for testing/demo control");
                 }
            }
            } // End ScopeContext
        }

        public async Task RunLoopAsync(System.Threading.CancellationToken token)
        {
             using (ScopeContext.PushProperty("NodeId", instanceId))
             {
                int frameCount = 0;
                while (!token.IsCancellationRequested)
                {
                    Update(0.1f);
                    if (frameCount % 60 == 0) PrintStatus();
                    try { await Task.Delay(33, token); } catch (TaskCanceledException) { break; }
                    frameCount++;
                }
             }
        }

        public void Update(float dt)
        {
            // Time Coordination Update
            _timeCoordinator?.Update();
            _slaveListener?.Update();

            // Process queued actions on the simulation thread
            while (_actionQueue.TryDequeue(out var action))
            {
                try 
                {
                    FdpLog<NetworkDemoApp>.Info("Executing queued action...");
                    action(World);
                    FdpLog<NetworkDemoApp>.Info("Queued action executed.");
                }
                catch (Exception ex)
                {
                    FdpLog<NetworkDemoApp>.Error("Error executing queued action: {0}", ex);
                }
            }

            if (isReplay)
            {
                World.Tick(); // CRITICAL: Advance global version in Replay
            }
            _timeCoordinator?.Update();
            _slaveListener?.Update();
            // Use Kernel.Update() to let the TimeController (Master/Stepped/Slave) drive the simulation time.
            // Using Kernel.Update(dt) bypasses the controller logic (breaking Stepped mode).
            Kernel.Update(); 
            // Kernel.Update(dt); 
            // Ensure events flow
            EventBus.SwapBuffers();
        }

        public void Stop()
        {
             Dispose();
        }

        public void Dispose()
        {
            if (!isReplay && recorder != null)
            {
                // Wait for async writes to complete
                if (!_testMode)
                {
                    System.Threading.Thread.Sleep(2000);
                }
                
                recorder.Dispose();
                var meta = new Fdp.Examples.NetworkDemo.Configuration.RecordingMetadata {
                    MaxEntityId = World.MaxEntityIndex,
                    Timestamp = DateTime.UtcNow,
                    NodeId = instanceId
                };
                try
                {
                    MetadataManager.Save(recordingPath + ".meta", meta);
                    FdpLog<NetworkDemoApp>.Info(
                        "[Recorder] Saved metadata to {0}.meta",
                        recordingPath);
                } 
                catch (Exception ex)
                {
                    FdpLog<NetworkDemoApp>.Error(
                        "[Recorder] Failed to save metadata: {0}",
                        ex.Message);
                }
            }
            
            participant?.Dispose();
            replaySystem?.Dispose();
            FdpLog<NetworkDemoApp>.Info("[SHUTDOWN] Done.");
        }
        
        public void PrintStatus()
        {
            PrintStatus(World, nodeMapper, localInternalId);
        }



        // Helper: SpawnLocalEntities
        // Publishes a SpawnEntityCommand to the world event bus.
        // NetworkSpawningSystem (registered in SpawningModule) processes the command
        // the next time the kernel ticks, handling all ECS/ELM setup internally.
        private static void SpawnLocalEntities(
            EntityRepository world,
            TkbDatabase tkb,
            int instanceId,
            int localInternalId)
        {
            if (!tkb.TryGetByName("CommandTank", out var template))
            {
                FdpLog<NetworkDemoApp>.Warn("[SpawnLocal] CommandTank template not found.");
                return;
            }

            // Deterministic non-zero NetworkId avoids the allocator and keeps
            // test IDs stable across runs (1 tank per node).
            long netId = (long)instanceId * 1000;

            world.Bus.PublishManaged(new SpawnEntityCommand
            {
                NetworkId         = netId,
                TkbType           = template.TkbType,
                DisType           = 1,
                OwnerNodeId       = localInternalId,
                InitType          = ModuleHost.Core.Network.Interfaces.ReliableInitType.AllPeers,
                InitialComponents = new System.Collections.Generic.List<object>
                {
                    new SimTransform
                    {
                        // Deterministic starting positions ensure position-length tests
                        // (e.g. DistributedReplayTests) reliably reach > 10 units after
                        // a fixed number of move steps regardless of node ordering.
                        Position = instanceId == 100
                            ? new Vector3(1f, 0f, 0f)
                            : new Vector3(0f, 1f, 0f)
                    },
                    new NetworkTransform(),
                    new EntityType { Name = "Tank", TypeId = 1 }
                }
            });

            FdpLog<NetworkDemoApp>.Info(
                "[SPAWN] Published SpawnEntityCommand for TkbType={0}, NetworkId={1}",
                template.TkbType,
                netId);
        }

        // Helper: PrintStatus
        static void PrintStatus(EntityRepository world, NodeIdMapper mapper, int localInstanceId)
        {
            var query = world.Query()
                .With<NetworkIdentity>()
                .With<FDP.Toolkit.Replication.Components.NetworkAuthority>() // Use Authority
                .Build();

            FdpLog<NetworkDemoApp>.Info("[STATUS] Frame snapshot:");
            
            int localCount = 0;
            int remoteCount = 0;
            
            foreach (var e in query)
            {
                 ref readonly var netId = ref world.GetComponentRO<NetworkIdentity>(e);
                 ref readonly var auth = ref world.GetComponentRO<FDP.Toolkit.Replication.Components.NetworkAuthority>(e);
                 
                 string ownershipInfo = "No Ownership";
                 if (world.HasComponent<ModuleHost.Core.Network.NetworkOwnership>(e))
                 {
                     ref readonly var own = ref world.GetComponentRO<ModuleHost.Core.Network.NetworkOwnership>(e);
                     ownershipInfo = string.Concat("Own(P:", own.PrimaryOwnerId, " L:", own.LocalNodeId, ")");
                 }

                 string typeName = "Unknown";
                 if (world.HasComponent<EntityType>(e)) typeName = world.GetComponent<EntityType>(e).Name;
                 
                 Vector3 pos = Vector3.Zero;
                 if (world.HasComponent<SimTransform>(e)) pos = world.GetComponent<SimTransform>(e).Position;
                 else if (world.HasComponent<NetworkTransform>(e)) pos = world.GetComponent<NetworkTransform>(e).LastPosition;

                 bool isLocal = auth.PrimaryOwnerId == localInstanceId;
                 string ownerStr = isLocal ? "LOCAL" : string.Concat("REMOTE(", auth.PrimaryOwnerId, ")");
                 
                 if (isLocal) localCount++; else remoteCount++;

                 var typeLabel = string.Format("{0,-12}", typeName);
                 var posLabel = string.Format("Pos: ({0:F1}, {1:F1}, {2:F1})", pos.X, pos.Y, pos.Z);
                 var line = string.Concat("  [", ownerStr, "] ", typeLabel, " ", posLabel, " NetID: ", netId.Value, " ", ownershipInfo);
                 FdpLog<NetworkDemoApp>.Info(line);
            }
            
            FdpLog<NetworkDemoApp>.Info(
                "[STATUS] Local: {0}, Remote: {1}",
                localCount,
                remoteCount);
        }

        /// <summary>
        /// Fallback ID allocator used when no DDS-backed allocator is available
        /// (e.g. when NetworkDemo is started without networking enabled).
        /// Entities published via SpawnEntityCommand with NetworkId = 0 will receive
        /// monotonically increasing IDs from this allocator.
        /// </summary>
        private sealed class SequentialIdAllocator : ModuleHost.Core.Network.Interfaces.INetworkIdAllocator
        {
            private long _nextId = 1;
            public long AllocateId() => System.Threading.Interlocked.Increment(ref _nextId);
            public void Reset(long startId = 0) => System.Threading.Interlocked.Exchange(ref _nextId, startId);
            public void Dispose() { }
        }
    }
}