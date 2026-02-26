using System;
using System.Collections.Generic;
using System.Threading;
using Bagira.Runner.Abstractions;
using Bagira.Runner.Models;
using Bagira.SimHost;
using Bagira.SimHost.Configuration;
using Bagira.SimHost.Modules;
using Bagira.SimHost.Utilities;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Time.Controllers;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Tkb;
using ModuleHost.Core;
using ModuleHost.Core.Network;
using ModuleHost.Core.Time;
using ModuleHost.Network.Cyclone.Modules;
using ModuleHost.Network.Cyclone.Services;
using ModuleHost.Network.Cyclone.Translators;
using CycloneDDS.Runtime;
using Bagira.BDC.SSTD;

using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;
using IDescriptorTranslator = Fdp.Interfaces.IDescriptorTranslator;

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
    public sealed class SimHostSubsystem : ISubsystem
    {
        // ── Subsystem identity ────────────────────────────────────────────────

        /// <inheritdoc/>
        public string Name => "SimHost";

        // ── Runtime objects ───────────────────────────────────────────────────

        private EntityRepository?       _world;
        private ModuleHostKernel?       _kernel;
        private SystemGroup?            _kernelGroup;
        private DdsIdAllocator?         _idAllocator;
        private bool                    _headless;
        private bool                    _initialized;

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
            var eventAccumulator = new EventAccumulator();
            _kernel = new ModuleHostKernel(_world, eventAccumulator);

            var eventBus    = new FdpEventBus();
            var timeConfig  = new TimeControllerConfig { Mode = TimeMode.Continuous, Role = TimeRole.Master };
            var timeCtrl    = TimeControllerFactory.Create(eventBus, timeConfig);
            timeCtrl.SetTimeScale(1.0f);
            _kernel.SetTimeController(timeCtrl);

            // ── 2. Data services ──────────────────────────────────────────────
            var ddsParticipant = new DdsParticipant((uint)domainId);
            var tkbDb          = new TkbDatabase();
            var entityMap      = new NetworkEntityMap();
            _idAllocator       = new DdsIdAllocator(ddsParticipant, "SimHostAllocator");

            // ── 3. Geodetic configuration ─────────────────────────────────────
            var wgs84 = new WGS84Transform();
            wgs84.SetOrigin(
                simConfig.GeodeticOrigin.Latitude,
                simConfig.GeodeticOrigin.Longitude,
                simConfig.GeodeticOrigin.Altitude);

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

            // ── 5. SimulationLogicModule ──────────────────────────────────────
            var simLogicModule = new SimulationLogicModule(
                doctrineRegistry,
                entityMap,
                vehicleAPI: null);

            _kernelGroup = new SystemGroup();
            _kernelGroup.Create(_world);
            simLogicModule.RegisterSystems(_kernelGroup);

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
                spawningSystem, entityMap, wgs84);
            _kernel.RegisterModule(simHostMod);

            // ── 7. Network module ─────────────────────────────────────────────
            var translators = new List<IDescriptorTranslator>();
            if (simHostMod.GeoEgressTranslator != null)
                translators.Add(simHostMod.GeoEgressTranslator);
            translators.Add(simHostMod.MissionIngressTranslator);
            translators.Add(simHostMod.MissionEgressTranslator);
            translators.Add(new AutoCycloneTranslator<EntityMaster>(ddsParticipant, "EntityMaster", 0, entityMap));

            var localNodeId = 1;
            var nodeMapper  = new NodeIdMapper(domainId, localNodeId);
            var topology    = new StaticNetworkTopology(localNodeId, new[] { localNodeId });

            var cycloneModule = new CycloneNetworkModule(
                ddsParticipant, nodeMapper, _idAllocator, topology, elm,
                customTranslators: translators,
                sharedEntityMap:   entityMap);
            _kernel.RegisterModule(cycloneModule);

            // ── 8. Kernel init ────────────────────────────────────────────────
            _kernel.Initialize();

            _initialized = true;
            Logger.Info("[SimHost] Initialized.");
        }

        /// <summary>
        /// Ticks the kernel and simulation-logic group by <paramref name="deltaTime"/> seconds.
        /// Called each frame by the orchestrator (or each loop iteration in standalone mode).
        /// </summary>
        public void Update(float deltaTime)
        {
            if (!_initialized) return;
            _kernel!.Update();
            _kernelGroup!.Run();
        }

        /// <summary>No-op — SimHost has no 3-D world visuals.</summary>
        public void DrawWorld() { }

        /// <summary>
        /// Renders ImGui control panels.  Skipped when <see cref="SubsystemConfig.Headless"/>
        /// was <c>true</c> during <see cref="Initialize"/>.
        /// </summary>
        public void DrawUI()
        {
            // Reserved for Phase R3: time-control and entity-spawner ImGui panels.
            // No panels are implemented yet; this method is intentionally empty.
        }

        /// <summary>Disposes all kernel resources.</summary>
        public void Shutdown()
        {
            Stop();
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
