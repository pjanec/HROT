using System.Numerics;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Fdp.Toolkit.Vis2D.Gizmos;

/// <summary>
/// ⭐ B-17 P2 / P7 ⑥c (📄 docs/UX/DESIGN_Map_Interaction.md §2.5, "P7 — the build"; sheet <c>map-gizmo-look-1.svg</c> row ②) — the on-map <b>Done</b> button: a blue
/// box with a green tick and the word "Done", drawn beside the shape. ONE drawing of it for every drawing gizmo (<see cref="PointSequenceGizmo"/>,
/// <see cref="AreaShapeGizmo"/>, and the editors of a drawn shape and a route), and ⭐ <b>ONE rectangle</b>: <see cref="Hit"/> is what a click tests
/// (<see cref="ShapeEditSession.HitDone"/>, <see cref="AreaShapeHandles.HitDone"/>) and <see cref="Draw(IDebugDrawBuilder, Vector2, float)"/> draws the same
/// <see cref="HalfWidth"/> × <see cref="HalfHeight"/>. It is the Roboto-has-no-✓ answer too: the hint says "Done", the button says "Done".
/// </summary>
public static class ShapeDoneMark
{
    private static readonly Rgba32 Green = new(90, 220, 110, 255);
    private static readonly Rgba32 ButtonBlue = new(0x23, 0x41, 0x5F, 255);
    private static readonly Rgba32 ButtonBorder = new(0x33, 0x69, 0xAD, 255);
    private static readonly Rgba32 Label = new(255, 255, 255, 255);

    /// <summary>The button's half width for unit <paramref name="d"/> (m): wider than tall, so the word fits.</summary>
    public static float HalfWidth(float d) => 3.2f * d;

    /// <summary>The button's half height for unit <paramref name="d"/> (m).</summary>
    public static float HalfHeight(float d) => 1.1f * d;

    /// <summary>Where the button's centre sits relative to the shape's top-right corner: clear of every point, the button's left edge a unit off the corner.</summary>
    public static Vector2 Offset(float d) => new(4.4f * d, 3f * d);

    /// <summary>Is <paramref name="at"/> on the button centred at <paramref name="centre"/>?</summary>
    public static bool Hit(Vector2 centre, float d, Vector2 at)
        => MathF.Abs(at.X - centre.X) <= HalfWidth(d) && MathF.Abs(at.Y - centre.Y) <= HalfHeight(d);

    /// <summary>Draws the Done button of <paramref name="session"/> (nothing with no points).</summary>
    public static void Draw(IDebugDrawBuilder draw, ShapeEditSession session)
    {
        if (session.Points.Count == 0) return;
        Draw(draw, session.DonePosition, session.DoneHalfSize);
    }

    /// <summary>Draws the button centred at <paramref name="done"/>, unit <paramref name="d"/> (world metres): the box, a tick at its left, the word.</summary>
    public static void Draw(IDebugDrawBuilder draw, Vector2 done, float d)
    {
        draw.DrawBox2D(done, new Vector2(HalfWidth(d), HalfHeight(d)), ButtonBorder, 0f, 1f, SizeMode.WorldMeters, fillColor: ButtonBlue);   // the 1 px border is an outline (pixels), the fill is world metres
        float tx = done.X - HalfWidth(d) * 0.78f;   // the tick: two strokes inside the left end
        draw.DrawLine(new Vector3(tx - d * 0.45f, done.Y, 0f), new Vector3(tx - d * 0.1f, done.Y - d * 0.45f, 0f), Green, 2f);
        draw.DrawLine(new Vector3(tx - d * 0.1f, done.Y - d * 0.45f, 0f), new Vector3(tx + d * 0.5f, done.Y + d * 0.45f, 0f), Green, 2f);
        // the word, to the right of the tick; the text is screen-sized (13 px), so lift it by half its height to centre it on the box
        draw.DrawText(done.X - HalfWidth(d) * 0.4f, done.Y, new Fdp.Core.FixedString32("Done"), Label, lineOffsetPx: -7f);
    }
}
