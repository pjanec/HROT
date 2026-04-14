using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Modules.Geographic;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Replication.Systems;
using Hrot.Common.Abstractions;
using Fdp.ModuleHost_Core;
using Fdp.ModuleHost_Core.Abstractions;
using Fdp.ModuleHost_Core.Network.Interfaces;
using NetworkEntityMap = FDP.Toolkit.Replication.Services.NetworkEntityMap;
// IOrchestrationTranslator lives in same namespace (Hrot.Common.Infrastructure)

namespace Hrot.Common.Infrastructure;

/// <summary>
/// Immutable snapshot of all infrastructure objects produced by <see cref="HrotNodeBuilder"/>.
/// Passed to subsystems so they can register modules, wire handlers, and add ECS systems
/// without re-running the bootstrap sequence.
/// </summary>
public sealed record HrotNodeContext
{
    /// <summary>The ECS world (entity repository).</summary>
    public required EntityRepository World { get; init; }

    /// <summary>The module-host kernel.</summary>
    public required ModuleHostKernel Kernel { get; init; }

    /// <summary>The live DDS participant. <c>null</c> in headless/test contexts where DDS is disabled.</summary>
    public DdsParticipant? Participant { get; init; }

    /// <summary>The application event bus.</summary>
    public required FdpEventBus EventBus { get; init; }

    /// <summary>Shared network entity map for ghost/egress lookups.</summary>
    public required NetworkEntityMap EntityMap { get; init; }

    /// <summary>The cluster slave wired with the four generic reference handlers.</summary>
    public required ClusterSlave ClusterSlave { get; init; }

    /// <summary>
    /// DDS <-> bus bridge for <c>NodeOpCommand</c> / <c>NodeOpStatus</c> / <c>NodeHeartbeat</c>.
    /// <c>null</c> when no DDS participant was provided.
    /// </summary>
    public IOrchestrationTranslator? SlaveTranslator { get; init; }

    /// <summary>
    /// Infrastructure <see cref="IEcsModule"/> instances (e.g. <c>EntityLifecycleModule</c>,
    /// <c>GeographicModule</c>) that must be registered on the kernel before subsystem modules.
    /// </summary>
    public required IReadOnlyList<IEcsModule> BaseModules { get; init; }

    /// <summary>
    /// Ghost-creation system shared between the network ingress translators and the replay handler.
    /// Exposed here so Phase 4 can wire it into <c>ReplayLoadClusterOpHandler</c>.
    /// <c>null</c> in headless/test contexts that skip NED replication.
    /// </summary>
    public GhostCreationSystem? GhostCreationSystem { get; init; }

    /// <summary>The DDS ID allocator for entity ID allocation. Null in headless contexts.</summary>
    public INetworkIdAllocator? IdAllocator { get; init; }

    /// <summary>The local node ID used for DDS identity and ID allocation.</summary>
    public int NodeId { get; init; }

    /// <summary>
    /// The TKB (toolkit database) shared by <c>EntityLifecycleModule</c> and spawning systems.
    /// Use this when constructing systems that require the same tkbDb as the lifecycle module.
    /// </summary>
    public ITkbDatabase? TkbDb { get; init; }

    /// <summary>
    /// The geodetic coordinate transform created by the builder.
    /// Use this instead of calling <c>HrotEnvironment.CreateGeoTransform()</c> again.
    /// </summary>
    public IGeographicTransform? GeoTransform { get; init; }

    /// <summary>
    /// The replication module bundling translator packs and their lifecycle systems.
    /// Set by <c>HrotNodeBuilderReplicationExtensions.Build()</c>.
    /// <c>null</c> only in legacy call sites that have not yet migrated to the extension Build().
    /// </summary>
    public INedReplicationModule? NedReplication { get; init; }

    /// <summary>
    /// Protocol-neutral replication interface. Aliases <see cref="NedReplication"/> for
    /// code that does not need NED-specific members.
    /// </summary>
    public IReplicationModule? Replication => NedReplication;
}
