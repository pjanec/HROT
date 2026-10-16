using System;
using System.Diagnostics;
using System.Threading;
using CycloneDDS.Runtime;
using Hrot.NED.Descriptors.Orchestration;

namespace Hrot.Network.NED.Factory;

/// <summary>
/// ⭐⭐ <c>C1</c> (Q91 §4-O, <c>R-4009</c>/<c>R-4016</c>) — asks the master for this process's node id, BEFORE the node is composed
/// (its participant bakes the id into sender tracking, so the ask runs on a short-lived participant of its own).
/// ⭐ Identified by computer name + process id. ⛔ No timeout (R-4001): with no master the process waits — its own idle — and says so.
/// </summary>
public static class NodeIdBroker
{
    /// <summary>The master's answer.</summary>
    public readonly record struct Grant(int NodeId, Guid SessionId, bool Refused, string Reason);

    public static Grant AwaitGrant(uint domainId, int forcedId, int preferredId, Action<string> log, CancellationToken ct = default)
    {
        string computer = Environment.MachineName;
        int pid = Environment.ProcessId;
        long hash = NodeIdKeys.ComputerHash(computer);

        using var participant = new DdsParticipant(domainId);
        using var writer = new DdsWriter<NodeIdRequest>(participant);
        using var reader = new DdsReader<NodeIdGrant>(participant);

        // ⭐ Durable (TransientLocal): written once, a master that starts later still receives it.
        writer.Write(new NodeIdRequest
        {
            ComputerHash = hash, ProcessId = pid, ComputerName = computer,
            ForcedId = forcedId, PreferredId = preferredId,
        });
        log($"[NodeId] asked the master for a node id ({computer} pid {pid}) — waiting for a cluster.");

        var sinceLog = Stopwatch.StartNew();
        while (!ct.IsCancellationRequested)
        {
            using (var loan = reader.Take())
                foreach (var sample in loan)
                {
                    if (!sample.IsValid) continue;
                    var g = sample.Data;
                    if (g.ComputerHash != hash || g.ProcessId != pid) continue;   // another process's answer
                    return new Grant(g.NodeId, g.SessionId, g.Refused, g.Reason ?? string.Empty);
                }

            if (sinceLog.Elapsed.TotalSeconds >= 5)
            {
                log("[NodeId] still waiting for a cluster master to grant a node id…");
                sinceLog.Restart();
            }
            Thread.Sleep(20);
        }
        ct.ThrowIfCancellationRequested();
        return default;
    }
}
