namespace Hrot.Core.Network;

/// <summary>
/// Ticks the cluster observer translator (SystemState, AssetInventory ingress).
/// Called in Phase 1 of slash Update() for observer nodes.
/// </summary>
public interface IOrchestrationObserver : IDisposable
{
    void Tick();
}
