using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tracking;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Lifecycle;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Time.Controllers;
using Hrot.Common.Orchestration;
using Hrot.Core.Network;
using Hrot.Map.Common;
using Hrot.NED.Descriptors.Orchestration;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Time;
using Fdp.Network.Cyclone.Services;
using NetworkEntityMap = Fdp.Toolkit.Replication.Services.NetworkEntityMap;

namespace Hrot.Common.Infrastructure;

/// <summary>
/// Fluent builder that constructs the full <see cref="HrotNodeContext"/> in one
/// <see cref="Build"/> call.
///
/// <para>Replaces the ~300-line bootstrap boilerplate in
/// <c>SimHostApp.OnLoad</c> with a 3-line call sequence:</para>
/// <code>
/// _context = new HrotNodeBuilder(config)
///     .WithRole("EyesAndMuscle", NodeRole.MuscleGround | NodeRole.ImageGenerator)
///     .Build();
/// </code>
///
/// <para>The builder is single-use: a second call to <see cref="Build"/> throws
/// <see cref="InvalidOperationException"/>.</para>
/// </summary>
public sealed class HrotNodeBuilder
{
    private readonly HrotNodeConfig _config;
    private string            _subsystemName = "Node";
    private INetworkFactory?  _networkFactory;
    private bool              _built;

    public HrotNodeBuilder(HrotNodeConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>Sets the human-readable subsystem name used in DDS heartbeat publications.</summary>
    public HrotNodeBuilder WithRole(string subsystemName, Hrot.Common.NodeRole role)
    {
        _subsystemName = subsystemName;
        return this;
    }

    /// <summary>
    /// Supplies the <see cref="INetworkFactory"/> used to create the ID allocator client.
    /// When provided, <see cref="Build"/> delegates ID allocator creation to the factory
    /// instead of directly instantiating <c>DdsIdAllocator</c>.
    /// </summary>
    public HrotNodeBuilder WithNetworkFactory(INetworkFactory? networkFactory)
    {
        _networkFactory = networkFactory;
        return this;
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the full initialization sequence and returns an immutable
    /// <see cref="HrotNodeContext"/>.  May only be called once per builder instance.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="Build"/> has already been called.</exception>
    public HrotNodeContext Build()
    {
        if (_built)
            throw new InvalidOperationException("HrotNodeBuilder.Build() may only be called once.");
        _built = true;

        // ── Part A: Generic engine (no DDS) ──────────────────────────────────

        // Step 1 — ECS world
        var world = new EntityRepository();

        // Step 2 — Event accumulator + kernel
        var eventAccumulator = new EventAccumulator();
        var kernel           = new ModuleHostKernel(world, eventAccumulator);

        // Step 3 — Event bus
        var eventBus = new FdpEventBus();

        // Step 4 — Time controller (Slave role; transitions handled by SlaveSyncController)
        var timeConfig = new TimeControllerConfig
        {
            Mode        = TimeMode.Continuous,
            Role        = TimeRole.Slave,
            LocalNodeId = _config.NodeId,
            SyncConfig  = Fdp.Toolkit.Time.Controllers.TimeConfig.Default,
        };
        var timeCtrl = TimeControllerFactory.Create(eventBus, timeConfig);
        kernel.SetTimeController(timeCtrl);
        eventBus.SwapBuffers();

        // ── Part B: Hrot / DDS-specific ───────────────────────────────────────

        DdsParticipant?  participant  = null;
        NetworkEntityMap entityMap   = new NetworkEntityMap();
        INetworkIdAllocator? idAllocator = null;

        if (!_config.Headless)
        {
            // Step 5 — DDS participant.
            // The participant MUST be provided by the Application Shell / Composition Root
            // (Rule 3: only the outermost executable may instantiate DdsParticipant).
            // Tracking must also be configured by the caller before any writer is created.
            participant = _config.ExternalParticipant;

            // Step 6 — Network entity map (already created above, reused here)

            // Step 7 — ID allocator client + routing wait
            //
            // ── ID Allocator routing ──────────────────────────────────────────
            // When a network factory is provided, delegate allocator creation to it
            // so the factory decides whether to return a DdsIdAllocator or a
            // SequentialIdAllocator (e.g. in offline/headless environments).
            // Without a factory, fall back to direct DDS instantiation (legacy path).
            if (_networkFactory != null)
            {
                // Factory path: delegate to factory regardless of participant availability.
                // NedNetworkFactory returns DdsIdAllocator when its internal participant is
                // non-null, SequentialIdAllocator otherwise.  Offline/mock factories always
                // return SequentialIdAllocator.
                idAllocator = _networkFactory.CreateIdAllocator(
                    (_config.SubsystemName ?? _subsystemName) + "Allocator",
                    skipRoutingWait: _config.SkipAllocatorRouting);
            }
            else if (participant != null)
            {
                // Legacy path (no factory injected): create DdsIdAllocator directly.
                var ddsAllocator = new DdsIdAllocator(participant, (_config.SubsystemName ?? _subsystemName) + "Allocator");
                if (!_config.SkipAllocatorRouting)
                    DdsIdAllocatorHelper.EnsureRouting(participant, ddsAllocator);
                idAllocator = ddsAllocator;
            }
        }
        else if (_config.ExternalParticipant != null)
        {
            // Headless mode with an externally-supplied participant:
            // use the participant for DDS communication (e.g. integration tests that need
            // ingress/egress but skip the Raylib window), but skip allocator routing.
            participant = _config.ExternalParticipant;
        }

        // Factory-provided allocator in headless mode: let the factory decide (returns
        // SequentialIdAllocator for offline/mock factories, enabling unit tests to spawn
        // entities without a live DDS participant.
        if (_networkFactory != null && idAllocator == null)
        {
            idAllocator = _networkFactory.CreateIdAllocator(
                (_config.SubsystemName ?? _subsystemName) + "Allocator",
                skipRoutingWait: true);
        }

        // Step 8 — ClusterSlave + SlaveTranslator
        var clusterSlave = new ClusterSlave(_config.NodeId, _subsystemName, eventBus);
        
        Hrot.Core.Network.ISlaveOrchestrationTranslator? slaveTranslator = null;
        if (participant != null && _networkFactory != null)
        {
            var nodeFactory = _networkFactory.ConfigureForNode(participant, _config.NodeId, Hrot.Common.NodeRole.None);
            slaveTranslator = nodeFactory.CreateSlaveOrchestratorTranslators(eventBus, _config.NodeId);
        }

        // Step 9 — Infrastructure EcsModules
        var tkbDb       = HrotEnvironment.CreateTkb();
        var geoTransform = HrotEnvironment.CreateGeoTransform();
        var elm         = new EntityLifecycleModule(tkbDb, new List<int>(), localNodeId: _config.NodeId);
        var geoModule   = new GeographicModule(geoTransform);
        var baseModules = new List<IEcsModule> { elm, geoModule };

        // Step 10 — Return context
        return new HrotNodeContext
        {
            World            = world,
            Kernel           = kernel,
            EventAccumulator = eventAccumulator,
            Participant      = participant,
            EventBus         = eventBus,
            EntityMap       = entityMap,
            ClusterSlave    = clusterSlave,
            SlaveTranslator = slaveTranslator,
            BaseModules     = baseModules,
            GhostCreationSystem = null,   // populated by NedReplicationModule after Build()
            IdAllocator     = idAllocator,
            NodeId          = _config.NodeId,
            TkbDb           = tkbDb,
            GeoTransform    = geoTransform,
        };
    }
}
