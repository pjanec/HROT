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
}
