namespace FDP.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// Payload for <see cref="Hrot.Common.Orchestration.Handlers.ReferenceEditLoadHandler"/> commands.
    /// <c>TargetState</c> must equal <c>ClusterState.LoadingEdit (10)</c> for the
    /// handler to perform any I/O; other target states are no-ops.
    /// </summary>
    public record struct EditLoadHandlerPayload(string? ScenarioId, bool IsNewScenario = false, int TargetState = 10);

    // ReferenceEditLoadHandler has moved to Hrot.Common.Orchestration.Handlers.
}
