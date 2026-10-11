using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Animation;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-1033 S4 (docs/DESIGN_Map_3D_Mode.md §6, "things come alive") — turns how an entity MOVED into how its moving parts look:
/// <list type="bullet">
///   <item><b>wheels</b> roll by the distance driven along the body's forward axis over the wheel radius — backwards when it
///   reverses;</item>
///   <item><b>limbs</b> swing at an amplitude from the shared gait rule (<see cref="LocomotionBlend"/> — walk and run weights, the
///   same rule the Stride blend tree plays), one stride cycle per <see cref="StrideMetres"/> walked;</item>
///   <item><b>a main rotor</b> spins at <see cref="RotorRevsPerSecond"/> of SIMULATION time while the aircraft is airborne or
///   moving, and stands still when it is parked.</item>
/// </list>
/// Everything is read from the entity's own position between frames, so it is the same on every host — the owner, a replica, a
/// replay — and a paused simulation (no movement, no sim time) freezes it (R-143). A jump of more than <see cref="TeleportMetres"/>
/// (a seek, a respawn) resets instead of spinning the wheels.
/// </summary>
public sealed class MotionTracker
{
    /// <summary>One stride cycle (left and right step) of a walking figure, in metres.</summary>
    public const float StrideMetres = 1.6f;

    /// <summary>The main rotor's speed (UH-60: about 258 rpm).</summary>
    public const float RotorRevsPerSecond = 4.3f;

    /// <summary>A position jump bigger than this between two frames is a teleport, not motion.</summary>
    public const float TeleportMetres = 40f;

    /// <summary>The swing amplitude (radians) at a full walk and a full run.</summary>
    public const float WalkSwing = 0.45f, RunSwing = 0.75f;

    private struct State { public Vector3 Last; public float Roll, Gait; public double SimTime; public bool Seen; }

    private readonly Dictionary<Entity, State> _states = new();
    private int _sweep;

    /// <summary>The pose of <paramref name="e"/>'s moving parts this frame. <paramref name="simTime"/> is the simulation clock
    /// (seconds); <paramref name="moving"/> spins a rotor (airborne or under way).</summary>
    public MotionPose Advance(Entity e, Vector3 position, Quaternion rotation, VisualFamily family, Vector3 size, double simTime,
                              bool rotorTurning)
    {
        if (!_states.TryGetValue(e, out var st) || !st.Seen)
            st = new State { Last = position, SimTime = simTime, Seen = true };

        var delta = position - st.Last;
        if (delta.Length() > TeleportMetres) delta = Vector3.Zero;
        double dt = simTime - st.SimTime;
        if (dt < 0 || dt > 5) dt = 0;   // a rewound or jumped clock

        var forward = Vector3.Transform(Vector3.UnitX, rotation);
        float along = Vector3.Dot(new Vector3(delta.X, delta.Y, 0f), new Vector3(forward.X, forward.Y, 0f));
        float planar = new Vector2(delta.X, delta.Y).Length();

        float swing = 0f, rotor = 0f;
        switch (family)
        {
            case VisualFamily.WheeledCar or VisualFamily.WheeledUtility:
            {
                float radius = MathF.Max(0.05f, ShapeKits.WheelRadiusOfHeight * size.Z);
                st.Roll = Wrap(st.Roll + along / radius);
                break;
            }
            case VisualFamily.Person:
            {
                float speed = dt > 1e-4 ? (float)(planar / dt) : 0f;
                var gait = LocomotionBlend.FromSpeed(speed);
                st.Gait = Wrap(st.Gait + planar / StrideMetres * MathF.PI * 2f);
                swing = (gait.Walk * WalkSwing + gait.Run * RunSwing) * MathF.Sin(st.Gait);
                break;
            }
            case VisualFamily.Helicopter when rotorTurning:
                rotor = (float)((simTime * RotorRevsPerSecond % 1.0) * MathF.PI * 2.0);
                break;
        }

        st.Last = position;
        st.SimTime = simTime;
        _states[e] = st;
        return new MotionPose(rotor, st.Roll, swing);
    }

    /// <summary>Forgets entities not seen for a while — called once per frame with the set drawn this frame.</summary>
    public void EndFrame(HashSet<Entity> drawn)
    {
        if (++_sweep < 120 || _states.Count <= drawn.Count) return;
        _sweep = 0;
        foreach (var e in _states.Keys.ToList())
            if (!drawn.Contains(e)) _states.Remove(e);
    }

    private static float Wrap(float a) => a % (MathF.PI * 2f);
}
