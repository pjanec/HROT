using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Common.Editing;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐ <c>CE-4624</c> MS-U2 (docs/designs/minefields/DESIGN_Minefields.md MR-8, KR-11, MF-12; the look: <c>minefields-visuals.svg</c>, panel A) — the ONE
/// drawing of a minefield, 2-D and 3-D. A field is born WITHOUT an overlay style, so the generic <see cref="MapOverlayGizmo"/> never draws it; this does,
/// for the viewer:
/// <list type="bullet">
///   <item><b>the truth</b> — the side-knowledge gate OFF (the default, KR-11: Zeus), or a station with no side (the instructor): the outline, EVERY mine
///     (an AP cross, an AT square, the derived pattern from a per-field cache), the struck ones (a star), the lanes;</item>
///   <item><b>a side, the gate ON</b> — the field KNOWN IN FULL (known at start, MF-15, or marked): the tactical graphic (outline, a light fill, a mine
///     symbol every ~60 m on the edge, pickets in 3-D) and the lanes known to that side, never single mines; otherwise only the side's knowledge
///     CELLS (suspected amber, known-mined red, known-clear green), no outline at all.</item>
/// </list>
/// ⭐ 0 B on a steady frame: the outline, the mines, the grid, the fill triangles and the label are rebuilt only when the field changes (its parameters,
/// its outline instance or its position).
/// </summary>
[GizmoProjector(typeof(SimTransform), typeof(Minefield), DrawsOffScreen = true)]
public sealed class MinefieldGizmo : IStatelessGizmo
{
    /// <summary>⭐ R-1054 — the side-limited map (the P-3 gate, KR-11): OFF ⇒ every viewer sees the truth (Zeus).</summary>
    public const string SideLimitedMap = "map.sideLimited";
    public static readonly GizmoSettingValue DefaultSideLimitedMap = GizmoSettingValue.From(false);

    /// <summary>The most mine marks drawn per field — a larger field draws every n-th (the instructor's dots are a picture, not a count).</summary>
    public const int MaxDots = 3000;

    public static readonly Rgba32 FieldColour = new(230, 85, 85, 230);
    public static readonly Rgba32 SuspectedColour = new(242, 192, 51, 70);
    public static readonly Rgba32 MinedColour = new(230, 85, 85, 110);
    public static readonly Rgba32 ClearColour = new(91, 208, 106, 100);
    public static readonly Rgba32 LaneColour = new(255, 255, 255, 235);
    public static readonly Rgba32 MineColour = new(216, 208, 184, 230);
    public static readonly Rgba32 StruckColour = new(255, 138, 58, 255);
    public static readonly Rgba32 FillColour = new(230, 85, 85, 40);

    private readonly GizmoSettingsRegistry? _settings;
    private readonly uint _sideLimitedKey = GizmoSettingsRegistry.ComputeHash(SideLimitedMap);
    private readonly Dictionary<Entity, FieldCache> _cache = new();
    private readonly HashSet<int> _gone = new();
    private readonly HashSet<int> _struck = new();
    private EntityRepository? _queryRepo;
    private EntityQuery? _strikes;

    public MinefieldGizmo() : this(null) { }

    public MinefieldGizmo(GizmoSettingsRegistry? settings)
    {
        _settings = settings;
        settings?.RegisterSetting(SideLimitedMap, DefaultSideLimitedMap);
    }

    /// <summary>⭐ R-1054 — whose eyes the map shows: null = the truth (gate off, or a station without a side), else the seat's side.</summary>
    public int? ViewerSide(ISimulationView view)
    {
        bool limited = _settings != null ? _settings.Read(_sideLimitedKey).BoolValue : DefaultSideLimitedMap.BoolValue;
        if (!limited) return null;
        return StationMode.SideOf(view) is { } side ? (int)side : null;
    }

