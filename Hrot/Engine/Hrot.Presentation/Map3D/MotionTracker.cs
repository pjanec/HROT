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

    /// <summary>⭐ CE-1093 (§6n N-9) — how long a helicopter's rotor keeps running after it last moved (s), how long it takes to spool up
    /// or down (s), and the smallest movement between two frames that counts (m).</summary>
    public const float RotorRunOnSeconds = 20f, RotorSpoolSeconds = 3f, MovedMetres = 0.02f;

    /// <summary>⭐ CE-1086 — a drone's drawn prop speed (not the real ~150 rev/s, which would alias into a standstill at frame rate).</summary>
    public const float DroneRotorRevsPerSecond = 7.3f;

    /// <summary>A position jump bigger than this between two frames is a teleport, not motion.</summary>
    public const float TeleportMetres = 40f;

    /// <summary>The swing amplitude (radians) at a full walk and a full run.</summary>
    public const float WalkSwing = 0.45f, RunSwing = 0.75f;

    /// <summary>⭐ WL-6 (W-13e) — how far the upper body leans forward (radians) at a full walk and a full run (about 2° and 12°).</summary>
    public const float WalkLean = 0.035f, RunLean = 0.21f;

    /// <summary>⭐ WL-6d — one crawl cycle (left and right elbow), in metres crawled.</summary>
    public const float CrawlMetres = 0.7f;

    /// <summary>⭐ WL-6d — the speed (m/s) at which a figure counts as fully moving (a crawl is ~0.3–1 m/s).</summary>
    public const float MovingSpeed = 0.3f;

    private struct State
    {
        public Vector3 Last; public float Roll, Gait, Crawl; public double SimTime; public bool Seen;
        public float Swing, Lean, Stride, Moving; public bool HasGait;   // ⭐ CE-1064 — the last gait, kept over a frame with no sim time
        public float RotorRate, RotorAngle; public double LastMoved; public bool HasMoved;   // ⭐ CE-1093 — the rotor's spool and its angle
    }

    private readonly Dictionary<Entity, State> _states = new();
    private int _sweep;

    /// <summary>The pose of <paramref name="e"/>'s moving parts this frame. <paramref name="simTime"/> is the simulation clock
    /// (seconds); <paramref name="moving"/> spins a rotor (airborne or under way).</summary>
    public MotionPose Advance(Entity e, Vector3 position, Quaternion rotation, VisualFamily family, Vector3 size, double simTime,
                              bool rotorTurning)
    {
        if (!_states.TryGetValue(e, out var st) || !st.Seen)
            st = new State { Last = position, SimTime = simTime, Seen = true, RotorRate = rotorTurning ? 1f : 0f };   // first seen flying: at speed

        var delta = position - st.Last;
        if (delta.Length() > TeleportMetres) delta = Vector3.Zero;
        double dt = simTime - st.SimTime;
        if (dt < 0 || dt > 5) dt = 0;   // a rewound or jumped clock

        var forward = Vector3.Transform(Vector3.UnitX, rotation);
        float along = Vector3.Dot(new Vector3(delta.X, delta.Y, 0f), new Vector3(forward.X, forward.Y, 0f));
        float planar = new Vector2(delta.X, delta.Y).Length();

        float swing = 0f, rotor = 0f, lean = 0f, stride = 0f, moving = 0f;
        switch (family)
        {
            case VisualFamily.WheeledCar or VisualFamily.WheeledUtility:
            {
                float radius = MathF.Max(0.05f, ShapeKits.WheelRadiusOfHeight * size.Z);
                st.Roll = Wrap(st.Roll + along / radius);
                break;
            }
            case VisualFamily.Person when dt <= 1e-4 && planar == 0f && st.HasGait:
                // ⭐ CE-1064 — no sim time passed and he did not move: this frame says nothing about his gait (a paused sim, a frame
                //   drawn between two sim ticks), so the figure keeps the last one — a pause FREEZES him mid-stride (R-143) instead of
                //   snapping him to his still pose (a crouched runner to a kneel) for a frame.
                swing = st.Swing; lean = st.Lean; stride = st.Stride; moving = st.Moving;
                break;
            case VisualFamily.Person:
            {
                float speed = dt > 1e-4 ? (float)(planar / dt) : 0f;
                var gait = LocomotionBlend.FromSpeed(speed);
                st.Gait = Wrap(st.Gait + planar / StrideMetres * MathF.PI * 2f);
                swing = (gait.Walk * WalkSwing + gait.Run * RunSwing) * MathF.Sin(st.Gait);
                lean = gait.Walk * WalkLean + gait.Run * RunLean;
                stride = Math.Clamp(gait.Walk + gait.Run, 0f, 1f);   // ⭐ WL-6c — how much he is walking (knees, the glide)
                moving = Math.Clamp((speed - 0.05f) / MovingSpeed, 0f, 1f);   // ⭐ WL-6d — how much he is moving at all (the crawl)
                st.Crawl = Wrap(st.Crawl + planar / CrawlMetres * MathF.PI * 2f);
                st.Swing = swing; st.Lean = lean; st.Stride = stride; st.Moving = moving; st.HasGait = true;
                break;
            }
            case VisualFamily.Helicopter:
            case VisualFamily.AttackHelicopter:   // ⭐ CE-1103 — its rotors spool as a helicopter's
            {
                // ⭐ CE-1093 (§6n N-9) — the rotor RUNS while it is airborne or has moved in the last RotorRunOnSeconds (taking off,
                //   landing, taxiing — the height test alone stood it still on the ground), and spools up / down over RotorSpoolSeconds
                if (delta.LengthSquared() > MovedMetres * MovedMetres) { st.LastMoved = simTime; st.HasMoved = true; }
                bool runs = rotorTurning || (st.HasMoved && simTime - st.LastMoved <= RotorRunOnSeconds);
                float step = (float)dt / RotorSpoolSeconds;
                st.RotorRate = runs ? MathF.Min(1f, st.RotorRate + step) : MathF.Max(0f, st.RotorRate - step);
                st.RotorAngle = Wrap(st.RotorAngle + st.RotorRate * RotorRevsPerSecond * MathF.PI * 2f * (float)dt);
                rotor = st.RotorAngle;
                break;
            }
            case VisualFamily.Drone when rotorTurning:
                // ⭐ CE-1086 — small props: a visible spin, not the real ~150 rev/s (which would alias into a standstill at frame rate)
                rotor = (float)((simTime * DroneRotorRevsPerSecond % 1.0) * MathF.PI * 2.0);
                break;
        }

        st.Last = position;
        st.SimTime = simTime;
        _states[e] = st;
        return new MotionPose(rotor, st.Roll, swing, lean, Stride: stride, GaitPhase: st.Gait, Moving: moving, CrawlPhase: st.Crawl);
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
