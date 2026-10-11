using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Raylib_cs;

namespace Fdp.Toolkit.Vis3D
{
    /// <summary>⭐ CE-1033 S3 — draws <see cref="GizmoRenderer3D"/>'s output with Raylib, inside the 3-D camera pass
    /// (HROT → Raylib by <see cref="HrotToRaylib"/>).</summary>
    public sealed class RaylibGizmoSink3D : IGizmoSink3D
    {
        /// <summary>The one instance — the sink has no state.</summary>
        public static readonly RaylibGizmoSink3D Shared = new();

        public void Line(Vector3 a, Vector3 b, Rgba32 ca, Rgba32 cb)
        {
            var ra = HrotToRaylib.Position(a);
            var rb = HrotToRaylib.Position(b);
            Rlgl.Begin(DrawMode.Lines);
            Rlgl.Color4ub(ca.R, ca.G, ca.B, ca.A); Rlgl.Vertex3f(ra.X, ra.Y, ra.Z);
            Rlgl.Color4ub(cb.R, cb.G, cb.B, cb.A); Rlgl.Vertex3f(rb.X, rb.Y, rb.Z);
            Rlgl.End();
        }

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Rgba32 colour)
        {
            var col = new Color(colour.R, colour.G, colour.B, colour.A);
            var ra = HrotToRaylib.Position(a);
            var rb = HrotToRaylib.Position(b);
            var rc = HrotToRaylib.Position(c);
            // ⭐ Both windings: whether a world triangle faces the camera depends on the view; the culled one costs nothing.
            Raylib.DrawTriangle3D(ra, rb, rc, col);
            Raylib.DrawTriangle3D(ra, rc, rb, col);
        }

        public void Sphere(Vector3 centre, float radius, Rgba32 colour, Rgba32 fill)
        {
            var c = HrotToRaylib.Position(centre);
            if (fill.A > 0) Raylib.DrawSphereEx(c, radius, 10, 16, new Color(fill.R, fill.G, fill.B, fill.A));
            if (colour.A > 0) Raylib.DrawSphereWires(c, radius, 10, 16, new Color(colour.R, colour.G, colour.B, colour.A));
        }
    }

    /// <summary>
    /// ⭐⭐ CE-1033 S3 (M13) — the label overlay: gizmo text, badges and icons drawn AFTER the 3-D pass, at their projected screen
    /// point, at the same constant pixel size the 2-D map uses (<c>GizmoMap.Presentation.GizmoTextDraw</c> — one text renderer).
    /// An anchored label is lifted to the top of its entity's drawn body (<paramref name="liftOf"/>), so a name sits on the
    /// tank, not inside it. Drawn on top of everything (name-tag style, M13); a label behind the camera is not drawn.
    /// </summary>
    public static class LabelOverlay3D
    {
        /// <summary>The lift for an anchored label whose body is unknown (m).</summary>
        public const float DefaultLift = 2f;

        /// <summary>Where each label lands on screen — railable without a GL context. Off-screen ⇒ <c>null</c>.</summary>
        public static Vector2? ScreenOf(in GizmoLabel3D label, MapCamera3D camera, System.Func<long, float?>? liftOf)
        {
            var world = label.World;
            if (label.AnchorId != 0) world.Z += liftOf?.Invoke(label.AnchorId) ?? DefaultLift;
            var s = camera.WorldToScreen(world);
            return s.X < -1e5f ? null : s;
        }

        /// <summary>Draws <paramref name="labels"/> (outside the 3-D camera pass).</summary>
        public static int Draw(IReadOnlyList<GizmoLabel3D> labels, MapCamera3D camera, System.Func<long, float?>? liftOf)
        {
            int drawn = 0;
            for (int i = 0; i < labels.Count; i++)
            {
                var label = labels[i];
                if (ScreenOf(in label, camera, liftOf) is not { } s) continue;
                var p = label.Primitive;
                var colour = new Color(p.Color.R, p.Color.G, p.Color.B, p.Color.A);
                switch (p.Shape)
                {
                    case DebugPrimitiveShape.Text:
                        GizmoMap.Presentation.GizmoTextDraw.DrawScreen(in p, s, colour);
                        break;
                    case DebugPrimitiveShape.EntityBadge:
                    {
                        var rich = p.BadgeRichText;
                        GizmoMap.Presentation.RichTextRenderer.DrawRichTextBadge(ref rich, (int)s.X, (int)s.Y, 12);
                        break;
                    }
                    default:
                        Raylib.DrawCircleV(s, 4f, Color.Yellow);   // the 2-D renderer's Icon fallback
                        break;
                }
                drawn++;
            }
            return drawn;
        }
    }
}
