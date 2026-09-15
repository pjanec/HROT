namespace Hrot.Common.Abstractions;

/// <summary>
/// NED-specific extension of <see cref="IReplicationModule"/>.
/// Defined in Hrot.Core so that HrotNodeContext can hold a typed reference without
/// Hrot.Core needing to reference Hrot.Network (which would create a cycle).
/// </summary>
public interface INedReplicationModule : IReplicationModule
{
    // IReplicationModule provides: GhostCreationSystem, DriveFromNetwork, NetworkLifecycleGroup
    // IEcsModule provides: string Name, ExecutionPolicy Policy,
    //                      RegisterSystems(ISystemRegistry), Tick(ISimulationView, float)

    /// <summary>
    /// Optional callback to invoke after a replay seek to flush stale network-cleanup tracking.
    /// Returns null when no network cleanup system is wired (e.g. headless tests).
    /// </summary>
    Action? AfterSeekCallback { get; }

    /// <summary>
    /// ⭐ CE-276 — the node's descriptor↔component ownership map (populated from the registered translators).
    /// Exposed so the ai-debug ownership surface can name an entity's descriptors and resolve a transfer scope.
    /// <see langword="null"/> on a module that carries no descriptor mapping.
    /// </summary>
    Fdp.Toolkit.Replication.Services.DescriptorOwnershipMap? DescriptorOwnershipMap { get; }
}
