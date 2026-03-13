using Bagira.BDC.SSTD;
using Bagira.DDS.DM;
using Bagira.IG.Components;
using Bagira.Map.Common;
using Bagira.Map.Common.Replication;
using Bagira.Map.Common.Replication.Egress;
using Bagira.Map.Common.Replication.Ingress;
using Bagira.Map.Common.Systems;
using Bagira.Map.Definitions.Tkb;
using Bagira.Runner.Abstractions;
using Bagira.Runner.Models;
using Bagira.SimHost;
using Bagira.SimHost.Brains;
using Bagira.SimHost.Components;
using Bagira.SimHost.Configuration;
using Bagira.SimHost.Modules;
using Bagira.SimHost.Systems;
using Bagira.SimHost.Utilities;
using CarKinem.Commands;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Components;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Tkb;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Lifecycle.Events;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using FDP.Toolkit.Time.Controllers;
using ModuleHost.Core;
using ModuleHost.Core.Network;
using ModuleHost.Core.Network.Interfaces;
using ModuleHost.Core.Time;
using ModuleHost.Network.Cyclone.Modules;
using ModuleHost.Network.Cyclone.Services;
using ModuleHost.Network.Cyclone.Systems;
using ModuleHost.Network.Cyclone.Translators;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using FDP.Toolkit.Vis2D.Components;
using IDescriptorTranslator = Fdp.Interfaces.IDescriptorTranslator;
using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;

namespace Bagira.Runner.Services
{
    /// <summary>
    /// <see cref="ISubsystem"/> implementation that embeds the SimHost simulation kernel.
    ///
    /// <para>Lifecycle:
    /// <list type="number">
    ///   <item><see cref="Initialize"/> — creates ECS world, kernel, modules, DDS participant.</item>
    ///   <item><see cref="Update"/> — ticks kernel + simulation-logic group (no rendering).</item>
    ///   <item><see cref="DrawWorld"/> — no-op (SimHost has no 3-D world visuals).</item>
    ///   <item><see cref="DrawUI"/> — renders ImGui control panels when not headless.</item>
    ///   <item><see cref="Shutdown"/> — disposes all managed resources.</item>
    /// </list>
    /// </para>
    /// <para>
    /// For standalone use outside the orchestrator, call <see cref="Start"/> after
    /// <see cref="Initialize"/> to spin up a background simulation thread, then
    /// <see cref="Stop"/> to gracefully shut it down.
    /// </para>
    /// </summary>
    public sealed class SimHostSubsystem : ISubsystem, IMapCameraProvider
    {
        // ── Subsystem identity ────────────────────────────────────────────────

        /// <inheritdoc/>
        public string Name => "SimHost";

        // ── Runtime objects ───────────────────────────────────────────────────

        private EntityRepository?       _world;
        private ModuleHostKernel?       _kernel;
        private SystemGroup?            _kernelGroup;
        private DdsIdAllocator?         _idAllocator;
        private DdsIdAllocatorServer?   _idAllocatorServer;
        private CancellationTokenSource? _idAllocatorServerCts;
        private Thread?                  _idAllocatorServerThread;
        private FdpEventBus?            _eventBus;
        private NetworkEntityMap?       _entityMap;
        private IGeographicTransform?   _geoTransform;
        private bool                    _headless;
        private bool                    _initialized;

        // ── Visualization (non-headless only) ─────────────────────────────────
        private SimHostVisualization?   _vis;
        private SimulationLogicModule?  _simLogicModule;

        // ── Background loop (standalone mode) ────────────────────────────────

        private CancellationTokenSource? _cts;
        private Thread?                  _loopThread;

        // ── Public ECS access ─────────────────────────────────────────────────

        /// <summary>
        /// Provides access to the ECS <see cref="EntityRepository"/> after
        /// <see cref="Initialize"/> has been called.  Returns <see langword="null"/>
        /// when the subsystem has not yet been initialised.
        /// </summary>
        public EntityRepository? World => _world;

        /// <inheritdoc/>
        public MapCamera? GetMapCamera() => _vis?.GetMapCamera();

        /// <summary>
        /// TestHook: exposes the NetworkEntityMap for integration test assertions.
        /// </summary>
        internal NetworkEntityMap TestHook_EntityMap => _entityMap
            ?? throw new InvalidOperationException("SimHostSubsystem is not initialized.");

