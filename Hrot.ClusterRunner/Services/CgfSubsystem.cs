using System.Linq;
using Hrot.CGF;
using Hrot.Map.Common;
using Hrot.Map.Common.Translators;
using Hrot.SimHost;
using Hrot.SimHost.Configuration;
using Hrot.SimHost.Modules;
using Hrot.SimHost.Network;
using Hrot.SimHost.Systems;
using Hrot.SimHost.Translators;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Network.Infrastructure;
using Hrot.Network.Routing;
using Hrot.NED.Descriptors;
using Hrot.NED.Descriptors.Orchestration;
using CycloneDDS.Runtime;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Lifecycle;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.NetworkSpawning.Systems;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using FDP.Framework.Runner;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using ModuleHost.Network.Cyclone.Modules;
using ModuleHost.Network.Cyclone.Systems;

namespace Hrot.ClusterRunner.Services;

/// <summary>
/// Hosts the CGF (Computer Generated Forces) subsystem under the Runner process.
/// Migrated in EAM-M003 to use <see cref="HrotNodeBuilder"/> instead of <see cref="CgfApplication"/>.
/// </summary>
public sealed class CgfSubsystem : ISubsystem
{
    private HrotNodeContext?         _context;
    private NetworkEntityMap?        _entityMap;
    private SimpleClusterStateCache? _clusterCache;
    private DdsReader<NodeHeartbeat>? _heartbeatReader;

    /// <inheritdoc/>
    public string Name => "CGF";

    /// <inheritdoc/>
    public System.Numerics.Vector4 TitleBarColor => new(0.08f, 0.22f, 0.38f, 1f);

    /// <summary>TestHook: exposes the ghost entity map for integration tests.</summary>
    internal NetworkEntityMap? GhostEntityMap => _entityMap;

    /// <summary>TestHook: exposes the CGF ECS world for integration tests.</summary>
    internal Fdp.Kernel.EntityRepository? World => _context?.World;

    /// <summary>
    /// TestHook: spawns an entity and publishes a <c>DeferredTakeOwnership</c> routing table
    /// that assigns the WorldPos descriptor to <paramref name="muscleNodeId"/>.
    ///
    /// <para>Mirrors what a full <c>CreateEntityRequestSystem(isDefaultProcessor:true)</c> would do
    /// without requiring ExCon wiring in integration tests.</para>
    /// </summary>
    internal long TestHook_SpawnEntityWithSplitAuthority(long tkbType, int muscleNodeId)
    {
        if (_context == null)
            throw new System.InvalidOperationException("CgfSubsystem not initialized.");

        long networkId = _context.IdAllocator?.AllocateId()
            ?? unchecked((long)System.Threading.Interlocked.Increment(ref _testIdCounter));

        // 1. Publish DeferredTakeOwnership FIRST (pre-genesis, before EntityMaster).
        var dtoCmd = new DeferredTakeOwnershipCommand { NetworkId = networkId };
        dtoCmd.Grants.Add(new DescriptorGrant
        {
            DescriptorTypeId = (long)EDescriptorType.dtWorldPos,
            NodeId           = muscleNodeId,
        });
        _context.World.Bus.PublishManaged(dtoCmd);

        // 2. Publish SpawnEntityCommand (CGF/Brain owns entity identity).
        _context.World.Bus.PublishManaged(new SpawnEntityCommand
        {
            NetworkId   = networkId,
            TkbType     = tkbType,
            OwnerNodeId = _context.NodeId,
            InitType    = ModuleHost.Core.Network.Interfaces.ReliableInitType.AllPeers,
            RequestId   = System.Guid.Empty,
        });

        return networkId;
    }

    private int _testIdCounter;