    public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
    {
        if (view is not EntityRepository repo) return;
        ref readonly var f = ref view.GetComponentRO<Minefield>(entity);
        var c = CacheOf(repo, entity, in f);
        if (c == null) return;
        int? side = ViewerSide(view);
        if (side is null) { DrawTruth(repo, entity, in f, c, draw); Pick(view, entity, c, draw); return; }
        int s = side.Value;
        bool full = (s >= 0 && s < 16 && (f.KnownAtStartSides & (1 << s)) != 0) || PlaceKnowledgeTable.Of(view).Marked(s, entity);
        if (full) { DrawKnown(in f, c, s, draw); Pick(view, entity, c, draw); }
        else DrawCells(view, entity, c, s, draw);   // ⛔ no pick: a side that has not seen the field cannot select it
    }

    // ── the three pictures ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private void DrawTruth(EntityRepository repo, Entity entity, in Minefield f, FieldCache c, IDebugDrawBuilder draw)
    {
        DrawOutline(c, draw);
        EdgeSymbols(c, draw);
        CollectGone(repo, entity);
        int stride = Math.Max(1, (c.Mines.Count + MaxDots - 1) / MaxDots);
        for (int i = 0; i < c.Mines.Count; i += stride)
        {
            var m = c.Mines[i];
            int key = Key(m.Row, m.Col);
            var p = new Vector3(m.At, c.MineZ[i]);
            if (_struck.Contains(key)) { Star(draw, p, 2.2f); continue; }
            if (_gone.Contains(key)) continue;   // cleared: nothing left to draw
            if (m.Type == MineType.AntiTank) Square(draw, p, 1.0f, MineColour);
            else Cross(draw, p, 0.9f, MineColour);
        }
        DrawLanes(in f, c, -1, draw);
    }

    private static void DrawKnown(in Minefield f, FieldCache c, int side, IDebugDrawBuilder draw)
    {
        for (int i = 0; i + 2 < c.Fill.Length; i += 3)
            draw.DrawFilledTriangle(c.Outline[c.Fill[i]], c.Outline[c.Fill[i + 1]], c.Outline[c.Fill[i + 2]], FillColour, target: Flat);   // 2-D only: the 3-D map shows the dashed outline + the pickets (panel B)
        DrawOutline(c, draw);
        EdgeSymbols(c, draw);
        for (int i = 0; i < c.Symbols.Count; i++)
        {
            var p = c.Symbols[i];
            // 3-D: a picket with a red triangle sign, point down, facing the field (MF-8's marking; panel B: ~2.2 m tall, a 1 m sign)
            var top = p + new Vector3(0f, 0f, 2.2f);
            draw.DrawLine(p, top, new Rgba32(80, 60, 40, 255), 3f, target: PipelineTarget.Viewport3D);
            var l = top + new Vector3(-0.55f, 0f, 0f); var r = top + new Vector3(0.55f, 0f, 0f); var b = top + new Vector3(0f, 0f, -0.9f);
            var m = (l + r + b) / 3f;
            draw.DrawLine(l, r, FieldColour, 4f, target: PipelineTarget.Viewport3D);
            draw.DrawLine(r, b, FieldColour, 4f, target: PipelineTarget.Viewport3D);
            draw.DrawLine(b, l, FieldColour, 4f, target: PipelineTarget.Viewport3D);
            draw.DrawLine(top + new Vector3(0f, 0f, -0.18f), b + new Vector3(0f, 0f, 0.3f), FieldColour, 6f, target: PipelineTarget.Viewport3D);   // the sign's body (a thick centre line fills the triangle)
            draw.DrawLine(m + new Vector3(-0.2f, 0f, 0.2f), m + new Vector3(0.2f, 0f, 0.2f), FieldColour, 6f, target: PipelineTarget.Viewport3D);
        }
        draw.DrawText(c.Centroid.X, c.Centroid.Y, c.Label, FieldColour);
        DrawLanes(in f, c, side, draw);
    }

