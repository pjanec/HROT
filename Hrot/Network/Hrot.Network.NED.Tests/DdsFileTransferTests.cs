using System;
using System.IO;
using System.Linq;
using System.Threading;
using CycloneDDS.Runtime;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Transfer;
using Hrot.Network.NED.Transfer;
using Xunit;

namespace Hrot.Network.NED.Tests;

/// <summary>
/// ⭐⭐⭐ <c>P6</c> part 2 (Q91 §4-E2, <c>R-4002</c>) — the DDS file transport over REAL DDS, in one process: one send serves three
/// targets, a bystander receives nothing and joins no partition, every target holds the bytes AND the sender's mtime, a target
/// that already holds a file is not sent it again, a missing source fails loudly with every target named, and every receiver
/// closes its transfer (its partition reader is disposed).
/// </summary>
public sealed class DdsFileTransferTests : IDisposable
{
    private const uint Domain = 179u;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ddsxfer-" + Guid.NewGuid().ToString("N"));

    public DdsFileTransferTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string NodeRoot(int id) => Path.Combine(_root, $"node-{id}");

    private string Source(string name, int bytes, DateTime mtime)
    {
        var dir = Path.Combine(_root, "nas");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        var data = new byte[bytes];
        new Random(bytes).NextBytes(data);
        File.WriteAllBytes(path, data);
        File.SetLastWriteTimeUtc(path, mtime);
        return path;
    }

    /// <summary>Runs the transfer while ticking every receiver on this thread (as their nodes' frame threads would).</summary>
    private static TransferStatus Run(IFileTransferProvider provider, TransferRequest request, params DdsFileReceiver[] receivers)
    {
        var transfer = provider.Start(request);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!transfer.Completion.IsCompleted && DateTime.UtcNow < deadline)
        {
            foreach (var r in receivers) r.Tick();
            Thread.Sleep(2);
        }
        Assert.True(transfer.Completion.IsCompleted, "the transfer did not finish: " + transfer.Status.Message);
        for (int i = 0; i < 20; i++) { foreach (var r in receivers) r.Tick(); Thread.Sleep(5); }   // let the last acks land
        return transfer.Status;
    }

    [Fact(Timeout = 60_000)]
    public void One_send_serves_three_targets_intact_and_a_bystander_gets_nothing()
    {
        var mtime = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var big   = Source("terrain.bin", 5 * TransferWire.ChunkBytes + 123, mtime);   // several chunks, a partial last one
        var small = Source("scenario.json", 40, mtime);

        using var masterP = new DdsParticipant(Domain);
        using var p1 = new DdsParticipant(Domain);
        using var p2 = new DdsParticipant(Domain);
        using var p3 = new DdsParticipant(Domain);
        using var pb = new DdsParticipant(Domain);
        using var provider = new DdsFileTransferProvider(masterP);
        using var r1 = new DdsFileReceiver(p1, 1, NodeRoot(1));
        using var r2 = new DdsFileReceiver(p2, 2, NodeRoot(2));
        using var r3 = new DdsFileReceiver(p3, 3, NodeRoot(3));
        using var bystander = new DdsFileReceiver(pb, 9, NodeRoot(9));
        Thread.Sleep(500);   // discovery

        var request = new TransferRequest(
            new[] { new TransferTarget(1, ""), new TransferTarget(2, ""), new TransferTarget(3, "") },
            new[] { new TransferItem(big, "Terrain/t1/terrain.bin"), new TransferItem(small, "scenarios/s1/scenario.json") });

        var status = Run(provider, request, r1, r2, r3, bystander);

        Assert.Equal(OrchestrationStatusCode.Success, status.Code);
        Assert.Equal(6, status.FilesDone);
        foreach (var id in new[] { 1, 2, 3 })
        {
            var gotBig = Path.Combine(NodeRoot(id), "Terrain", "t1", "terrain.bin");
            Assert.Equal(File.ReadAllBytes(big), File.ReadAllBytes(gotBig));
            Assert.Equal(mtime, File.GetLastWriteTimeUtc(gotBig));   // ⭐ the freshness rule's identity survives the wire
            Assert.Equal(File.ReadAllBytes(small), File.ReadAllBytes(Path.Combine(NodeRoot(id), "scenarios", "s1", "scenario.json")));
            Assert.Empty(Directory.GetFiles(NodeRoot(id), "*" + NodeShareProvider.PartialSuffix, SearchOption.AllDirectories));
        }
        Assert.False(Directory.Exists(NodeRoot(9)), "a bystander must receive nothing");
        Assert.Equal(0, r1.ActiveTransfers + r2.ActiveTransfers + r3.ActiveTransfers + bystander.ActiveTransfers);

        // ⭐ the same files again: every target already holds them — nothing is sent
        var again = Run(provider, request, r1, r2, r3, bystander);
        Assert.Equal(OrchestrationStatusCode.Success, again.Code);
        Assert.Equal((0, 6), (again.FilesDone, again.FilesSkipped));
    }

    [Fact(Timeout = 60_000)]
    public void A_missing_source_fails_loudly_and_names_every_target()
    {
        using var masterP = new DdsParticipant(Domain + 1);
        using var p1 = new DdsParticipant(Domain + 1);
        using var provider = new DdsFileTransferProvider(masterP);
        using var r1 = new DdsFileReceiver(p1, 1, NodeRoot(1));

        var status = Run(provider, new TransferRequest(new[] { new TransferTarget(1, "") },
            new[] { new TransferItem(Path.Combine(_root, "nas", "absent.zip"), "TKB/absent.zip") }), r1);

        Assert.Equal(OrchestrationStatusCode.Failure, status.Code);
        Assert.Equal(new[] { 1 }, status.FailedNodeIds);
        Assert.True(status.FilesFailed > 0);
    }
}
