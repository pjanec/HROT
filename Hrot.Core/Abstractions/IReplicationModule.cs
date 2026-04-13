using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Scheduling;
using FDP.Toolkit.Replication.Systems;

namespace Hrot.Common.Abstractions; // preserve old namespace for callers

/// <summary>
/// Protocol-neutral interface for the network replication subsystem.
/// Replaces the NED-specific INedReplicationModule.
/// </summary>
public interface IReplicationModule : IEcsModule
{
    GhostCreationSystem GhostCreationSystem { get; }
    bool DriveFromNetwork { get; }

    /// <summary>
    /// System group that gates ghost promotions during replay playback.
    /// </summary>
    NetworkLifecycleSystemGroup NetworkLifecycleGroup { get; }
}
