using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Animals;
using Fdp.Toolkit.Animation;

using MotionRules = Hrot.Look.Vehicles.MotionRules;

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
    public const float RotorRevsPerSecond = MotionRules.RotorRevsPerSecond;   // ⭐ CE-11003 (RR-3) — the shared rules

    /// <summary>⭐ CE-1093 (§6n N-9) — how long a helicopter's rotor keeps running after it last moved (s), how long it takes to spool up
    /// or down (s), and the smallest movement between two frames that counts (m).</summary>
    public const float RotorRunOnSeconds = MotionRules.RotorRunOnSeconds, RotorSpoolSeconds = MotionRules.RotorSpoolSeconds, MovedMetres = MotionRules.MovedMetres;

    /// <summary>⭐ CE-1086 — a drone's drawn prop speed (not the real ~150 rev/s, which would alias into a standstill at frame rate).</summary>
    public const float DroneRotorRevsPerSecond = MotionRules.DroneRotorRevsPerSecond;

    /// <summary>⭐ CE-4618 — a search radar's antenna: turns per second while the radar radiates (a ~1.7 s sweep, a short-range SAM's), spooling up and down over
    /// <see cref="RotorSpoolSeconds"/> like a rotor.</summary>
    public const float AntennaRevsPerSecond = MotionRules.AntennaRevsPerSecond;

    /// <summary>⭐ CE-4624 — a mine clearer's flail drum: turns per second while the flail is on (a flail lashes fast), spooling over <see cref="FlailSpoolSeconds"/>.</summary>
    public const float FlailRevsPerSecond = MotionRules.FlailRevsPerSecond, FlailSpoolSeconds = MotionRules.FlailSpoolSeconds;

    /// <summary>⭐ CE-4621 — a motorcycle's lean into a turn: <c>atan(v · yawRate / g)</c>, at most <see cref="MaxBank"/> (rad, ~35°), eased with <see cref="BankSeconds"/> (s). A DRAWING only.</summary>
    public const float MaxBank = MotionRules.MaxBank, BankSeconds = MotionRules.BankSeconds;

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
        public float Yaw, Bank; public bool HasYaw;   // ⭐ CE-4621 — a motorcycle's heading and how far it leans into a turn
        public float AnimalSpeed; public float LastZ; public bool HasZ;   // ⭐ CE-4623 — an animal's smoothed speed (m/s) and its last height
        public double Wall; public bool HasWall; public float Pace;   // ⭐ CE-1353 — the frame clock and the sim's smoothed pace on it
        public float Heave;   // ⭐ CE-4634 — a dragon's visual bob this frame (m, body up), what its riders and the rider's eye move with (SwayOf)
        public float Beat, Tuck;   // ⭐ CE-4634 — a dragon's beat share (0 glide … 1 beating) and how far its legs are tucked, each eased over DragonPoses.BlendSeconds
    }

    /// <summary>
    /// ⭐ <c>CE-1353</c> — how quickly the rotor's smoothed pace follows the simulation's (s). The sim advances in fixed steps — 0, 1 or 2
    /// in a drawn frame — so a rotor turned by each frame's SIM time jumped 0°, 26° or 52° frame to frame, and four blades (the picture
    /// repeats every 90°) seemed to flick back and forth. 🔒 User, 2026-10-25: "apache possessed - flight model is shivering (left to right
    /// and back slightly) when helicopter is hovering" · "jitter not happening when simulation is stepped". The angle now advances by the
    /// FRAME time × the sim's pace eased over this long: an even spin at the sim's speed, still stopping when the sim pauses.
    /// </summary>
    public const float PaceSeconds = 0.25f;

    private readonly Dictionary<Entity, State> _states = new();
    private readonly Dictionary<long, State> _keyed = new();   // ⭐ AQ93 §15.8 (CE-4031) — bodies with no entity (SUMO's far agents)
    private int _sweep, _keyedSweep;

    /// <summary>The pose of <paramref name="e"/>'s moving parts this frame. <paramref name="simTime"/> is the simulation clock
    /// (seconds); <paramref name="moving"/> spins a rotor (airborne or under way).</summary>
    public MotionPose Advance(Entity e, Vector3 position, Quaternion rotation, VisualFamily family, Vector3 size, double simTime,
                              bool rotorTurning, double? wallTime = null, AnimalMode mode = AnimalMode.Calm, float height = 0f)
    {
        _states.TryGetValue(e, out var st);
        var pose = Step(ref st, position, rotation, family, size, simTime, rotorTurning, wallTime, mode, height, (uint)e.Index);
        _states[e] = st;
        return pose;
    }

    /// <summary>⭐ CE-4621 — how far <paramref name="e"/> (a motorcycle) leaned into its turn when it was last drawn (rad, about +x; 0 when never seen) — what its seated riders lean with.</summary>
    public float BankOf(Entity e) => _states.TryGetValue(e, out var st) ? st.Bank : 0f;

    /// <summary>⭐ CE-4634 — the ONE visual sway of <paramref name="e"/>'s drawn body off its sim pose, in its BODY frame (pre-multiply the body matrix): a motorcycle's bank
    /// (CE-4621) and a dragon's wingbeat bob. The body, its seated riders and a rider's camera eye all move with it, so they cannot drift apart. Identity when never seen. 0 B.</summary>
    public Matrix4x4 SwayOf(Entity e)
    {
        if (!_states.TryGetValue(e, out var st) || (st.Bank == 0f && st.Heave == 0f)) return Matrix4x4.Identity;
        var m = st.Bank != 0f ? Matrix4x4.CreateRotationX(st.Bank) : Matrix4x4.Identity;
        if (st.Heave != 0f) m *= Matrix4x4.CreateTranslation(0f, 0f, st.Heave);
        return m;
    }

    /// <summary>⭐ AQ93 §15.8 (CE-4031) — the same, for a body with no entity, by a stable <paramref name="key"/> (its network id).</summary>
    public MotionPose Advance(long key, Vector3 position, Quaternion rotation, VisualFamily family, Vector3 size, double simTime)
    {
        _keyed.TryGetValue(key, out var st);
        var pose = Step(ref st, position, rotation, family, size, simTime, rotorTurning: false);
        _keyed[key] = st;
        return pose;
    }

    private static MotionPose Step(ref State st, Vector3 position, Quaternion rotation, VisualFamily family, Vector3 size, double simTime,
                                   bool rotorTurning, double? wallTime = null, AnimalMode mode = AnimalMode.Calm, float height = 0f, uint seed = 0)
    {
        if (!st.Seen)
            st = new State { Last = position, SimTime = simTime, Seen = true, RotorRate = rotorTurning ? 1f : 0f };   // first seen flying: at speed

        var delta = position - st.Last;
        if (delta.Length() > TeleportMetres) delta = Vector3.Zero;
        double dt = simTime - st.SimTime;
        if (dt < 0 || dt > 5) dt = 0;   // a rewound or jumped clock
        // ⭐ CE-1353 — the spin's own clock: the frame time × the sim's smoothed pace (PaceSeconds); no frame clock ⇒ the sim's dt
        float spinDt = (float)dt;
        if (wallTime is { } wall)
        {
            double wdt = st.HasWall ? wall - st.Wall : 0.0;
            st.Wall = wall; st.HasWall = true;
            if (wdt > 1e-5 && wdt < 1.0)
            {
                float rate = Math.Clamp((float)(dt / wdt), 0f, 16f);
                st.Pace += (rate - st.Pace) * (1f - MathF.Exp(-(float)wdt / PaceSeconds));
                spinDt = st.Pace * (float)wdt;
            }
            else spinDt = 0f;
        }

        var forward = Vector3.Transform(Vector3.UnitX, rotation);
        float along = Vector3.Dot(new Vector3(delta.X, delta.Y, 0f), new Vector3(forward.X, forward.Y, 0f));
        float planar = new Vector2(delta.X, delta.Y).Length();

        float swing = 0f, rotor = 0f, lean = 0f, stride = 0f, moving = 0f, antenna = 0f, bank = 0f, wing = 0f, head = 0f, tuck = 0f, flail = 0f;
        var gaitKind = QuadGait.Stand;
        switch (family)
        {
            case VisualFamily.WheeledCar or VisualFamily.WheeledUtility:
            {
                st.Roll = MotionRules.Roll(st.Roll, along, MotionRules.CarWheelRadius(size.Z));   // ⭐ CE-11003 (RR-3)
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
            case VisualFamily.Cattle or VisualFamily.Sheep or VisualFamily.Horse or VisualFamily.Dog or VisualFamily.Cat or VisualFamily.Pig or VisualFamily.Goat or VisualFamily.Rabbit:
            {
                // ⭐ CE-4623 (AU-3) — a quadruped: the speed (smoothed over 0.12 s) picks the gait, the distance walked advances ITS cycle (feet do not slide), each leg has its own phase
                //   (QuadrupedGaits.LegAngle); a paused clock (dt 0) keeps the speed and the phase, freezing it mid-stride; a jump (height above the ground) tucks the legs
                if (dt > 1e-4) st.AnimalSpeed += ((float)(planar / dt) - st.AnimalSpeed) * (1f - MathF.Exp(-(float)dt / 0.12f));
                var bands = QuadrupedGaits.BandsOf(family);
                gaitKind = QuadrupedGaits.Classify(st.AnimalSpeed, bands);
                if (family == VisualFamily.Rabbit && gaitKind > QuadGait.Walk) gaitKind = QuadGait.Hop;   // a rabbit walks, then HOPS (fore pair together, hind pair together)
                if (gaitKind != QuadGait.Stand) st.Gait = Wrap(st.Gait + planar / QuadrupedGaits.Stride(gaitKind, bands) * MathF.PI * 2f);
                swing = QuadrupedGaits.Amplitude(gaitKind);
                moving = Math.Clamp(st.AnimalSpeed / MovingSpeed, 0f, 1f);
                head = AnimalPoses.HeadPitch(family, mode, st.AnimalSpeed, simTime, seed);
                tuck = Math.Clamp((height - 0.12f) / 0.3f, 0f, 1f);
                break;
            }
            case VisualFamily.Poultry:
            {
                // ⭐ CE-4623 — a hen: two legs that step (the man's leg roles), the head that pecks when calm and still, the wings that flutter when it runs frightened or hops
                if (dt > 1e-4) st.AnimalSpeed += ((float)(planar / dt) - st.AnimalSpeed) * (1f - MathF.Exp(-(float)dt / 0.12f));
                st.Gait = Wrap(st.Gait + planar / 0.28f * MathF.PI * 2f);
                swing = st.AnimalSpeed > QuadrupedGaits.StandBelow ? 0.75f * MathF.Sin(st.Gait) : 0f;
                head = st.AnimalSpeed < 0.3f && mode is AnimalMode.Calm or AnimalMode.Recover ? AnimalPoses.Peck(simTime, seed) : 0f;
                wing = height > 0.1f || (mode == AnimalMode.Flee && st.AnimalSpeed > 1.5f) ? AnimalPoses.Flutter(simTime, seed) : 0f;
                break;
            }
            case VisualFamily.Bird:
            {
                // ⭐ CE-4623 — a lone bird: flapping while it moves or climbs / drops, folded wings (another kit) perched
                if (dt > 1e-4) st.AnimalSpeed += ((float)(planar / dt) - st.AnimalSpeed) * (1f - MathF.Exp(-(float)dt / 0.12f));
                float vz = st.HasZ && dt > 1e-4 ? MathF.Abs(position.Z - st.LastZ) / (float)dt : 0f;
                st.LastZ = position.Z; st.HasZ = true;
                wing = st.AnimalSpeed > 1.2f || vz > 0.6f ? AnimalPoses.Flap(simTime, seed) : 0f;
                moving = wing != 0f ? 1f : 0f;
                break;
            }
            case VisualFamily.Dragon:
            {
                // ⭐ CE-4634 (DESIGN_Dragon §8.2 W, L) — rotorTurning = it FLIES (FlightState.Airborne). In flight: the wings BEAT while it climbs or flies slowly and GLIDE at
                //   speed, the legs tucked; landed: the wings fold (the other kit) and the legs walk. The beat and the tuck ease over a second; the stroke runs on sim time, so a
                //   paused sim (dt 0) freezes it mid-beat, and every host draws the same stroke. Floats in the state — 0 B.
                if (dt > 1e-4) st.AnimalSpeed += ((float)(planar / dt) - st.AnimalSpeed) * (1f - MathF.Exp(-(float)dt / 0.3f));
                float climb = st.HasZ && dt > 1e-4 ? (position.Z - st.LastZ) / (float)dt : 0f;
                if (!st.HasZ && rotorTurning) st.Tuck = 1f;   // first seen in the air: already tucked
                st.LastZ = position.Z; st.HasZ = true;
                st.Beat = DragonPoses.Ease(st.Beat, rotorTurning ? DragonPoses.BeatTarget(st.AnimalSpeed, climb) : 0f, (float)dt);
                st.Tuck = DragonPoses.Ease(st.Tuck, rotorTurning ? 1f : 0f, (float)dt);
                st.Heave = rotorTurning ? DragonPoses.Heave(simTime, st.Beat) : 0f;   // ⭐ the bob, in step with the stroke; none on the ground
                if (rotorTurning) wing = DragonPoses.Wing(simTime, st.Beat);
                else if (st.AnimalSpeed > MovingSpeed)
                {
                    gaitKind = QuadGait.Walk;
                    st.Gait = Wrap(st.Gait + planar / DragonPoses.StrideMetres * MathF.PI * 2f);
                }
                swing = QuadrupedGaits.Amplitude(gaitKind);
                tuck = st.Tuck;
                moving = rotorTurning || st.AnimalSpeed > MovingSpeed ? 1f : 0f;
                break;
            }
            case VisualFamily.Motorcycle:
            {
                // ⭐ CE-4621 — the wheels roll on the bike's own radius; a turn banks the body: the lean a rider needs, atan(v·ω/g), left turn = the top toward the left (a NEGATIVE turn about +x)
                st.Roll = MotionRules.Roll(st.Roll, along, MotionRules.MotoWheelRadius);
                float yaw = MathF.Atan2(forward.Y, forward.X);
                float target = 0f;
                if (st.HasYaw && dt > 1e-4)
                {
                    float dyaw = yaw - st.Yaw;
                    if (dyaw > MathF.PI) dyaw -= MathF.PI * 2f; else if (dyaw < -MathF.PI) dyaw += MathF.PI * 2f;
                    float speed = along / (float)dt, rate = dyaw / (float)dt;
                    target = MotionRules.BankTarget(speed, rate);   // ⭐ CE-11003 (RR-3) — the shared rule
                    st.Bank = MotionRules.EaseBank(st.Bank, target, (float)dt);
                }
                else if (!st.HasYaw) st.Bank = 0f;
                st.Yaw = yaw; st.HasYaw = true;
                bank = st.Bank;
                break;
            }
            case VisualFamily.AirDefenceVehicle:
            {
                // ⭐ CE-4618 — the wheels roll on the truck's own radius; the antenna spools up while the radar radiates (rotorTurning = RadarActivity.IsOn) and down
                //   when it stops, turning by the frame clock × the sim's pace like a rotor (CE-1353) — so it is the same on every host and a paused sim stops it
                st.Roll = MotionRules.Roll(st.Roll, along, MotionRules.SamWheelRadius);
                st.RotorRate = MotionRules.Spool(st.RotorRate, rotorTurning, (float)dt, MotionRules.RotorSpoolSeconds);
                st.RotorAngle = MotionRules.Spin(st.RotorAngle, st.RotorRate, MotionRules.AntennaRevsPerSecond, spinDt);
                antenna = st.RotorAngle;
                break;
            }
            case VisualFamily.FloatingBridge:
            {
                st.Roll = MotionRules.Roll(st.Roll, along, ShapeKits.FloatingBridgeWheelRadius);   // ⭐ CE-6502 — the truck's wheels roll on its own radius
                break;
            }
            case VisualFamily.MineClearer:
            {
                // ⭐ CE-4624 — tracked: no wheel to roll. The flail drum spools up while the flail is on (rotorTurning = FlailActivity.IsOn) and down when it stops, turning by the frame
                //   clock × the sim's pace like the antenna / a rotor (CE-1353) — the same on every host, and a paused sim stops it. No allocation: floats in the state.
                st.RotorRate = MotionRules.Spool(st.RotorRate, rotorTurning, (float)dt, MotionRules.FlailSpoolSeconds);
                st.RotorAngle = MotionRules.Spin(st.RotorAngle, st.RotorRate, MotionRules.FlailRevsPerSecond, spinDt);
                flail = st.RotorAngle;
                break;
            }
            case VisualFamily.Helicopter:
            case VisualFamily.AttackHelicopter:   // ⭐ CE-1103 — its rotors spool as a helicopter's
            {
                // ⭐ CE-1093 (§6n N-9) — the rotor RUNS while it is airborne or has moved in the last RotorRunOnSeconds (taking off,
                //   landing, taxiing — the height test alone stood it still on the ground), and spools up / down over RotorSpoolSeconds
                if (delta.LengthSquared() > MovedMetres * MovedMetres) { st.LastMoved = simTime; st.HasMoved = true; }
                bool runs = MotionRules.RotorRuns(rotorTurning, st.HasMoved, st.LastMoved, simTime);
                st.RotorRate = MotionRules.Spool(st.RotorRate, runs, (float)dt, MotionRules.RotorSpoolSeconds);
                st.RotorAngle = MotionRules.Spin(st.RotorAngle, st.RotorRate, MotionRules.RotorRevsPerSecond, spinDt);   // ⭐ CE-1353
                rotor = st.RotorAngle;
                break;
            }
            case VisualFamily.Drone when rotorTurning:
                // ⭐ CE-1086 — small props: a visible spin, not the real ~150 rev/s (which would alias into a standstill at frame rate)
                rotor = MotionRules.DroneProp(simTime);
                break;
        }

        st.Last = position;
        st.SimTime = simTime;
        return new MotionPose(rotor, st.Roll, swing, lean, Stride: stride, GaitPhase: st.Gait, Moving: moving, CrawlPhase: st.Crawl, Antenna: antenna, Bank: bank, Wing: wing, Head: head, Tuck: tuck, Gait: gaitKind, Flail: flail);
    }

    /// <summary>Forgets entities not seen for a while — called once per frame with the set drawn this frame.</summary>
    public void EndFrame(HashSet<Entity> drawn)
    {
        if (++_sweep < 120 || _states.Count <= drawn.Count) return;
        _sweep = 0;
        _gone.Clear();   // ⭐ CE-4555 — a reused list (was ToList every sweep)
        foreach (var e in _states.Keys) if (!drawn.Contains(e)) _gone.Add(e);
        foreach (var e in _gone) _states.Remove(e);
    }

    /// <summary>⭐ CE-4031 — forgets keyed bodies not seen for a while (<see cref="Advance(long, Vector3, Quaternion, VisualFamily, Vector3, double)"/>).</summary>
    public void EndFrame(HashSet<long> drawn)
    {
        if (++_keyedSweep < 120 || _keyed.Count <= drawn.Count) return;
        _keyedSweep = 0;
        _goneKeys.Clear();
        foreach (var k in _keyed.Keys) if (!drawn.Contains(k)) _goneKeys.Add(k);
        foreach (var k in _goneKeys) _keyed.Remove(k);
    }

    private static float Wrap(float a) => a % (MathF.PI * 2f);
    private readonly List<Entity> _gone = new();
    private readonly List<long> _goneKeys = new();
}
