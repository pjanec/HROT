using System;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Orchestration;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// ⭐⭐ <c>C1</c> (Q91 §4-O, <c>R-4009</c>/<c>R-4016</c>) — the master assigns node ids: a process (computer + pid) gets a dynamic id
/// that is never reused in the session, asking again gets the same answer, a forced id held by another LIVE process is refused,
/// and the master answers a request read off its bus with a grant on the same key.
/// </summary>
public sealed class NodeIdAllocatorTests
{
    private static readonly Func<int, bool> NoneLive = _ => false;

    [Fact]
    public void Two_processes_alike_get_different_dynamic_ids_and_asking_again_is_stable()
    {
        var a = new NodeIdAllocator();
        var first  = a.Grant("vm", 100, 0, 0, NoneLive);
        var second = a.Grant("vm", 101, 0, 0, NoneLive);
        Assert.Equal(NodeIdAllocator.FirstDynamicId, first.NodeId);
        Assert.Equal(NodeIdAllocator.FirstDynamicId + 1, second.NodeId);   // ⭐ CE-4001: two Players launched alike no longer collide
        Assert.Equal(first.NodeId, a.Grant("VM", 100, 0, 0, NoneLive).NodeId);   // same process, any case of the name
    }

    [Fact]
    public void An_id_is_never_reused_in_the_session_even_after_its_process_is_gone()
    {
        var a = new NodeIdAllocator();
        var gone = a.Grant("vm", 100, 0, 0, NoneLive).NodeId;
        var next = a.Grant("vm", 200, 0, 0, NoneLive).NodeId;   // the first process is not live any more
        Assert.NotEqual(gone, next);
    }

    [Fact]
    public void A_forced_id_held_by_another_live_process_is_refused_and_a_free_one_is_granted()
    {
        var a = new NodeIdAllocator();
        Assert.Equal(100, a.Grant("vm", 1, forcedId: 100, 0, NoneLive).NodeId);
        var clash = a.Grant("vm", 2, forcedId: 100, 0, id => id == 100);
        Assert.True(clash.Refused);
        Assert.Contains("pid 1", clash.Reason);
        Assert.False(a.Grant("vm", 3, forcedId: 100, 0, NoneLive).Refused);   // its holder is gone: free again
    }

    [Fact]
    public void A_free_preferred_id_is_kept()
    {
        var a = new NodeIdAllocator();
        Assert.Equal(10_042, a.Grant("vm", 7, 0, preferredId: 10_042, NoneLive).NodeId);
    }

    [Fact(Timeout = 10_000)]
    public void The_master_answers_a_request_on_its_bus_with_a_grant_carrying_its_session()
    {
        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, new ClusterConfiguration
        {
            MandatoryRoles = Fdp.Core.NodeRole.None,   // CE-3344: not a readiness suite — default roles would hold the latch closed with no nodes
            Mandatory = Array.Empty<string>(), HeartbeatTimeoutSeconds = 60f, TransactionHistoryCapacity = 10,
        });
        bus.PublishManaged(new NodeIdRequestEvent { ComputerName = "vm", ProcessId = 4242 });
        bus.SwapBuffers();
        master.Tick();
        bus.SwapBuffers();

