using System.Numerics;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Fdp.Toolkit.Vis2D.Gizmos;

/// <summary>
/// ⭐ B-17 P2 (📄 docs/UX/DESIGN_Map_Interaction.md §2.5) — the on-map ✓ ("Done"): a green square with a tick, drawn beside the shape. ONE drawing of it for every
/// drawing gizmo (<see cref="PointSequenceGizmo"/>, <see cref="AreaShapeGizmo"/>, and the editors of a drawn shape and a route); a click on it is the
/// session's <see cref="ShapeEditSession.HitDone"/> (or, for <see cref="AreaShapeGizmo"/>, <see cref="AreaShapeHandles.HitDone"/>).
/// </summary>
public static class ShapeDoneMark
{
    private static readonly Rgba32 Green = new(90, 220, 110, 255);

    /// <summary>Draws the ✓ of <paramref name="session"/> (nothing with no points).</summary>
    public static void Draw(IDebugDrawBuilder draw, ShapeEditSession session)
    {
        if (session.Points.Count == 0) return;
        Draw(draw, session.DonePosition, session.DoneHalfSize);
    }

    /// <summary>Draws the ✓ centred at <paramref name="done"/>, half a side <paramref name="d"/> (world metres).</summary>
    public static void Draw(IDebugDrawBuilder draw, Vector2 done, float d)
    {
        draw.DrawLine(new Vector3(done.X - d, done.Y - d, 0f), new Vector3(done.X + d, done.Y - d, 0f), Green, 2f);
        draw.DrawLine(new Vector3(done.X + d, done.Y - d, 0f), new Vector3(done.X + d, done.Y + d, 0f), Green, 2f);
        draw.DrawLine(new Vector3(done.X + d, done.Y + d, 0f), new Vector3(done.X - d, done.Y + d, 0f), Green, 2f);
        draw.DrawLine(new Vector3(done.X - d, done.Y + d, 0f), new Vector3(done.X - d, done.Y - d, 0f), Green, 2f);
        draw.DrawLine(new Vector3(done.X - d * 0.6f, done.Y, 0f), new Vector3(done.X - d * 0.1f, done.Y - d * 0.6f, 0f), Green, 2f);
        draw.DrawLine(new Vector3(done.X - d * 0.1f, done.Y - d * 0.6f, 0f), new Vector3(done.X + d * 0.6f, done.Y + d * 0.6f, 0f), Green, 2f);
    }
}
