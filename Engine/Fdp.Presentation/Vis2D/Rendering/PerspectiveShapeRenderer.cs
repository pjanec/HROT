using System;
using System.Numerics;
using Raylib_cs;

namespace Fdp.Toolkit.Vis2D.Rendering;

/// <summary>
/// Stateless, zero-allocation routine that projects a <see cref="Shapes.EntityShapeProfile"/>
/// from normalized local space into 2-D screen space, optionally deforming vertices
/// based on the entity's roll/pitch via an exaggerated perspective model.
///
/// <para>
/// <b>Projection model:</b>
/// <list type="number">
///   <item>
///     Each vertex is first scaled from normalized space to physical meters using
///     <paramref name="lengthMeters"/> (X, Z axes) and <paramref name="widthMeters"/>
///     (Y axis), optionally multiplied by <paramref name="visualScaleMultiplier"/>.
///   </item>
///   <item>
///     The vertex is then rotated into 3-D world space using the entity's quaternion
///     rotation (from <c>SimTransform.Rotation</c>).
///   </item>
///   <item>
///     A perspective scale factor <c>1 + rotated.Z * exaggerationCoefficient</c>
///     is applied to the 2-D (X, Y) components of the result.  When an aircraft
///     rolls right, the left wing acquires positive local Z after rotation, so
///     its projected footprint grows; the right wing shrinks.  Setting
///     <paramref name="exaggerationCoefficient"/> to 0 yields a flat top-down view.
///   </item>
/// </list>
/// </para>
///
/// <para>
/// The hot path uses <c>stackalloc</c> to project vertices without heap allocation.
/// The maximum supported vertex count per polyline is 64.
/// </para>
/// </summary>
public static class PerspectiveShapeRenderer
{
    // Maximum vertices supported per polyline before falling back to heap.
    private const int StackVertexLimit = 64;

    // Default line width used when PolylineDefinition.LineThickness == 0.
    private const float DefaultLineThickness = 2f;