    private static void DrawCells(ISimulationView view, Entity entity, FieldCache c, int side, IDebugDrawBuilder draw)
    {
        var table = PlaceKnowledgeTable.Of(view);
        var g = c.Grid;
        for (int row = 0; row < g.Rows; row++)
            for (int col = 0; col < g.Cols; col++)
            {
                var k = table.Knows(side, entity, col, row);
                if (k == MineCellKnowledge.Unknown) continue;
                var colour = k == MineCellKnowledge.Suspected ? SuspectedColour : k == MineCellKnowledge.KnownMined ? MinedColour : ClearColour;
                CellQuad(g, col, row, out var a, out var b, out var cc, out var d);
                draw.DrawFilledTriangle(a, b, cc, colour);
                draw.DrawFilledTriangle(a, cc, d, colour);
            }
    }

    /// <summary>The outline is the field's click target (select → its Details form), where the viewer can see the field.</summary>
    private static void Pick(ISimulationView view, Entity entity, FieldCache c, IDebugDrawBuilder draw)
    {
        if (c.OutlineSource is IAreaOutline o) EntityPresentationGizmoShared.EmitPickSegments(draw, view, entity, o.Points, c.Anchor, isClosed: true);
    }

    // ── the pieces ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The 2-D map only: <c>Map2D | FlatOnly</c> — a 3-D pass never draws it (the 3-D map has its own look, panel B).</summary>
    private const PipelineTarget Flat = PipelineTarget.Map2D | PipelineTarget.FlatOnly;

    /// <summary>⭐ Panel A / B — the outline: SOLID in the 2-D map, DASHED in 3-D (a known field's red dashes on the ground).</summary>
    private static void DrawOutline(FieldCache c, IDebugDrawBuilder draw)
    {
        for (int i = 0; i + 1 < c.Draped.Count; i++)
        {
            draw.DrawLine(c.Draped[i], c.Draped[i + 1], FieldColour, 2.2f, target: Flat);
            draw.DrawLine(c.Draped[i], c.Draped[i + 1], FieldColour, 2.5f, target: PipelineTarget.Viewport3D, style: LineStyle.Dashed);
        }
    }

    /// <summary>The ⊘ signs on the edge, every ~60 m (2-D only; the 3-D map has pickets instead).</summary>
    private static void EdgeSymbols(FieldCache c, IDebugDrawBuilder draw)
    {
        for (int i = 0; i < c.Symbols.Count; i++)
        {
            var p = c.Symbols[i];
            Ring(draw, p, 3.0f, FieldColour, Flat);
            draw.DrawLine(p + new Vector3(-2.1f, -2.1f, 0f), p + new Vector3(2.1f, 2.1f, 0f), FieldColour, 1.6f, target: Flat);
        }
    }

    private static void DrawLanes(in Minefield f, FieldCache c, int side, IDebugDrawBuilder draw)
    {
        for (int i = 0; i < f.LaneCount && i < Minefield.MaxLanes; i++)
        {
            var lane = f.Lane(i);
            if (side >= 0 && (side >= 16 || (lane.KnownToSides & (1 << side)) == 0)) continue;
            var d = lane.B - lane.A; float len = d.Length();
            if (len < 0.1f) continue;
            var dir = d / len; var n = new Vector2(-dir.Y, dir.X) * (lane.Width * 0.5f);
            foreach (float sg in Sides)
            {
                var a = lane.A + n * sg; var b = lane.B + n * sg;
                draw.DrawLine(c.Lift(a), c.Lift(b), LaneColour, 1.6f, style: LineStyle.Dashed);
            }
            Chevron(draw, c, lane.A + d * 0.08f, dir, lane.Width);
            Chevron(draw, c, lane.A + d * 0.92f, dir, lane.Width);
        }
    }

    private static readonly float[] Sides = { 1f, -1f };

    private static void Chevron(IDebugDrawBuilder draw, FieldCache c, Vector2 at, Vector2 dir, float width)
    {
        var n = new Vector2(-dir.Y, dir.X) * (width * 0.35f); var back = -dir * (width * 0.35f);
        var tip = c.Lift(at);
        draw.DrawLine(c.Lift(at + back + n), tip, LaneColour, 2f);
        draw.DrawLine(c.Lift(at + back - n), tip, LaneColour, 2f);
    }

