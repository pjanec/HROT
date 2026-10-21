using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Weather;
// Disambiguate from GizmoMap.Contracts' FixedString32 (the gizmo namespace re-exports it).
using FixedString32 = Fdp.Core.FixedString32;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ <c>CE-1192</c> (UI-5a, 📄 docs/DESIGN_Command_Display.md §6 D1) — every unit's SECTOR area outlined in that unit's colour with its
/// name, and every NoFire area hatched red. Both come from the replicated <see cref="EngagementArea"/> (<c>AllowArea</c>, <c>NoFire</c>:
/// area network ids), so every node draws them. ONE global gizmo: many men list the same NoFire area (looked up through the chain), so
/// each area is drawn once per frame however many units name it. The outline is the area's own (<see cref="WeatherField.TryOutline"/>,
/// points relative to its transform — the shape <c>EngagementAreas</c> tests a shot against). ⭐ <c>CE-1225</c> (§8 S-1…S-3): everything on the
/// <see cref="DebugTraceLayers.Sectors"/> layer (off by default), and every COMMANDER's arc too — a unit some replicated
/// <see cref="UnitSubordinate.Commander"/> names (a platoon's slice on each squad, the support squad's arc) — for everyone, on the ground.
/// </summary>
[GizmoProjector]
public sealed class SectorAreasGizmo : IGlobalStatelessGizmo
{
    /// <summary>A unit's colour: the palette at its network id (stable across frames and nodes).</summary>
    public static readonly Rgba32[] UnitColours =
    {
        new(64, 128, 255, 230), new(40, 190, 90, 230), new(230, 160, 0, 230), new(170, 90, 230, 230), new(0, 190, 200, 230), new(230, 90, 160, 230),
    };
    public static readonly Rgba32 NoFireColour = new(230, 40, 40, 230);
    /// <summary>The NoFire hatch: diagonal lines this far apart (m).</summary>
    public const float HatchSpacing = 3f;

    private EntityRepository? _queryRepo;
    private EntityQuery? _units;
    private EntityQuery? _subordinates;
    private readonly Dictionary<long, Entity> _sectors = new();
    private readonly HashSet<long> _noFire = new();
    private readonly List<(Vector2 A, Vector2 B)> _hatch = new();
    private readonly HashSet<Entity> _commanders = new();

    public static Rgba32 ColourOf(long unitNetId) => UnitColours[(int)((ulong)unitNetId % (ulong)UnitColours.Length)];

    public void Draw(ISimulationView view, IDebugDrawBuilder draw)
    {
        if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<EngagementArea>()) return;
        if (!draw.IsLayerActive(DebugTraceLayers.Sectors)) return;
        if (!ReferenceEquals(repo, _queryRepo))
        {
            _queryRepo = repo;
            _units = repo.Query().With<EngagementArea>().Build();
            _subordinates = repo.IsComponentTypeRegistered<UnitSubordinate>() ? repo.Query().With<UnitSubordinate>().Build() : null;
        }
        _sectors.Clear();
        _noFire.Clear();
        Commanders(repo, _subordinates, _commanders);
        foreach (var unit in _units!)
        {
            ref readonly var a = ref repo.GetComponentRO<EngagementArea>(unit);
            if (a.AllowArea != 0) _sectors.TryAdd(a.AllowArea, unit);
            for (int i = 0; i < a.NoFire.Count && i < EngagementArea.MaxNoFire; i++)
                if (a.NoFire.Items[i] != 0) _noFire.Add(a.NoFire.Items[i]);
            if (a.ArcSet != 0 && _commanders.Contains(unit)) SectorArcGizmo.DrawArc(draw, in a, ColourOf(NetOf(repo, unit)));
        }

        foreach (var (areaNet, unit) in _sectors)
        {
            if (!TryArea(repo, areaNet, out var points, out var origin)) continue;
            long unitNet = NetOf(repo, unit);
            var colour = ColourOf(unitNet);
            EntityPresentationGizmoShared.DrawClosedPolylineOutline(draw, points, origin, colour, thickness: 2.5f, layer: DebugTraceLayers.Sectors);
            var c = origin + Centroid(points);
            draw.DrawText(c.X, c.Y, new FixedString32(Truncate(NameOf(repo, unit, unitNet))), colour, layer: DebugTraceLayers.Sectors, fontSizePx: 11f);
        }

