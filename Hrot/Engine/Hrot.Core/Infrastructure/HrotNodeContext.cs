using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication.Systems;
using Hrot.Common.Abstractions;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using NetworkEntityMap = Fdp.Toolkit.Replication.Services.NetworkEntityMap;
using Fdp.Toolkit.NetworkSpawning;
// IOrchestrationTranslator lives in same namespace (Hrot.Common.Infrastructure)

namespace Hrot.Common.Infrastructure;

/// <summary>
/// Immutable snapshot of all infrastructure objects produced by <see cref="HrotNodeBuilder"/>.
/// Passed to subsystems so they can register modules, wire handlers, and add ECS systems
/// without re-running the bootstrap sequence.
///
/// <para>⭐⭐⭐ <b>OWNERSHIP — <c>QA-001</c>.</b> The builder CREATES <see cref="World"/> and
/// <see cref="Kernel"/>, so the context OWNS them and <see cref="Dispose"/> tears them down.
/// ⛔ Every consumer must call <c>context.Dispose()</c> on shutdown — ⛔ NOT
/// <c>context.Kernel.Dispose()</c>, which is what all four consumers used to do and is exactly how
/// the world came to be leaked on every node teardown.</para>
///
/// <para>📐 <b>Measured, 2026-08-26:</b> the four consumers that receive a world through this record
/// (<c>SimHostApp</c>, <c>IgApplication</c>, <c>CgfSubsystem</c>, <c>EyesAndMuscleSubsystem</c>) all
/// disposed the kernel and none disposed the world; the three that build their own world directly
/// (<c>CgfApplication</c>, <c>EditorSubsystem</c>, <c>ScenarioSubsystem</c>) all dispose it. ⇒ the
/// defect was the MISSING OWNERSHIP CONTRACT on this record, not four independent oversights.
/// One leaked <see cref="EntityRepository"/> holds an <c>int[1_000_000]</c> free list plus one
/// <c>NativeChunkTable</c> per registered component; accumulated across an integration run it
/// exhausted a 16 GB box and aborted the test host.</para>
///
/// <para>⚠ <see cref="Participant"/> is deliberately NOT disposed here — its ownership is genuinely
/// conditional (a test harness or an outer composition root may own it), so it stays with whoever
/// created it.</para>
/// </summary>
public sealed record HrotNodeContext : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// ⭐ Disposes what the builder created: the <see cref="Kernel"/> first (so module providers and
    /// in-flight tasks are torn down while their storage is still valid), then the
    /// <see cref="World"/>. Idempotent — safe to call from a shutdown path that may run twice.
    /// ⛔ Does not touch <see cref="Participant"/>; see the ownership note on the type.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Kernel.Dispose();
        World.Dispose();
    }

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
    public Hrot.Core.Network.ISlaveOrchestrationTranslator? SlaveTranslator { get; init; }

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

    /// <summary>Event accumulator for checkpoint event preservation (CGF-SCN-2 S503).</summary>
    public required EventAccumulator EventAccumulator { get; init; }
}
