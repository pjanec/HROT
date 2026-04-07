using ModuleHost.Core.Abstractions;

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
}
