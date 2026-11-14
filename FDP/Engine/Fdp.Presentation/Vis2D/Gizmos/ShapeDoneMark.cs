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

    /// <summary>The 2-D map only: <c>Map2D | FlatOnly</c> — a 3-D pass never draws a FlatOnly primitive, and the 2-D renderer skips one without Map2D (<c>DebugPrimitiveRenderer2D.cs:119</c>).</summary>
    private const PipelineTarget Flat = PipelineTarget.Map2D | PipelineTarget.FlatOnly;

    /// <summary>
    /// Draws the button centred at <paramref name="done"/>, unit <paramref name="d"/> (world metres). ⭐ B-17 P7-C — TWO looks, one per view, so neither is wrong in the other:
    /// <b>2-D</b> (<see cref="Flat"/>): the world-metre box (the click rectangle, <see cref="Hit"/>), a tick at its left and the word. <b>3-D</b> (<see cref="PipelineTarget.Viewport3D"/>): the same word as a constant-pixel
    /// <see cref="GizmoTextStyle.Button"/> at the anchor — a world-metre box draped over relief in perspective is a slab. ⚠ The click is still tested in world metres around <paramref name="done"/>
    /// (no zoom reaches a raw-input gizmo: its events carry only the world point and cross the wire), so in 3-D the drawn pixel button and the click rectangle are the same place but not the same size.
    /// </summary>
    public static void Draw(IDebugDrawBuilder draw, Vector2 done, float d)
    {
        draw.DrawBox2D(done, new Vector2(HalfWidth(d), HalfHeight(d)), ButtonBorder, 0f, 1f, SizeMode.WorldMeters, target: Flat, fillColor: ButtonBlue);   // the 1 px border is an outline (pixels), the fill is world metres
        float tx = done.X - HalfWidth(d) * 0.78f;   // the tick: two strokes inside the left end
        draw.DrawLine(new Vector3(tx - d * 0.45f, done.Y, 0f), new Vector3(tx - d * 0.1f, done.Y - d * 0.45f, 0f), Green, 2f, SizeMode.ScreenPixels, Flat);
        draw.DrawLine(new Vector3(tx - d * 0.1f, done.Y - d * 0.45f, 0f), new Vector3(tx + d * 0.5f, done.Y + d * 0.45f, 0f), Green, 2f, SizeMode.ScreenPixels, Flat);
        // the word, to the right of the tick; the text is screen-sized (13 px), so lift it by half its height to centre it on the box
        Word(draw, done.X - HalfWidth(d) * 0.4f, done.Y, GizmoTextStyle.Plain, Flat);
        // 3-D: the button as one constant-pixel Button-style text at the anchor
        Word(draw, done.X - d, done.Y, GizmoTextStyle.Button, PipelineTarget.Viewport3D);
    }

    private static void Word(IDebugDrawBuilder draw, float x, float y, GizmoTextStyle style, PipelineTarget target)
    {
        var p = DebugPrimitive.MakeText(x, y, new Fdp.Toolkit.Diagnostics.Gizmos.FixedString32("Done"), Label, lineOffsetPx: -7f, style: style);
        p.TargetView = target;
        draw.EmitRaw(in p);
    }
}
