using System.Numerics;

namespace Fdp.Toolkit.Vis2D.Shapes;

/// <summary>
/// A single polyline element that makes up part of an <see cref="EntityShapeProfile"/>.
///
/// <para>
/// Vertices are defined in <b>normalized local space</b> where the range
/// <c>[-0.5, 0.5]</c> in X and Y maps to the entity's physical length and width
/// respectively, and Z represents the local up/down offset (used for perspective
/// parallax of elevated parts such as rotor blades).
/// </para>
///
/// <para>
/// The renderer scales X and Z by <c>lengthMeters</c> and Y by <c>widthMeters</c>
/// before applying the entity's rotation quaternion.
/// </para>
/// </summary>
public readonly struct PolylineDefinition
{
    /// <summary>
    /// Vertices in normalized local space.
    /// X = forward/back  (scaled by entity length),
    /// Y = left/right    (scaled by entity width),
    /// Z = up/down       (scaled by entity length; used for perspective parallax).
    /// </summary>
    public Vector3[] LocalVertices { get; init; }

    /// <summary>
    /// When true, an extra edge is drawn from the last vertex back to the first.
    /// </summary>
    public bool IsClosed { get; init; }

    /// <summary>
    /// When true, the interior of the polygon is filled using a triangle fan
    /// before the outline is drawn.
    /// </summary>
    public bool IsFilled { get; init; }

    /// <summary>
    /// Line thickness in screen pixels.
    /// Zero means use the renderer's default (2 px).
    /// </summary>
    public float LineThickness { get; init; }

    /// <summary>
    /// Draw this polyline only when <b>at least one</b> of these condition flags
    /// is set.  <see cref="EntityShapeCondition.None"/> means "always draw".
    /// </summary>
    public EntityShapeCondition ShowWhen { get; init; }

    /// <summary>
    /// Suppress this polyline when <b>any</b> of these condition flags is set.
    /// <see cref="EntityShapeCondition.None"/> means "never suppress".
    /// </summary>
    public EntityShapeCondition HideWhen { get; init; }
}
