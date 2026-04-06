using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using FDP.Toolkit.Orchestration;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Hrot.Common.Orchestration;
using ModuleHost.Core;
using ModuleHost.Core.Abstractions;

namespace Hrot.ClusterRunner.Infrastructure;

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
    /// DDS ↔ bus bridge for <c>NodeOpCommand</c> / <c>NodeOpStatus</c> / <c>NodeHeartbeat</c>.
    /// <c>null</c> when no DDS participant was provided.
    /// </summary>
    public NodeOpSlaveTranslator? SlaveTranslator { get; init; }

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
}
