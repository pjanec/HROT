using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Stride.Core;

/// <summary>
/// Resolved shape dimensions for a Stride physics/visual primitive.
/// All "0 =&gt; default" rules from <see cref="StrideRenderModelDefDto"/> have already been
/// applied before this struct is handed to <see cref="IStrideVisualFactory"/>.
/// </summary>
public readonly struct ShapeDims
{
    /// <summary>Capsule/Cylinder/Sphere radius (metres).</summary>
    public readonly float Radius;

    /// <summary>Capsule/Cylinder/OrientedBox height along the up-axis (metres).</summary>
    public readonly float Height;

    /// <summary>Oriented-box half-extent, X axis (East, metres).</summary>
    public readonly float HalfX;

    /// <summary>Oriented-box half-extent, Y axis (North, metres).</summary>
    public readonly float HalfY;

    /// <summary>Oriented-box half-extent, Z axis (Up, metres).</summary>
    public readonly float HalfZ;

    /// <summary>Constructs dimensions for a capsule / cylinder / sphere.</summary>
    public ShapeDims(float radius, float height)
    {
        Radius = radius;
        Height = height;
        HalfX  = 0f;
        HalfY  = 0f;
        HalfZ  = 0f;
    }

    /// <summary>Constructs dimensions for an oriented box.</summary>
    public ShapeDims(float halfX, float halfY, float halfZ)
    {
        Radius = 0f;
        Height = 0f;
        HalfX  = halfX;
        HalfY  = halfY;
        HalfZ  = halfZ;
    }

    /// <summary>
    /// Constructs dimensions for a capsule / cylinder / sphere with explicit height.
    /// Named factory to disambiguate from the box overload.
    /// </summary>
    public static ShapeDims Capsule(float radius, float height)
        => new ShapeDims(radius, height);

    /// <summary>Constructs dimensions for an oriented box from full half-extents.</summary>
    public static ShapeDims Box(float halfX, float halfY, float halfZ)
        => new ShapeDims(halfX, halfY, halfZ);
}
