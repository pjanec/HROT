using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Terrain;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐⭐ <c>CE-4592</c> (P-5 part 2b; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md §4a G-19, §9.16; mockup <c>docs/UX/ground-shaping-and-ditches.svg</c> panels B, C, D) — what ONE ground
/// shape / ditch DOES, measured on the merge every node runs: the shape compiled by the ONE expander, put in the overlay document with the roads it reaches (the ONE
/// <see cref="OverlayExpansion.WorldDocument(TerrainWorld, IReadOnlyList{JsonObject}, TerrainRoads?)"/>), parsed and merged — so the read-out cannot disagree with what the save
/// and the live bake will make. ⭐ The draw panel's hint, the 2-D tint and the Details Effect section read the same object. Built on a user action (a point added, a value
/// typed), never per frame (R-2084): the callers cache it by <see cref="Key"/>.
/// </summary>
public sealed class GroundEffect
{
    /// <summary>The tint's cells: each square <see cref="CellSize"/> m from <see cref="CellMin"/>, the ground's change (m, + up) there.</summary>
    public Vector2[] CellMin { get; private init; } = Array.Empty<Vector2>();
    public float[] CellDelta { get; private init; } = Array.Empty<float>();
    public float CellSize { get; private init; }
    /// <summary>The steepest road grade the shape makes (a fraction; 0 = no road reached) — G-19.</summary>
    public float RoadGrade { get; private init; }
    /// <summary>Square metres inside the shape (a ditch: its strip).</summary>
    public float Area { get; private init; }
    /// <summary>The largest change of the ground (m, its sign kept).</summary>
    public float Change { get; private init; }
    /// <summary>Buildings within the shape's reach — islands, kept at their ground (G-18).</summary>
    public int HousesKept { get; private init; }
    /// <summary>Road corridors the shape reaches — re-levelled (G-24), a ditch stops at them (G-29).</summary>
    public int RoadsKept { get; private init; }
    /// <summary>The obstacle quads of a ditch after the road cut (0 for a non-ditch).</summary>
    public int ObstacleParts { get; private init; }
    public bool IsDitch { get; private init; }
    public ulong Key { get; private init; }

    public bool OverGrade => RoadGrade > GroundShapes.GradeWarning;

    public static readonly GroundEffect None = new();

    /// <summary>A key for the inputs (mode, value, the points) — equal keys ⇒ the same effect.</summary>
    public static ulong KeyOf(string mode, float value, IReadOnlyList<Vector2> points)
    {
        ulong h = 1469598103934665603UL;
        void Mix(uint v) { h ^= v; h *= 1099511628211UL; }
        foreach (char c in mode) Mix(c);
        Mix(BitConverter.SingleToUInt32Bits(value));
        for (int i = 0; i < points.Count; i++) { Mix(BitConverter.SingleToUInt32Bits(points[i].X)); Mix(BitConverter.SingleToUInt32Bits(points[i].Y)); }
        Mix((uint)points.Count);
        return h;
    }

    /// <summary>
    /// The effect of a ground feature of <paramref name="mode"/> (<c>level</c> / <c>raise</c> / <c>lower</c>, or a ditch preset) and <paramref name="value"/> (its <c>Pad</c>) over
    /// <paramref name="points"/> (terrain-local; an area ≥ 3, a ditch line ≥ 2) on <paramref name="base"/>. <see cref="None"/> when the base has no height grid or the shape is short.
    /// </summary>
    public static GroundEffect Of(TerrainWorld @base, TerrainRoads? roads, TerrainAssets assets, string mode, float value, IReadOnlyList<Vector2> points)
    {
        bool ditch = DitchPresets.Of(mode) is not null;
        if (@base.Height == null || points.Count < (ditch ? 2 : 3)) return None;
        var pts = new Vector2[points.Count];
        for (int i = 0; i < pts.Length; i++) pts[i] = points[i];
        var features = FeatureExpander.OfKind(TerrainFeature.Ground, "preview", mode, 0f, value, uint.MaxValue, Vector2.Zero, pts, @base, assets, Array.Empty<string?>());
        var overlay = TerrainWorldParser.Parse(OverlayExpansion.WorldDocument(@base, features, roads).ToJsonString(), @base.Name + "/preview", assets);
        var merged = TerrainWorldMerge.Merge(@base, overlay);
        if (merged.Height == null || overlay.GroundShapes.Count == 0) return None;

        // the reach: every shape's polygon + skirt
        var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
        float area = 0f, grade = 0f;
        foreach (var s in overlay.GroundShapes)
        {
            float skirt = GroundShapes.SkirtOf(s, @base.Height);
            foreach (var q in s.Polygon) { min = Vector2.Min(min, q - new Vector2(skirt)); max = Vector2.Max(max, q + new Vector2(skirt)); }
            area += TerrainDrawOptions.Area(s.Polygon);
            grade = MathF.Max(grade, GroundShapes.SteepestRoadGrade(merged, overlay.RoadCorridors, in s));
        }
        int houses = 0;
        foreach (var b in @base.Buildings)
        {
            if (b.Footprint.Length < 3) continue;
            var lo = new Vector2(float.MaxValue); var hi = new Vector2(float.MinValue);
            foreach (var q in b.Footprint) { lo = Vector2.Min(lo, q); hi = Vector2.Max(hi, q); }
            if (hi.X >= min.X && lo.X <= max.X && hi.Y >= min.Y && lo.Y <= max.Y) houses++;
        }
        int obstacles = 0;
        foreach (var s in merged.GroundShapes) if (s.Obstacle) obstacles++;

        // the tint: ≤ 64 × 64 cells over the reach, at least the grid's own spacing
        float extent = MathF.Max(max.X - min.X, max.Y - min.Y);
        float step = MathF.Max(@base.Height.CellSize, extent / 64f);
        var mins = new List<Vector2>(); var deltas = new List<float>();
        float change = 0f;
        for (float y = min.Y; y < max.Y; y += step)
            for (float x = min.X; x < max.X; x += step)
            {
                float cx = x + step * 0.5f, cy = y + step * 0.5f;
                float d = merged.Height.Sample(cx, cy) - @base.Height.Sample(cx, cy);
                if (MathF.Abs(d) < 0.05f) continue;
                mins.Add(new Vector2(x, y)); deltas.Add(d);
                if (MathF.Abs(d) > MathF.Abs(change)) change = d;
            }
        return new GroundEffect
        {
            CellMin = mins.ToArray(), CellDelta = deltas.ToArray(), CellSize = step, RoadGrade = grade, Area = area, Change = change,
            HousesKept = houses, RoadsKept = overlay.RoadCorridors.Count, ObstacleParts = obstacles, IsDitch = ditch, Key = KeyOf(mode, value, points),
        };
    }
}
