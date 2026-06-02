using Fdp.Toolkit.Tkb.Attributes;
using StructEdit.Core.Attributes;

namespace Fdp.Toolkit.Tkb.Domain
{
    /// <summary>
    /// Collision shape used to build the physics body that backs an entity on the
    /// Stride node. The render model is decorative; this shape is what the physics
    /// engine actually simulates and what the reverse-sync reads back into
    /// <c>SimTransform</c> / <c>SimVelocity</c>.
    /// </summary>
    public enum CollisionShapeKind : byte
    {
        /// <summary>No physics body (visual-only / static decoration).</summary>
        None = 0,

        /// <summary>Upright capsule — humanoid characters.</summary>
        Capsule = 1,

        /// <summary>Oriented box — vehicles and crates.</summary>
        OrientedBox = 2,

        /// <summary>Sphere.</summary>
        Sphere = 3,

        /// <summary>Cylinder.</summary>
        Cylinder = 4,

        /// <summary>Convex/triangle mesh derived from the render model.</summary>
        MeshFromModel = 5,
    }

    /// <summary>
    /// Stride-specific visual + collision binding for an entity class
    /// (TKB descriptor <c>"Stride.RenderModelDef"</c>). Maps an FDP entity type to a
    /// concrete Stride model/skeleton asset and the collision shape used to build its
    /// physics body on the Stride node.
    ///
    /// <para>This descriptor is intentionally <b>engine-specific</b>: 3D engines are not
    /// interchangeable, and asset references / shape settings are always concrete to one
    /// engine. A future 3D engine gets its own descriptor (e.g. <c>"&lt;Engine&gt;.RenderModelDef"</c>)
    /// with its own settings; nothing here is meant to be shared across engines. The asset
    /// reference strings are Stride asset URLs.</para>
    /// </summary>
    [TkbDescriptor("Stride.RenderModelDef")]
    public record StrideRenderModelDefDto
    {
        /// <summary>
        /// Stride asset URL of the Model to instantiate (e.g. <c>"Models/mannequinModel"</c>).
        /// Empty =&gt; the binder falls back to a procedural primitive matching
        /// <see cref="ShapeKind"/>.
        /// </summary>
        public string ModelAssetRef { get; init; } = "";

        /// <summary>
        /// Stride asset URL of the Skeleton for skinned/animated models
        /// (e.g. <c>"Models/mannequinModel Skeleton"</c>). Empty for rigid models.
        /// </summary>
        public string SkeletonAssetRef { get; init; } = "";

        /// <summary>Uniform scale applied to the visual model (1 = as authored).</summary>
        public float Scale { get; init; } = 1f;

        /// <summary>Render-model local offset from the physics-body origin, X (East).</summary>
        [EditUnit("m")]
        public float OffsetX { get; init; }

        /// <summary>Render-model local offset from the physics-body origin, Y (North).</summary>
        [EditUnit("m")]
        public float OffsetY { get; init; }

        /// <summary>Render-model local offset from the physics-body origin, Z (Up).</summary>
        [EditUnit("m")]
        public float OffsetZ { get; init; }

        /// <summary>Collision shape kind for the physics body backing this entity.</summary>
        public CollisionShapeKind ShapeKind { get; init; } = CollisionShapeKind.Capsule;

        /// <summary>
        /// Capsule / Cylinder / Sphere radius (m). 0 =&gt; default from the entity's
        /// <c>PhysicsCollider.Radius</c> at bind time.
        /// </summary>
        [EditUnit("m")]
        public float ShapeRadius { get; init; }

        /// <summary>Capsule / Cylinder / OrientedBox height along the up-axis (m).</summary>
        [EditUnit("m")]
        public float ShapeHeight { get; init; }

        /// <summary>
        /// Oriented-box half-extent, X (East), used when <see cref="ShapeKind"/> is
        /// <see cref="CollisionShapeKind.OrientedBox"/>. 0 =&gt; default from the entity's
        /// <c>VehicleParametersDto.Length</c>.
        /// </summary>
        [EditUnit("m")]
        public float BoxHalfX { get; init; }

        /// <summary>
        /// Oriented-box half-extent, Y (North). 0 =&gt; default from the entity's
        /// <c>VehicleParametersDto.Width</c>.
        /// </summary>
        [EditUnit("m")]
        public float BoxHalfY { get; init; }

        /// <summary>
        /// Oriented-box half-extent, Z (Up). 0 =&gt; default from <see cref="ShapeHeight"/>.
        /// </summary>
        [EditUnit("m")]
        public float BoxHalfZ { get; init; }
    }
}
