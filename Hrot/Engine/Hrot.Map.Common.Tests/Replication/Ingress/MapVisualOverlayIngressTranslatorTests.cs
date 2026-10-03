using System.Collections.Generic;
using Hrot.IG.Components;
using Hrot.Map.Common.Replication.Ingress;
using Hrot.NED.Descriptors;
using Hrot.NED.Common;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Xunit;

namespace Hrot.Map.Common.Tests.Replication.Ingress;

/// <summary>
/// ⭐ Ownership build S8 / F-5 — the overlay ingress skips the sample its OWN egress published (the creator owns
/// <c>dtMapVisualOverlay</c>, CREATOR group): looping back, the last published polyline would overwrite a newer local
/// edit. A replica, or a ghost still being built (no record), takes it. 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c>
/// §3 F-5, §5.6 S8.
/// </summary>
public class MapVisualOverlayIngressTranslatorTests
{
    private const long NetId = 42;

    [Fact]
    public void OnTheRecordedOwner_ItsOwnOverlayLoopingBack_IsNotApplied()
    {
        var (world, translator) = Build(primaryOwner: 1, local: 1);
        Apply(world, translator);

        Assert.False(world.HasManagedComponent<EditablePolyline>(Entity(world)));
    }

    [Fact]
    public void OnAReplica_TheOwnersOverlay_IsApplied()
    {
        var (world, translator) = Build(primaryOwner: 1, local: 2);
        Apply(world, translator);

        Assert.True(world.HasManagedComponent<EditablePolyline>(Entity(world)));
    }

    private static (EntityRepository, MapVisualOverlayIngressTranslator) Build(int primaryOwner, int local)
    {
        var world = new EntityRepository();
        world.RegisterComponent<NetworkAuthority>();
        world.RegisterComponent<SimTransform>();
        world.RegisterComponent<MapOverlayStyle>();
        world.RegisterManagedComponent<EditablePolyline>();
        world.RegisterManagedComponent<DescriptorOwnership>();

        var map = new NetworkEntityMap();
        var e   = world.CreateEntity();
        world.AddComponent(e, new NetworkAuthority(primaryOwnerId: primaryOwner, localNodeId: local));
        world.AddComponent(e, new SimTransform());
        map.Register(NetId, e);

        return (world, new MapVisualOverlayIngressTranslator(null, map, null, new GhostCreationSystem(map), local));
    }

    private static void Apply(EntityRepository world, MapVisualOverlayIngressTranslator translator)
    {
        var view = (ISimulationView)world;
        var cmd  = (EntityCommandBuffer)view.GetCommandBuffer();
        translator.ProcessSample(new MapVisualOverlay { EntityId = (int)NetId, Points = new List<GeoPoint>() }, cmd, world);
        cmd.Playback(world);
    }

    private static Entity Entity(EntityRepository world)
    {
        foreach (var e in world.Query().With<NetworkAuthority>().Build()) return e;
        return Fdp.Core.Entity.Null;
    }
}
