using System;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>The four numbers of an air arena (DESIGN_Air_Combat §3 AC-10): centre (x, y), radius, floor and ceiling (m above the ground under the centre).</summary>
public readonly record struct ArenaShape(Vector2 Centre, float Radius, float FloorM, float CeilingM);

/// <summary>The four handles of the arena's authoring gizmo (mockup <c>air-combat-arena.svg</c>): the centre, the radius on the ring (east), the floor (south-west on the ring) and the ceiling (south-east).</summary>
public enum ArenaHandle : byte { None = 0, Centre, Radius, Floor, Ceiling }

/// <summary>
/// ⭐ CE-4647 (AC-15 step 6; docs/designs/air-attack/DESIGN_Air_Combat.md §3 AC-10, §4d; mockup <c>air-combat-arena.svg</c>) — the arena's LOOK and its AUTHORING: <see cref="Draw"/> is the ONE drawing of an arena (2-D: a faint dashed ring and a 6 % fill, a label,
/// the four handles while selected; 3-D: the floor ring and the ceiling ring, dashed, joined by four ticks, spokes on the floor), <see cref="Handles"/> the pure maths of the four handles (where each is, which one a point hits, what dragging it does — clamped).
/// <para>🔧 <c>ArenaSeam</c> (the minefield's R-1055 precedent): the arena is the air lane's replicated <c>AirArena</c> component (AC-10; not merged yet — ⛔ a stand-in component would need a component id, ui's block is exhausted), so the drawing and the handle maths work on the
/// <see cref="ArenaShape"/> a projector fills; ⭐ re-bind = a <c>[GizmoProjector(typeof(SimTransform), typeof(AirArena))]</c> class that reads the component into an <see cref="ArenaShape"/>, calls <see cref="Draw"/>, and applies a drag by the one edit path (undo / redo),
/// plus the Details form's four fields (<see cref="Clamp"/> is its validation). Floor and ceiling are heights ABOVE THE GROUND at the centre (a stand-in reading of "floor 150 m"; the air lane's `AirArenaSystem` fixes it).</para>
/// ⭐ HOT PATH: no allocation in <see cref="Draw"/> (a ring is a fixed 72-segment loop, no lists).
/// </summary>
public static class AirArenaGizmo
{
    public static readonly Rgba32 RingColour = new(106, 176, 255, 150);
    public static readonly Rgba32 CeilingColour = new(106, 176, 255, 90);
    public static readonly Rgba32 FillColour = new(106, 176, 255, 15);   // ≈ 6 %
    public static readonly Rgba32 HandleColour = new(242, 192, 51, 255);

    /// <summary>The ring is drawn in this many segments; the 2-D fill in as many triangles.</summary>
    public const int Segments = 72;
    /// <summary>The limits an arena may be authored to (m): a stage must fit the map and be watchable.</summary>
    public const float MinRadius = 500f, MaxRadius = 30000f, MinHeight = 0f, MaxHeight = 20000f, MinBand = 200f;

    private const PipelineTarget Flat = PipelineTarget.Map2D | PipelineTarget.FlatOnly;

    /// <summary>The arena of <paramref name="a"/> on the ground at the centre <paramref name="groundZ"/>; <paramref name="selected"/> draws the handles (and the label).</summary>
    public static void Draw(IDebugDrawBuilder draw, in ArenaShape a, float groundZ, bool selected)
    {
        var c = a.Centre; float r = a.Radius;
        float floor = groundZ + a.FloorM, ceiling = groundZ + a.CeilingM;
        // 2-D: the ring, flat, dashed, with the faint fill
        var prev = new Vector3(c.X + r, c.Y, groundZ);
        for (int i = 1; i <= Segments; i++)
        {
            float t = i * MathF.Tau / Segments;
            var next = new Vector3(c.X + MathF.Cos(t) * r, c.Y + MathF.Sin(t) * r, groundZ);
            draw.DrawLine(prev, next, RingColour, 1.6f, target: Flat, style: LineStyle.Dashed);
            draw.DrawFilledTriangle(c, new Vector2(prev.X, prev.Y), new Vector2(next.X, next.Y), FillColour, target: Flat);
            // 3-D: the floor ring and the ceiling ring
            draw.DrawLine(new Vector3(prev.X, prev.Y, floor), new Vector3(next.X, next.Y, floor), RingColour, 3f, target: PipelineTarget.Viewport3D, style: LineStyle.Dashed);
            draw.DrawLine(new Vector3(prev.X, prev.Y, ceiling), new Vector3(next.X, next.Y, ceiling), CeilingColour, 2f, target: PipelineTarget.Viewport3D, style: LineStyle.Dashed);
            prev = next;
        }
        for (int k = 0; k < 4; k++)   // four ticks between the rings, four spokes on the floor
        {
            float t = k * MathF.PI / 2f;
            var rim = new Vector2(c.X + MathF.Cos(t) * r, c.Y + MathF.Sin(t) * r);
            draw.DrawLine(new Vector3(rim.X, rim.Y, floor), new Vector3(rim.X, rim.Y, ceiling), CeilingColour, 1f, target: PipelineTarget.Viewport3D, style: LineStyle.Dotted);
            draw.DrawLine(new Vector3(c.X, c.Y, floor), new Vector3(rim.X, rim.Y, floor), CeilingColour, 1f, target: PipelineTarget.Viewport3D, style: LineStyle.Dotted);
        }
        if (!selected) return;
        for (var h = ArenaHandle.Centre; h <= ArenaHandle.Ceiling; h++)   // ⭐ screen-pixel boxes (a 9 m handle on a 6 km ring would be a speck): the centre a square, the radius a diamond, the floor a flat bar, the ceiling a tall one
        {
            var p = Handles.Position(in a, h, groundZ);
            var extents = h switch { ArenaHandle.Floor => new Vector2(9f, 5f), ArenaHandle.Ceiling => new Vector2(5f, 9f), _ => new Vector2(7f, 7f) };
            draw.DrawBox2D(new Vector2(p.X, p.Y), extents, HandleColour, h == ArenaHandle.Radius ? 45f : 0f, 2f, SizeMode.ScreenPixels, Flat, fillColor: HandleColour);
        }
    }

