using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tracking;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Orchestration.Handlers;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Time.Controllers;
using Hrot.Common.Orchestration;
using Hrot.Map.Common;
using Hrot.NED.Descriptors.Orchestration;
using ModuleHost.Core;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Time;
using ModuleHost.Network.Cyclone.Services;
using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;

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
    private string   _subsystemName = "Node";
    private bool     _built;

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
            SyncConfig  = FDP.Toolkit.Time.Controllers.TimeConfig.Default,
        };
        var timeCtrl = TimeControllerFactory.Create(eventBus, timeConfig);
        kernel.SetTimeController(timeCtrl);
        eventBus.SwapBuffers();

        // ── Part B: Hrot / DDS-specific ───────────────────────────────────────

        DdsParticipant? participant  = null;
        NetworkEntityMap entityMap   = new NetworkEntityMap();
        DdsIdAllocator?  idAllocator = null;

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
            if (participant != null)
            {
                idAllocator = new DdsIdAllocator(participant, (_config.SubsystemName ?? _subsystemName) + "Allocator");
                if (!_config.SkipAllocatorRouting)
                    DdsIdAllocatorHelper.EnsureRouting(participant, idAllocator);
            }
        }
        else if (_config.ExternalParticipant != null)
        {
            // Headless mode with an externally-supplied participant:
            // use the participant for DDS communication (e.g. integration tests that need
            // ingress/egress but skip the Raylib window), but skip allocator routing.
            participant = _config.ExternalParticipant;
        }

        // Step 8 — ClusterSlave + NodeOpSlaveTranslator (inline)
        var clusterSlave             = new ClusterSlave(_config.NodeId, _subsystemName, eventBus);
        NodeOpSlaveTranslator? slaveTranslator = null;
        if (participant != null)
        {
            slaveTranslator = new NodeOpSlaveTranslator(
                commandReader:   new DdsReader<NodeOpCommand>(participant),
                statusWriter:    new DdsWriter<NodeOpStatus>(participant),
                heartbeatWriter: new DdsWriter<NodeHeartbeat>(participant),
                bus:             eventBus,
                nodeId:          _config.NodeId);
        }

        var storageProvider = new LocalDiskStorageProvider(_config.LocalTempRoot ?? @"C:\FDP_Temp");
        var localTempRoot   = _config.LocalTempRoot ?? @"C:\FDP_Temp";
        clusterSlave.RegisterHandler(new ReferencePreviewHandler(world));
        clusterSlave.RegisterHandler(new ReferencePrefetchHandler(storageProvider));
        clusterSlave.RegisterHandler(new ReferenceArchiveHandler(localTempRoot, _config.NodeId));
        clusterSlave.RegisterHandler(new ReferenceLiveLoadHandler(null, null, localTempRoot));

        // Step 9 — Infrastructure EcsModules
        var tkbDb       = HrotEnvironment.CreateTkb();
        var geoTransform = HrotEnvironment.CreateGeoTransform();
        var elm         = new EntityLifecycleModule(tkbDb, new List<int>(), localNodeId: _config.NodeId);
        var geoModule   = new GeographicModule(geoTransform);
        var baseModules = new List<IEcsModule> { elm, geoModule };

        // Step 10 — Return context
        return new HrotNodeContext
        {
            World           = world,
            Kernel          = kernel,
            Participant     = participant,
            EventBus        = eventBus,
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
