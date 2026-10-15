using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Transfer;
using Xunit;

namespace Fdp.Toolkits.Tests.Orchestration;

/// <summary>
/// ⭐⭐ <c>P6</c> part 2 (Q91 §4-E2) — the route per target: a node that advertised a share gets the share request unchanged; any
/// other node gets the DDS transport with each item's path made relative to that node's staging root; with no DDS transport
/// every node goes to the share (today's behaviour); and the share token round-trips.
/// </summary>
public sealed class RoutingTransferProviderTests
{
    private sealed class Spy : IFileTransferProvider
    {
        public readonly List<TransferRequest> Requests = new();
        public string Transport => "spy";
        public bool CanReach(TransferTarget target) => true;
        public IFileTransfer Start(TransferRequest request)
        {
            Requests.Add(request);
            return new Done();
        }
        private sealed class Done : IFileTransfer
        {
            public Guid Id { get; } = Guid.NewGuid();
            public TransferStatus Status => new(OrchestrationStatusCode.Success, "", 0, 0, 0, 0, 0, 0, Array.Empty<int>());
            public Task Completion => Task.CompletedTask;
            public void Cancel() { }
        }
    }

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "stage");
    private static string NodeRoot(int id) => Path.Combine(Root, "nodes", $"node-{id}");

    [Fact]
    public void A_node_with_a_share_gets_the_share_and_one_without_gets_dds_with_paths_relative_to_its_root()
    {
        var share = new Spy();
        var dds = new Spy();
        var router = new RoutingTransferProvider(share, dds, hasShare: id => id == 1, nodeRootSeenByMaster: NodeRoot);

        var dir1 = Path.Combine(NodeRoot(1), "scenarios", "s1");
        var dir2 = Path.Combine(NodeRoot(2), "scenarios", "s1");
        router.Start(new TransferRequest(
            new[] { new TransferTarget(1, dir1), new TransferTarget(2, dir2) },
            new[] { new TransferItem("/nas/s1/scenario.json", "scenario.json") }));

        var viaShare = Assert.Single(share.Requests);
        Assert.Equal(new[] { 1 }, viaShare.Targets.Select(t => t.NodeId));
        var viaDds = Assert.Single(dds.Requests);
        Assert.Equal(new[] { 2 }, viaDds.Targets.Select(t => t.NodeId));
        Assert.Equal(Path.Combine("scenarios", "s1", "scenario.json"), viaDds.Items.Single().RelativePath);
    }

    /// <summary>
    /// 🔴 measured live: the orchestrator's own node advertises nothing; routed to DDS it never answered and a load parked
    /// forever. ⭐ A node advertising neither a share nor a DDS receiver goes to the share — today's copy.
    /// </summary>
    [Fact]
    public void A_node_with_no_share_and_no_dds_receiver_goes_to_the_share_never_waits_on_dds()
    {
        var share = new Spy();
        var dds = new Spy();
        var router = new RoutingTransferProvider(share, dds, hasShare: _ => false, nodeRootSeenByMaster: NodeRoot,
                                                 canReceiveDds: id => id == 2);
        router.Start(new TransferRequest(new[] { new TransferTarget(300, NodeRoot(300)), new TransferTarget(2, NodeRoot(2)) },
            new[] { new TransferItem("/nas/x", "x") }));
        Assert.Equal(new[] { 300 }, Assert.Single(share.Requests).Targets.Select(t => t.NodeId));
        Assert.Equal(new[] { 2 }, Assert.Single(dds.Requests).Targets.Select(t => t.NodeId));
    }

    [Fact]
    public void With_no_dds_transport_every_node_goes_to_the_share()
    {
        var share = new Spy();
        var router = new RoutingTransferProvider(share, dds: null, hasShare: _ => false, nodeRootSeenByMaster: NodeRoot);
        router.Start(new TransferRequest(new[] { new TransferTarget(1, NodeRoot(1)), new TransferTarget(2, NodeRoot(2)) },
            new[] { new TransferItem("/nas/x", "x") }));
        Assert.Equal(new[] { 1, 2 }, Assert.Single(share.Requests).Targets.Select(t => t.NodeId));
    }

    [Fact]
    public void The_share_token_round_trips_and_none_means_no_share()
    {
        Assert.Equal("/stage/nodes/node-1", TransferTokens.ParseShare(new[] { "fdp.role.map2d", TransferTokens.Share("/stage/nodes/node-1") }));
        Assert.Null(TransferTokens.ParseShare(new[] { "fdp.role.map2d" }));
    }
}