        /// <summary>
        /// TestHook: spawns an entity via the network spawning pipeline and returns its network ID.
        /// </summary>
        internal long TestHook_SpawnEntity(long tkbType, GeoPosition position)
        {
            if (_world == null || _idAllocator == null || _entityMap == null)
                throw new InvalidOperationException("SimHostSubsystem is not initialized.");

            long networkId = _idAllocator.AllocateId();

            var initialComponents = new List<object>();
            if (_geoTransform != null)
            {
                var cart = _geoTransform.ToCartesian(position.Latitude, position.Longitude, position.Altitude);
                var cartPos = new Vector3((float)cart.X, (float)cart.Y, (float)cart.Z);
                initialComponents.Add(new SimTransform
                {
                    Position = cartPos,
                    Rotation = Quaternion.Identity
                });
            }

            _world.Bus.PublishManaged(new SpawnEntityCommand
            {
                NetworkId = networkId,
                TkbType = tkbType,
                DisType = 0,
                OwnerNodeId = 1,
                InitType = ReliableInitType.AllPeers,
                InitialComponents = initialComponents,
                RequestId = Guid.Empty
            });

            return networkId;
        }

        /// <summary>
        /// TestHook: simulates a SimHost-side entity drag by teleporting the entity to
        /// <paramref name="worldPos"/>. <c>GetComponentRW&lt;SimTransform&gt;</c> stamps
        /// <c>EntityHeader.LastChangeTick</c>, which <c>SmartEgressUtil.ShouldPublish</c>
        /// detects on the next egress pass so GeoSpatial is published immediately without
        /// requiring an explicit <c>MarkDirty</c> call.
        /// </summary>
        internal void TestHook_SimulateDrag(long networkId, Vector2 worldPos)
        {
            if (_world == null || _entityMap == null)
                throw new InvalidOperationException("SimHostSubsystem is not initialized.");

            if (!_entityMap.TryGetEntity(networkId, out var entity))
                throw new InvalidOperationException($"Entity with networkId={networkId} not found in entity map.");

            if (!_world.IsAlive(entity) || !_world.HasComponent<SimTransform>(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive or has no SimTransform.");

            ref var tf = ref _world.GetComponentRW<SimTransform>(entity);
            tf.Position = new Vector3(worldPos.X, worldPos.Y, 0f);
            // GetComponentRW stamps EntityHeader.LastChangeTick; SmartEgressUtil.ShouldPublish
            // detects the change and publishes immediately without an explicit MarkDirty call.
        }

        /// <summary>
        /// TestHook: directly assigns the WanderMilitary BTree doctrine to the entity without
        /// going through the DDS <c>MissionControlRequest</c> round-trip.
        ///
        /// <para>Purpose: removes DDS cold-start timing sensitivity from integration tests that
        /// only want to verify GeoSpatial egress update frequency for moving entities.
        /// The entity must already exist in the SimHost world (spawned via
        /// <see cref="TestHook_SpawnEntity"/>).</para>
        /// </summary>
        internal void TestHook_AssignWanderMission(long networkId)
        {
            if (_world == null || _entityMap == null)
                throw new InvalidOperationException("SimHostSubsystem is not initialized.");

            if (!_entityMap.TryGetEntity(networkId, out var entity))
                throw new InvalidOperationException($"Entity with networkId={networkId} not found in entity map.");

            if (!_world.IsAlive(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive.");

            // Step 1: Update MissionPlanQueue with a single-phase WanderMilitary plan.
            // This mirrors what MissionControlRequestSystem does, but without DDS.
            var newPhase = new MissionPhase
            {
                DoctrineId   = SimHostDoctrineIds.WanderMilitary_BT,
                Trigger      = FDP.Toolkit.Behavior.Components.MissionTrigger.TimerElapsed,
                TriggerParam = float.MaxValue, // holds forever — never triggers transition
            };

            if (_world.HasComponent<MissionPlanQueue>(entity))
            {
                // Read-modify-write pattern: avoid mutating an inline-array field via
                // a direct ref from GetComponentRW because of the InlineArray defensive-copy trap.
                var queue = _world.GetComponent<MissionPlanQueue>(entity); // copy
                queue.CurrentPhase        = 0;
                queue.PhaseElapsedSeconds = 0f;
                queue.PhaseCount          = 1;
                Span<MissionPhase> phases = queue.Phases; // safe, in-place access
                phases[0] = newPhase;
                _world.SetComponent(entity, queue);
            }
            else
            {
                var queue = new MissionPlanQueue
                {
                    CurrentPhase        = 0,
                    PhaseElapsedSeconds = 0f,
                    PhaseCount          = 1,
                };
                // Mutate the local copy's inline buffer then add it to the world.
                Span<MissionPhase> phases = queue.Phases;
                phases[0] = newPhase;
                _world.AddComponent(entity, queue);
            }

            // Step 2: Directly activate the doctrine on DoctrineState so the entity
            // starts moving immediately on the next CarKinematics tick, without waiting
            // for MissionDirectorSystem (which requires a correctly-updated MissionPlanQueue).
            // This is the same operation MissionDirectorSystem would perform on the next tick.
            if (_world.HasComponent<DoctrineState>(entity))
            {
                ref var doctrine = ref _world.GetComponentRW<DoctrineState>(entity);
                unchecked { doctrine.InstanceId++; }
                doctrine.ActiveDoctrineHash = SimHostDoctrineIds.WanderMilitary_BT;
            }
        }

        /// <summary>
        /// TestHook: returns the current <see cref="SimTransform"/> of the entity with the given
        /// network ID, or <c>default</c> if not found / no component.
        /// </summary>
        internal SimTransform TestHook_GetSimTransform(long networkId)
        {
            if (_world == null || _entityMap == null) return default;
            if (!_entityMap.TryGetEntity(networkId, out var entity)) return default;
            if (!_world.IsAlive(entity) || !_world.HasComponent<SimTransform>(entity)) return default;
            return _world.GetComponent<SimTransform>(entity);
        }

        /// <summary>
        /// TestHook: returns the current <see cref="DoctrineState"/> of the entity with the given
        /// network ID, or <c>default</c> if not found / no component.
        /// </summary>
        internal DoctrineState TestHook_GetDoctrineState(long networkId)
        {
            if (_world == null || _entityMap == null) return default;
            if (!_entityMap.TryGetEntity(networkId, out var entity)) return default;
            if (!_world.IsAlive(entity) || !_world.HasComponent<DoctrineState>(entity)) return default;
            return _world.GetComponent<DoctrineState>(entity);
        }

        /// <summary>
        /// TestHook: returns <c>true</c> if the entity has <see cref="MissionPlanQueue"/> component.
        /// </summary>
        internal bool TestHook_HasMissionPlanQueue(long networkId)
        {
            if (_world == null || _entityMap == null) return false;
            if (!_entityMap.TryGetEntity(networkId, out var entity)) return false;
            return _world.IsAlive(entity) && _world.HasComponent<MissionPlanQueue>(entity);
        }

        /// <summary>
        /// TestHook: returns the current <see cref="MissionPlanQueue"/> of the entity, or default.
        /// </summary>
        internal MissionPlanQueue TestHook_GetMissionPlanQueue(long networkId)
        {
            if (_world == null || _entityMap == null) return default;
            if (!_entityMap.TryGetEntity(networkId, out var entity)) return default;
            if (!_world.IsAlive(entity) || !_world.HasComponent<MissionPlanQueue>(entity)) return default;
            return _world.GetComponent<MissionPlanQueue>(entity);
        }

        /// <summary>
        /// TestHook: directly activates the WanderMilitary doctrine on the entity's
        /// <see cref="DoctrineState"/> without going through <see cref="MissionDirectorSystem"/>.
        /// Use this to verify whether the BTree / CarKinem pipeline works when doctrine
        /// is forced on, independent of MissionDirectorSystem correctness.
        /// </summary>
        internal void TestHook_ForceDoctrineActive(long networkId)
        {
            if (_world == null || _entityMap == null)
                throw new InvalidOperationException("SimHostSubsystem is not initialized.");

            if (!_entityMap.TryGetEntity(networkId, out var entity))
                throw new InvalidOperationException($"Entity with networkId={networkId} not found.");

            if (!_world.IsAlive(entity))
                throw new InvalidOperationException($"Entity {entity} is not alive.");

            if (_world.HasComponent<DoctrineState>(entity))
            {
                ref var doctrine = ref _world.GetComponentRW<DoctrineState>(entity);
                unchecked { doctrine.InstanceId++; }
                doctrine.ActiveDoctrineHash = SimHostDoctrineIds.WanderMilitary_BT;
            }
        }

        /// <summary>
        /// TestHook: returns child entities that reference the given parent via <see cref="PartMetadata"/>.
        /// </summary>
        internal List<Entity> TestHook_GetChildEntities(Entity parentEntity)
        {
            if (_world == null)
                throw new InvalidOperationException("SimHostSubsystem is not initialized.");

            var children = new List<Entity>();
            var query = _world.Query().With<PartMetadata>().Build();
            foreach (var entity in query)
            {
                var meta = _world.GetComponent<PartMetadata>(entity);
                if (meta.ParentEntity == parentEntity)
                    children.Add(entity);
            }

            return children;
        }

        // ── ISubsystem ────────────────────────────────────────────────────────

        /// <summary>
        /// Creates the ECS world, registers all SimHost modules, and connects to DDS.
        /// Mirrors the initialisation sequence from <c>Bagira.SimHost/Program.cs</c>.
        /// </summary>
        public void Initialize(SubsystemConfig config)
        {
            _headless = config.Headless;

            Logger.Info("[SimHost] Initializing...");

            // Load JSON config (generates defaults if missing).
            var simConfig = SimHostConfig.Load("config.json");

            var domainId = config.DomainId > 0 ? config.DomainId : simConfig.DomainId;
            Logger.Info($"[SimHost] Domain ID:       {domainId}");
            Logger.Info($"[SimHost] Simulation Rate: {simConfig.SimulationRateHz} Hz");

            // ── 1. Kernel ─────────────────────────────────────────────────────
            _world = new EntityRepository();
            RegisterSimComponents(_world);
            var eventAccumulator = new EventAccumulator();
            _kernel = new ModuleHostKernel(_world, eventAccumulator);

            _eventBus    = new FdpEventBus();
            var timeConfig  = new TimeControllerConfig { Mode = TimeMode.Continuous, Role = TimeRole.Master };
            var timeCtrl    = TimeControllerFactory.Create(_eventBus, timeConfig);
            timeCtrl.SetTimeScale(1.0f);
            _kernel.SetTimeController(timeCtrl);
            _eventBus.SwapBuffers();

            // ── 2. Data services ──────────────────────────────────────────────
            var ddsParticipant = new DdsParticipant((uint)domainId);
            var tkbDb          = BagiraEnvironment.CreateTkb();
            var entityMap      = new NetworkEntityMap();
            _entityMap = entityMap;

            // ── ID-allocator server: start as early as possible on its own thread ──
            // The server must be running (and DDS-matched) before the client sends its
            // first request.  Starting it here — before the client is even created —
            // ensures the DDS pub/sub match completes in the background while the rest
            // of initialisation proceeds.
            _idAllocatorServer     = new DdsIdAllocatorServer(ddsParticipant);
            _idAllocatorServerCts  = new CancellationTokenSource();
            _idAllocatorServerThread = new Thread(() => RunIdAllocatorServerLoop(_idAllocatorServerCts.Token))
            {
                IsBackground = true,
                Name         = "SimHost-IdAllocServer"
            };
            _idAllocatorServerThread.Start();

            // Client is created AFTER the server thread is running.  DdsIdAllocator will
            // wait for the PublicationMatched event (server reader matched) before sending
            // the first request, so there is no "write-before-match" race.
            _idAllocator = new DdsIdAllocator(ddsParticipant, "SimHostAllocator");

            // ── 3. Geodetic configuration ─────────────────────────────────────
            // Use the shared factory so that SimHostSubsystem, SimHostApp and IgApplication
            // all agree on the same reference origin (Berlin by default).
            var wgs84 = BagiraEnvironment.CreateGeoTransform();
            _geoTransform = wgs84;

            // ── 3a. JSON Attribute Compiler ───────────────────────────────────
            // Shared by CreateEntityRequestSystem (entity creation path) and
            // UpdateEntityAttributeRequestSystem (live attribute update path).
            var jsonAttributeCompiler = AttributeCompilerFactory.Build(wgs84);

            // ── 4. Doctrine registry ──────────────────────────────────────────
            var doctrineRegistry = new DoctrineRegistry();
            doctrineRegistry.Register(SimHostDoctrineIds.MoveTo_BT, "MoveToLocation",
                new DoctrineDefinition { Name = "MoveToLocation", BrainTier = BehaviorConstants.BrainTierBTree });
            doctrineRegistry.Register(SimHostDoctrineIds.FollowRoute_BT, "FollowRoute",
                new DoctrineDefinition { Name = "FollowRoute",   BrainTier = BehaviorConstants.BrainTierBTree });
            doctrineRegistry.Register(SimHostDoctrineIds.JoinFormation_BT, "JoinFormation",
                new DoctrineDefinition { Name = "JoinFormation", BrainTier = BehaviorConstants.BrainTierBTree });
            doctrineRegistry.Register(SimHostDoctrineIds.Idle_HSM, "Idle",
                new DoctrineDefinition { Name = "Idle",          BrainTier = BehaviorConstants.BrainTierHsm });
            doctrineRegistry.Register(SimHostDoctrineIds.WanderMilitary_BT, "WanderMilitary",
                new DoctrineDefinition
                {
                    Name             = "WanderMilitary",
                    BrainTier        = BehaviorConstants.BrainTierBTree,
                    BTreeInterpreter = SimHostNodes.BuildWanderMilitaryInterpreter(),
                });

            // ── 5. SimulationLogicModule ──────────────────────────────────────
            // Load road network from file so the visualizer can show roads.
            var roadNetwork = new RoadNetworkBlob();
            try { roadNetwork = RoadNetworkLoader.LoadFromJson("Assets/sample_road.json"); }
            catch { /* run fine without roads */ }

            _simLogicModule = new SimulationLogicModule(
                doctrineRegistry,
                entityMap,
                vehicleAPI:  null,
                roadNetwork: roadNetwork);

            _kernelGroup = new SystemGroup();
            _kernelGroup.Create(_world);
            _kernelGroup.AddSystem(new MissionControlRequestSystem(ddsParticipant, entityMap, doctrineRegistry));
            _kernelGroup.AddSystem(new UpdateEntityDescriptorRequestSystem(ddsParticipant, entityMap, wgs84));
            _kernelGroup.AddSystem(new UpdateEntityAttributeRequestSystem(ddsParticipant, entityMap, wgs84, jsonAttributeCompiler));
            _simLogicModule.RegisterSystems(_kernelGroup, _kernelGroup, _kernelGroup);

            // Seed GlobalTime singleton.
            _world.SetSingletonUnmanaged(new GlobalTime
            {
                DeltaTime = 1.0f / simConfig.SimulationRateHz,
                TimeScale = 1.0f
            });

            // ── 6. Toolkit modules ────────────────────────────────────────────
            var geoModule = new GeographicModule(wgs84);
            _kernel.RegisterModule(geoModule);

            var elm = new EntityLifecycleModule(tkbDb, new List<int>());
            _kernel.RegisterModule(elm);

            var spawningSystem = new NetworkSpawningSystem(
                tkbDb, elm, entityMap, _idAllocator, localNodeId: 1);

            var simHostMod = new SimHostModule(
                ddsParticipant, tkbDb, _idAllocator, 1,
                spawningSystem, entityMap, doctrineRegistry,
                new GhostCreationSystem(entityMap), wgs84, jsonAttributeCompiler);
            _kernel.RegisterModule(simHostMod);
            // ── 7. Network module ─────────────────────────────────────────────
            var localNodeId = 1;
            var translators = new List<IDescriptorTranslator>();
            if (simHostMod.GeoEgressTranslator != null)
                translators.Add(simHostMod.GeoEgressTranslator);
            if (simHostMod.MapOverlayEgressTranslator != null)
                translators.Add(simHostMod.MapOverlayEgressTranslator);
            translators.Add(simHostMod.MissionIngressTranslator);
            translators.Add(simHostMod.MissionEgressTranslator);
            var entityMasterEgressTranslator = new EntityMasterEgressTranslator(ddsParticipant, entityMap, localNodeId);
            translators.Add(entityMasterEgressTranslator);
            translators.Add(new EntityInfoEgressTranslator(ddsParticipant, entityMap)); // Task 18
            translators.Add(new FireInteractionEventTranslator(ddsParticipant, entityMap));
            translators.Add(new TimePulseEgressTranslator(ddsParticipant, _eventBus));

            _kernel.RegisterGlobalSystem(
                new CycloneNetworkCleanupSystem(entityMasterEgressTranslator));
            _kernel.RegisterGlobalSystem(
                new DisposalMonitoringSystem(entityMap));
            var nodeMapper  = new NodeIdMapper(domainId, localNodeId);
            var topology    = new StaticNetworkTopology(localNodeId, new[] { localNodeId });

            var cycloneModule = new CycloneNetworkModule(
                ddsParticipant, nodeMapper, _idAllocator, topology, elm,
                customTranslators: translators,
                sharedEntityMap:   entityMap);
            _kernel.RegisterModule(cycloneModule);

            // ── 8. Kernel init ────────────────────────────────────────────────
            _kernel.Initialize();

            // ── 9. Visualization (skipped in headless mode) ───────────────────
            if (!_headless)
            {
                _vis = new SimHostVisualization();
                _vis.Initialize(
                    _world,
                    _kernel,
                    _simLogicModule.RoadNetwork,
                    _simLogicModule.TrajectoryPool,
                    _simLogicModule.FormationTemplates);
                Logger.Info("[SimHost] Visualization initialized.");
            }

            _initialized = true;
            Logger.Info("[SimHost] Initialized.");
        }

        /// <summary>
        /// Ticks the kernel and simulation-logic group by <paramref name="deltaTime"/> seconds.
        /// Called each frame by the orchestrator (or each loop iteration in standalone mode).
        /// </summary>
        /// <remarks>
        /// IMPORTANT: <see cref="SystemGroup.Run"/> must execute before <see cref="ModuleHostKernel.Update"/>
        /// so that incoming DDS requests (e.g. UpdateEntityDescriptorRequest) are processed and
        /// <see cref="SmartEgressUtil.MarkDirty"/> is called <em>before</em> the egress translators'
        /// ScanAndPublish pass runs.  Reversing the order causes a one-rolling-window delay (~10 s)
        /// before position changes triggered by IG drag-and-drop are reflected back on the IG.
        /// </remarks>
        public void Update(float deltaTime)
        {
            if (!_initialized) return;
            // Note: _idAllocatorServer runs on its own background thread; no explicit pump needed here.
            _vis?.Update(deltaTime);
            _kernelGroup!.Run();   // process incoming requests first (sets dirty flags)
            _kernel!.Update();     // then run egress scan (picks up dirty → publishes immediately)
            _eventBus?.SwapBuffers();
        }

        /// <summary>Renders the 2-D map canvas (road graph + vehicle entities).</summary>
        public void DrawWorld()
        {
            _vis?.DrawWorld();
        }

        /// <summary>Renders ImGui control panels (spawn, simulation controls, inspector).</summary>
        public void DrawUI()
        {
            _vis?.DrawUI();
        }

        /// <summary>Disposes all kernel resources.</summary>
        public void Shutdown()
        {
            Stop();
            _vis?.Dispose();
            _vis = null;
            // Stop the background allocator-server thread before disposing resources.
            _idAllocatorServerCts?.Cancel();
            _idAllocatorServerThread?.Join(TimeSpan.FromSeconds(2));
            _idAllocatorServerCts?.Dispose();
            _idAllocatorServerCts = null;
            _idAllocatorServerThread = null;
            _idAllocatorServer?.Dispose();
            _idAllocatorServer = null;
            _idAllocator?.Dispose();
            _kernelGroup?.Dispose();
            _initialized = false;
            Logger.Info("[SimHost] Shutdown complete.");
        }

        // ── Standalone helpers ────────────────────────────────────────────────

        /// <summary>
        /// Starts a background simulation thread (~60 Hz).
        /// Use this when running SimHost standalone (outside the orchestrator update loop).
        /// The orchestrator calls <see cref="Update"/> directly and does not use this method.
        /// </summary>
        public void Start()
        {
            if (_cts != null) return; // already running
            _cts        = new CancellationTokenSource();
            _loopThread = new Thread(() => RunLoop(_cts.Token))
            {
                IsBackground = true,
                Name         = "SimHost-Loop"
            };
            _loopThread.Start();
            Logger.Info("[SimHost] Background loop started.");
        }

        /// <summary>
        /// Signals the background simulation thread to stop and waits for it to exit.
        /// Safe to call even when <see cref="Start"/> was never called.
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
            _loopThread?.Join(TimeSpan.FromSeconds(3));
            _cts?.Dispose();
            _cts        = null;
            _loopThread = null;
        }

        // ── Private ───────────────────────────────────────────────────────────

        /// <summary>
        /// Registers all ECS component types and events used by the SimHost simulation.
        /// Delegates to <see cref="SimHostComponentRegistry.RegisterAll"/>.
        /// </summary>
        private static void RegisterSimComponents(EntityRepository world)
            => SimHostComponentRegistry.RegisterAll(world);

        private void RunIdAllocatorServerLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                _idAllocatorServer?.ProcessRequests();
                Thread.Sleep(1); // ~1 kHz polling — fast enough for low-latency allocation
            }
            Logger.Info("[SimHost] IdAllocatorServer loop exited.");
        }

        private void RunLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                Update(0f); // dt managed internally by time controller
                Thread.Sleep(1); // ~1 ms yield; time controller manages dt
            }
            Logger.Info("[SimHost] Background loop exited.");
        }
    }
}
