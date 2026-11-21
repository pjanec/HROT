using System;
using System.Text.Json;
using Fdp.Core.Serialization;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;

namespace Hrot.Network.Orchestration;

/// <summary>
/// ⭐ CE-4330 ③ fact ① — the ONE encoding of the master's <see cref="StorageJob"/> list on <see cref="StorageJobsTopic"/>: the master's writer (<see cref="ClusterOpMasterTranslator"/>) and every node's reader
/// (<see cref="ClusterFactsIngress"/>) go through here, so the two never drift. Mirrors <see cref="ReplaySessionWire"/>.
/// </summary>
public static class StorageJobsWire
{
    /// <summary>The master's list as a sample (<see cref="StorageJobsTopic.NodeId"/> 0 = the master).</summary>
    public static StorageJobsTopic ToTopic(StorageJob[]? jobs) => new()
    {
        NodeId   = 0,
        JobsJson = JsonSerializer.Serialize(jobs ?? Array.Empty<StorageJob>(), FdpJsonOptionsRegistry.DefaultRelaxed),
    };

    /// <summary>The jobs in a sample, or <see langword="null"/> when its JSON does not parse (a sample from a newer master is skipped).</summary>
    public static StorageJob[]? FromTopic(in StorageJobsTopic topic)
    {
        if (string.IsNullOrWhiteSpace(topic.JobsJson)) return null;
        try { return JsonSerializer.Deserialize<StorageJob[]>(topic.JobsJson, FdpJsonOptionsRegistry.DefaultRelaxed); }
        catch (JsonException) { return null; }
    }
}