    /// <summary>The four handles: where they are, which one a point hits, what dragging one does.</summary>
    public static class Handles
    {
        /// <summary>The handle's place on the map (z = <paramref name="groundZ"/>): the centre; the radius on the ring at east; the floor on the ring at south-west; the ceiling on the ring at south-east.</summary>
        public static Vector3 Position(in ArenaShape a, ArenaHandle h, float groundZ)
        {
            var c = a.Centre;
            return h switch
            {
                ArenaHandle.Radius => new Vector3(c.X + a.Radius, c.Y, groundZ),
                ArenaHandle.Floor => new Vector3(c.X - a.Radius * 0.7071f, c.Y - a.Radius * 0.7071f, groundZ),
                ArenaHandle.Ceiling => new Vector3(c.X + a.Radius * 0.7071f, c.Y - a.Radius * 0.7071f, groundZ),
                _ => new Vector3(c.X, c.Y, groundZ),
            };
        }

        /// <summary>The handle within <paramref name="tolerance"/> metres of <paramref name="at"/> (the nearest wins; the centre before the ring ones), else none.</summary>
        public static ArenaHandle HitTest(in ArenaShape a, Vector2 at, float tolerance)
        {
            var best = ArenaHandle.None; float bestD = tolerance;
            for (var h = ArenaHandle.Centre; h <= ArenaHandle.Ceiling; h++)
            {
                var p = Position(in a, h, 0f);
                float d = Vector2.Distance(new Vector2(p.X, p.Y), at);
                if (d <= bestD) { bestD = d; best = h; }
            }
            return best;
        }

        /// <summary>The arena after dragging <paramref name="h"/> to the map point <paramref name="to"/>: the centre moves there; the radius becomes its distance from the centre; the floor or the ceiling follows the point's distance along the vertical of the drag
        /// (the SOUTH-WEST handle is the floor: dragging it OUT lowers the floor's value to 0 … the ceiling's in: both are "metres = the distance travelled from the centre × <paramref name="metresPerMetre"/>") — see <see cref="Clamp"/>.</summary>
        public static ArenaShape Drag(in ArenaShape a, ArenaHandle h, Vector2 to, float metresPerMetre = 0.25f)
        {
            switch (h)
            {
                case ArenaHandle.Centre: return Clamp(a with { Centre = to });
                case ArenaHandle.Radius: return Clamp(a with { Radius = Vector2.Distance(a.Centre, to) });
                case ArenaHandle.Floor: return Clamp(a with { FloorM = Vector2.Distance(a.Centre, to) * metresPerMetre });
                case ArenaHandle.Ceiling: return Clamp(a with { CeilingM = Vector2.Distance(a.Centre, to) * metresPerMetre });
                default: return a;
            }
        }
    }

    /// <summary>The form's and the gizmo's ONE validation: the radius inside <see cref="MinRadius"/> … <see cref="MaxRadius"/>, heights inside 0 … <see cref="MaxHeight"/>, the ceiling at least <see cref="MinBand"/> above the floor (the floor yields to the ceiling when they meet).</summary>
    public static ArenaShape Clamp(in ArenaShape a)
    {
        float radius = Math.Clamp(float.IsFinite(a.Radius) ? a.Radius : MinRadius, MinRadius, MaxRadius);
        float floor = Math.Clamp(float.IsFinite(a.FloorM) ? a.FloorM : MinHeight, MinHeight, MaxHeight - MinBand);
        float ceiling = Math.Clamp(float.IsFinite(a.CeilingM) ? a.CeilingM : MaxHeight, MinHeight + MinBand, MaxHeight);
        if (ceiling < floor + MinBand) ceiling = MathF.Min(MaxHeight, floor + MinBand);
        if (ceiling < floor + MinBand) floor = ceiling - MinBand;
        return new ArenaShape(a.Centre, radius, floor, ceiling);
    }

    /// <summary>The arena's 2-D label ("AIR ARENA  r 6.0 km  floor 150 m  ceiling 4000 m"), culture-invariant (R-4221).</summary>
    public static string LabelOf(in ArenaShape a)
        => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"AIR ARENA  r {a.Radius / 1000f:0.0} km  floor {a.FloorM:0} m  ceiling {a.CeilingM:0} m");
}
