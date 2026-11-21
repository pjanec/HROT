using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.UI.Common.AddEntity;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐⭐ Terrain authoring U-2 (mockup 2, the ghost at the track) — how a terrain GHOST is drawn: the block's dashed outline, each house as a tan filled footprint, each wood green, and the two hint lines under the cursor with their
/// verdict colour. ⭐ ONE routine for the placing tool; it only emits primitives, so the 2-D map and the 3-D view both show it. ⛔ No allocation: index loops over the shape's arrays (R-2084); the strings are the shape's own, built once.
/// </summary>
public static class TerrainGhostDraw
{
    public static readonly Rgba32 OutlineColour = new(120, 190, 255, 230), HouseFill = new(232, 200, 150, 150), HouseLine = new(240, 220, 170, 230), WoodFill = new(40, 110, 60, 120), WoodLine = new(90, 170, 100, 220),
                                  OkColour = new(110, 210, 130, 255), WarnColour = new(240, 190, 70, 255), TextColour = new(255, 255, 255, 255),
                                  HighlightColour = new(245, 180, 60, 255), FactsColour = new(200, 205, 215, 255);   // ⭐ CE-5030 — the mockup's amber hover outline; the facts line grey

    public static void Draw(IDebugDrawBuilder draw, TerrainGhostShape shape, Vector3 cursor, float lineStep)
    {
        var outline = shape.Outline;
        for (int i = 0; i < outline.Length; i++)
        {
            var a = outline[i]; var b = outline[(i + 1) % outline.Length];
            if (shape.Highlight) draw.DrawLine(new Vector3(a.X, a.Y, 0f), new Vector3(b.X, b.Y, 0f), HighlightColour, 2.4f);   // ⭐ CE-5030 — the building a building edit would act on
            else draw.DrawLine(new Vector3(a.X, a.Y, 0f), new Vector3(b.X, b.Y, 0f), OutlineColour, 1.6f, style: LineStyle.Dashed);
        }
        var woods = shape.Woods;
        for (int w = 0; w < woods.Length; w++) Poly(draw, woods[w], WoodFill, WoodLine);
        var houses = shape.Houses;
        for (int h = 0; h < houses.Length; h++) Poly(draw, houses[h], HouseFill, HouseLine);
        // two lines under the cursor, as the mockup's hint: the keys, then the verdict (green tick / amber warning)
        draw.DrawTextLong(cursor.X, cursor.Y - 2f * lineStep, shape.Line1, TextColour, style: GizmoTextStyle.Backdrop);
        if (shape.Line3.Length == 0) { draw.DrawTextLong(cursor.X, cursor.Y - 3f * lineStep, shape.Line2, shape.Fits ? OkColour : WarnColour, style: GizmoTextStyle.Backdrop); return; }
        draw.DrawTextLong(cursor.X, cursor.Y - 3f * lineStep, shape.Line2, FactsColour, style: GizmoTextStyle.Backdrop);
        draw.DrawTextLong(cursor.X, cursor.Y - 4f * lineStep, shape.Line3, shape.Fits ? OkColour : WarnColour, style: GizmoTextStyle.Backdrop);
    }

    private static void Poly(IDebugDrawBuilder draw, Vector2[] p, Rgba32 fill, Rgba32 line)
    {
        for (int i = 1; i + 1 < p.Length; i++) draw.DrawFilledTriangle(p[0], p[i], p[i + 1], fill);   // a fan: a convex footprint (a house, a wood's 8-gon) fills exactly
        for (int i = 0; i < p.Length; i++)
        {
            var a = p[i]; var b = p[(i + 1) % p.Length];
            draw.DrawLine(new Vector3(a.X, a.Y, 0f), new Vector3(b.X, b.Y, 0f), line, 1.2f);
        }
    }
}
