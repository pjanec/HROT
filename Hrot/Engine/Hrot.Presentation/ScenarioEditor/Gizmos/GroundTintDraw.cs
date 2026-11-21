using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.UI.Common.AddEntity;

namespace Hrot.ScenarioEditor.Gizmos;

/// <summary>
/// ⭐⭐ <c>CE-4592</c> (P-5 part 2b; mockup <c>docs/UX/ground-shaping-and-ditches.svg</c> B — <i>"+2.5 m (the tint: the change, strongest inside — NEW)"</i>) — the tint of the
/// height CHANGE a ground shape / ditch makes, drawn under the shape while it is drawn: each <see cref="GroundEffect"/> cell a filled square, amber where the ground rises, blue where it
/// falls, more opaque where the change is larger. Islands (a house + its margin, a road corridor) change nothing, so they stay untinted by construction. ⭐ Emits primitives only — the
/// 2-D map and the 3-D view both show it. ⛔ No allocation: an index loop over the effect's arrays (R-2084); the effect itself is rebuilt only when the shape changes.
/// </summary>
public static class GroundTintDraw
{
    public static readonly Rgba32 Raise = new(232, 160, 64, 255), Lower = new(64, 144, 224, 255);
    /// <summary>The alpha of the faintest and the strongest change.</summary>
    public const byte MinAlpha = 40, MaxAlpha = 150;

    public static void Draw(IDebugDrawBuilder draw, GroundEffect fx)
    {
        var mins = fx.CellMin; var deltas = fx.CellDelta;
        if (mins.Length == 0) return;
        float most = MathF.Max(MathF.Abs(fx.Change), 1e-3f), s = fx.CellSize;
        for (int i = 0; i < mins.Length; i++)
        {
            float d = deltas[i];
            var c = d >= 0f ? Raise : Lower;
            c.A = (byte)(MinAlpha + (MaxAlpha - MinAlpha) * MathF.Min(1f, MathF.Abs(d) / most));
            var a = mins[i]; var b = a + new Vector2(s, 0f); var cc = a + new Vector2(s, s); var e = a + new Vector2(0f, s);
            draw.DrawFilledTriangle(a, b, cc, c);
            draw.DrawFilledTriangle(a, cc, e, c);
        }
    }
}
