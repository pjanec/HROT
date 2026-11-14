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
public sealed class EntityBoxPicker : IBodyPicker3D
{
    private readonly Func<EntityRepository?> _world;
    private readonly Func<ITkbDatabase?> _tkb;
    private readonly EntityBodyLayer3D _bodies;
    private EntityRepository? _queryWorld;
    private EntityQuery? _query;

    /// <summary>⭐ U-9 (CE-4340) — the pixel margin added round the drawn box, so a far, tiny entity can still be hovered / picked: the hit box is the drawn box plus this many SCREEN pixels (not metres).</summary>
    public const float MarginPixels = 6f;

    private readonly Func<Vector3, float>? _metresPerPixelAt;

    /// <param name="metresPerPixelAt">⭐ U-9 — the camera's metres per pixel at a world point (<c>1 / MapCamera3D.ZoomAt</c>); null ⇒ the exact drawn box, no margin (headless rails).</param>
    public EntityBoxPicker(Func<EntityRepository?> world, Func<ITkbDatabase?> tkb, EntityBodyLayer3D bodies, Func<Vector3, float>? metresPerPixelAt = null)
    {
        _metresPerPixelAt = metresPerPixelAt;
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _tkb = tkb ?? throw new ArgumentNullException(nameof(tkb));
        _bodies = bodies ?? throw new ArgumentNullException(nameof(bodies));
    }

    public PickFilter Kind => PickFilter.Entities;

    /// <summary>⭐ CE-1466 — whether the entity with <paramref name="networkId"/> is drawn as a body this picker hit-tests (so a 3-D press on it is the ray's, never its 2-D footprint's).</summary>
    public bool HasBody(long networkId)
    {
        var world = _world();
        if (world == null || networkId == 0 || networkId > int.MaxValue || !world.IsComponentTypeRegistered<TkbIdentity>()) return false;
        if (_bodies.Hidden) return false;   // ⭐ CE-4582 (§6am D-9) — Units ▸ Entities off: no body drawn, none to press on
        var e = Fdp.Toolkit.Replication.Services.NetworkIdResolver.ResolveNetworkId(world, (int)networkId);   // the entity itself, not its carrier
        if (e.IsNull || e == _bodies.FirstPerson || !world.IsAlive(e) || !world.HasComponent<TkbIdentity>(e)) return false;
        return _bodies.TryGetBox(world, e, _tkb(), out _, out _, out _);
    }

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
        // ⭐ CE-4558 (§6ak D-6) — through the boxes the last window frame DREW (BodyPickIndex): the cells under the ray, not every entity in the world
        var index = _bodies.PickIndex;
        if (index.HasFrame)
        {
            if (!index.TryPick(origin, direction, maxDistance, _metresPerPixelAt, _bodies.FirstPerson, out _, out int hitNet, out var at, out float hitAt)) return false;
            hit = new PickResult(at, PickKind.Entity, hitAt, hitNet);
            return true;
        }
        var tkb = _tkb();
        bool hasNet = world.IsComponentTypeRegistered<NetworkIdentity>();
        float best = maxDistance;
        int tested = 0;
        foreach (var e in _query!)
        {
            if (e == _bodies.FirstPerson) continue;   // ⭐ CE-1080 — the eye is inside the operator's own body: never pick it
            if (!_bodies.TryGetBox(world, e, tkb, out var centre, out var rotation, out var half)) continue;
            tested++;
            // ⭐ U-9 (CE-4340) — the drawn box plus a few PIXELS of margin at the box's depth: tight on a near body, a few metres on a far one (a screen tolerance, never a world one)
            float margin = _metresPerPixelAt is null ? 0f : MarginPixels * _metresPerPixelAt(centre);
            if (!RayBox.Intersect(origin, direction, centre, rotation, half + new Vector3(margin), out float t) || t > best) continue;
            best = t;
            int net = hasNet && world.HasComponent<NetworkIdentity>(e) ? world.GetComponentRO<NetworkIdentity>(e).Value : 0;
            hit = new PickResult(world.GetComponentRO<SimTransform>(e).Position, PickKind.Entity, t, net);
        }
        BodyDrawStats.RecordPick(tested);
        return hit.Kind == PickKind.Entity;
    }
}
