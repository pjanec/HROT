using Hrot.CGF;
using Hrot.Map.Common;
using Hrot.Map.Common.Translators;
using Hrot.SimHost.Translators;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.Network.Infrastructure;
using Hrot.NED.Descriptors;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.NetworkSpawning.Events;
using FDP.Toolkit.Replication.Components;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using FDP.Framework.Runner;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;

namespace Hrot.ClusterRunner.Services;

/// <summary>
/// Hosts the CGF (Computer Generated Forces) subsystem under the Runner process.
/// Migrated in EAM-M003 to use <see cref="HrotNodeBuilder"/> instead of <see cref="CgfApplication"/>.
/// </summary>
public sealed class CgfSubsystem : ISubsystem
{
    private HrotNodeContext?  _context;
    private NetworkEntityMap? _entityMap;

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
        _context.Kernel.RegisterModule(new CgfLogicPack(doctrineRegistry, _entityMap));

        // ── Initialize ─────────────────────────────────────────────────────────
        _context.Kernel.Initialize();
    }

    /// <inheritdoc/>
    public void Update(float deltaTime)
    {
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
        _context?.Kernel.Dispose();
        _context?.Participant?.Dispose();
        _context = null;
    }
}

