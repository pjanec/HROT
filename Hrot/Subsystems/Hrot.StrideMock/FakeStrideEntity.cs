using System.Numerics;

namespace Hrot.StrideMock;

/// <summary>
/// Mutable runtime representation of a live ECS entity in the Stride scene.
/// Updated each frame by <see cref="SyncFdpToStrideScript"/>.
/// </summary>
public sealed class FakeStrideEntity
{
    /// <summary>World-space position (metres, flat-Earth Cartesian).</summary>
    public Vector3 Position { get; set; }

    /// <summary>Yaw angle in radians (rotation around the world-up Z axis).</summary>
    public float Rotation { get; set; }
}
