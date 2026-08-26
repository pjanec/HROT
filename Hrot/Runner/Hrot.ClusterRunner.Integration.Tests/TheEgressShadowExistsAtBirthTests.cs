using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Hrot.Map.Common;
using Hrot.NED.Common;
using Hrot.NED.Descriptors;
using CoreGeoPoint = Hrot.Core.Mission.GeoPoint;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>AX-011</c> — the egress shadow exists at birth on the node that OWNS <c>SimTransform</c>.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §13 · tracker <c>AX-009</c>/<c>AX-011</c>.</para>
///
/// <para>🔴 <b>THE DEFECT THESE RAILS EXIST FOR, measured `2026-08-26`.</b>
/// <c>GeoSpatialEgressTranslator.ScanAndPublish</c> queries
/// <c>SimTransform</c> + <c>NetworkTransform</c> + <c>NetworkIdentity</c>. ⛔ The production TKB catalog never
/// declares <c>NetworkTransform</c> and nothing on the owner side attached it, so that query matched
/// <b>ZERO</b> entities and SimHost published <b>no <c>WorldPos</c> at all</b>. The IG ghost therefore never
/// received <c>SimTransform</c> — a <b>HARD</b> mandatory component — so <c>GhostPromotionSystem</c>
/// correctly declined to promote it, forever. ⇒ <b>21 of 51</b> tests in this assembly failed on one
/// missing component.</para>
///
/// <para>⭐⭐ <b>Why these rails are worth their runtime.</b> ⛔ A unit rail on the translator would have
/// passed: hand it an entity with the shadow and it publishes correctly. The defect was that <b>nothing
/// created the shadow</b> — a gap between two correct components, which only a real spawn on a real
/// cluster can see. 📌 The same lesson as <c>AX-010</c>.</para>
/// </summary>
public class TheEgressShadowExistsAtBirthTests
{
    private const int SettleFrames = 120;

    private readonly ITestOutputHelper _out;
    public TheEgressShadowExistsAtBirthTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// ⭐⭐⭐ <b>THE INVARIANT, stated positively: an owned entity with a <c>SimTransform</c> carries the
    /// shadow.</b>
    ///
    /// <para>⭐ Asserted on a REAL spawn through <c>NetworkSpawningSystem</c>, not on a hand-built entity —
    /// the gap was in the spawn path, so that is where it must be checked.</para>
    /// </summary>
    [Fact]
    public void AnOwnedEntityCarriesTheEgressShadowFromBirth()
    {
        using var harness = new HrotRunnerHarness();

        long networkId = Spawn(harness);
        var shWorld = harness.SimHost.World!;
        Assert.True(harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out var e));

