using System;
using System.Numerics;
using CoreGeoPoint = Hrot.Core.Mission.GeoPoint;
using Fdp.Core;
using Fdp.Modules.Geographic.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Patching;
using Hrot.Map.Common;
using Hrot.NED.Common;
using Hrot.SimHost.Installers;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>AX-005</c> — THE ROUND TRIP, on a real <c>--mode all</c> cluster.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §11 · discharges §9.4's open item.</para>
///
/// <para>⭐⭐ <b>What the unit rails could not prove.</b> <c>TheGateCannotBeForgottenTests</c> proves the
/// ROUTER picks <c>Requested</c> for an unowned component, and that the OWNER's installer applies
/// <c>GeoHeading</c> through the existing compass conversion. ⛔ Neither proves the two halves are joined:
/// the FDP-internal command has to reach the egress translator, become a DDS sample, cross a real
/// CycloneDDS domain, be picked up by the owner's request system, and pass its authority gate. ⭐ Every one
/// of those is a registration that can simply be absent — 📌 the failure mode this programme keeps
/// finding *(a capability that looks built and does nothing)*.</para>
///
/// <para>⭐⭐⭐ <b><c>HrotRunnerHarness</c> IS the multi-node cluster</b> — measured in the <c>CE-029</c>
/// barrier work: it boots Orchestrator + SimHost + IG + ExCon as separate subsystems on ONE real
/// CycloneDDS domain, which is exactly what <c>--mode all</c> runs. ⇒ this is not a mock of the round
/// trip, it is the round trip.</para>
///
/// <para>⚠ <b>The IG is the non-owning node here</b>, not CGF: the harness always boots it, and the
/// property under test is *"a node that does not own the component"* — which IG is, in exactly the way
/// CGF is. ⛔ Naming CGF specifically would test the harness's optional subsystem, not the seam.</para>
/// </summary>
public class AttributeChangeRequestRoundTripTests
{
    private const int SpawnTimeoutFrames     = 120;
    private const int RoundTripTimeoutFrames = 300;

    /// <summary>⚠ Quaternion components, after a float32 wire round trip through degrees.</summary>
    private const float RotationTolerance = 1e-3f;

    private readonly ITestOutputHelper _out;

    public AttributeChangeRequestRoundTripTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// ⭐⭐⭐ <b>THE EGRESS HALF, on the real cluster — and it is green.</b>
    ///
    /// <para>⭐⭐ An unowned write on the IG must leave the node as a DDS <c>UpdateEntityAttributeRequest</c>
    /// carrying the BINARY record. That exercises every link this batch built: the router's
    /// <c>Requested</c> branch → <c>EntityAttributeChangeRequests</c> → the world bus → the
    /// <b>registered</b> <c>UpdateEntityAttributeCommandEgressTranslator</c> → <c>R-134</c>'s conversion →
    /// the wire. ⛔ Every one of those is a registration that could simply be absent, which is exactly what
    /// a unit rail cannot see.</para>
    ///
    /// <para>⭐⭐⭐ <b>Why the entity is created LOCALLY rather than replicated in.</b> 📐 Measured
    /// <c>2026-08-25</c> against the clean tree at <c>03f92fefe</c>: SimHost→IG entity replication does not
    /// complete in this environment — <c>DragDropIntegrationTests</c> fails identically with *"IG did not
    /// receive entity (netId=1)"* on a build with NONE of this batch's changes. ⇒ ⭐ a locally-created
    /// entity with a <c>NetworkIdentity</c> and no <c>SimTransform</c> is the SAME shape the router sees
    /// for a replica it does not own *(nothing local to write ⇒ ask the owner)*, and it does not depend on
    /// the broken link. ⚠ Stated plainly: this is a deliberate narrowing around a PRE-EXISTING defect, not
    /// a claim that the narrowing is as strong as the full trip — the full trip is
    /// <see cref="ANonOwningNodeRotatesASimHostOwnedEntity"/>, and it is red for that reason.</para>
    ///
    /// <para>⭐ <b>The red-proof</b> *(by inverse edit, not kept in the tree)*: remove the binary arm from
    /// <c>UpdateEntityAttributeCommandEgressTranslator.ScanAndPublish</c> — the command then carries no
    /// records, the translator's own "neither arm" guard skips it, and no sample arrives.</para>
    /// </summary>
    [Fact]
    public void AnUnownedWriteLeavesTheNodeAsADdsChangeRequest()
    {
        using var harness = new HrotRunnerHarness();

        var igWorld = harness.Ig.App.World;

        // ⭐ The unowned-replica shape: a network identity, and nothing local to write.
        const long ReplicaNetworkId = 987_654L;
        var ghost = igWorld.CreateEntity();
        igWorld.AddComponent(ghost, new NetworkIdentity { Value = ReplicaNetworkId });

        const double TargetHeadingDeg = 137.0;

        using var observerParticipant = new CycloneDDS.Runtime.DdsParticipant((uint)harness.DomainId);
        using var reader = new CycloneDDS.Runtime.DdsReader<Hrot.NED.Messages.UpdateEntityAttributeRequest>(
            observerParticipant, "UpdateEntityAttributeRequest");

        // ⚠ Discovery first — a reader created after the write misses the sample.
        harness.PumpFrames(20);

        var writer = EntityWriteRouter.For(igWorld);
        var route  = writer.Write(ghost, AttributeIds.GeoHeading, TargetHeadingDeg);
        _out.WriteLine($"[C1] IG write route = {route}");
        Assert.Equal(EntityWriteRoute.Requested, route);

        // ⚠ The DDS message is a STRUCT, so this is a value + a found flag rather than a nullable.
        var  seen  = default(Hrot.NED.Messages.UpdateEntityAttributeRequest);
        bool found = false;
        harness.PumpUntil(() =>
        {
            // ⚠ `DdsLoan`/`DdsSample` are ref structs — they live entirely inside this lambda; only the
            //   managed `Data` payload escapes.
            using var loan = reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                if (sample.Data.EntityId != (int)ReplicaNetworkId) continue;
                seen  = sample.Data;
                found = true;
                return true;
            }
            return false;
        }, RoundTripTimeoutFrames);

