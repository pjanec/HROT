using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis3D;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-1033 S2 (P1) — a 3-D click on an entity hits the box it is DRAWN in (its shape-kit size, its heading), not the sim's
/// collider (a bounding circle, too wide for a long vehicle, absent on some types). The hit point is the entity's OWN position,
/// so the layers' 2-D pick boxes — centred on it — contain it and the input chain stays unchanged (§3.8).
/// </summary>
public sealed class EntityBoxPicker : IPicker3D
{
    private readonly Func<EntityRepository?> _world;
    private readonly Func<ITkbDatabase?> _tkb;
    private readonly EntityBodyLayer3D _bodies;
    private EntityRepository? _queryWorld;
    private EntityQuery? _query;

    public EntityBoxPicker(Func<EntityRepository?> world, Func<ITkbDatabase?> tkb, EntityBodyLayer3D bodies)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _tkb = tkb ?? throw new ArgumentNullException(nameof(tkb));
        _bodies = bodies ?? throw new ArgumentNullException(nameof(bodies));
    }

    public PickFilter Kind => PickFilter.Entities;

    public bool TryPick(Vector3 origin, Vector3 direction, float maxDistance, out PickResult hit)
    {
        hit = PickResult.None;
        var world = _world();
        if (world == null || !world.IsComponentTypeRegistered<TkbIdentity>() || !world.IsComponentTypeRegistered<SimTransform>()) return false;
        if (!ReferenceEquals(world, _queryWorld))
        {
            _queryWorld = world;
            _query = world.Query().With<SimTransform>().With<TkbIdentity>().WithLifecycle(EntityLifecycle.All).Build();
        }
        var tkb = _tkb();
        bool hasNet = world.IsComponentTypeRegistered<NetworkIdentity>();
        float best = maxDistance;
        foreach (var e in _query!)
        {
            if (!_bodies.TryGetBox(world, e, tkb, out var centre, out var rotation, out var half)) continue;
            if (!RayBox.Intersect(origin, direction, centre, rotation, half, out float t) || t > best) continue;
            best = t;
            long net = hasNet && world.HasComponent<NetworkIdentity>(e) ? world.GetComponentRO<NetworkIdentity>(e).Value : 0;
            hit = new PickResult(world.GetComponentRO<SimTransform>(e).Position, PickKind.Entity, t, net);
        }
        return hit.Kind == PickKind.Entity;
    }
}
