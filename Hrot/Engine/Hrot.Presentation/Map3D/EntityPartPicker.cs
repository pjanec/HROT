using System.Collections.Generic;
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
    private readonly HashSet<Entity> _own = new();

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
        OwnRide(world, _bodies.FirstPerson, _own);
        foreach (var e in _query!)
        {
            if (_own.Contains(e)) continue;   // ⭐ CE-1080 — never the operator's own body; ⭐ CE-1307 — nor the vehicle he rides, nor what rides it
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

    /// <summary>
    /// ⭐ <c>CE-1307</c> — what the crosshair must look past: the operator's own body, the vehicle he sits in (a gunner, a passenger) and
    /// everything riding it (the crew, its weapon stations) — or, driving it, everything riding HIS vehicle. From the chase camera the ray
    /// crosses them first, and the gun was told to aim at its own hull.
    /// </summary>
    public static void OwnRide(EntityRepository world, Entity me, HashSet<Entity> own)
    {
        own.Clear();
        if (me.IsNull) return;
        own.Add(me);
        bool embarked = world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>()
                        && world.HasComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(me);
        var vehicle = embarked ? world.GetComponentRO<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>(me).VehicleEntity : me;
        if (vehicle.IsNull || !world.IsAlive(vehicle)) return;
        own.Add(vehicle);
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.PassengerBuffer>()
            || !world.HasComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(vehicle)) return;
        ref readonly var riders = ref world.GetComponentRO<Fdp.Toolkit.Behavior.Components.PassengerBuffer>(vehicle);
        for (int i = 0; i < Fdp.Toolkit.Behavior.Components.PassengerBuffer.Capacity; i++)
            if (!riders.Passengers[i].IsNull) own.Add(riders.Passengers[i]);
    }

    private static bool IsDrawnAsPerson(ITkbDatabase? tkb, EntityRepository world, Entity e)
        => tkb != null && tkb.TryGetByType(world.GetComponentRO<TkbIdentity>(e).TkbType, out var t)
           && EntityBodyLayer3D.Resolve(t).Family == VisualFamily.Person;
}
