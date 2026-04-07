using Hrot.CGF;
using Hrot.Map.Common;
using Hrot.Map.Common.Translators;
using Hrot.SimHost.Translators;
using Hrot.Common;
using Hrot.Common.Infrastructure;
using Hrot.ClusterRunner.Replication;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.NetworkSpawning.Events;
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
    private IEcsModule?       _nedReplicationModule;
    private NetworkEntityMap? _entityMap;

    /// <inheritdoc/>
    public string Name => "CGF";

    /// <inheritdoc/>
    public System.Numerics.Vector4 TitleBarColor => new(0.08f, 0.22f, 0.38f, 1f);

    /// <summary>TestHook: exposes the ghost entity map for integration tests.</summary>
    internal NetworkEntityMap? GhostEntityMap => _entityMap;

    /// <summary>TestHook: exposes the CGF ECS world for integration tests.</summary>
    internal Fdp.Kernel.EntityRepository? World => _context?.World;

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
            .Build();

        _entityMap = _context.EntityMap;
        CgfComponentRegistry.RegisterAll(_context.World);

        // ── Register base infrastructure modules ───────────────────────────────
        foreach (var m in _context.BaseModules)
            _context.Kernel.RegisterModule(m);

        // ── Register NedReplicationModule (Brain role) ─────────────────────────
        // Replaces: EntityStatesIngressPack + ActuatorIntentsEgressPack + GhostCleanupModule
        _nedReplicationModule = new NedReplicationModule(
            participant:  _context.Participant,
            role:         NodeRole.Brain,
            entityMap:    _entityMap,
            geoTransform: HrotEnvironment.CreateGeoTransform(),
            // Use world.Bus so that events published by EntityMasterIngressTranslator.ProcessDispose()
            // during the Input kernel phase are made visible to GhostDestructionSystem (PostSimulation)
            // via view.ConsumeManagedEvents<T>() after the kernel's internal Bus.SwapBuffers().
            // Using _context.EventBus (a separate FdpEventBus) would cause a bus mismatch.
            eventBus:     _context.World.Bus,
            localNodeId:  nodeConfig.NodeId,
            domainId:     config.DomainId);
        _context.Kernel.RegisterModule(_nedReplicationModule);

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
        _context?.Kernel.Update(deltaTime);
        _context?.EventBus.SwapBuffers();
    }

    /// <inheritdoc/>
    public void DrawWorld() { }

    /// <inheritdoc/>
    public void DrawUI() { }

    /// <inheritdoc/>
    public void Shutdown()
    {
        _nedReplicationModule = null;
        _context?.Kernel.Dispose();
        _context?.Participant?.Dispose();
        _context = null;
    }
}

