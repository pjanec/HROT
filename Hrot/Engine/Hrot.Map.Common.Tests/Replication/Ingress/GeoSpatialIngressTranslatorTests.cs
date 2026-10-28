using System.Collections.Generic;
using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Fdp.Modules.Geographic;
using Hrot.Map.Common.Replication.Ingress;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.Map.Common.Replication.Utils;
using Xunit;

namespace Hrot.Map.Common.Tests.Replication.Ingress;

/// <summary>
/// Unit tests for the authority-mask loopback guard in
/// <see cref="GeoSpatialIngressTranslator"/>.
///
/// <para>
/// When the Muscle node (SimHost) is granted authority over <c>SimTransform</c> via
/// AuthorityMask, <c>GeoSpatialIngressTranslator.Decode</c> must
/// suppress the DDS loopback packet and NOT overwrite <c>SimTransform</c>.
/// Failing to do so causes the "shivering" symptom where physics and network
/// ingress fight over the entity position every frame.
/// </para>
///
/// <para>Tests: GEOINGRESS-1 … GEOINGRESS-4.</para>
/// </summary>
[Collection("SerialTests")]   // ⭐ it clears the process-wide ComponentTypeRegistry
public sealed class GeoSpatialIngressTranslatorTests
{
    private const int  LocalNodeId     = 1;
    private const int  RemoteNodeId    = 2;

    // ── Testable subclass ─────────────────────────────────────────────────────

    /// <summary>
    /// Exposes the <c>protected Decode</c> method for white-box unit testing.
    /// </summary>
    private sealed class TestableGeoSpatialIngressTranslator : GeoSpatialIngressTranslator
    {
        public TestableGeoSpatialIngressTranslator(
            IGeographicTransform geoTransform,
            NetworkEntityMap     entityMap,
            GhostCreationSystem  ghostSystem,
            long                 localNodeId)
            : base(participant: null, entityMap, ghostSystem, localNodeId)
        { }

        public void TestDecode(in WorldPos data, IEntityCommandBuffer cmd, ISimulationView view)
            => Decode(data, cmd, view);
    }

    // ── Infrastructure ────────────────────────────────────────────────────────

    // ⭐ AQ107 B — the ingress no longer converts; this transform is only handed to the (now ignoring) constructor.
    private sealed class IdentityGeoTransform : IGeographicTransform
    {
        public void SetOrigin(double lat, double lon, double alt) { }
        public Vector3 ToCartesian(double lat, double lon, double alt)
            => new Vector3((float)lat, (float)lon, (float)alt);
        public (double lat, double lon, double alt) ToGeodetic(Vector3 pos)
            => (pos.X, pos.Y, pos.Z);
    }

    private static EntityRepository CreateWorld()
    {
        ComponentTypeRegistry.Clear();
        var world = new EntityRepository();
        world.RegisterComponent<NetworkIdentity>();
        world.RegisterComponent<NetworkAuthority>();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<NetworkTransform>();
        world.RegisterComponent<NetworkVelocity>();
        return world;
    }

    private static (TestableGeoSpatialIngressTranslator translator, NetworkEntityMap map) CreateTranslator()
    {
        var entityMap    = new NetworkEntityMap();
        var geoTransform = new IdentityGeoTransform();
        var ghostSystem  = new GhostCreationSystem(entityMap);
        var translator   = new TestableGeoSpatialIngressTranslator(
            geoTransform, entityMap, ghostSystem, LocalNodeId);
        return (translator, entityMap);
    }

    /// <summary>Creates a WorldPos sample at an arbitrary local-ENU position (AQ107 B / D): identity rotation, no motion.</summary>
    private static WorldPos MakeSample(long entityId, float x = 1f, float y = 2f)
        => WorldPosCodec.Encode(entityId, simTimeSeconds: 5.0, new Vector3(x, y, 0f), Quaternion.Identity, Vector3.Zero, Vector3.Zero);

    // ── GEOINGRESS-1: Remote entity — ingress updates SimTransform ────────────

    /// <summary>
    /// For an entity owned by a remote node (no NetworkAuthority, no DescriptorOwnership),
    /// Decode must update both <c>SimTransform</c> and <c>NetworkTransform</c>.
    /// </summary>
    [Fact]
    public void RemoteEntity_NoAuthority_SimTransformUpdated()
    {
        using var world = CreateWorld();
        var (translator, map) = CreateTranslator();

        var entity = world.CreateEntity();
        long netId = 99L;
        world.AddComponent(entity, new NetworkIdentity(netId));
        world.AddComponent(entity, new SimTransform());
        world.AddComponent(entity, new NetworkTransform());
        world.AddComponent(entity, new NetworkVelocity());
        map.Register(netId, entity);

        var sample = MakeSample(netId, x: 10f, y: 20f);
        ISimulationView view = world;
        var cmd = view.GetCommandBuffer();
        translator.TestDecode(sample, cmd, view);
        ((EntityCommandBuffer)cmd).Playback(world);

        var transform = world.GetComponent<SimTransform>(entity);
        Assert.Equal(10f, transform.Position.X, precision: 3);
        Assert.Equal(20f, transform.Position.Y, precision: 3);
    }

