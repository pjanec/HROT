namespace FDP.Toolkit.Orchestration
{
    /// <summary>
    /// Toolkit-owned plain value type representing a status acknowledgement
    /// published by a slave node back to the orchestrator.  The
    /// <see cref="StatusCode"/> field uses the unified tiered scheme defined in
    /// <see cref="OrchestrationStatusCode"/> — no separate <c>ErrorCode</c> field.
    /// </summary>
    public readonly record struct OrchestrationStatus(
        Guid   TransactionId,
        int    NodeId,
        int    StatusCode,
        bool   IsParticipating,
        string ResultJson);
}