        var grant = Assert.Single(bus.ReadManaged<NodeIdGrantEvent>());
        Assert.Equal((4242, NodeIdAllocator.FirstDynamicId, false), (grant.ProcessId, grant.NodeId, grant.Refused));
        Assert.Equal(master.SessionId, grant.SessionId);
    }

    /// <summary>
    /// ⭐ CE-4331 ⑥ second half — a node's request carries its KIND and its previous id on the wire (<c>NodeIdRequestWire</c>, the ONE mapping), and the master answers a bus request that names a kind INSIDE its range: a CGF gets 400, a second CGF 401,
    /// a CGF that held 401 before gets 401 back when it is free, a request with no kind a dynamic id. 🔴 Mutations (each compiles): <c>ToEvent</c> dropping <c>SubsystemName</c> ⇒ the wire cell reds; the master not passing <c>req.SubsystemName</c> ⇒ the 400 cell reds.
    /// </summary>
    [Fact(Timeout = 10_000)]
    public void CE4331_TheRequestCarriesItsKind_AndTheMasterGrantsInsideTheKindsRange()
    {
        var sample = Hrot.Network.Orchestration.NodeIdRequestWire.ToTopic("PC1", 7, 0, 401, "CGF");
        Assert.Equal("CGF", sample.SubsystemName);
        var ev = Hrot.Network.Orchestration.NodeIdRequestWire.ToEvent(sample);
        Assert.Equal(("PC1", 7, 0, 401, "CGF"), (ev.ComputerName, ev.ProcessId, ev.ForcedId, ev.PreferredId, ev.SubsystemName));
        Assert.Null(Hrot.Network.Orchestration.NodeIdRequestWire.ToEvent(Hrot.Network.Orchestration.NodeIdRequestWire.ToTopic("PC1", 7, 0, 0, null)).SubsystemName);   // no kind (an older sender) ⇒ none

        var bus = new FdpEventBus();
        using var master = new ClusterMaster(bus, new ClusterConfiguration
        {
            MandatoryRoles = Fdp.Core.NodeRole.None, Mandatory = Array.Empty<string>(), HeartbeatTimeoutSeconds = 60f, TransactionHistoryCapacity = 10,
        });
        int Ask(string computer, int pid, int preferred, string? kind)
        {
            bus.PublishManaged(new NodeIdRequestEvent { ComputerName = computer, ProcessId = pid, PreferredId = preferred, SubsystemName = kind });
            bus.SwapBuffers(); master.Tick(); bus.SwapBuffers();
            return Assert.Single(bus.ReadManaged<NodeIdGrantEvent>()).NodeId.Value;
        }
        Assert.Equal(400, Ask("pc1", 10, 0, "CGF"));
        Assert.Equal(401, Ask("pc2", 11, 0, "CGF"));                                          // the second CGF
        Assert.Equal(402, Ask("pc3", 12, 402, "CGF"));                                        // its previous id, free ⇒ kept
        Assert.Equal(100, Ask("pc4", 13, 0, "Player"));
        Assert.Equal(NodeIdAllocator.FirstDynamicId, Ask("pc5", 14, 0, null));                // no kind ⇒ dynamic
    }

    /// <summary>
    /// ⭐ G-7 (Review Source popup, mockup ③) — a node's name from its id alone, read from the SAME table as the offsets: the kind whose range holds it
    /// and its place there. 🔴 Mutation: NameOf returns "Node N" ⇒ red.
    /// </summary>
    [Fact]
    public void G7_ANodesName_FromItsIdRange_IsItsKindAndPlace()
    {
        Assert.Equal("SimHost-1", NodeIdRanges.NameOf(1));
        Assert.Equal("Player-1", NodeIdRanges.NameOf(100));
        Assert.Equal("CGF-1", NodeIdRanges.NameOf(400));
        Assert.Equal("CGF-2", NodeIdRanges.NameOf(401));
        Assert.Equal("Traffic-1", NodeIdRanges.NameOf(800));
        Assert.Equal("Node 650", NodeIdRanges.NameOf(650));
        Assert.Equal(400, NodeIdRanges.OffsetOf("cgf"));
        Assert.Equal(600, NodeIdRanges.OffsetOf("Editor"));
    }

    /// <summary>
    /// ⭐⭐ CE-4331 ⑥ (📄 docs/DESIGN_Recording_Storage.md §9b) — a node that says its KIND gets the lowest free id of its kind's range (NodeIdRanges): two CGFs
    /// get 400 and 401, a Player 100; the same cluster relaunched under a new master gets the same ids (a new allocator, nothing remembered);
    /// a replacement for a gone CGF under a new master takes the vacant 400; its previous id wins when it is free.
    /// 🔴 Mutation: the range loop removed ⇒ every node gets a dynamic id (10000+) ⇒ red.
    /// </summary>
    [Fact]
    public void CE4331_ANodeOfAKind_GetsTheLowestFreeIdOfItsRange_AndTheSameIdsComeBackOnARelaunch()
    {
        var a = new NodeIdAllocator();
        Assert.Equal(400, a.Grant("pc1", 10, 0, 0, NoneLive, "CGF").NodeId.Value);
        Assert.Equal(401, a.Grant("pc2", 11, 0, 0, NoneLive, "cgf").NodeId.Value);
        Assert.Equal(100, a.Grant("pc3", 12, 0, 0, NoneLive, "Player").NodeId.Value);
        Assert.Equal(1,   a.Grant("pc4", 13, 0, 0, NoneLive, "SimHost").NodeId.Value);

        var relaunch = new NodeIdAllocator();   // a new session: other pids, nothing remembered
        Assert.Equal(400, relaunch.Grant("pc1", 20, 0, 0, NoneLive, "CGF").NodeId.Value);
        Assert.Equal(401, relaunch.Grant("pc2", 21, 0, 0, NoneLive, "CGF").NodeId.Value);

        var replaced = new NodeIdAllocator();   // pc1 died; a new computer of the kind joins while pc2 (401) runs
        Assert.Equal(401, replaced.Grant("pc2", 30, 0, preferredId: 401, NoneLive, "CGF").NodeId.Value);   // its previous id first
        Assert.Equal(400, replaced.Grant("pc9", 31, 0, 0, id => id == 401, "CGF").NodeId.Value);           // the vacant slot

        var full = new NodeIdAllocator();
        Assert.Equal(NodeIdAllocator.FirstDynamicId, full.Grant("pc", 1, 0, 0, id => id >= 400 && id < 500, "CGF").NodeId.Value);   // a full range ⇒ dynamic
    }
}
