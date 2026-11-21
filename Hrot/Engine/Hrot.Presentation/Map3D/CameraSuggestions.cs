using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis3D;
using Hrot.Map.Common.Components;

namespace Hrot.UI.Common.Map3D;

/// <summary>⭐ CE-11006 (RR-6) — a camera driver says what it is doing (source, mode, bound unit, distance, side); the pose is the camera's own.</summary>
public interface ICameraSuggests
{
    void Describe(EntityRepository world, ref CameraSuggestion s);
}

/// <summary>
/// ⭐⭐ CE-11006 (RR-6, R-11000; docs/designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md D-1) — the ONE writer of the world's
/// <see cref="CameraSuggestion"/>: called once per frame by <see cref="MapViewModeLayer"/> (every map host has it), it composes the driver's description
/// (<see cref="ICameraSuggests"/>; none = the operator's free camera) with the 3-D camera's pose. 0 B (a struct, set by value).
/// </summary>
public static class CameraSuggestions
{
    public static void Write(EntityRepository world, MapCamera3D camera)
    {
        var s = new CameraSuggestion { Source = CameraSource.Operator, Mode = CameraMode.Free, Distance = camera.Distance };
        if (camera.Driver is ICameraSuggests d) d.Describe(world, ref s);
        s.Position = camera.Eye;
        s.LookAt = camera.LookAt;
        s.Orientation = OrientationOf(camera.Forward, camera.Up);
        s.FovyDegrees = camera.FovyDegrees;
        world.SetSingletonUnmanaged(s);
    }

    /// <summary>The rotation carrying +X onto <paramref name="forward"/> and +Z onto <paramref name="up"/> (+Y = their left).</summary>
    public static Quaternion OrientationOf(Vector3 forward, Vector3 up)
    {
        if (forward.LengthSquared() < 1e-8f) return Quaternion.Identity;
        var f = Vector3.Normalize(forward);
        var left = Vector3.Cross(up, f);
        if (left.LengthSquared() < 1e-8f) left = Vector3.Cross(Vector3.UnitZ, f);
        if (left.LengthSquared() < 1e-8f) left = Vector3.UnitY;
        left = Vector3.Normalize(left);
        var u = Vector3.Cross(f, left);
        var m = new Matrix4x4(f.X, f.Y, f.Z, 0f, left.X, left.Y, left.Z, 0f, u.X, u.Y, u.Z, 0f, 0f, 0f, 0f, 1f);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }
}
