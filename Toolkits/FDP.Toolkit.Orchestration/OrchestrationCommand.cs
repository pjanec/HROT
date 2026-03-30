namespace FDP.Toolkit.Orchestration
{
    /// <summary>
    /// Toolkit-owned plain value type representing a command dispatched by the
    /// orchestrator to a slave node.  Serves as the currency exchanged between
    /// an <see cref="IOrchestrationTransport"/> implementation and the toolkit
    /// <c>DrillSlave</c> — no DDS types appear at the boundary.
    /// </summary>
    public readonly record struct OrchestrationCommand(
        Guid   TransactionId,
        int    TargetNodeId,
        int    OperationId,
        string PayloadJson);
}
