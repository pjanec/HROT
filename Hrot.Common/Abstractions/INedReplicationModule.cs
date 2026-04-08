using FDP.Toolkit.Replication.Systems;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Scheduling;

namespace Hrot.Common.Abstractions;

/// <summary>
/// Minimal abstraction over the NED replication module.
/// Defined in Hrot.Common so that HrotNodeContext can hold a typed reference without
/// Hrot.Common needing to reference Hrot.Network (which would create a cycle).
/// </summary>
public interface INedReplicationModule : IEcsModule
{
    // IEcsModule provides: string Name, ExecutionPolicy Policy,
    //                      RegisterSystems(ISystemRegistry), Tick(ISimulationView, float)

    /// <summary>
    /// When <c>true</c>, dead-reckoning runs on all non-authority entities regardless of
    /// lifecycle state (pure ImageGenerator: every entity is remote).
    /// When <c>false</c>, only entities in <c>EntityLifecycle.Ghost</c> state are smoothed,
    /// preventing DR from overriding locally-owned entities on combined Muscle+IG nodes.
    /// </summary>
    bool DriveFromNetwork { get; }

    /// <summary>
    /// Ghost-creation system that materialises replica entities from incoming DDS samples.
    /// Exposed so orchestration handlers can wire replay lifecycle gates to the same instance
    /// that the replication translators use.
    /// </summary>
    GhostCreationSystem GhostCreationSystem { get; }

    /// <summary>
    /// System group that gates <see cref="GhostCreationSystem"/> during replay playback.
    /// <see cref="NetworkLifecycleSystemGroup.Enabled"/> is set to <c>false</c> by the
    /// <c>ReplayLoadClusterOpHandler</c> to prevent ghost promotions mid-replay.
    /// </summary>
    NetworkLifecycleSystemGroup NetworkLifecycleGroup { get; }
}
