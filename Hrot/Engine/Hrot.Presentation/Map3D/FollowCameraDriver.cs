using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ <c>CE-1293</c> (docs/DESIGN_Unit_Camera.md UC-2, R-1032) — WATCHING a unit: the 3-D camera follows it on the distance continuum
/// (<see cref="UnitCameraRig"/>). The wheel moves along it — far, the camera orbits the unit and looks at it; near, it is over his
/// shoulder looking where the camera points; all the way in, it is at his eyes looking where HE looks (his gaze, <c>LookOf.EyeForward</c>).
/// The right mouse button drags the orbit (at his eyes: offsets the look while held, springing back). W / A / S / D, Esc or a map
/// (middle-button) drag hand the camera back to the free camera; so does the unit's end. Not possession: the unit is not controlled.
/// </summary>
public sealed class FollowCameraDriver : ICameraDriver
{
    private readonly Func<EntityRepository?> _world;
    private readonly EntityBodyLayer3D? _bodies;
    private readonly List<Fdp.Toolkit.World.TraceCrossing> _trace = new();
    private float _yaw, _pitch, _arm, _lookYawOffset, _lookPitchOffset;
    private bool _placed;

    public FollowCameraDriver(Func<EntityRepository?> world, Entity unit, EntityBodyLayer3D? bodies, float distance = StartDistance)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        Unit = unit;
        _bodies = bodies;
        Distance = distance;
    }

    /// <summary>The unit followed.</summary>
    public Entity Unit { get; }

    /// <summary>Where on the continuum the camera is (m; 0 = his eyes).</summary>
    public float Distance { get; private set; }

    /// <summary>How far away the follow starts (m): far enough to see him in his surroundings.</summary>
    public const float StartDistance = 14f;

    /// <summary>The farthest the wheel goes (m).</summary>
    public const float MaxDistance = 400f;

    /// <summary>Below this the camera is at his eyes (m).</summary>
    public const float EyesBelow = 0.4f;

    /// <summary>Each wheel notch scales the distance by this (in) or its inverse (out).</summary>
    public const float WheelFactor = 1.2f;

    /// <summary>How fast a held-look offset at his eyes springs back (1/s).</summary>
    public const float LookSpringRate = 4f;

    /// <summary>The orbit's turn per pixel of a right-button drag (rad).</summary>
    public const float OrbitPerPixel = 0.005f;

    /// <summary>At his eyes the view is a pair of eyes; elsewhere it looks AT him like the free camera.</summary>
    public bool ViewsFromEye => Distance < EyesBelow;

    /// <summary>True while the unit is there to follow.</summary>
    public bool Alive => _world() is { } w && w.IsAlive(Unit) && w.HasComponent<SimTransform>(Unit);

    public void Update(MapCamera3D camera, float dt)
    {
        if (!Alive) { camera.Driver = null; return; }
        var w = _world()!;
        var tf = w.GetComponentRO<SimTransform>(Unit);
        var scheme = SchemeOf(w);
        var eye = EyeOf(w, tf, scheme);
        var centre = new Vector3(eye.X, eye.Y, (tf.Position.Z + eye.Z) * 0.5f);
        if (!_placed) { _yaw = HeadingOf(tf.Rotation); _pitch = -0.35f; _placed = true; }

        if (ViewsFromEye)
        {
            // at his eyes: where HE looks, the mouse's held offset springing back
            var gaze = GazeOf(w, tf);
            float k = MathF.Exp(-LookSpringRate * dt);
            _lookYawOffset *= k; _lookPitchOffset *= k;
            camera.Yaw = MathF.Atan2(gaze.Y, gaze.X) + _lookYawOffset;
            camera.Pitch = Math.Clamp(MathF.Asin(Math.Clamp(gaze.Z, -1f, 1f)) + _lookPitchOffset, -1.4f, 1.4f);
            camera.Distance = camera.MinDistance;
            camera.LookAt = eye + camera.Forward * camera.Distance;
            return;
        }

        camera.Yaw = _yaw;
        camera.Pitch = _pitch;
        var forward = camera.Forward;
        var pivot = UnitCameraRig.Pivot(centre, eye, forward, Distance, scheme, side: 1);
        var wanted = UnitCameraRig.Camera(pivot, forward, Distance, orbitPitch: true);
        var free = UnitCameraRig.SpringArm(pivot, wanted, FirstHit);
        _arm = UnitCameraRig.EaseArm(_arm == 0f ? Vector3.Distance(pivot, free) : _arm, Vector3.Distance(pivot, free), dt);
        var at = pivot + Vector3.Normalize(wanted - pivot) * _arm;
        if (camera.GroundHeight is { } ground) at.Z = MathF.Max(at.Z, ground(at.X, at.Y) + FpsCameraDriver.CameraAboveGround);
        camera.Distance = camera.MinDistance;
        camera.LookAt = at + forward * camera.Distance;
    }

    public bool HandleInput(MapCamera3D camera, IInputProvider input)
    {
        if (input.IsKeyPressed((MapKeyboardKey)Raylib_cs.KeyboardKey.Escape)
            || input.IsKeyDown((MapKeyboardKey)Raylib_cs.KeyboardKey.W) || input.IsKeyDown((MapKeyboardKey)Raylib_cs.KeyboardKey.A)
            || input.IsKeyDown((MapKeyboardKey)Raylib_cs.KeyboardKey.S) || input.IsKeyDown((MapKeyboardKey)Raylib_cs.KeyboardKey.D)
            || input.IsMouseButtonDown(MapMouseButton.Middle))   // a map drag (UC-2)
        {
            camera.Driver = null;   // the operator takes the camera back
            return false;
        }
        bool used = false;
        float wheel = input.MouseWheelMove;
        if (wheel != 0f)
        {
            Distance = Step(Distance, wheel);
            used = true;
        }
        if (input.IsMouseButtonDown(MapMouseButton.Right))
        {
            var d = input.MouseDelta;
            if (ViewsFromEye) { _lookYawOffset -= d.X * OrbitPerPixel; _lookPitchOffset = Math.Clamp(_lookPitchOffset - d.Y * OrbitPerPixel, -1.2f, 1.2f); }
            else { _yaw -= d.X * OrbitPerPixel; _pitch = Math.Clamp(_pitch - d.Y * OrbitPerPixel, -1.4f, 1.4f); }
            used = true;
        }
        return used;
    }

    /// <summary>The distance after a wheel move: in (positive) brings him closer, to his eyes below <see cref="EyesBelow"/>; out from his
    /// eyes starts at the shoulder.</summary>
    public static float Step(float distance, float wheel)
    {
        if (distance < EyesBelow) return wheel < 0f ? UnitCameraRig.SideFullAt : 0f;
        float next = wheel > 0f ? distance / MathF.Pow(WheelFactor, wheel) : distance * MathF.Pow(WheelFactor, -wheel);
        return next < EyesBelow * 2f && wheel > 0f ? 0f : Math.Clamp(next, EyesBelow, MaxDistance);
    }

    public void Detached(MapCamera3D camera) => camera.Roll = 0f;

    private float? FirstHit(Vector3 from, Vector3 to)
    {
        if (_world() is not { } w || Fdp.Toolkit.World.WorldQuery.Of(w) is not { } query) return null;
        query.Trace(from, to, Fdp.Toolkit.World.TracePurpose.Sight, _trace);
        foreach (var c in _trace)
            if (c.ClosedBarrier || c.Loss < FpsCameraDriver.ArmOpaque) return c.T;
        return null;
    }

    private ControlScheme SchemeOf(EntityRepository w) => Fdp.Toolkit.Motion.Locomotions.Of(w, Unit) switch
    {
        Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft or Fdp.Toolkit.Tkb.Domain.LocomotionClass.FixedWing => ControlScheme.Rotorcraft,
        Fdp.Toolkit.Tkb.Domain.LocomotionClass.Wheeled or Fdp.Toolkit.Tkb.Domain.LocomotionClass.Tracked => ControlScheme.Vehicle,
        _ => ControlScheme.Person,
    };

    private Vector3 EyeOf(EntityRepository w, in SimTransform tf, ControlScheme scheme)
    {
        if (_bodies is { } bodies && bodies.TryGetEyeLocal(Unit, out var local)) return tf.Position + Vector3.Transform(local, tf.Rotation);
        return tf.Position + new Vector3(0f, 0f, scheme == ControlScheme.Person ? 1.6f : 2.2f);
    }

    /// <summary>Where his eyes point: his gaze where the sim keeps one (<c>LookOf.EyeForward</c>), else his heading.</summary>
    private Vector3 GazeOf(EntityRepository w, in SimTransform tf)
    {
        var gaze = Fdp.Toolkit.Perception.Look.LookOf.EyeForward(w, Unit, tf.Rotation);
        return gaze.LengthSquared() > 1e-6f ? Vector3.Normalize(gaze) : Vector3.UnitX;
    }

    private static float HeadingOf(Quaternion rotation)
    {
        var f = Vector3.Transform(Vector3.UnitX, rotation);
        return MathF.Atan2(f.Y, f.X);
    }
}
