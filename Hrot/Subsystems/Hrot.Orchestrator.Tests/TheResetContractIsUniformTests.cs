using Fdp.Toolkit.NetworkSpawning;
using Hrot.Core.Network;
using Xunit;

namespace Hrot.Orchestrator.Tests;

/// <summary>
/// 🔴🔴 <b><c>HN-037</c> — <c>Reset(X)</c> MEANS ONE THING.</b>
/// 📄 <c>docs/DESIGN_Deterministic_Network_Ids.md</c> §11.
///
/// <para><b>Why this rail exists, and it is not the rail the design asked for.</b> §11 assumed one
/// allocation path could be had by pointing every load at one authority. 📐 Measured while building:
/// <c>Reset(1000)</c> gave <b>three different answers</b> across the five production allocators —</para>
/// <list type="table">
///   <item><term><c>1000</c></term><description>the editor's nested allocator, <c>DdsIdAllocator</c>
///   (post-increment / server-assigned)</description></item>
///   <item><term><c>1001</c></term><description><c>Hrot.Core.SequentialIdAllocator</c>,
///   <c>IgSequentialIdAllocator</c> — both PRE-increment, so the counter <i>was</i> the last id
///   issued</description></item>
///   <item><term>throws</term><description><c>BlockIdManager</c> — a pool with nothing to seed from</description></item>
/// </list>
/// <para>⇒ ⛔ <i>"one single allocation path"</i> could not be true while the reset meant three things: the
/// same <c>ResetToBase(1000)</c> would have produced a first authored id of 1000 on a DDS cluster and 1001
/// on a headless one, and the cross-host parity rail would have caught it only in the configuration it
/// happens to run in. ⭐ The contract is therefore stated in terms of <b>the next id issued</b> — the
/// observable — rather than a counter no two implementations agree on.</para>
///
/// <para>⚠ <b>What this rail can and cannot reach.</b> The editor's authority is a PRIVATE NESTED class and
/// <c>IgSequentialIdAllocator</c> is <c>internal</c>, so neither is constructible from here; the DDS pair
/// needs a live participant and is covered by <c>Fdp.Network.Cyclone.Tests</c>'
/// <c>GlobalReset_ResetsServerCounter</c> *(which already asserts "next allocations should start from
/// 5000")*. ⇒ this covers the one that was BOTH wrong and reachable, and the system parity rail
/// (<c>ClusterConformanceRails</c>) covers the editor end-to-end.</para>
/// </summary>
public sealed class TheResetContractIsUniformTests
{
    /// <summary>
    /// ⭐⭐ The contract, on the allocator that was violating it: after <c>Reset(1000)</c> the very next id is
    /// <b>1000</b>, not 1001.
    /// </summary>
    [Fact]
    public void After_reset_the_next_id_is_the_requested_one()
    {
        using var allocator = new SequentialIdAllocator();

        allocator.Reset(WorldIdAuthority.WorldBase);

        Assert.Equal(WorldIdAuthority.WorldBase, allocator.AllocateId());
    }

    /// <summary>⭐ And it keeps counting from there — the "one monotonic sequence" half of §11b.</summary>
    [Fact]
    public void The_sequence_continues_from_the_base()
    {
        using var allocator = new SequentialIdAllocator();

        allocator.Reset(WorldIdAuthority.WorldBase);

        Assert.Equal(new[] { 1000L, 1001L, 1002L },
            new[] { allocator.AllocateId(), allocator.AllocateId(), allocator.AllocateId() });
    }

    /// <summary>
    /// ⭐⭐ <b>A reload really does start over.</b> 📌 This is the drift §11a measured: a second
    /// <c>LoadScenarioByName</c> in one process used to allocate <c>1008–1015</c> because nothing reset.
    /// ⚠ Asserted on a SECOND reset after ids were spent, so a no-op <c>Reset</c> cannot pass it.
    /// </summary>
    [Fact]
    public void A_second_world_reset_starts_the_sequence_over()
    {
        using var allocator = new SequentialIdAllocator();

        allocator.Reset(WorldIdAuthority.WorldBase);
        for (int i = 0; i < 8; i++) allocator.AllocateId();      // the seven authored entities and one more

        allocator.Reset(WorldIdAuthority.WorldBase);

        Assert.Equal(WorldIdAuthority.WorldBase, allocator.AllocateId());
    }

    /// <summary>
    /// ⭐ The adapter the editor's master uses is a pass-through to that same contract — ⛔ it does NOT
    /// compensate per implementation, which would put the divergence back one layer down.
    /// </summary>
    [Fact]
    public void The_world_authority_adapter_honours_the_same_contract()
    {
        using var allocator = new SequentialIdAllocator();

        WorldIdAuthority.FromAllocator(allocator).ResetToBase(WorldIdAuthority.WorldBase);

        Assert.Equal(WorldIdAuthority.WorldBase, allocator.AllocateId());
    }
}
