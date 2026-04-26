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
}