        foreach (var areaNet in _noFire)
        {
            if (!TryArea(repo, areaNet, out var points, out var origin)) continue;
            EntityPresentationGizmoShared.DrawClosedPolylineOutline(draw, points, origin, NoFireColour, thickness: 2f, layer: DebugTraceLayers.Sectors);
            Hatch(points, HatchSpacing, _hatch);
            foreach (var (a, b) in _hatch)
                draw.DrawLine(new Vector3(origin + a, 0f), new Vector3(origin + b, 0f), NoFireColour, 1f, layer: DebugTraceLayers.Sectors);
        }
    }

    /// <summary>The units some replicated <see cref="UnitSubordinate.Commander"/> names (§7 D4's test of a commander), read through
    /// <paramref name="subordinates"/> (a query over <see cref="UnitSubordinate"/>; null = none), into <paramref name="into"/>.</summary>
    public static void Commanders(EntityRepository repo, EntityQuery? subordinates, HashSet<Entity> into)
    {
        into.Clear();
        if (subordinates == null) return;
        foreach (var e in subordinates)
        {
            var commander = repo.GetComponentRO<UnitSubordinate>(e).Commander;
            if (!commander.IsNull) into.Add(commander);
        }
    }

    private static long NetOf(EntityRepository repo, Entity unit)
        => repo.IsComponentTypeRegistered<NetworkIdentity>() && repo.HasComponent<NetworkIdentity>(unit)
            ? repo.GetComponentRO<NetworkIdentity>(unit).Value : unit.Index;

    private static bool TryArea(EntityRepository repo, long areaNet, out IReadOnlyList<Vector2> points, out Vector2 origin)
    {
        points = Array.Empty<Vector2>();
        origin = default;
        var area = NetworkIdResolver.ResolveNetworkId(repo, areaNet);
        if (area.IsNull || !repo.IsAlive(area) || !WeatherField.TryOutline(repo, area, out var outline)) return false;
        if (repo.HasComponent<SimTransform>(area))
        {
            var p = repo.GetComponentRO<SimTransform>(area).Position;
            origin = new Vector2(p.X, p.Y);
        }
        points = outline.Points;
        return true;
    }

    private static string NameOf(EntityRepository repo, Entity unit, long net)
    {
        if (repo.IsComponentTypeRegistered<EntityInfo>() && repo.HasComponent<EntityInfo>(unit))
        {
            string name = repo.GetComponentRO<EntityInfo>(unit).Name.ToString();
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        return $"#{net}";
    }

    private static string Truncate(string s) => s.Length <= 31 ? s : s[..31];

    public static Vector2 Centroid(IReadOnlyList<Vector2> points)
    {
        var sum = Vector2.Zero;
        for (int i = 0; i < points.Count; i++) sum += points[i];
        return points.Count == 0 ? sum : sum / points.Count;
    }

    /// <summary>
    /// The hatch of a polygon: lines <c>x − y = k · spacing·√2</c> (45°, <paramref name="spacing"/> m apart) clipped to it — each line's
    /// crossings with the edges, sorted along it and paired (even-odd), so a concave polygon gets one segment per inside stretch.
    /// Public for the rail.
    /// </summary>
    public static void Hatch(IReadOnlyList<Vector2> poly, float spacing, List<(Vector2 A, Vector2 B)> into)
    {
        into.Clear();
        if (poly.Count < 3 || spacing <= 0f) return;
        float min = float.MaxValue, max = float.MinValue;
        foreach (var p in poly) { float d = p.X - p.Y; min = MathF.Min(min, d); max = MathF.Max(max, d); }
        float step = spacing * MathF.Sqrt(2f);
        Span<float> ts = stackalloc float[64];
        for (float k = MathF.Ceiling(min / step) * step; k <= max; k += step)
        {
            int n = 0;
            for (int i = 0; i < poly.Count && n < ts.Length; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                float da = a.X - a.Y - k, db = b.X - b.Y - k;
                if ((da < 0f) == (db < 0f) || da == db) continue;
                float u = da / (da - db);
                var hit = a + (b - a) * u;
                ts[n++] = hit.X + hit.Y;   // position along the line (x + y grows along it)
            }
            var span = ts[..n];
            span.Sort();
            for (int j = 0; j + 1 < n; j += 2)
            {
                // the line is x − y = k; a point on it at s = x + y is ((s + k) / 2, (s − k) / 2)
                into.Add((new Vector2((span[j] + k) / 2f, (span[j] - k) / 2f), new Vector2((span[j + 1] + k) / 2f, (span[j + 1] - k) / 2f)));
            }
        }
    }
}
