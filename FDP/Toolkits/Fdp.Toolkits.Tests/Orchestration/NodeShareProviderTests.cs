using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Transfer;
using Xunit;

namespace Fdp.Toolkits.Tests.Orchestration;

/// <summary>
/// ⭐⭐ <c>P6</c> (Q91 §4-E2, R-4002) — the share transport behind <see cref="IFileTransferProvider"/>: one request serves
/// several targets, a file the target already holds is skipped by the one freshness rule (length + mtime), a failure names
/// its node with a code and a message, and a cancelled transfer leaves no partial file.
/// </summary>
public sealed class NodeShareProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xfer-" + Guid.NewGuid().ToString("N"));

    public NodeShareProviderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string Source(string name, string content, DateTime? mtime = null)
    {
        var dir = Path.Combine(_root, "nas");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, mtime ?? new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));
        return path;
    }

    private TransferTarget Node(int id) => new(id, Path.Combine(_root, "nodes", $"node-{id}"));

    [Fact(Timeout = 10_000)]
    public async Task OneRequest_DeliversEveryItem_ToEveryTarget_KeepingTheSourceTimestamp()
    {
        var a = Source("a.json", "alpha");
        var b = Source("b.zip", "bravo-bytes");
        var targets = new[] { Node(1), Node(2), Node(3) };

        var transfer = new NodeShareProvider().Start(new TransferRequest(targets,
            new[] { new TransferItem(a, "a.json"), new TransferItem(b, Path.Combine("TKB", "b.zip")) }));
        await transfer.Completion;

        var s = transfer.Status;
        Assert.Equal(OrchestrationStatusCode.Success, s.Code);
        Assert.Equal((6, 0, 0, 6), (s.FilesDone, s.FilesSkipped, s.FilesFailed, s.FilesTotal));
        Assert.Equal(s.BytesTotal, s.BytesDone);
        foreach (var t in targets)
        {
            Assert.Equal("alpha", File.ReadAllText(Path.Combine(t.Root, "a.json")));
            var copied = Path.Combine(t.Root, "TKB", "b.zip");
            Assert.Equal(File.GetLastWriteTimeUtc(b), File.GetLastWriteTimeUtc(copied));   // ⭐ the skip rule's identity
            Assert.Empty(Directory.GetFiles(t.Root, "*" + NodeShareProvider.PartialSuffix, SearchOption.AllDirectories));
        }
    }

    [Fact(Timeout = 10_000)]
    public async Task AFileTheTargetAlreadyHolds_IsSkipped_AndAChangedOneIsSent()
    {
        var a = Source("a.json", "alpha");
        var node = Node(1);
        var provider = new NodeShareProvider();
        await provider.Start(new TransferRequest(new[] { node }, new[] { new TransferItem(a, "a.json") })).Completion;

        var again = provider.Start(new TransferRequest(new[] { node }, new[] { new TransferItem(a, "a.json") }));
        await again.Completion;
        Assert.Equal((0, 1), (again.Status.FilesDone, again.Status.FilesSkipped));

        Source("a.json", "alpha-2", new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc));
        var changed = provider.Start(new TransferRequest(new[] { node }, new[] { new TransferItem(a, "a.json") }));
        await changed.Completion;
        Assert.Equal((1, 0), (changed.Status.FilesDone, changed.Status.FilesSkipped));
        Assert.Equal("alpha-2", File.ReadAllText(Path.Combine(node.Root, "a.json")));
    }

    [Fact(Timeout = 10_000)]
    public async Task AFailingTarget_IsNamed_WithACodeAndAMessage_AndTheOthersStillReceive()
    {
        var a = Source("a.json", "alpha");
        var blocker = Path.Combine(_root, "blocker");
        File.WriteAllText(blocker, "a file, so no directory can be created under it");
        var bad = new TransferTarget(7, Path.Combine(blocker, "sub"));

        var transfer = new NodeShareProvider().Start(new TransferRequest(new[] { Node(1), bad },
            new[] { new TransferItem(a, "a.json") }));
        await transfer.Completion;

        var s = transfer.Status;
        Assert.Equal(OrchestrationStatusCode.Failure, s.Code);
        Assert.Equal(new[] { 7 }, s.FailedNodeIds);
        Assert.Contains("node 7", s.Message);
        Assert.True(File.Exists(Path.Combine(Node(1).Root, "a.json")));
    }

    [Fact(Timeout = 10_000)]
    public async Task AMissingSource_FailsLoudly_NeverAsAnEmptySuccess()
    {
        var transfer = new NodeShareProvider().Start(new TransferRequest(new[] { Node(1) },
            new[] { new TransferItem(Path.Combine(_root, "nas", "absent.zip"), "absent.zip") }));
        await transfer.Completion;
        Assert.Equal(OrchestrationStatusCode.Failure, transfer.Status.Code);
        Assert.Equal(1, transfer.Status.FilesFailed);
    }

    [Fact(Timeout = 10_000)]
    public async Task ACancelledTransfer_ReportsCancelled_AndLeavesNoPartialFile()
    {
        var a = Source("a.json", "alpha");
        var transfer = new NodeShareProvider().Start(new TransferRequest(
            Enumerable.Range(1, 40).Select(Node).ToArray(), new[] { new TransferItem(a, "a.json") }));
        transfer.Cancel();
        await transfer.Completion;   // ⛔ never faults

        Assert.True(transfer.Status.IsFinished);
        Assert.Contains(transfer.Status.Code, new[] { OrchestrationStatusCode.Cancelled, OrchestrationStatusCode.Success });
        var nodes = Path.Combine(_root, "nodes");
        if (Directory.Exists(nodes))
            Assert.Empty(Directory.GetFiles(nodes, "*" + NodeShareProvider.PartialSuffix, SearchOption.AllDirectories));
    }

    [Fact]
    public void ATargetWithNoShare_IsNotReachable_AndStartRefusesIt()
    {
        var provider = new NodeShareProvider();
        var noShare = new TransferTarget(3, string.Empty);
        Assert.False(provider.CanReach(noShare));
        Assert.Throws<ArgumentException>(() => provider.Start(new TransferRequest(new[] { noShare }, Array.Empty<TransferItem>())));
    }
}
