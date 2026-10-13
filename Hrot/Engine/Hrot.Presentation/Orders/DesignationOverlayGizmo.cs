using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Common.Orders;

namespace Hrot.Presentation.Orders;

/// <summary>
/// ⭐ <c>CE-1059</c> (U6, design §13.12 O8) — draws each SELECTED unit's designations (§13.3, <see cref="OrderDesignations"/>) so the
/// standing target / point / route / sector a later order will take is never hidden state: a dashed line from the unit to each —
/// target red, point green (+ a ring), route amber (its waypoints drawn), sector blue (its outline drawn). A passive global gizmo
/// (the <c>LayerControlGizmo</c> shape: no focus, no input), registered by <c>MapInteractionPack</c> on every live map host.
/// </summary>
public sealed class DesignationOverlayGizmo : IEntityStatefulGizmo
{
    public static readonly Rgba32 TargetColor = new(235, 70, 70, 230);
    public static readonly Rgba32 PointColor  = new(80, 220, 110, 230);
    public static readonly Rgba32 RouteColor  = new(240, 180, 40, 230);
    public static readonly Rgba32 AreaColor   = new(80, 150, 255, 230);

    private readonly Func<IReadOnlyCollection<Entity>> _selected;

    public DesignationOverlayGizmo(Func<IReadOnlyCollection<Entity>> selected)
        => _selected = selected ?? throw new ArgumentNullException(nameof(selected));

    public bool RequiresExclusiveFocus => false;
    public bool IsFocused { get; private set; }
    public void SetFocus(bool isFocused) => IsFocused = isFocused;

    public void UpdateAndDraw(ISimulationView view, float deltaTime, IDebugDrawBuilder draw)
    {
        if (view is not EntityRepository world || OrderDesignations.Of(world) is not { } store) return;
        if (!world.HasSingletonManaged<NetworkEntityMap>() || !world.IsComponentTypeRegistered<SimTransform>()) return;
        var map = world.GetSingletonManaged<NetworkEntityMap>()!;
        var geo = world.HasSingletonManaged<IGeographicTransform>() ? world.GetSingletonManaged<IGeographicTransform>() : null;
        foreach (var unit in _selected())
        {
            if (unit.IsNull || !world.IsAlive(unit) || !world.HasComponent<SimTransform>(unit) || !map.TryGetNetworkId(unit, out long id))
                continue;
            var from = world.GetComponentRO<SimTransform>(unit).Position;
            foreach (var (kind, value) in store.Of(id))
                DrawOne(world, map, geo, draw, from, kind, value);
        }
    }

    private static void DrawOne(EntityRepository world, NetworkEntityMap map, IGeographicTransform? geo, IDebugDrawBuilder draw,
        Vector3 from, DesignationKind kind, JsonNode value)
    {
        if (kind == DesignationKind.MapPoint)
        {
            if (PointOf(value, geo) is not { } at) return;
            draw.DrawLine(from, at, PointColor, 2f, style: LineStyle.Dashed);
            draw.DrawSphere(at, 3f, PointColor, thickness: 2f, sizeMode: SizeMode.WorldMeters);
            return;
        }
        if (!(value is JsonValue v && v.TryGetValue<long>(out long target)) || !map.TryGetEntity(target, out var e)
            || !world.IsAlive(e) || !world.HasComponent<SimTransform>(e))
            return;
        var to = world.GetComponentRO<SimTransform>(e).Position;
        var color = kind switch { DesignationKind.Route => RouteColor, DesignationKind.Area => AreaColor, _ => TargetColor };
        draw.DrawLine(from, to, color, 2f, style: LineStyle.Dashed);
        if (kind == DesignationKind.Route && world.HasManagedComponent<Hrot.Map.Common.Components.RoutePlan>(e))
        {
            var wp = ((ISimulationView)world).GetManagedComponentRO<Hrot.Map.Common.Components.RoutePlan>(e).Waypoints;
            for (int i = 1; i < wp.Count; i++) draw.DrawLine(wp[i - 1].Position, wp[i].Position, color, 4f);
        }
        else if (kind == DesignationKind.Area && world.HasManagedComponent<Hrot.IG.Components.EditablePolyline>(e))
        {
            // ⚠ the points are OFFSETS from the area's own position (EditablePolyline's one convention)
            var pts = ((ISimulationView)world).GetManagedComponentRO<Hrot.IG.Components.EditablePolyline>(e).Points;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                draw.DrawLine(new Vector3(to.X + a.X, to.Y + a.Y, to.Z), new Vector3(to.X + b.X, to.Y + b.Y, to.Z), color, 4f);
            }
        }
    }

    /// <summary>A designated point in world metres: geodetic <c>[lat, lon]</c> through the transform; without one, <c>[y, x]</c>
    /// (OrderGeo's fallback, as <c>MenuOrders</c> writes it).</summary>
    public static Vector3? PointOf(JsonNode value, IGeographicTransform? geo)
    {
        if (value is not JsonArray { Count: >= 2 } a) return null;
        double p0 = a[0]!.GetValue<double>(), p1 = a[1]!.GetValue<double>();
        return geo != null ? geo.ToCartesian(p0, p1, 0) : new Vector3((float)p1, (float)p0, 0f);
    }

    public void OnInteractionStarted(GizmoPickToken token, Vector3 worldPos) { }
    public void OnDragUpdate(Vector3 worldPos) { }
    public void OnCommit(Vector3 worldPos) { }
    public void OnCancel() { }
    public void OnMenuAction(int actionId) { }
    public void OnMouseEvent(MapMouseButton button, bool isPressed, Vector3 worldPos) { }
    public void OnKeyEvent(MapKeyboardKey key, bool isPressed) { }
    public void Dispose() { }
}