    private static void Cross(IDebugDrawBuilder draw, Vector3 p, float r, Rgba32 colour)
    {
        draw.DrawLine(p + new Vector3(-r, 0f, 0f), p + new Vector3(r, 0f, 0f), colour, 1.2f);
        draw.DrawLine(p + new Vector3(0f, -r, 0f), p + new Vector3(0f, r, 0f), colour, 1.2f);
    }

    private static void Square(IDebugDrawBuilder draw, Vector3 p, float r, Rgba32 colour)
    {
        var a = p + new Vector3(-r, -r, 0f); var b = p + new Vector3(r, -r, 0f); var cc = p + new Vector3(r, r, 0f); var d = p + new Vector3(-r, r, 0f);
        draw.DrawLine(a, b, colour, 1.4f); draw.DrawLine(b, cc, colour, 1.4f); draw.DrawLine(cc, d, colour, 1.4f); draw.DrawLine(d, a, colour, 1.4f);
    }

    private static void Star(IDebugDrawBuilder draw, Vector3 p, float r)
    {
        for (int i = 0; i < 4; i++)
        {
            float a = i * MathF.PI / 4f; var o = new Vector3(MathF.Cos(a) * r, MathF.Sin(a) * r, 0f);
            draw.DrawLine(p - o, p + o, StruckColour, 2f);
        }
    }

    private static void Ring(IDebugDrawBuilder draw, Vector3 p, float r, Rgba32 colour, PipelineTarget target)
    {
        const int N = 10;
        var prev = p + new Vector3(r, 0f, 0f);
        for (int i = 1; i <= N; i++)
        {
            float a = i * MathF.Tau / N; var next = p + new Vector3(MathF.Cos(a) * r, MathF.Sin(a) * r, 0f);
            draw.DrawLine(prev, next, colour, 1.6f, target: target);
            prev = next;
        }
    }

    /// <summary>The four world corners of knowledge cell (<paramref name="col"/>, <paramref name="row"/>), on the field's rotated lattice.</summary>
    public static void CellQuad(in MinefieldKnowledgeOps.KnowledgeGrid g, int col, int row, out Vector2 a, out Vector2 b, out Vector2 c, out Vector2 d)
    {
        var o = g.OriginUv;
        a = MinePattern.ToWorld(o + new Vector2(col * g.Cell, row * g.Cell), g.Anchor, g.OrientationDeg);
        b = MinePattern.ToWorld(o + new Vector2((col + 1) * g.Cell, row * g.Cell), g.Anchor, g.OrientationDeg);
        c = MinePattern.ToWorld(o + new Vector2((col + 1) * g.Cell, (row + 1) * g.Cell), g.Anchor, g.OrientationDeg);
        d = MinePattern.ToWorld(o + new Vector2(col * g.Cell, (row + 1) * g.Cell), g.Anchor, g.OrientationDeg);
    }

    private static int Key(short row, short col) => (row << 16) | (ushort)col;

    /// <summary>The struck and cleared mines of <paramref name="field"/> this frame, from every <see cref="MinefieldStrikes"/> record keyed by its guid.</summary>
    private void CollectGone(EntityRepository repo, Entity field)
    {
        _gone.Clear(); _struck.Clear();
        if (!repo.IsComponentTypeRegistered<MinefieldStrikes>() || !repo.IsComponentTypeRegistered<EntityGuid>() || !repo.HasComponent<EntityGuid>(field)) return;
        var guid = repo.GetComponentRO<EntityGuid>(field).Value;
        if (!ReferenceEquals(repo, _queryRepo)) { _queryRepo = repo; _strikes = repo.Query().With<MinefieldStrikes>().Build(); }
        foreach (var e in _strikes!)
        {
            ref readonly var s = ref repo.GetComponentRO<MinefieldStrikes>(e);
            if (s.Field != guid) continue;
            for (int i = 0; i < s.Count && i < MinefieldStrikes.Capacity; i++)
            {
                var (row, col, how) = s.Get(i);
                int key = Key(row, col);
                _gone.Add(key);
                if (how == MineGone.Struck) _struck.Add(key);
            }
        }
    }

