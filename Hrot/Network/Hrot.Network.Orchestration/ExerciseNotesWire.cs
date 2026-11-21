using System;
using System.Text.Json;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;

namespace Hrot.Network.Orchestration;

/// <summary>
/// ⭐ CE-4325 (E7) — the ONE encoding of the master's <see cref="ExerciseNote"/> list on <see cref="ExerciseNotesTopic"/>: the master's writer (<see cref="ClusterOpMasterTranslator"/>) and every node's reader
/// (<see cref="ClusterFactsIngress"/>) go through here, so the two never drift. Mirrors <see cref="StorageJobsWire"/>.
/// </summary>
public static class ExerciseNotesWire
{
    /// <summary>The master's list as a sample (<see cref="ExerciseNotesTopic.NodeId"/> 0 = the master).</summary>
    public static ExerciseNotesTopic ToTopic(Fdp.Core.ExerciseId exerciseId, ExerciseNote[]? notes) => new()
    {
        NodeId     = 0,
        ExerciseId = (Guid)exerciseId,
        NotesJson  = JsonSerializer.Serialize(notes ?? Array.Empty<ExerciseNote>(), FdpJsonOptionsRegistry.DefaultRelaxed),
    };

    /// <summary>The notes in a sample, or <see langword="null"/> when its JSON does not parse (a sample from a newer master is skipped).</summary>
    public static ExerciseNote[]? FromTopic(in ExerciseNotesTopic topic)
    {
        if (string.IsNullOrWhiteSpace(topic.NotesJson)) return null;
        try { return JsonSerializer.Deserialize<ExerciseNote[]>(topic.NotesJson, FdpJsonOptionsRegistry.DefaultRelaxed); }
        catch (JsonException) { return null; }
    }
}
