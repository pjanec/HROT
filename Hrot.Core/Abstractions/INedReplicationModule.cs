using ModuleHost.Core.Scheduling;

namespace Hrot.Common.Abstractions;

/// <summary>
/// NED-specific extension of <see cref="IReplicationModule"/>.
/// Defined in Hrot.Core so that HrotNodeContext can hold a typed reference without
/// Hrot.Core needing to reference Hrot.Network (which would create a cycle).
/// </summary>
public interface INedReplicationModule : IReplicationModule
{
    // IReplicationModule provides: GhostCreationSystem, DriveFromNetwork
    // IEcsModule provides: string Name, ExecutionPolicy Policy,
    //                      RegisterSystems(ISystemRegistry), Tick(ISimulationView, float)

    /// <summary>
    /// System group that gates <see cref="IReplicationModule.GhostCreationSystem"/> during replay playback.
    /// <see cref="NetworkLifecycleSystemGroup.Enabled"/> is set to <c>false</c> by the
    /// <c>ReplayLoadClusterOpHandler</c> to prevent ghost promotions mid-replay.
    /// </summary>
    NetworkLifecycleSystemGroup NetworkLifecycleGroup { get; }
}
