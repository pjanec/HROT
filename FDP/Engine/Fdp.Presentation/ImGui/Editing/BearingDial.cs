using System;
using System.Numerics;

namespace Fdp.Presentation.Editing;

/// <summary>
/// ⭐ CE-4206 R3 (DESIGN_Map_Picking_Unification §6.3 F7) — the PURE maths of the <c>Bearing</c> leaf's compass dial: a small circle, north up, a needle you drag.
/// Screen frame (x right, y DOWN); a bearing is a COMPASS angle (0 = north, 90 = east, clockwise), as <c>Bearing</c> / <c>AreaLink.Contains</c> read it.
/// </summary>
public static class BearingDial
{
    /// <summary>Degrees wrapped into [0, 360).</summary>
    public static float Wrap(float degrees)
        => float.IsFinite(degrees) ? ((degrees % 360f) + 360f) % 360f : 0f;

    /// <summary>The bearing the mouse points at from the dial's <paramref name="center"/>: straight up = 0, right = 90, down = 180, left = 270. The centre itself = 0.</summary>
    public static float FromDrag(Vector2 center, Vector2 mouse)
    {
        var d = mouse - center;
        if (d.LengthSquared() < 1e-6f) return 0f;
        return Wrap(MathF.Atan2(d.X, -d.Y) * (180f / MathF.PI));
    }

    /// <summary>Where the needle ends: <paramref name="radius"/> from the centre toward <paramref name="degrees"/>.</summary>
    public static Vector2 NeedleEnd(Vector2 center, float radius, float degrees)
    {
        float rad = degrees * (MathF.PI / 180f);
        return center + new Vector2(MathF.Sin(rad), -MathF.Cos(rad)) * radius;
    }
}