        Assert.True(shWorld.HasComponent<SimTransform>(e), "precondition: the owner has a SimTransform");
        Assert.True(shWorld.HasComponent<NetworkTransform>(e),
            "AX-011: an owned entity with a SimTransform must carry the NetworkTransform egress shadow. " +
            "Without it GeoSpatialEgressTranslator's query skips the entity and NO WorldPos is ever published.");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The translator's OWN query, reproduced verbatim, must match the entity.</b>
    ///
    /// <para>⭐⭐ This is the rail that would have caught the original defect in one line. ⛔ Asserting only
    /// *"the component is present"* is weaker: it would still pass if the translator's query later grew a
    /// fourth clause nothing satisfies. ⭐ Reproducing the query couples the rail to the actual consumer.
    /// 📐 Before the fix this matched <b>0</b>; without the <c>NetworkTransform</c> clause it matched 1.</para>
    /// </summary>
    [Fact]
    public void TheEgressQueryActuallyMatchesTheOwnedEntity()
    {
        using var harness = new HrotRunnerHarness();

        Spawn(harness);
        var shWorld = harness.SimHost.World!;

        int matches = 0;
        foreach (var _ in shWorld.Query()
                     .With<SimTransform>()
                     .With<NetworkTransform>()
                     .With<NetworkIdentity>()
                     .WithLifecycle(EntityLifecycle.All)
                     .Build())
            matches++;

        _out.WriteLine($"GeoSpatialEgressTranslator's query matches {matches} entities");
        Assert.True(matches >= 1,
            "AX-011: GeoSpatialEgressTranslator's own scan query must match the spawned owned entity. " +
            "0 here means the egress is silently inert — the exact shape of the original defect.");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The OUTCOME, not the mechanism: <c>WorldPos</c> reaches the wire.</b>
    ///
    /// <para>⭐ The two rails above can both pass while nothing is published *(a broken authority check, a
    /// dead egress system)*. ⛔ This one is the property the cluster actually needs, observed with an
    /// independent DDS reader on the harness domain. 📐 Before the fix: **0 samples across 300 frames**.</para>
    /// </summary>
    [Fact]
    public void TheOwnerActuallyPublishesWorldPosToTheWire()
    {
        using var harness = new HrotRunnerHarness();

        // ⚠ Subscribe BEFORE the spawn — WorldPos is volatile; a late reader misses the samples.
        using var observer = new CycloneDDS.Runtime.DdsParticipant((uint)harness.DomainId);
        using var reader   = new CycloneDDS.Runtime.DdsReader<WorldPos>(observer, "WorldPos");
        harness.PumpFrames(10);

        Spawn(harness);

        int samples = 0;
        for (int i = 0; i < 10; i++)
        {
            harness.PumpFrames(20);
            using var loan = reader.Take();
            foreach (var s in loan) if (s.IsValid) samples++;
        }

        _out.WriteLine($"WorldPos samples observed on the wire: {samples}");
        Assert.True(samples > 0,
            "AX-011: the owning node must publish WorldPos for an entity it owns. 0 samples means the " +
            "egress scan found nothing — no other node can ever learn where this entity is.");
    }

    /// <summary>
    /// ⭐⭐ <b>The owner has AUTHORITY over its own shadow, not merely the component.</b>
    ///
    /// <para>⚠ Authority is a separate bit from presence *(<c>AX-006</c>)*, and the spawn path snapshots the
    /// component mask into the authority mask at one specific instant — so a shadow attached at the wrong
    /// moment would be **present but unowned**. ⭐ The hook grants it explicitly rather than relying on that
    /// ordering, and this rail is what keeps the grant honest.</para>
    /// </summary>
    [Fact]
    public void TheOwnerHasAuthorityOverItsOwnShadow()
    {
        using var harness = new HrotRunnerHarness();

        long networkId = Spawn(harness);
        var shWorld = harness.SimHost.World!;
        Assert.True(harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out var e));

        Assert.True(shWorld.HasAuthority<NetworkTransform>(e),
            "AX-011: the owner must hold authority over the shadow it writes every tick.");
        Assert.True(shWorld.HasAuthority<SimTransform>(e),
            "…and over the SimTransform it shadows.");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The shadow is seeded to ZEROS, and that is a behavioural requirement rather than a detail.</b>
    ///
    /// <para>⭐⭐ The translator publishes only when the live pose differs from the shadow, or when the salted
    /// heartbeat fires at <c>% 600</c> ticks. ⛔ Seeding the shadow from the entity's CURRENT
    /// <c>SimTransform</c> would make the first comparison say *"has not moved"*, so a stationary spawned
    /// entity would be invisible to every other node for up to <b>600 ticks — 10 s at 60 Hz</b>. ⇒ this rail
    /// asserts the FIRST publish is prompt, which is the observable consequence of correct seeding.</para>
    ///
    /// <para>⚠ Stated as a bound, not an exact frame: the salt staggers entities deliberately, and asserting
    /// an exact tick would rail the salt rather than the seeding.</para>
    /// </summary>
    [Fact]
    public void AStationaryOwnedEntityIsPublishedPromptlyNotAfterTheHeartbeat()
    {
        using var harness = new HrotRunnerHarness();

        using var observer = new CycloneDDS.Runtime.DdsParticipant((uint)harness.DomainId);
        using var reader   = new CycloneDDS.Runtime.DdsReader<WorldPos>(observer, "WorldPos");
        harness.PumpFrames(10);

        Spawn(harness, pumpFrames: 0);   // ⚠ do not settle — we are timing the FIRST publish

        int firstSampleFrame = -1;
        for (int frame = 1; frame <= 300 && firstSampleFrame < 0; frame++)
        {
            harness.PumpFrames(1);
            using var loan = reader.Take();
            foreach (var s in loan) if (s.IsValid) { firstSampleFrame = frame; break; }
        }

        _out.WriteLine($"first WorldPos sample at frame {firstSampleFrame} (heartbeat interval is 600)");
        Assert.True(firstSampleFrame >= 0 && firstSampleFrame < 300,
            "AX-011: the first WorldPos must not wait for the 600-tick heartbeat. A shadow seeded from the " +
            "current SimTransform makes the first change-detection compare say 'unchanged', which hides a " +
            "stationary entity from the cluster for ~10 s at 60 Hz.");
    }

    /// <summary>
    /// ⭐⭐ <b>A REPLICA is not given the shadow by this path — and needs no help.</b>
    ///
    /// <para>⭐ The fix is deliberately scoped to the owner: a replica's shadow is written by
    /// <c>GeoSpatialIngressTranslator</c> on first receipt. ⛔ Attaching it to every ghost at birth would
    /// spend 28 bytes per replica duplicating what the ingress path already provisions.</para>
    ///
    /// <para>⚠ Asserted as *"the IG's copy arrives WITH a position"*, not as *"the IG has no shadow"* — by
    /// the time replication settles the ingress translator has legitimately created it, so asserting
    /// absence would rail a race. ⭐ What matters is that the replica ends up correct.</para>
    /// </summary>
    [Fact]
    public void AReplicaGetsItsShadowFromIngressNotFromBirth()
    {
        using var harness = new HrotRunnerHarness();

        long networkId = Spawn(harness);
        var igWorld = harness.Ig.App.World;

        bool arrived = harness.PumpUntil(() =>
            harness.Ig.App.TestHook_EntityMap.TryGetEntity(networkId, out var ig)
            && igWorld.IsAlive(ig)
            && igWorld.HasComponent<SimTransform>(ig),
            SettleFrames);

        Assert.True(arrived,
            "AX-011: the replica must receive a SimTransform — that is what the owner's WorldPos carries, " +
            "and what GhostPromotionSystem's HARD requirement waits for.");

        harness.Ig.App.TestHook_EntityMap.TryGetEntity(networkId, out var igE);
        var pos = igWorld.GetComponent<SimTransform>(igE).Position;
        _out.WriteLine($"IG replica position: ({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2})");
        Assert.NotEqual(Vector3.Zero, pos);
    }

    // ── helper ───────────────────────────────────────────────────────────────

    private static long Spawn(HrotRunnerHarness harness, int pumpFrames = SettleFrames)
    {
        long networkId = harness.SimHost.TestHook_SpawnEntity(
            TkbEntityTypes.Tank_M1Abrams,
            new CoreGeoPoint { Latitude = 52.521, Longitude = 13.406, Altitude = 0 });

        if (pumpFrames > 0) harness.PumpFrames(pumpFrames);
        return networkId;
    }
}
