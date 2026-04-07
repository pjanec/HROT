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
    // Extend this interface when SubsystemOrchestrator hot-swap logic demands more surface.
}