        Assert.True(found,
            "No UpdateEntityAttributeRequest reached the wire. The change-request path broke between the " +
            "router and DDS: EntityAttributeChangeRequests → world bus → " +
            "UpdateEntityAttributeCommandEgressTranslator (SharedTranslatorPack) → DdsWriter.");

        _out.WriteLine($"[C2] sample entityId={seen.EntityId} records={seen.AttributeRecords?.Count ?? 0}");

        // ⭐⭐ R-134's conversion, asserted on the WIRE: the internal kind became the network type.
        Assert.NotNull(seen.AttributeRecords);
        var record = Assert.Single(seen.AttributeRecords!);
        Assert.Equal(AttributeIds.GeoHeading, record.AttributeId);
        Assert.Equal(Hrot.NED.Messages.AttributeValueType.KindFloat64, record.Value.ValueType);
        Assert.Equal(TargetHeadingDeg, record.Value.DoubleValue, 6);

        // ⚠ And the JSON arm stayed empty — ⛔ sending both would apply the change twice on the owner.
        Assert.True(string.IsNullOrEmpty(seen.AttributePatchJson));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>A non-owning node rotates a SimHost-owned entity, and SimHost applies it.</b>
    ///
    /// <para>🔴🔴 <b>THIS RAIL IS RED ON THIS BASE, AND NOT BECAUSE OF THIS BATCH.</b> 📐 Measured
    /// <c>2026-08-25</c> on a CLEAN tree at <c>03f92fefe</c> *(the started-marker — none of Axis-B's
    /// changes present, 0 build errors)*: <c>DragDropIntegrationTests</c> fails at the SAME step with
    /// *"IG did not receive entity (netId=1) within 120 frames"*, and 21 of this assembly's tests fail
    /// the same way before the host process crashes. ⇒ SimHost→IG entity replication does not complete in
    /// this environment, so the rail cannot reach the gesture it is about.</para>
    ///
    /// <para>⛔ <b>Kept rather than skipped</b> *(<c>R-131</c>: do not filter around a defect)</b>. ⭐ It is
    /// the rail the design's §9.4 asks for, and it turns green the moment replication does — 📌 which makes
    /// it a live probe on that defect rather than a note in a report nobody re-reads. ⭐ The half of the
    /// trip that CAN be proven here is proven, green, by
    /// <see cref="AnUnownedWriteLeavesTheNodeAsADdsChangeRequest"/> *(request reaches the wire)* and
    /// <see cref="TheOwningNodeWritesTheSameAttributeDirectly"/> *(the owner applies it)*.</para>
    /// </summary>
    [Fact]
    public void ANonOwningNodeRotatesASimHostOwnedEntity()
    {
        using var harness = new HrotRunnerHarness();

        // ── 1. SimHost spawns and OWNS a tank ─────────────────────────────────
        long tkbType   = TkbEntityTypes.Tank_M1Abrams;
        var  spawnGeo  = new CoreGeoPoint { Latitude = 52.521, Longitude = 13.406, Altitude = 0 };
        long networkId = harness.SimHost.TestHook_SpawnEntity(tkbType, spawnGeo);
        _out.WriteLine($"[A1] SimHost spawned networkId={networkId}");

        // ── 2. Wait for the replica to reach the IG ───────────────────────────
        Assert.True(
            harness.PumpUntil(() => IgHasEntity(harness, networkId), SpawnTimeoutFrames),
            $"IG never received entity netId={networkId} within {SpawnTimeoutFrames} frames. " +
            "🔴 PRE-EXISTING (measured on the clean tree at 03f92fefe): SimHost→IG entity replication " +
            "does not complete in this environment — DragDropIntegrationTests fails identically with " +
            "none of Axis-B present. This rail is blocked on THAT defect, not on the change-request " +
            "path; see AnUnownedWriteLeavesTheNodeAsADdsChangeRequest for the half that is provable here.");

        var igWorld = harness.Ig.App.World;
        harness.Ig.App.TestHook_EntityMap.TryGetEntity(networkId, out var igEntity);

        var shWorld = harness.SimHost.World!;
        Assert.True(harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out var shEntity),
            "SimHost entity not found in its own entity map.");

