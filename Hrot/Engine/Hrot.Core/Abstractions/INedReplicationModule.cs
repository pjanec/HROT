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
}