    // ── ⭐ AQ107 D — the pose crosses the wire as a quaternion: ECS → WorldPos → ECS ──────────────────────────

    public static IEnumerable<object[]> Poses()
    {
        yield return new object[] { "heading 30 / pitch 10 / roll -20", Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(0.4f, 0.17f, -0.35f)) };
        yield return new object[] { "yaw 250 deg about Z",               Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 250f * MathF.PI / 180f) };
        yield return new object[] { "nose straight UP (CE-4068)",        Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2f) };
        yield return new object[] { "nose straight DOWN (CE-4068)",      Quaternion.CreateFromAxisAngle(Vector3.UnitY,  MathF.PI / 2f) };
        yield return new object[] { "nose up, rolled and yawed",         Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.1f) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2f) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.6f)) };
        // the SAME rotation as a quaternion with w < 0 (q and -q are one rotation): the sender must canonicalise it
        var q = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(1f, 2f, 3f)), 2.4f));
        yield return new object[] { "w < 0 before canonicalisation",     new Quaternion(-q.X, -q.Y, -q.Z, -q.W) };
        yield return new object[] { "a half turn (w = 0)",               Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI) };
    }

    /// <summary>
    /// ⭐⭐ <b>AQ107 D acceptance (a)</b> — a pose (position, rotation, velocity, angular velocity) round-trips ECS → <c>WorldPos</c> → ECS:
    /// position within 1 mm, rotation within 1e-6 (quaternion dot ≥ 1 − 1e-6, sign-insensitive), velocity and angular velocity exact —
    /// including a nose straight up and straight down (CE-4068: the Euler wire lost the heading there) and a rotation whose w is negative
    /// before canonicalisation. ✅ Red-proof (run): <c>QuaternionWireCodec.Encode</c> without the <c>w ≥ 0</c> canonicalisation ⇒ the
    /// "w &lt; 0" case decodes to the INVERSE rotation ⇒ red.
    /// </summary>
    [Theory]
    [MemberData(nameof(Poses))]
    public void AQ107D_APose_RoundTripsEcsToWorldPosToEcs(string _, Quaternion rotation)
    {
        using var world = CreateWorld();
        world.RegisterComponent<SimVelocity>();
        var (translator, map) = CreateTranslator();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new NetworkIdentity(77L));
        world.AddComponent(entity, new SimTransform());
        world.AddComponent(entity, new NetworkTransform());
        world.AddComponent(entity, new NetworkVelocity());
        world.AddComponent(entity, new SimVelocity());
        map.Register(77L, entity);

        var position = new Vector3(1234.567f, -8765.432f, 42.125f);
        var linear   = new Vector3(1.5f, -2f, 0.25f);
        var angular  = new Vector3(0.1f, -0.2f, 0.3f);
        var sample   = WorldPosCodec.Encode(77L, simTimeSeconds: 12.5, position, rotation, linear, angular);

        ISimulationView view = world;
        var cmd = view.GetCommandBuffer();
        translator.TestDecode(sample, cmd, view);
        ((EntityCommandBuffer)cmd).Playback(world);

        var st = world.GetComponent<SimTransform>(entity);
        Assert.True(Vector3.Distance(position, st.Position) < 0.001f, $"position {st.Position}");
        float dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(rotation), st.Rotation));
        Assert.True(dot >= 1f - 1e-6f, $"rotation dot {dot:R}");
        Assert.Equal(linear,  world.GetComponent<NetworkVelocity>(entity).Value);
        var vel = world.GetComponent<SimVelocity>(entity);
        Assert.Equal(linear,  vel.Linear);
        Assert.Equal(angular, vel.Angular);
        var shadow = world.GetComponent<NetworkTransform>(entity);
        Assert.Equal(12.5, shadow.SimStamp);                                  // the sim time is the dead-reckoning anchor
        Assert.True(MathF.Abs(Quaternion.Dot(shadow.LastRotation, st.Rotation)) >= 1f - 1e-6f);
    }

    /// <summary>
    /// ⭐⭐ <b>AQ107 D acceptance (b)</b> — an ALL-ZERO <c>WorldPos</c> (a struct nobody filled in) decodes to the IDENTITY rotation and no NaN
    /// anywhere. ✅ Red-proof (run): the decoder leaving <c>w</c> at 0 ⇒ a zero (invalid) quaternion ⇒ red.
    /// </summary>
    [Fact]
    public void AQ107D_AnAllZeroWorldPos_DecodesToTheIdentity_NoNaN()
    {
        using var world = CreateWorld();
        var (translator, map) = CreateTranslator();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new NetworkIdentity(78L));
        world.AddComponent(entity, new SimTransform());
        world.AddComponent(entity, new NetworkTransform());
        world.AddComponent(entity, new NetworkVelocity());
        map.Register(78L, entity);

        ISimulationView view = world;
        var cmd = view.GetCommandBuffer();
        translator.TestDecode(new WorldPos { EntityId = 78L }, cmd, view);
        ((EntityCommandBuffer)cmd).Playback(world);

        var st = world.GetComponent<SimTransform>(entity);
        Assert.Equal(Quaternion.Identity, st.Rotation);
        Assert.Equal(Quaternion.Identity, world.GetComponent<NetworkTransform>(entity).LastRotation);
        Assert.Equal(Vector3.Zero, st.Position);
        Assert.False(float.IsNaN(st.Rotation.X + st.Rotation.Y + st.Rotation.Z + st.Rotation.W + st.Position.X));
    }

    /// <summary>
    /// ⭐ AQ107 D — a vector part LONGER than 1 (float rounding at a half-turn, or a corrupt sample) decodes to a finite UNIT quaternion, never
    /// NaN. ✅ Red-proof (run): <c>QuaternionWireCodec.Decode</c> computing <c>w = sqrt(1 − |v|²)</c> without the <c>max(0, …)</c> clamp ⇒ NaN ⇒ red.
    /// </summary>
    [Theory]
    [InlineData(0.8f, 0.6f, 0.1f)]
    [InlineData(1f, 1f, 1f)]
    public void AQ107D_AVectorPartOverUnitLength_DecodesToAUnitQuaternion_NotNaN(float x, float y, float z)
    {
        using var world = CreateWorld();
        var (translator, map) = CreateTranslator();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new NetworkIdentity(79L));
        world.AddComponent(entity, new SimTransform());
        world.AddComponent(entity, new NetworkTransform());
        world.AddComponent(entity, new NetworkVelocity());
        map.Register(79L, entity);

        var sample = MakeSample(79L) with { OrientationXyz = new OrientationQuat { X = x, Y = y, Z = z } };
        ISimulationView view = world;
        var cmd = view.GetCommandBuffer();
        translator.TestDecode(sample, cmd, view);
        ((EntityCommandBuffer)cmd).Playback(world);

        var q = world.GetComponent<SimTransform>(entity).Rotation;
        Assert.False(float.IsNaN(q.X + q.Y + q.Z + q.W));
        Assert.Equal(1f, q.Length(), 5);
    }

    // ── GEOINGRESS-2: Primary-owner loopback — SimTransform NOT updated ───────

    /// <summary>
    /// When the local node is the primary owner (NetworkAuthority.HasAuthority == true),
    /// Decode must NOT overwrite SimTransform — it should suppress the loopback packet.
    /// </summary>
    [Fact]
    public void PrimaryOwner_LoopbackSuppressed_SimTransformUnchanged()
    {
        using var world = CreateWorld();
        var (translator, map) = CreateTranslator();

        var entity = world.CreateEntity();
        long netId = 100L;
        world.AddComponent(entity, new NetworkIdentity(netId));
        world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: LocalNodeId, localNodeId: LocalNodeId));
        var originalPos = new Vector3(999f, 888f, 0f);
        world.AddComponent(entity, new SimTransform { Position = originalPos });
        world.SetAuthority<SimTransform>(entity, true);
        world.AddComponent(entity, new NetworkTransform());
        world.AddComponent(entity, new NetworkVelocity());
        map.Register(netId, entity);

        var sample = MakeSample(netId, x: 1f, y: 2f);
        ISimulationView view = world;
        var cmd = view.GetCommandBuffer();
        translator.TestDecode(sample, cmd, view);
        ((EntityCommandBuffer)cmd).Playback(world);

        var transform = world.GetComponent<SimTransform>(entity);
        Assert.Equal(originalPos, transform.Position);
    }

    /// <summary>
    /// ⭐ <c>CE-2348</c> ② — the owner's own sample comes back by loopback and rewrites the shadow; the EGRESS's fields of it (the velocity
    /// it last sent, the tick it last sent on) survive the echo. ⛔ Before: the echo reset both to zero every frame ⇒ a stopped hull's zero
    /// velocity never read as a change, and the heartbeat fired every frame. ✅ Red-proof: the ingress writing a fresh
    /// <c>NetworkTransform</c> without the two fields ⇒ red.
    /// </summary>
    [Fact]
    public void TheOwnersLoopback_KeepsWhatItsEgressLastSent_CE2348()
    {
        using var world = CreateWorld();
        var (translator, map) = CreateTranslator();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new NetworkIdentity(101L));
        world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: LocalNodeId, localNodeId: LocalNodeId));
        world.AddComponent(entity, new SimTransform());
        world.SetAuthority<SimTransform>(entity, true);
        world.AddComponent(entity, new NetworkTransform { LastVelocity = new Vector3(8f, 0f, 0f), LastSentTick = 4321 });
        world.AddComponent(entity, new NetworkVelocity());
        map.Register(101L, entity);

        ISimulationView view = world;
        var cmd = view.GetCommandBuffer();
        translator.TestDecode(MakeSample(101L, x: 3f, y: 4f), cmd, view);
        ((EntityCommandBuffer)cmd).Playback(world);

        var shadow = world.GetComponent<NetworkTransform>(entity);
        Assert.Equal(new Vector3(8f, 0f, 0f), shadow.LastVelocity);
        Assert.Equal(4321u, shadow.LastSentTick);
        Assert.Equal(3f, shadow.LastPosition.X, precision: 3);   // the echo still refreshes the position it carries
    }

    // ── GEOINGRESS-3: Split-authority (Muscle) loopback — SimTransform NOT updated

    /// <summary>
    /// When the Muscle node holds split-authority for <c>SimTransform</c> via
    /// AuthorityMask, Decode must NOT overwrite SimTransform.
    /// This is the "shivering" fix: the entity's physics position must be
    /// preserved even though the network packet carries a stale loopback echo.
    /// </summary>
    [Fact]
    public void SplitAuthorityMuscle_LoopbackSuppressed_SimTransformUnchanged()
    {
        using var world = CreateWorld();
        var (translator, map) = CreateTranslator();

        var entity = world.CreateEntity();
        long netId = 101L;
        world.AddComponent(entity, new NetworkIdentity(netId));
        // Brain is primary owner — Muscle is NOT the primary owner.
        world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: RemoteNodeId, localNodeId: LocalNodeId));
        var physicsPos = new Vector3(500f, 600f, 0f);
        world.AddComponent(entity, new SimTransform { Position = physicsPos });
        world.AddComponent(entity, new NetworkTransform());
        world.AddComponent(entity, new NetworkVelocity());
        map.Register(netId, entity);

        // DeferredTakeoverSystem already ran: Muscle was granted SimTransform authority.
        world.SetAuthority<SimTransform>(entity, true);

        // A loopback packet arrives — Muscle should drop it for SimTransform.
        var sample = MakeSample(netId, x: 1f, y: 2f);
        ISimulationView view3 = world;
        var cmd3 = view3.GetCommandBuffer();
        translator.TestDecode(sample, cmd3, view3);
        ((EntityCommandBuffer)cmd3).Playback(world);

        // SimTransform must retain the physics position, not the loopback packet position.
        var transform = world.GetComponent<SimTransform>(entity);
        Assert.Equal(physicsPos, transform.Position);

        // NetworkTransform IS still updated (used for ghost rendering on other nodes).
        var netTransform = world.GetComponent<NetworkTransform>(entity);
        Assert.Equal(1f, netTransform.LastPosition.X, precision: 3);
    }

    // ── GEOINGRESS-4: Split-authority belonging to ANOTHER node — SimTransform updated

    /// <summary>
    /// When local node does not own <c>SimTransform</c> in AuthorityMask, it must apply
    /// the incoming packet to SimTransform normally.
    /// </summary>
    [Fact]
    public void SplitAuthorityOtherNode_IngressApplied_SimTransformUpdated()
    {
        using var world = CreateWorld();
        var (translator, map) = CreateTranslator();

        var entity = world.CreateEntity();
        long netId = 102L;
        world.AddComponent(entity, new NetworkIdentity(netId));
        world.AddComponent(entity, new NetworkAuthority(primaryOwnerId: RemoteNodeId, localNodeId: LocalNodeId));
        world.AddComponent(entity, new SimTransform { Position = Vector3.Zero });
        world.AddComponent(entity, new NetworkTransform());
        world.AddComponent(entity, new NetworkVelocity());
        map.Register(netId, entity);

        // Local node does not own SimTransform for this entity.
        world.SetAuthority<SimTransform>(entity, false);

        var sample = MakeSample(netId, x: 7f, y: 8f);
        ISimulationView view4 = world;
        var cmd4 = view4.GetCommandBuffer();
        translator.TestDecode(sample, cmd4, view4);
        ((EntityCommandBuffer)cmd4).Playback(world);

        // Ingress must update SimTransform since local node is NOT the authority.
        var transform = world.GetComponent<SimTransform>(entity);
        Assert.Equal(7f, transform.Position.X, precision: 3);
        Assert.Equal(8f, transform.Position.Y, precision: 3);
    }
}