        var before = shWorld.GetComponent<SimTransform>(shEntity).Rotation;
        _out.WriteLine($"[A2] SimHost rotation before: ({before.X:F4},{before.Y:F4},{before.Z:F4},{before.W:F4})");

        // ── 3. The IG asks for a heading it has no authority to write ─────────
        //    ⭐ This is the PRODUCTION composition — the same call the rotate gizmo makes.
        var writer = EntityWriteRouter.For(igWorld);

        const double TargetHeadingDeg = 137.0;
        var route = writer.Write(igEntity, AttributeIds.GeoHeading, TargetHeadingDeg);
        _out.WriteLine($"[A3] IG write route = {route}");

        // ⭐⭐ The router must NOT have written locally. ⛔ `Direct` here would mean the IG believes it owns
        //    SimTransform, which would make the whole request path dead code on this node.
        Assert.Equal(EntityWriteRoute.Requested, route);

        // ── 4. Pump until the OWNER's rotation matches ────────────────────────
        var expected = SimTransformBridgeSystem.HeadingDegToRotation((float)TargetHeadingDeg);

        bool applied = harness.PumpUntil(
            () => RotationsMatch(shWorld.GetComponent<SimTransform>(shEntity).Rotation, expected),
            RoundTripTimeoutFrames);

        var after = shWorld.GetComponent<SimTransform>(shEntity).Rotation;
        _out.WriteLine($"[A4] SimHost rotation after : ({after.X:F4},{after.Y:F4},{after.Z:F4},{after.W:F4})");
        _out.WriteLine($"[A4] expected              : ({expected.X:F4},{expected.Y:F4},{expected.Z:F4},{expected.W:F4})");

        Assert.True(applied,
            $"SimHost never applied the requested heading within {RoundTripTimeoutFrames} frames. " +
            "The change-request did not complete the round trip: IG bus → egress translator → DDS → " +
            "SimHost request system → SimTransformHeadingInstaller.");
    }

    /// <summary>
    /// ⭐⭐ <b>And the SAME gesture on the OWNER routes <c>Direct</c></b> — no wire involved.
    ///
    /// <para>⛔ Without this, the rail above could pass on a build where EVERY write became a request,
    /// which would be a different defect wearing the same green. ⭐ The two together pin the discriminator,
    /// not just one of its branches.</para>
    /// </summary>
    [Fact]
    public void TheOwningNodeWritesTheSameAttributeDirectly()
    {
        using var harness = new HrotRunnerHarness();

        long tkbType   = TkbEntityTypes.Tank_M1Abrams;
        var  spawnGeo  = new CoreGeoPoint { Latitude = 52.521, Longitude = 13.406, Altitude = 0 };
        long networkId = harness.SimHost.TestHook_SpawnEntity(tkbType, spawnGeo);

        harness.PumpFrames(10);

        var shWorld = harness.SimHost.World!;
        Assert.True(harness.SimHost.TestHook_EntityMap.TryGetEntity(networkId, out var shEntity));

        var writer = EntityWriteRouter.For(shWorld);
        var route  = writer.Write(shEntity, AttributeIds.GeoHeading, 42.0);
        _out.WriteLine($"[B1] SimHost write route = {route}");

        Assert.Equal(EntityWriteRoute.Direct, route);

        var expected = SimTransformBridgeSystem.HeadingDegToRotation(42f);
        Assert.True(RotationsMatch(shWorld.GetComponent<SimTransform>(shEntity).Rotation, expected),
            "The owning node's direct write did not land in SimTransform.Rotation.");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static bool IgHasEntity(HrotRunnerHarness harness, long networkId)
    {
        if (!harness.Ig.App.TestHook_EntityMap.TryGetEntity(networkId, out var e)) return false;
        var world = harness.Ig.App.World;
        return world.IsAlive(e)
            && world.HasComponent<NetworkIdentity>(e)
            && world.HasComponent<SimTransform>(e);
    }

    private static bool RotationsMatch(Quaternion a, Quaternion b)
        => Math.Abs(a.X - b.X) < RotationTolerance
        && Math.Abs(a.Y - b.Y) < RotationTolerance
        && Math.Abs(a.Z - b.Z) < RotationTolerance
        && Math.Abs(a.W - b.W) < RotationTolerance;
}