    // ── the per-field cache ────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>What a field's drawing is built from, rebuilt only when the field changes.</summary>
    public sealed class FieldCache
    {
        public Minefield Params;
        public object? OutlineSource;
        public Vector2 Anchor;
        public readonly List<Vector2> Outline = new();
        public readonly List<Vector3> Draped = new();
        public readonly List<Mine> Mines = new();
        public readonly List<float> MineZ = new();
        public readonly List<Vector3> Symbols = new();
        public int[] Fill = Array.Empty<int>();
        public MinefieldKnowledgeOps.KnowledgeGrid Grid;
        public Vector2 Centroid;
        public Fdp.Core.FixedString32 Label;
        internal Fdp.Toolkit.World.IWorldQuery? Ground;

        public Vector3 Lift(Vector2 p) => new(p.X, p.Y, (Ground?.GroundHeightAt(p.X, p.Y) ?? 0f) + 0.15f);
    }

    /// <summary>The field's cache, rebuilt when its parameters, outline instance or position changed; null when it has no outline yet.</summary>
    public FieldCache? CacheOf(EntityRepository repo, Entity entity, in Minefield f)
    {
        var source = repo.GetManagedComponentByTypeId(entity, GlobalComponentIds.EditablePolyline);
        if (source is not IAreaOutline outline || outline.Points.Count < 3) return null;
        var at = repo.GetComponentRO<SimTransform>(entity).Position;
        var anchor = new Vector2(at.X, at.Y);
        if (_cache.TryGetValue(entity, out var c) && ReferenceEquals(c.OutlineSource, source) && c.Anchor == anchor && Same(in c.Params, in f)) return c;
        c ??= new FieldCache();
        _cache[entity] = c;
        c.Params = f; c.OutlineSource = source; c.Anchor = anchor;
        c.Ground = Fdp.Toolkit.World.WorldQuery.Of(repo);
        c.Outline.Clear();
        for (int i = 0; i < outline.Points.Count; i++) c.Outline.Add(anchor + outline.Points[i]);
        c.Mines.Clear();
        MinePattern.MinesIn(in f, anchor, c.Outline, c.Mines);
        c.MineZ.Clear();
        for (int i = 0; i < c.Mines.Count; i++) c.MineZ.Add(c.Lift(c.Mines[i].At).Z);
        c.Grid = MinefieldKnowledgeOps.Grid(in f, anchor, c.Outline);
        c.Fill = PolygonMath.Triangulate(c.Outline);
        c.Draped.Clear(); c.Symbols.Clear();
        var sum = Vector2.Zero;
        for (int i = 0; i < c.Outline.Count; i++)
        {
            var a = c.Outline[i]; var b = c.Outline[(i + 1) % c.Outline.Count];
            sum += a;
            float len = Vector2.Distance(a, b);
            int steps = Math.Max(1, (int)MathF.Ceiling(len / 10f));          // draped every 10 m, so a 3-D edge follows the ground
            for (int k = 0; k < steps; k++) c.Draped.Add(c.Lift(Vector2.Lerp(a, b, k / (float)steps)));
            int marks = Math.Max(1, (int)(len / 60f));                       // a mine symbol / picket every ~60 m of edge
            for (int k = 0; k < marks; k++) c.Symbols.Add(c.Lift(Vector2.Lerp(a, b, (k + 0.5f) / marks)));
        }
        c.Draped.Add(c.Draped[0]);
        c.Centroid = sum / c.Outline.Count;
        c.Label = new Fdp.Core.FixedString32(LabelOf(in f));
        return c;
    }

    /// <summary>"Mixed · 14 m" — the field's type and spacing, invariant culture (R-4221).</summary>
    public static string LabelOf(in Minefield f)
        => (f.Type switch { MineType.AntiPersonnel => "AP", MineType.AntiTank => "AT", _ => "Mixed" })
           + " · " + f.SpacingM.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " m";

    private static bool Same(in Minefield a, in Minefield b)
        => System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref System.Runtime.CompilerServices.Unsafe.AsRef(in a), 1))
           .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref System.Runtime.CompilerServices.Unsafe.AsRef(in b), 1)));
}
