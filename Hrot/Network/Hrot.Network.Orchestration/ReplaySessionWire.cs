using System;
using System.Text.Json;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;

namespace Hrot.Network.Orchestration;

/// <summary>
/// ⭐ CE-3329 (rows 32, 45) — the ONE encoding of <see cref="ReplaySession"/> on <see cref="ReplaySessionTopic"/>: the master's writer
/// (<see cref="ClusterOpMasterTranslator"/>) and both readers (<c>NodeOpSlaveTranslator</c>, <c>OrchestrationObserverTranslator</c>)
/// go through here, so the three never drift.
/// </summary>
public static class ReplaySessionWire
{
    public static ReplaySessionTopic ToTopic(ReplaySession session) => new()
    {
        SessionId   = session.SessionId,
        ExerciseId  = session.ExerciseId,
        SessionJson = JsonSerializer.Serialize(session, FdpJsonOptionsRegistry.DefaultRelaxed),
    };

    /// <summary>The session in a sample, or <see langword="null"/> when its JSON does not parse (a sample from a newer master is skipped).</summary>
    public static ReplaySession? FromTopic(in ReplaySessionTopic topic)
    {
        if (string.IsNullOrWhiteSpace(topic.SessionJson)) return null;
        try { return JsonSerializer.Deserialize<ReplaySession>(topic.SessionJson, FdpJsonOptionsRegistry.DefaultRelaxed); }
        catch (JsonException) { return null; }
    }
}
