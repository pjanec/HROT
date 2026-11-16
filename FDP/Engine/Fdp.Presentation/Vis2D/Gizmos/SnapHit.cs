using System.Numerics;

namespace Fdp.Toolkit.Vis2D.Gizmos;

/// <summary>What a drawing point snapped to (<see cref="SnapKind.None"/> = nothing in reach).</summary>
public enum SnapKind : byte { None = 0, LineEnd = 1, Wall = 2, RoadEdge = 3 }

/// <summary>
/// ⭐ CE-5009 (docs/DESIGN_Terrain_Editor_UX.md §5 TE-6, §6 S-4 build notes) — one snap answer: the snapped <see cref="Point"/>, the target it lies on
/// (<see cref="A"/>–<see cref="B"/>: the wall / road edge segment, or the end point twice) and its <see cref="Kind"/>. A value: the hook allocates nothing.
/// The drawing gizmo is generic (FDP); a host supplies the hook (the Hrot terrain tools: road edges, house walls, line ends).
/// </summary>
public readonly record struct SnapHit(Vector2 Point, Vector2 A, Vector2 B, SnapKind Kind)
{
    public static readonly SnapHit None = default;
    public bool Hit => Kind != SnapKind.None;
}
