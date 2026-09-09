using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.Orchestration.Preview;
using Fdp.Toolkit.Replication.Services;
using Xunit;

namespace Fdp.Toolkits.Tests.Orchestration;

/// <summary>
/// ⭐⭐⭐ <b><c>HN-017</c> — A PREVIEW LEAVES NO TRACE. The requirement, asserted.</b>
/// 📄 <c>docs/DESIGN_Deterministic_Network_Ids.md</c> §1 *(the requirement)* · §2b *(the enumeration)* ·
/// §4c *(the chosen approach)*.
///
/// <para>🔒 <b>User, `2026-08-23`:</b> <i>"each node needs to remember the ids/chunks used during the run
/// and on world reset to simply reset to their beginning while the central allocatore stays where it is
/// for potential fresh allocations"</i> — and <i>"for repeated runs of the same we would like to have same
/// ids."</i></para>
///
/// <para>⚠⚠ <b>Why the end-to-end proof is a UNIT rail and not the system one.</b> 📐 The system rail would
/// read ids from <c>GET /entities</c>, and <c>HN-015</c> makes that endpoint answer <b>500</b> after any
/// runtime spawn *(a non-finite float inside the scenario serializer that <c>ExtractEntities</c> reuses)*.
/// ⛔ That is a pre-existing defect outside this batch's surface, and its tripwire ships separately. ⭐ These
/// rails assert the same claims against the real bracket and the real allocators, with no API in the way.</para>
/// </summary>
public sealed class APreviewLeavesNoTraceTests
{
    // ══ the guarantee, per allocator shape ═══════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The SCALAR case: capture, spend, restore ⇒ the SAME ids again.</b>
    /// ⭐ Parameterised over both production scalar allocators because 📌 §4b measured that they DISAGREE on
    /// what the counter means — pre-increment *(last issued)* vs post-increment *(next to issue)* — and the
    /// contract has to hold for both. ⛔ A single-implementation rail would have proved the easier half.
    /// </summary>
    [Theory]
    [InlineData(typeof(Hrot.Core.Network.SequentialIdAllocator))]
    public void A_scalar_allocator_reissues_the_same_ids_after_a_restore(System.Type allocatorType)
    {
        var alloc = (INetworkIdAllocator)System.Activator.CreateInstance(allocatorType)!;
        var restorable = Assert.IsAssignableFrom<IRestorableIdAllocator>(alloc);

        // Burn a few so the position is not the initial one — an at-construction capture would pass for
        // the wrong reason.
        _ = alloc.AllocateId();
        _ = alloc.AllocateId();

        var position = restorable.CaptureIssuingPosition();
        Assert.NotNull(position);

        var first = new[] { alloc.AllocateId(), alloc.AllocateId(), alloc.AllocateId() };

        restorable.RestoreIssuingPosition(position!);

        var second = new[] { alloc.AllocateId(), alloc.AllocateId(), alloc.AllocateId() };

        Assert.Equal(first, second);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The POOLED case — this is the half <c>Reset(Read())</c> could never do.</b>
    /// 📄 §4b: <c>BlockIdManager.Reset</c> clears the pool and <b>ignores its argument</b>, so an identity
    /// built on <c>Reset</c> was impossible. ⭐ The user's framing — <i>"remember the ids/chunks used and
    /// reset to their beginning"</i> — makes the pool itself the position, and then it works.
    /// </summary>
    [Fact]
    public void A_pooled_allocator_reissues_the_same_ids_after_a_restore()
    {
        var pool = new BlockIdManager(lowWaterMark: 0);
        pool.AddBlock(start: 5000, count: 20);

        _ = pool.AllocateId();   // spend a couple before capturing

        var position = ((IRestorableIdAllocator)pool).CaptureIssuingPosition();
        Assert.NotNull(position);

        var first = new[] { pool.AllocateId(), pool.AllocateId(), pool.AllocateId() };

        ((IRestorableIdAllocator)pool).RestoreIssuingPosition(position!);

        var second = new[] { pool.AllocateId(), pool.AllocateId(), pool.AllocateId() };

        Assert.Equal(first, second);
    }

    /// <summary>
    /// ⛔⛔ <b>AND THE BOUNDARY, ASSERTED — ids taken from a NEW block mid-preview are NOT re-offered.</b>
    ///
    /// <para>⭐⭐⭐ 📄 §4c states the guarantee as <i>"exact while the preview stays within the ids the node
    /// already held; past that the prefix repeats and the tail differs"</i>. ⚠ <b>This is the half that
    /// keeps it honest:</b> re-offering an id the central authority handed out AFTER the capture would be a
    /// cross-node collision — reproducible-looking and occasionally wrong, the worst outcome.</para>
    ///
    /// <para>⛔ So the restore replaces the pool rather than prepending to it.</para>
    /// </summary>
    [Fact]
    public void Ids_acquired_after_the_capture_are_not_reoffered()
    {
        var pool = new BlockIdManager(lowWaterMark: 0);
        pool.AddBlock(start: 7000, count: 2);

        var position = ((IRestorableIdAllocator)pool).CaptureIssuingPosition();
        Assert.NotNull(position);

        // Drain what we held, then take a FRESH block — the cluster's high-water mark has moved.
        var held = new[] { pool.AllocateId(), pool.AllocateId() };
        pool.AddBlock(start: 9000, count: 2);
        var fresh = pool.AllocateId();

        ((IRestorableIdAllocator)pool).RestoreIssuingPosition(position!);

        // ⭐ The held ids come back…
        Assert.Equal(held, new[] { pool.AllocateId(), pool.AllocateId() });
        // …⛔ and the id from the new block does NOT.
        Assert.Empty(new[] { fresh }.Intersect(held));
        Assert.Equal(0, pool.AvailableCount);
    }

    /// <summary>
    /// ⚠ <b>An allocator with nothing to restore says so — it does not invent a token.</b>
    /// ⭐ That is what lets <see cref="PreviewStateBracket"/> report a non-reproducible preview instead of
    /// promising one. 📌 The silent-default rule: a fake snapshot would make the failure invisible.
    /// </summary>
    [Fact]
    public void An_empty_pool_reports_no_restorable_position()
        => Assert.Null(((IRestorableIdAllocator)new BlockIdManager()).CaptureIssuingPosition());

    // ══ the entity map — the participant that makes the allocator fix SAFE ═══

    /// <summary>
    /// 🔴🔴 <b>THE MAP IS WHY THE ALLOCATOR FIX IS SAFE — and why it could not ship alone.</b>
    ///
    /// <para>📐 §2b: <c>NetworkEntityMap.Register</c> THROWS on a duplicate id, and a preview's rewind does
    /// not touch the map. ⇒ ⛔⛔ the moment ids repeat exactly, preview 2 re-registers preview 1's ids and
    /// <b>throws</b> — the allocator's drift was the only thing hiding it.</para>
    ///
    /// <para>⭐ This rail is the proof the pair is coherent: restore the map, and the same id can be
    /// registered again.</para>
    /// </summary>
    [Fact]
    public void Restoring_the_map_lets_the_same_id_be_registered_again()
    {
        var map = new NetworkEntityMap();
        var before = map.CaptureState();

        map.Register(1008, new Entity(1, 1));
        Assert.True(map.TryGetEntity(1008, out _));

        // ⛔ Without the restore this is the exception the fix would otherwise cause.
        Assert.Throws<System.InvalidOperationException>(() => map.Register(1008, new Entity(2, 1)));

        map.RestoreState(before);

        Assert.False(map.TryGetEntity(1008, out _));
        map.Register(1008, new Entity(2, 1));      // ⭐ no throw — the trace is gone
        Assert.True(map.TryGetEntity(1008, out _));
    }

    /// <summary>
    /// ⭐⭐ <b>The LATE-RESOLVED map — the SimHost ordering, asserted.</b>
    /// 📐 Measured `2026-08-23`: <c>SimHostApp</c> sets the <c>NetworkEntityMap</c> managed singleton
    /// <b>after</b> <c>NodeBootstrapper.BuildOrchestration</c> has registered the preview handler ⇒ ⛔ an
    /// eager participant built at the registration site would throw. ⭐ This rail pins that the
    /// repository-resolved participant works when the singleton appears LATER — i.e. between construction
    /// and the first <c>Capture()</c>.
    /// </summary>
    [Fact]
    public void The_repository_resolved_map_restores_a_singleton_registered_after_construction()
    {
        using var repo = new EntityRepository();
        var participant = PreviewParticipants.EntityMapFromRepository(repo);   // ⭐ built BEFORE the map exists

        var map = new NetworkEntityMap();
        repo.SetSingletonManaged(map);                                          // ⭐ …the SimHost ordering

        var token = participant.Capture();
        Assert.NotNull(token);

        map.Register(2001, new Entity(1, 1));
        participant.Restore(token!);

        Assert.False(map.TryGetEntity(2001, out _));
    }

    /// <summary>
    /// ⚠ <b>A repository with no map says so — it does not fabricate an empty one.</b>
    /// ⛔ Fabricating would let a node report a reproducible preview it cannot deliver; ⭐ and it would also
    /// hide a genuine misconfiguration behind a working-looking restore.
    /// </summary>
    [Fact]
    public void A_repository_with_no_map_singleton_reports_no_position()
    {
        using var repo = new EntityRepository();
        Assert.Null(PreviewParticipants.EntityMapFromRepository(repo).Capture());
    }

    // ══ the bracket — one place, both handlers, and it REPORTS ═══════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The bracket restores every participant, and that is the whole "leaves no trace" claim.</b>
    /// ⭐ Driven through the same object both preview handlers hold, so this rail covers the cluster path and
    /// the editor path at once *(<c>HN-016</c>: there are two handlers, one bracket)*.
    /// </summary>
    [Fact]
    public void The_bracket_restores_every_participant()
    {
        var alloc = new Hrot.Core.Network.SequentialIdAllocator();
        var map   = new NetworkEntityMap();

        var bracket = new PreviewStateBracket(new[]
        {
            PreviewParticipants.IdAllocator(alloc),
            PreviewParticipants.EntityMap(map),
        });

        Assert.Equal(new[] { "id-allocator", "network-entity-map" }, bracket.ParticipantNames);

        bracket.Capture();
        Assert.Empty(bracket.UnrestorableParticipants);

        var first = alloc.AllocateId();
        map.Register(first, new Entity(1, 1));

        bracket.Restore();

        // ⭐ The same id is issued again AND the map will accept it — the two halves together.
        Assert.Equal(first, alloc.AllocateId());
        map.Register(first, new Entity(2, 1));
    }

    /// <summary>
    /// ⭐⭐ <b>A participant that cannot be restored is NAMED, not silently skipped.</b>
    /// ⛔ This is §4c's boundary made visible: an operator or agent must be able to learn that this preview
    /// will not reproduce, rather than discover it from surprising ids.
    /// </summary>
    [Fact]
    public void An_unrestorable_participant_is_reported()
    {
        var bracket = new PreviewStateBracket(new[]
        {
            PreviewParticipants.IdAllocator(new BlockIdManager()),   // empty pool ⇒ no position
        });

        bracket.Capture();

        Assert.Equal(new[] { "id-allocator" }, bracket.UnrestorableParticipants);
    }

    /// <summary>
    /// ⚠ <b>An empty participant list is legal and silent</b> — ExCon, IG and the CGF skeleton carry no ECS
    /// state and pass <c>liveRepo: null</c> too. ⛔ It must not read as a misconfiguration.
    /// </summary>
    [Fact]
    public void No_participants_is_legal()
    {
        var bracket = new PreviewStateBracket(System.Array.Empty<IPreviewRewindable>());
        bracket.Capture();
        bracket.Restore();

        Assert.Empty(bracket.ParticipantNames);
        Assert.Empty(bracket.UnrestorableParticipants);
    }

    /// <summary>
    /// ⭐ <b>An aborted preview restores NOTHING.</b> ⛔ The repo was never rewound, so putting the allocator
    /// back would hand out ids the world still holds.
    /// </summary>
    [Fact]
    public void A_discarded_capture_is_not_applied()
    {
        var alloc = new Hrot.Core.Network.SequentialIdAllocator();
        var bracket = new PreviewStateBracket(new[] { PreviewParticipants.IdAllocator(alloc) });

        bracket.Capture();
        var spent = alloc.AllocateId();
        bracket.Discard();
        bracket.Restore();          // no-op after a discard

        Assert.NotEqual(spent, alloc.AllocateId());
    }
}
