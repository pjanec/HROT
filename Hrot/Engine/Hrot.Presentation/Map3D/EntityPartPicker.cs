using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Physics.BodyParts;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Vis3D;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐⭐ CE-1126 (docs/DESIGN_Entity_Control.md §8g F-4) — what the FPS crosshair rests on: an entity by the PARTS a round meets
/// (<see cref="BodyPartRay"/> — the function the bullet solver calls, so the crosshair on his head is the round on his head), and the hit
/// point is where the ray enters the part. A body with no parts (an icon unit, a type the toolkit has no shape for) keeps the drawn box of
/// <see cref="EntityBodyLayer3D.TryGetBox"/>. Answers <see cref="PickFilter.EntityParts"/> only — RTS selection keeps
/// <see cref="EntityBoxPicker"/>'s generous box.
/// </summary>
public sealed class EntityPartPicker : IPicker3D
{
    private readonly Func<EntityRepository?> _world;
    private readonly Func<ITkbDatabase?> _tkb;
    private readonly EntityBodyLayer3D _bodies;
    private readonly BodyBox[] _scratch = new BodyBox[VehicleBody.MaxBoxes];
    private EntityRepository? _queryWorld;
    private EntityQuery? _query;

    public EntityPartPicker(Func<EntityRepository?> world, Func<ITkbDatabase?> tkb, EntityBodyLayer3D bodies)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _tkb = tkb ?? throw new ArgumentNullException(nameof(tkb));
        _bodies = bodies ?? throw new ArgumentNullException(nameof(bodies));
    }

    public PickFilter Kind => PickFilter.EntityParts;

    public bool TryPick(Vector3 origin, Vector3 direction, float maxDistance, out PickResult hit)
    {
        hit = PickResult.None;
        var world = _world();
        if (world == null || maxDistance <= 0f || !world.IsComponentTypeRegistered<TkbIdentity>() || !world.IsComponentTypeRegistered<SimTransform>()) return false;
        if (!ReferenceEquals(world, _queryWorld))
        {
            _queryWorld = world;
            _query = world.Query().With<SimTransform>().With<TkbIdentity>().WithLifecycle(EntityLifecycle.All).Build();
        }
        var tkb = _tkb();
        VehicleBody.Prime(tkb);
        bool hasNet = world.IsComponentTypeRegistered<NetworkIdentity>();
        var end = origin + direction * maxDistance;
        float best = maxDistance;
        foreach (var e in _query!)
        {
            if (e == _bodies.FirstPerson) continue;   // ⭐ CE-1080 — never the operator's own body
            if (!_bodies.TryGetBox(world, e, tkb, out var centre, out var rotation, out var half)) continue;   // not drawn ⇒ not aimed at
            float t;
            if (BodyPartRay.TryHit(world, e, origin, end, _scratch, out var part, out bool hasParts)) t = part.T * maxDistance;
            else if (hasParts) continue;   // through the gap between his arm and his chest
            else if (IsDrawnAsPerson(tkb, world, e)
                     && BodyPartRay.Person(world, e, world.GetComponentRO<SimTransform>(e), 0f, origin, end, out var p) == BodyPartTest.Hit)
                t = p.T * maxDistance;   // a figure the sim does not mark a pedestrian here (no VehicleParams on this node)
            else if (!IsDrawnAsPerson(tkb, world, e) && RayBox.Intersect(origin, direction, centre, rotation, half, out float tb)) t = tb;
            else continue;
            if (t > best) continue;
            best = t;
            long net = hasNet && world.HasComponent<NetworkIdentity>(e) ? world.GetComponentRO<NetworkIdentity>(e).Value : 0;
            hit = new PickResult(origin + direction * t, PickKind.Entity, t, net);
        }
        return hit.Kind == PickKind.Entity;
    }

    private static bool IsDrawnAsPerson(ITkbDatabase? tkb, EntityRepository world, Entity e)
        => tkb != null && tkb.TryGetByType(world.GetComponentRO<TkbIdentity>(e).TkbType, out var t)
           && EntityBodyLayer3D.Resolve(t).Family == VisualFamily.Person;
}