    /// <inheritdoc/>
    public void Initialize(SubsystemConfig config)
    {
        // ── Build common infrastructure ────────────────────────────────────────
        var nodeConfig = new HrotNodeConfig
        {
            DomainId      = config.DomainId,
            NodeId        = config.NodeId != 0 ? config.NodeId : 400,
            // config.Headless == true means unit-test / offline mode (no DDS, no allocator wait).
            // In integration tests CgfHarness passes Headless = false, and the OrchestratorSubsystem
            // (started by the accompanying HrotRunnerHarness) provides the DdsIdAllocatorServer.
            Headless      = config.Headless,
            SubsystemName = "CGF",
        };
        _context = new HrotNodeBuilder(nodeConfig)
            .WithRole("CgfNode", NodeRole.Brain)
            .WithReplication(NodeRole.Brain)
            .Build();

        _entityMap = _context.EntityMap;
        CgfComponentRegistry.RegisterAll(_context.World);

        // ── Register base infrastructure modules ───────────────────────────────
        foreach (var m in _context.BaseModules)
            _context.Kernel.RegisterModule(m);

        // ── Register NedReplicationModule (Brain role) ─────────────────────────
        // Replaces: EntityStatesIngressPack + ActuatorIntentsEgressPack + GhostCleanupModule
        _context.Kernel.RegisterModule(_context.NedReplication!);

        // ── Register CGF simulation logic (Brain-specific) ─────────────────────
        var doctrineRegistry = new DoctrineRegistry();
        SimHostDoctrineSetup.RegisterAll(doctrineRegistry, _context.GeoTransform!);
        _context.Kernel.RegisterModule(new CgfLogicPack(doctrineRegistry, _entityMap));

        // ── Wire CreateEntityRequestSystem (CGF is the cluster-default processor) ─
        // This makes CGF intercept broadcast CreateEntityRequests (Owner == 0) and spawn
        // entities, delegating WorldPos (kinematics) to the least-loaded Muscle node via
        // DeferredTakeOwnership. SimHost nodes keep isDefaultProcessor=false.
        if (_context.Participant != null)
        {
            var tkbDb        = _context.TkbDb!;
            var idAllocator  = _context.IdAllocator!;
            var geoTransform = _context.GeoTransform!;
            var elm          = (EntityLifecycleModule)_context.BaseModules
                                   .First(m => m is EntityLifecycleModule);

            var requestSource = new DdsCreateEntityRequestSource(_context.Participant);
            var ackSink       = new DdsCreateUpdateDeleteEntityAckSink(_context.Participant);

            var jsonCompiler      = AttributeCompilerFactory.Build(geoTransform);
            var binaryInterpreter = AttributeCompilerFactory.BuildBinaryInterpreter(geoTransform);

            var finalizationSystem = new NedRequestFinalizationSystem(ackSink, _entityMap!);

            _clusterCache    = new SimpleClusterStateCache();
            _heartbeatReader = new DdsReader<NodeHeartbeat>(_context.Participant);
            var ownershipStrategy = new BrainMuscleOwnershipStrategy(_clusterCache);

            var requestSystem = new CreateEntityRequestSystem(
                requestSource:        requestSource,
                ackSink:              ackSink,
                tkbDb:                tkbDb,
                idAllocator:          idAllocator,
                localNodeId:          _context.NodeId,
                geoTransform:         geoTransform,
                jsonAttributeCompiler: jsonCompiler,
                binaryInterpreter:    binaryInterpreter,
                finalizationSystem:   finalizationSystem,
                isDefaultProcessor:   true,
                ownershipStrategy:    ownershipStrategy);

            var spawnSystem = new NetworkSpawningSystem(
                tkbDb,
                elm,
                _entityMap!,
                idAllocator,
                _context.NodeId);

            _context.Kernel.RegisterModule(new SimHostModule(
                spawnSystem:        spawnSystem,
                requestSystem:      requestSystem,
                finalizationSystem: finalizationSystem));

            var auxTranslators = SimHostAuxiliaryTranslatorPack.Create(
                _context.Participant,
                _entityMap!,
                _context.EventBus,
                _context.NodeId,
                NodeRole.Brain);

            _context.Kernel.RegisterGlobalSystem(new CycloneNetworkIngressSystem(auxTranslators.ToArray()));
            _context.Kernel.RegisterGlobalSystem(new CycloneEgressSystem(auxTranslators.ToArray()));
            _context.Kernel.RegisterGlobalSystem(new CycloneNetworkCleanupSystem(auxTranslators));
        }

        // ── Initialize ─────────────────────────────────────────────────────────
        _context.Kernel.Initialize();
    }

    /// <inheritdoc/>
    public void Update(float deltaTime)
    {
        // Poll DDS NodeHeartbeat to keep the cluster cache up-to-date so that
        // BrainMuscleOwnershipStrategy can find the least-loaded Muscle node.
        if (_heartbeatReader != null && _clusterCache != null)
        {
            using var loan = _heartbeatReader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                _clusterCache.UpdateNode(new NodeCapability
                {
                    NodeId             = sample.Data.NodeId,
                    Role               = MapSubsystemNameToRole(sample.Data.SubsystemName),
                    CpuUsagePercent    = sample.Data.CpuUsagePercent,
                    RamUsedBytes       = sample.Data.RamUsedBytes,
                    LastSeenUtcSeconds = (double)sample.Data.WallTicksUtc / System.TimeSpan.TicksPerSecond,
                });
            }
        }

        _context?.SlaveTranslator?.Tick();
        _context?.ClusterSlave.Tick();
#pragma warning disable CS0618 // legacy Update(float) used intentionally in CgfSubsystem
        _context?.Kernel.Update(deltaTime);
#pragma warning restore CS0618
        _context?.EventBus.SwapBuffers();
    }

    /// <inheritdoc/>
    public void DrawWorld() { }

    /// <inheritdoc/>
    public void DrawUI() { }

    /// <inheritdoc/>
    public void Shutdown()
    {
        _heartbeatReader?.Dispose();
        _heartbeatReader = null;
        _context?.Kernel.Dispose();
        _context?.Participant?.Dispose();
        _context = null;
    }

    /// <summary>
    /// Maps a subsystem's published name to its <see cref="NodeRole"/> for cluster cache population.
    /// Nodes not matching a known name receive <see cref="NodeRole.None"/> and are ignored by
    /// <see cref="BrainMuscleOwnershipStrategy"/> queries.
    /// </summary>
    private static NodeRole MapSubsystemNameToRole(string? name) =>
        name switch
        {
            "SimHost"    => NodeRole.AllInOne,
            "CGF"        => NodeRole.Brain,
            "IG"         => NodeRole.ImageGenerator,
            _            => NodeRole.None,
        };
}

