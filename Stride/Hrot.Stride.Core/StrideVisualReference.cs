using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Stride.Core;

/// <summary>
/// Shadow component that records the binding between an FDP entity and its
/// Stride-side visual handle.
///
/// <para>
/// Stored in a parallel <c>Dictionary&lt;Entity, StrideVisualReference&gt;</c>
/// managed by <see cref="StrideVisualBindingSystem"/> (not in the ECS repository
/// itself, because Stride-layer objects cannot be blitted into a fixed-size ECS
/// component slot). The dictionary key is the FDP <see cref="Fdp.Core.Entity"/>;
/// the value is this reference record.
/// </para>
///
/// <para>
/// The <c>ShapeKind</c> and resolved <c>Dims</c> are stored here so that
/// <c>PhysicsBodyLifecycleSystem</c> (P1) can read the shape without re-resolving
/// the TKB descriptor.
/// </para>
/// </summary>
public sealed class StrideVisualReference
{
    /// <summary>
    /// Opaque handle returned by <see cref="IStrideVisualFactory.CreateModelVisual"/>
    /// or <see cref="IStrideVisualFactory.CreateProceduralVisual"/>.
    /// </summary>
    public object VisualHandle { get; }

    /// <summary>
    /// Collision shape kind stored for P1 <c>PhysicsBodyLifecycleSystem</c> reuse.
    /// </summary>
    public CollisionShapeKind ShapeKind { get; }

    /// <summary>
    /// Resolved shape dimensions (all "0 =&gt; default" rules applied).
    /// Stored for P1 physics-body creation without re-resolving.
    /// </summary>
    public ShapeDims Dims { get; }

    /// <summary>
    /// Whether this is a model-based visual (<c>true</c>) or a procedural primitive (<c>false</c>).
    /// </summary>
    public bool IsModelVisual { get; }

    /// <summary>
    /// Initialises the reference from the factory output.
    /// </summary>
    public StrideVisualReference(
        object visualHandle,
        CollisionShapeKind shapeKind,
        ShapeDims dims,
        bool isModelVisual)
    {
        VisualHandle   = visualHandle;
        ShapeKind      = shapeKind;
        Dims           = dims;
        IsModelVisual  = isModelVisual;
    }
}
