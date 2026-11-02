using System;
using System.Numerics;
using Raylib_cs;

namespace GizmoMap.Presentation.Shapes
{
    public static class PerspectiveShapeRenderer
    {
        public static void RenderShape(
            EntityShapeProfile shape,
            Vector2 worldPos,
            Quaternion rotation,
            float lengthMeters,
            float widthMeters,
            Color color,
            float exaggerationCoefficient = 0.05f,
            float visualScaleMultiplier = 1.0f,
            EntityShapeCondition currentCondition = EntityShapeCondition.None,
            float zoom = 1.0f)
        {
            float safeZoom = zoom > 0f ? zoom : 1f;

            // ⭐ SR-191 — a TRANSIENT entity: the footprint is filled at ~55 % of the side colour UNDER the outline, which stays solid. (Every
            //   profile is outline-only, so the fill is the footprint, not the element: a soldier's open polyline has none of its own.)
            if ((currentCondition & EntityShapeCondition.Transient) != 0)
                DrawTransientFill(worldPos, rotation, lengthMeters * visualScaleMultiplier, widthMeters * visualScaleMultiplier, color);

            foreach (var element in shape.Elements)
            {
                if (element.LocalVertices == null || element.LocalVertices.Length == 0)
                    continue;
                if (element.ShowWhen != EntityShapeCondition.None && (element.ShowWhen & currentCondition) == 0)
                    continue;
                if (element.HideWhen != EntityShapeCondition.None && (element.HideWhen & currentCondition) != 0)
                    continue;

                int n = element.LocalVertices.Length;
#pragma warning disable CA2014 // stackalloc size depends on element.LocalVertices.Length which varies per element
                Span<Vector2> pts = n <= 64 ? stackalloc Vector2[n] : new Vector2[n];
#pragma warning restore CA2014
                for (int i = 0; i < n; i++)
                {
                    var p = element.LocalVertices[i];
                    var local = new Vector3(
                        p.X * lengthMeters * visualScaleMultiplier,
                        p.Y * widthMeters * visualScaleMultiplier,
                        p.Z * lengthMeters * visualScaleMultiplier);
                    var r = Vector3.Transform(local, rotation);
                    float s = MathF.Max(0.1f, 1f + r.Z * exaggerationCoefficient);
                    pts[i] = worldPos + new Vector2(r.X, r.Y) * s;
                }

                float thickness = (element.LineThickness > 0f ? element.LineThickness : 2f) / safeZoom;
                for (int i = 0; i < n - 1; i++)
                    Raylib.DrawLineEx(pts[i], pts[i + 1], thickness, color);
                if (element.IsClosed && n > 2)
                    Raylib.DrawLineEx(pts[n - 1], pts[0], thickness, color);
            }

            if ((currentCondition & EntityShapeCondition.Destroyed) != 0)
                DrawDestroyedCross(worldPos, rotation, lengthMeters * visualScaleMultiplier, widthMeters * visualScaleMultiplier, color, safeZoom);
        }

        /// <summary>⭐ SR-191 — the share of the side colour's alpha a transient entity's FILL keeps (the outline keeps all of it).</summary>
        public const float TransientFillAlpha = 0.55f;

        /// <summary>⭐ SR-191 — the fill colour of a transient entity: the side colour at <see cref="TransientFillAlpha"/> of its alpha.</summary>
        public static Color TransientFillColor(Color sideColor)
            => new(sideColor.R, sideColor.G, sideColor.B, (byte)MathF.Round(sideColor.A * TransientFillAlpha));

        /// <summary>The footprint's four corners in world space (counter-clockwise) — railable without a GL context.</summary>
        public static (Vector2 A, Vector2 B, Vector2 C, Vector2 D) FootprintCorners(Vector2 worldPos, Quaternion rotation, float lengthMeters, float widthMeters)
        {
            float hl = lengthMeters * 0.5f, hw = widthMeters * 0.5f;
            Vector2 At(float x, float y)
            {
                var r = Vector3.Transform(new Vector3(x, y, 0f), rotation);
                return worldPos + new Vector2(r.X, r.Y);
            }
            return (At(-hl, -hw), At(hl, -hw), At(hl, hw), At(-hl, hw));
        }

        /// <summary>⭐ SR-191 — fills the footprint at <see cref="TransientFillColor"/>; drawn in both windings (Raylib culls a clockwise triangle).</summary>
        public static void DrawTransientFill(Vector2 worldPos, Quaternion rotation, float lengthMeters, float widthMeters, Color sideColor)
        {
            var (a, b, c, d) = FootprintCorners(worldPos, rotation, lengthMeters, widthMeters);
            var fill = TransientFillColor(sideColor);
            Raylib.DrawTriangle(a, b, c, fill); Raylib.DrawTriangle(a, c, b, fill);
            Raylib.DrawTriangle(a, c, d, fill); Raylib.DrawTriangle(a, d, c, fill);
        }

        /// <summary>⭐ W2 (VE-O) — the MIL-STD-2525 destroyed marking: an X corner to corner across the shape's footprint, at the
        /// outline's own weight (tuned on the Xvfb frame rail: a heavier, overshooting X buried the symbol).</summary>
        public static void DrawDestroyedCross(Vector2 worldPos, Quaternion rotation, float lengthMeters, float widthMeters, Color color, float zoom)
        {
            var (a, b, c, d) = DestroyedCross(worldPos, rotation, lengthMeters, widthMeters);
            float thickness = 2f / (zoom > 0f ? zoom : 1f);
            Raylib.DrawLineEx(a, b, thickness, color);
            Raylib.DrawLineEx(c, d, thickness, color);
        }

        /// <summary>The X's two strokes (a→b, c→d) in world space — railable without a GL context.</summary>
        public static (Vector2 A, Vector2 B, Vector2 C, Vector2 D) DestroyedCross(Vector2 worldPos, Quaternion rotation, float lengthMeters, float widthMeters)
        {
            const float Reach = 0.5f;   // half-extent: corner to corner
            float hl = lengthMeters * Reach, hw = widthMeters * Reach;
            Vector2 At(float x, float y)
            {
                var r = Vector3.Transform(new Vector3(x, y, 0f), rotation);
                return worldPos + new Vector2(r.X, r.Y);
            }
            return (At(-hl, -hw), At(hl, hw), At(-hl, hw), At(hl, -hw));
        }
    }
}
