namespace Hrot.Common.Infrastructure; // keep in same namespace as HrotNodeConfig

/// <summary>Marker interface for the cluster management DDS translator.
/// Implemented by NodeOpSlaveTranslator in Hrot.Network.Orchestration.</summary>
public interface IOrchestrationTranslator : IDisposable
{
    /// <summary>Called each frame to pump DDS reads and bus publishes.</summary>
    void Tick();
}
