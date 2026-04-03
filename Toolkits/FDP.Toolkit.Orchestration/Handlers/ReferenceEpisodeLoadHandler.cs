namespace FDP.Toolkit.Orchestration.Handlers
{
    /// <summary>
    /// Payload for <see cref="Hrot.Common.Orchestration.Handlers.ReferenceEpisodeLoadHandler"/> episode operations.
    /// Used for both <c>StartEpisode</c> and <c>StopEpisode</c> intents.
    /// </summary>
    public record struct EpisodeHandlerPayload(System.Guid EpisodeId, string? ScenarioId, bool IsStart);

    // ReferenceEpisodeLoadHandler has moved to Hrot.Common.Orchestration.Handlers.
}
