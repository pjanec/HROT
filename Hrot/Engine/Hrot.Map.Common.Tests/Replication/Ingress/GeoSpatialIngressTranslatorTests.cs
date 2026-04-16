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
using Hrot.NED.Common;
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
            : base(participant: null, entityMap, geoTransform, ghostSystem, localNodeId)
        { }

        public void TestDecode(in WorldPos data, IEntityCommandBuffer cmd, ISimulationView view)
            => Decode(data, cmd, view);
    }

    // ── Infrastructure ────────────────────────────────────────────────────────

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

    /// <summary>Creates a WorldPos sample at an arbitrary position.</summary>
    private static WorldPos MakeSample(long entityId, float lat = 1f, float lon = 2f)
        => new WorldPos
        {
            EntityId = (int)entityId,
            Pos      = new GeoPoint { Latitude = lat, Longitude = lon, Altitude = 0 },
            Ori      = new EulerOri { Heading = 0 },
            Vel      = new AngularVector(),
        };

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

        var sample = MakeSample(netId, lat: 10f, lon: 20f);
        ISimulationView view = world;
        var cmd = view.GetCommandBuffer();
        translator.TestDecode(sample, cmd, view);
        ((EntityCommandBuffer)cmd).Playback(world);

        var transform = world.GetComponent<SimTransform>(entity);
        Assert.Equal(10f, transform.Position.X, precision: 3);
        Assert.Equal(20f, transform.Position.Y, precision: 3);
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

        var sample = MakeSample(netId, lat: 1f, lon: 2f);
        ISimulationView view = world;
        var cmd = view.GetCommandBuffer();
        translator.TestDecode(sample, cmd, view);
        ((EntityCommandBuffer)cmd).Playback(world);

        var transform = world.GetComponent<SimTransform>(entity);
        Assert.Equal(originalPos, transform.Position);
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
        var sample = MakeSample(netId, lat: 1f, lon: 2f);
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

        var sample = MakeSample(netId, lat: 7f, lon: 8f);
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