    /// <summary>
    /// Renders all visible elements of <paramref name="shape"/> at
    /// <paramref name="worldPos"/> using the supplied rotation and sizing.
    /// </summary>
    /// <param name="shape">Shape profile to render.</param>
    /// <param name="worldPos">Entity centre in 2-D map/world space.</param>
    /// <param name="rotation">Entity orientation (from <c>SimTransform.Rotation</c>).</param>
    /// <param name="lengthMeters">Physical length of the entity in metres.</param>
    /// <param name="widthMeters">Physical width of the entity in metres.</param>
    /// <param name="color">Fill / stroke colour resolved by the hosting visualizer.</param>
    /// <param name="exaggerationCoefficient">
    /// Controls perspective distortion.  0 = flat top-down; ~0.05 is a subtle effect.
    /// </param>
    /// <param name="visualScaleMultiplier">
    /// Uniform scale applied after physical sizing.  1.0 = true real-world dimensions.
    /// Raise above 1.0 when entities are too small to click at tactical zoom.
    /// </param>
    /// <param name="currentCondition">
    /// The entity's current runtime condition flags, used to evaluate
    /// <c>ShowWhen</c> / <c>HideWhen</c> masks on each element.
    /// </param>
    public static void RenderShape(
        Shapes.EntityShapeProfile     shape,
        Vector2                       worldPos,
        Quaternion                    rotation,
        float                         lengthMeters,
        float                         widthMeters,
        Color                         color,
        float                         exaggerationCoefficient = 0.05f,
        float                         visualScaleMultiplier   = 1.0f,
        Shapes.EntityShapeCondition   currentCondition        = Shapes.EntityShapeCondition.None)
    {
        foreach (var element in shape.Elements)
        {
            if (!IsVisible(element, currentCondition))
                continue;

            int vCount = element.LocalVertices?.Length ?? 0;
            if (vCount == 0)
                continue;

            float thickness = element.LineThickness > 0f
                ? element.LineThickness
                : DefaultLineThickness;

            if (vCount <= StackVertexLimit)
            {
                Span<Vector2> projected = stackalloc Vector2[vCount];
                ProjectAll(element.LocalVertices!, projected,
                           worldPos, rotation,
                           lengthMeters, widthMeters,
                           exaggerationCoefficient, visualScaleMultiplier);
                DrawElement(projected, element.IsFilled, element.IsClosed, color, thickness);
            }
            else
            {
                // Rare fallback for unusually detailed profiles.
                Vector2[] projected = new Vector2[vCount];
                ProjectAll(element.LocalVertices!, projected,
                           worldPos, rotation,
                           lengthMeters, widthMeters,
                           exaggerationCoefficient, visualScaleMultiplier);
                DrawElement(projected, element.IsFilled, element.IsClosed, color, thickness);
            }
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool IsVisible(
        in Shapes.PolylineDefinition element,
        Shapes.EntityShapeCondition  condition)
    {
        // ShowWhen check: if the mask is non-zero, at least one flag must match.
        if (element.ShowWhen != Shapes.EntityShapeCondition.None &&
            (element.ShowWhen & condition) == 0)
            return false;

        // HideWhen check: if any flag matches, suppress.
        if (element.HideWhen != Shapes.EntityShapeCondition.None &&
            (element.HideWhen & condition) != 0)
            return false;

        return true;
    }

    private static void ProjectAll(
        Vector3[]  localVerts,
        Span<Vector2> projected,
        Vector2    worldPos,
        Quaternion rotation,
        float      L,
        float      W,
        float      exaggeration,
        float      scaleMultiplier)
    {
        for (int i = 0; i < localVerts.Length; i++)
        {
            projected[i] = ProjectVertex(
                localVerts[i], worldPos, rotation,
                L, W, exaggeration, scaleMultiplier);
        }
    }

    /// <summary>
    /// Projects a single normalized local vertex into 2-D world coordinates.
    /// </summary>
    internal static Vector2 ProjectVertex(
        Vector3    normalizedPos,
        Vector2    worldPos,
        Quaternion rotation,
        float      L,
        float      W,
        float      exaggeration,
        float      scaleMultiplier = 1.0f)
    {
        // 1. Scale normalized [-0.5, 0.5] coordinates to physical metres.
        float effL = L * scaleMultiplier;
        float effW = W * scaleMultiplier;
        var localMeters = new Vector3(
            normalizedPos.X * effL,
            normalizedPos.Y * effW,
            normalizedPos.Z * effL);   // Z uses length scale (rotor elevation, etc.)

        // 2. Rotate into world 3-D space using the entity's physics orientation.
        Vector3 rotated = Vector3.Transform(localMeters, rotation);

        // 3. Perspective-exaggeration scale based on rotated Z.
        //    Positive Z (vertex lifted above ground) expands the 2-D footprint,
        //    negative Z (vertex below ground) contracts it.
        float scale = 1.0f + (rotated.Z * exaggeration);
        scale = MathF.Max(0.1f, scale); // prevent geometry inversion at extreme values

        // 4. Output 2-D canvas coordinates (the MapCamera zoom converts to pixels).
        return worldPos + new Vector2(rotated.X, rotated.Y) * scale;
    }

    private static void DrawElement(
        Span<Vector2> pts,
        bool          filled,
        bool          closed,
        Color         color,
        float         thickness)
    {
        int n = pts.Length;

        // Triangle-fan fill (handles non-planar / non-convex deformation).
        if (filled && n >= 3)
        {
            for (int i = 1; i < n - 1; i++)
                Raylib.DrawTriangle(pts[0], pts[i], pts[i + 1], color);
        }

        // Outline edges.
        for (int i = 0; i < n - 1; i++)
            Raylib.DrawLineEx(pts[i], pts[i + 1], thickness, color);

        // Close the polygon.
        if (closed && n > 2)
            Raylib.DrawLineEx(pts[n - 1], pts[0], thickness, color);
    }
}
