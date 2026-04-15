namespace Hrot.Core.Network;

/// <summary>
/// Ticks the slave-side orchestration transport (NodeOpCommand ingress,
/// NodeOpStatus + NodeHeartbeat egress). Called in Phase 1 of slave Update().
/// </summary>
public interface ISlaveOrchestrationTranslator : IDisposable
{
    void Tick();
}
