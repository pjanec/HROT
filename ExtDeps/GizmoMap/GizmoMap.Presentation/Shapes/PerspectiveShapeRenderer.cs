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
        }
    }
}
