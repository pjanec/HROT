using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Services;
using Hrot.ScenarioEditor.Gizmos;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐⭐ CE-5011 B (docs/DESIGN_Terrain_Editor_UX.md §5 TE-10; mockup panel F) — the DRAWING GHOST of a terrain line / area feature and its stay-until-baked: while the shape is drawn (the <c>PointSequenceGizmo.Underlay</c>) a wall / fence / hedge is a thick line in its kind's
/// colour, a tree Row a dot per tree at the expander's own places (<see cref="FeatureExpander.LinePlacesSpaced"/> — never <c>OfKind</c>, which builds JSON), a tree Group / a scatter the area tinted. After a finish the shape STAYS drawn, fainter, until the live bake has taken that
/// feature (<see cref="LiveTerrainBakeInfo"/> lists its entity guid) — or a safety of <see cref="SafetyMs"/> — so it never blinks out between the finish and the bake (≈ 5 s warm).
/// ⭐ HOT PATH (R-2084): the dots are rebuilt only when the committed points / the spacing change; a steady frame allocates nothing.
/// </summary>
public sealed class TerrainDrawGhost
{
    /// <summary>A just-committed shape is dropped after this long even if the bake never lists it (ms).</summary>
    public const long SafetyMs = 30_000;

    private sealed class Pending
    {
        public byte Kind; public bool Closed; public Vector2 Anchor; public long StartMs; public Guid Guid;
        public readonly List<Vector2> Points = new(); public readonly List<Vector2> Dots = new();
    }

    private readonly Func<EntityRepository?> _world;
    private readonly Func<long> _nowMs;
    private readonly List<Pending> _pending = new();
    private readonly List<Vector2> _dots = new();
    private ulong _dotsKey = ulong.MaxValue;

    public TerrainDrawGhost(Func<EntityRepository?> world, Func<long>? nowMs = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _nowMs = nowMs ?? (() => Environment.TickCount64);
    }

    /// <summary>The tree positions of the Row being drawn (a rail's measure): one per tree, at <see cref="CountAlong"/>'s places.</summary>
    public IReadOnlyList<Vector2> RowDots => _dots;

    /// <summary>How many just-committed shapes are still waiting for the bake.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>The <c>PointSequenceGizmo.Underlay</c> for the feature <paramref name="o"/> being drawn.</summary>
    public Action<IDebugDrawBuilder, IReadOnlyList<Vector2>> UnderlayOf(TerrainDrawOptions o) => (draw, pts) => Draw(draw, pts, o);

    /// <summary>Whether a drawing ghost exists for <paramref name="o"/> (a wall / fence / hedge, a tree Row / Group, a scatter).</summary>
    public static bool Applies(TerrainDrawOptions o) => o.IsLine || o.IsTreeRow || o.IsTreeGroup || o.IsScatter;

    /// <summary>Draws the shape being drawn (<paramref name="pts"/> = the committed points) and the just-committed ones still waiting for the bake.</summary>
    public void Draw(IDebugDrawBuilder draw, IReadOnlyList<Vector2> pts, TerrainDrawOptions o)
    {
        byte layer = DebugTraceLayers.TacticalGraphics;
        var colour = TerrainFeatureLook.ColourOf(o.Kind);
        if (o.IsLine)
        {
            for (int i = 1; i < pts.Count; i++)
                draw.DrawLine(new Vector3(pts[i - 1].X, pts[i - 1].Y, 0f), new Vector3(pts[i].X, pts[i].Y, 0f), With(colour, 150), 4f, SizeMode.ScreenPixels, layer: layer);
        }
        else if (o.IsTreeRow)
        {
            ulong key = KeyOf(pts, o.SpacingShown);
            if (key != _dotsKey)   // only when the committed points / the spacing change (a user action)
            {
                _dotsKey = key; _dots.Clear();
                if (pts.Count >= 2) foreach (var (p, _) in FeatureExpander.LinePlacesSpaced(pts, o.SpacingShown, FeatureExpander.MaxScatterItems)) _dots.Add(p);
            }
            for (int i = 0; i < _dots.Count; i++) Dot(draw, _dots[i], colour, 200, layer);
        }
        else if (o.IsTreeGroup || o.IsScatter) Tint(draw, pts, colour, layer, 46);
        DrawPending(draw, layer);
    }

    /// <summary>Remembers the just-committed feature <paramref name="cmd"/> (born with <paramref name="o"/>) so it stays drawn until the bake takes it.</summary>
    public void Committed(TerrainDrawOptions o, SpawnEntityCommand cmd)
    {
        var poly = cmd.InitialComponents?.OfType<EditablePolyline>().FirstOrDefault();
        if (poly?.Points is not { Count: > 0 } rel) return;
        var at = cmd.InitialTransform?.Position ?? Vector3.Zero;
        var p = new Pending { Kind = o.Kind, Closed = !o.IsLine && !o.IsTreeRow, Anchor = new Vector2(at.X, at.Y), StartMs = _nowMs() };
        foreach (var r in rel) p.Points.Add(p.Anchor + r);
        if (o.IsTreeRow && p.Points.Count >= 2) foreach (var (d, _) in FeatureExpander.LinePlacesSpaced(p.Points, o.SpacingShown, FeatureExpander.MaxScatterItems)) p.Dots.Add(d);
        _pending.Add(p);
    }

    private void DrawPending(IDebugDrawBuilder draw, byte layer)
    {
        if (_pending.Count == 0) return;
        Prune();
        foreach (var p in _pending)
        {
            var colour = TerrainFeatureLook.ColourOf(p.Kind);
            if (p.Dots.Count > 0) { for (int i = 0; i < p.Dots.Count; i++) Dot(draw, p.Dots[i], colour, 90, layer); continue; }
            if (p.Closed) Tint(draw, p.Points, colour, layer, 24);
            int n = p.Points.Count, segs = p.Closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                var a = p.Points[i]; var b = p.Points[(i + 1) % n];
                draw.DrawLine(new Vector3(a.X, a.Y, 0f), new Vector3(b.X, b.Y, 0f), With(colour, 90), p.Closed ? 1.4f : 4f, SizeMode.ScreenPixels, layer: layer);
            }
        }
    }

    /// <summary>Drops the shapes the bake has taken (its info lists the feature's guid) and those older than <see cref="SafetyMs"/>; finds the new entity of a shape by its kind and place.</summary>
    private void Prune()
    {
        var world = _world();
        long now = _nowMs();
        LiveTerrainBakeInfo? info = world is not null && world.HasSingletonManaged<LiveTerrainBakeInfo>() ? world.GetSingletonManaged<LiveTerrainBakeInfo>() : null;
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var p = _pending[i];
            if (now - p.StartMs > SafetyMs) { _pending.RemoveAt(i); continue; }
            if (p.Guid == Guid.Empty && world is not null && world.IsComponentTypeRegistered<TerrainFeature>() && world.IsComponentTypeRegistered<EntityGuid>() && world.IsComponentTypeRegistered<SimTransform>())
                foreach (var e in world.Query().With<TerrainFeature>().With<EntityGuid>().With<SimTransform>().Build())
                {
                    var pos = world.GetComponentRO<SimTransform>(e).Position;
                    if (world.GetComponentRO<TerrainFeature>(e).Kind == p.Kind && MathF.Abs(pos.X - p.Anchor.X) < 0.05f && MathF.Abs(pos.Y - p.Anchor.Y) < 0.05f) { p.Guid = world.GetComponentRO<EntityGuid>(e).Value; break; }
                }
            if (p.Guid != Guid.Empty && info is not null && info.TryGet(p.Guid, out _)) _pending.RemoveAt(i);
        }
    }

    private static void Dot(IDebugDrawBuilder draw, Vector2 at, Rgba32 colour, byte alpha, byte layer)
        => draw.DrawSphere(new Vector3(at.X, at.Y, 0f), 4f, With(colour, alpha), 1f, SizeMode.ScreenPixels, layer: layer, fillColor: With(colour, (byte)(alpha / 2)));

    /// <summary>The area tinted: a translucent fan from the first corner (the shape's own outline is the tool's).</summary>
    private static void Tint(IDebugDrawBuilder draw, IReadOnlyList<Vector2> pts, Rgba32 colour, byte layer, byte alpha)
    {
        for (int i = 1; i + 1 < pts.Count; i++) draw.DrawFilledTriangle(pts[0], pts[i], pts[i + 1], With(colour, alpha), layer);
    }

    private static Rgba32 With(Rgba32 c, byte a) => new(c.R, c.G, c.B, a);

    private static ulong KeyOf(IReadOnlyList<Vector2> pts, float spacing)
    {
        ulong h = 1469598103934665603UL;
        void Mix(long v) { h ^= (ulong)v; h *= 1099511628211UL; }
        Mix(pts.Count); Mix((long)MathF.Round(spacing * 100f));
        for (int i = 0; i < pts.Count; i++) { Mix((long)MathF.Round(pts[i].X * 100f)); Mix((long)MathF.Round(pts[i].Y * 100f)); }
        return h;
    }
}
