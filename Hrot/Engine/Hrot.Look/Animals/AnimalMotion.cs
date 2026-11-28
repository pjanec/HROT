using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Animals;

namespace Hrot.UI.Common.Map3D;

// ⭐ CE-11005 (RR-5, docs/designs/vehicle-appearance/DESIGN_Look_Rules_For_Renderers.md) — moved VERBATIM from Hrot.Presentation into the shared look
//   library so an external renderer calls the same rule the map does; the namespace is kept (as VisualFamily's was) so no caller changed — the
//   rename to Hrot.Look.* is one Roslyn rename, filed on CE-11000.

/// <summary>
/// ⭐ CE-1033 S4 (docs/DESIGN_Map_3D_Mode.md §6, "things come alive") — the moving parts' pose for one frame: the main-rotor
/// angle, the wheels' roll angle (radians) and the limb swing (radians, + = the left leg forward). Default: all still.
/// </summary>
/// <para>⭐ WL-6 (docs/DESIGN_Weapon_Layers.md W-13e) — and the block figure's UPPER BODY (<see cref="KitPart.Upper"/> parts): <paramref name="Lean"/>
/// tilts it forward about the hip (radians; a run, and an aim pitched down), <paramref name="Twist"/> turns it about the vertical
/// (radians, counter-clockwise; an aim to the side), about a hip at <paramref name="HipZ"/> of the figure's height.</para>
/// <para>⭐ WL-6c — the gait itself: <paramref name="Stride"/> (0..1, how much the figure is walking or running) and its
/// <paramref name="GaitPhase"/> (radians; <c>Swing = amplitude · sin(phase)</c>); <paramref name="KneeFlexL"/> / <paramref name="KneeFlexR"/>
/// bend each shin back about its knee (radians) — the foot lifting as the leg swings through.</para>
/// <para>⭐ WL-6d — <paramref name="Moving"/> (0..1, from the speed alone — a crawl is too slow to read as a walk) and the
/// <paramref name="CrawlPhase"/> (radians, one cycle per <c>MotionTracker.CrawlMetres</c>).</para>
/// <para>⭐ CE-1204 (docs/DESIGN_Entity_Control.md §8l LK-5) — <paramref name="HeadTurn"/>: the HEAD (the <see cref="FaceOn.Front"/> part)
/// turns about its own vertical axis by this much off the torso (radians, counter-clockwise), before the torso's <paramref name="Twist"/>
/// carries it — so the head points where his eyes do (the replicated look's head yaw).</para>
/// <para>⭐ CE-4623 (animals, AU-3) — <paramref name="Gait"/> + <paramref name="GaitPhase"/> + <paramref name="Swing"/> (the amplitude) give each quadruped leg its own swing
/// (<see cref="QuadrupedGaits.LegAngle"/>); <paramref name="Tuck"/> (0..1) draws the legs folded up in a jump; <paramref name="Wing"/> is a bird's wing angle (radians);
/// <paramref name="Head"/> an animal's head pitch about its withers (+ = down).</para>
/// <para>⭐ CE-4624 — <paramref name="Flail"/>: the mine clearer's flail drum angle (radians) about its lateral axis (+ = the top turning forward, <see cref="PartRole.FlailDrum"/>).</para>
public readonly record struct MotionPose(float Rotor, float WheelRoll, float Swing, float Lean = 0f, float Twist = 0f, float HipZ = 0.5f,
                                         float Stride = 0f, float GaitPhase = 0f, float KneeFlexL = 0f, float KneeFlexR = 0f,
                                         float Moving = 0f, float CrawlPhase = 0f, float HeadTurn = 0f, float Antenna = 0f, float Bank = 0f,
                                         float Wing = 0f, float Head = 0f, float Tuck = 0f, QuadGait Gait = QuadGait.Stand, float Flail = 0f)
{
    public static readonly MotionPose Still = default;
}

/// <summary>⭐ CE-4623 — the ONE place the animals' replicated state is read (the re-bind seam: backend's component layouts replace the stand-ins here, nowhere else).</summary>
public static class AnimalReader
{
    /// <summary>The brain's mode of <paramref name="e"/> (Calm where it has no state).</summary>
    public static AnimalMode ModeOf(EntityRepository world, Entity e)
        => world.IsComponentTypeRegistered<AnimalState>() && world.HasComponent<AnimalState>(e) ? world.GetComponentRO<AnimalState>(e).Mode : AnimalMode.Calm;

    /// <summary>The flock state of <paramref name="e"/>; false for an entity that is a single body.</summary>
    public static bool FlockOf(EntityRepository world, Entity e, out FlockState flock)
    {
        if (world.IsComponentTypeRegistered<FlockState>() && world.HasComponent<FlockState>(e)) { flock = world.GetComponentRO<FlockState>(e); return true; }
        flock = default;
        return false;
    }
}

/// <summary>⭐ CE-4623 — the quadruped's gait by speed.</summary>
public enum QuadGait : byte { Stand = 0, Walk, Trot, Canter, Gallop,
    /// <summary>A rabbit's hop (appended): the fore pair together, then the hind pair together.</summary>
    Hop }

/// <summary>⭐ CE-4623 (AU-3, AR-13) — one species' gait: the speeds (m/s) where walk gives way to trot, trot to canter, canter to gallop, and the metres one full stride of each gait covers.</summary>
public readonly record struct GaitBands(float WalkMax, float TrotMax, float CanterMax, float StrideWalk, float StrideTrot, float StrideCanter, float StrideGallop);

/// <summary>
/// ⭐ CE-4623 (AU-3, AR-13) — the QUADRUPED's gaits, a visual descriptor per species. A leg's swing is <c>amplitude · sin(phase + 2π · offset)</c>: the offset is that leg's
/// fraction of the cycle — a walk's four beats (lateral sequence), a trot's two diagonal pairs, a canter's three beats, a gallop's two pairs — so the legs read as the gait the
/// speed calls for. Pure, allocation-free (the legs are asked every frame).
/// </summary>
public static class QuadrupedGaits
{
    private const float TwoPi = MathF.PI * 2f;

    /// <summary>Under this smoothed speed (m/s) the animal stands: legs still.</summary>
    public const float StandBelow = 0.15f;

    /// <summary>How much of the neck's pitch the head tilts back against it (the head is a part of its own: it follows the neck and counter-rotates, so a grazing muzzle points down).</summary>
    public const float HeadCounter = 0.45f;

    /// <summary>The swing amplitude (radians) of a stand, walk, trot, canter and gallop.</summary>
    public static float Amplitude(QuadGait g) => g switch { QuadGait.Walk => 0.40f, QuadGait.Trot => 0.55f, QuadGait.Canter => 0.70f, QuadGait.Gallop => 0.95f, QuadGait.Hop => 0.85f, _ => 0f };

    /// <summary>The species' gait bands.</summary>
    public static GaitBands BandsOf(VisualFamily f) => f switch
    {
        VisualFamily.Horse => new(2.2f, 4.8f, 7.8f, 1.7f, 3.0f, 3.8f, 5.6f),
        VisualFamily.Cattle => new(1.7f, 3.6f, 5.8f, 1.3f, 2.2f, 2.9f, 4.2f),
        VisualFamily.Sheep => new(1.4f, 3.0f, 5.0f, 0.8f, 1.4f, 1.8f, 2.6f),
        VisualFamily.Goat => new(1.5f, 3.2f, 5.2f, 0.9f, 1.5f, 2.0f, 2.8f),
        VisualFamily.Pig => new(1.5f, 3.2f, 5.0f, 0.8f, 1.4f, 1.9f, 2.6f),
        VisualFamily.Dog => new(1.8f, 3.8f, 6.0f, 0.9f, 1.6f, 2.2f, 3.0f),
        VisualFamily.Cat => new(1.3f, 3.0f, 5.0f, 0.5f, 0.9f, 1.2f, 1.8f),
        VisualFamily.Rabbit => new(0.8f, 3.0f, 6.0f, 0.25f, 0.5f, 0.5f, 0.5f),   // walks to 0.8 m/s, then hops (MotionTracker turns every faster gait into Hop): ~0.5 m a hop
        _ => new(1.5f, 3.5f, 5.5f, 1.0f, 1.8f, 2.4f, 3.4f),
    };

    /// <summary>The gait a <paramref name="speed"/> (m/s) calls for in <paramref name="b"/>.</summary>
    public static QuadGait Classify(float speed, in GaitBands b)
        => speed < StandBelow ? QuadGait.Stand : speed <= b.WalkMax ? QuadGait.Walk : speed <= b.TrotMax ? QuadGait.Trot : speed <= b.CanterMax ? QuadGait.Canter : QuadGait.Gallop;

    /// <summary>Metres per full stride of <paramref name="g"/>.</summary>
    public static float Stride(QuadGait g, in GaitBands b) => g switch { QuadGait.Walk => b.StrideWalk, QuadGait.Trot => b.StrideTrot, QuadGait.Canter => b.StrideCanter, _ => b.StrideGallop };

    /// <summary>The cycle fraction (0..1) at which leg <paramref name="leg"/> (0 front-left, 1 front-right, 2 hind-left, 3 hind-right) is at the phase's zero.</summary>
    public static float Offset(QuadGait g, int leg) => g switch
    {
        // walk: the lateral sequence — left hind, left fore, right hind, right fore, a quarter cycle apart
        QuadGait.Walk => leg switch { 0 => 0.25f, 1 => 0.75f, 2 => 0f, _ => 0.5f },
        // trot: the diagonals move together — left fore with right hind, right fore with left hind
        QuadGait.Trot => leg switch { 0 => 0f, 1 => 0.5f, 2 => 0.5f, _ => 0f },
        // canter: three beats — right hind, then left hind with right fore, then left fore (a left lead)
        QuadGait.Canter => leg switch { 0 => 0.66f, 1 => 0.33f, 2 => 0.33f, _ => 0f },
        // gallop: the hind pair, then the fore pair — a pair each, the second of each a hair behind the first
        QuadGait.Gallop => leg switch { 0 => 0.62f, 1 => 0.50f, 2 => 0f, _ => 0.12f },
        // hop: the fore pair together, the hind pair together a little behind (a bound)
        QuadGait.Hop => leg < 2 ? 0f : 0.15f,
        _ => 0f,
    };

    /// <summary>How far a leg is folded up in a jump (radians, + = the foot forward): the fore legs tuck forward and up, the hind ones trail behind.</summary>
    public static float TuckAngle(int leg) => leg < 2 ? 1.05f : -0.75f;

    /// <summary>⭐ The angle (radians, + = the foot toward +x) leg <paramref name="leg"/> is drawn at: its swing in the gait's cycle, folded toward its tuck by <see cref="MotionPose.Tuck"/>.</summary>
    public static float LegAngle(in MotionPose m, int leg)
    {
        float a = m.Swing * MathF.Sin(m.GaitPhase + TwoPi * Offset(m.Gait, leg));
        return m.Tuck > 0f ? a + (TuckAngle(leg) - a) * m.Tuck : a;
    }
}

/// <summary>⭐ CE-4623 (AU-3) — the animals' pose rules that are not the legs: the head by mode, a wing's beat, a hen's peck. Pure functions of (mode, speed, sim time, a per-animal seed).</summary>
public static class AnimalPoses
{
    private static float Frac(double x) => (float)(x - Math.Floor(x));
    private static float Smooth(float x) { x = Math.Clamp(x, 0f, 1f); return x * x * (3f - 2f * x); }

    /// <summary>The neck's pitch (radians, + = nose down) that brings a grazing muzzle to the ground.</summary>
    public static float GrazePitch(VisualFamily f) => f switch
    {
        VisualFamily.Horse => 1.7f, VisualFamily.Cattle => 1.05f, VisualFamily.Sheep => 1.0f, VisualFamily.Goat => 1.0f, VisualFamily.Pig => 0.55f, VisualFamily.Dog => 0.45f, _ => 0f,
    };

    /// <summary>Does the species lower its head to feed (graze / root) when calm and standing?</summary>
    public static bool Grazes(VisualFamily f) => f is VisualFamily.Horse or VisualFamily.Cattle or VisualFamily.Sheep or VisualFamily.Goat or VisualFamily.Pig;

    /// <summary>The head's pitch of a quadruped: grazing (down, in a seeded cycle of ~9–15 s of sim time) when calm and standing; low while it walks calm; raised alert; level in flight.</summary>
    public static float HeadPitch(VisualFamily f, AnimalMode mode, float speed, double simTime, uint seed)
    {
        switch (mode)
        {
            case AnimalMode.Alert: return -0.30f;
            case AnimalMode.Flee: return f == VisualFamily.Horse ? -0.25f : -0.05f;
            case AnimalMode.Guard: return -0.10f;
            case AnimalMode.Rest: return -0.10f;
        }
        if (!Grazes(f)) return 0f;
        if (speed > QuadrupedGaits.StandBelow) return GrazePitch(f) * 0.15f;
        float period = 9f + (seed % 7u);
        float u = Frac(simTime / period + (seed % 97u) / 97.0);
        float down = Smooth(u / 0.06f) * (1f - Smooth((u - 0.74f) / 0.06f));   // down for ~70 % of the cycle, eased
        return -0.10f + (GrazePitch(f) + 0.10f) * down;
    }

    /// <summary>A hen's head: it pecks — pulses down to the ground ~1.4 times a second, with pauses.</summary>
    public static float Peck(double simTime, uint seed)
    {
        float u = Frac(simTime * 0.7 + (seed % 53u) / 53.0);
        float pulse = MathF.Max(0f, MathF.Sin(u * MathF.PI * 2f * 2f));
        return 1.15f * MathF.Pow(pulse, 4f) * (u < 0.65f ? 1f : 0f);
    }

    /// <summary>A flying bird's wing angle (radians, + = the tip up): a beat of ~6.5 Hz with a glide now and then.</summary>
    public static float Flap(double simTime, uint seed)
    {
        float phase = (seed % 31u) / 31f;
        bool glide = MathF.Sin((float)(simTime * 0.6 * MathF.PI * 2f) + phase * 6f) > 0.85f;
        return glide ? 0.12f : 0.75f * MathF.Sin((float)(simTime * 6.5 * MathF.PI * 2.0) + phase * MathF.PI * 2f) + 0.1f;
    }

    /// <summary>A hen's flutter: the wings opened and beating fast.</summary>
    public static float Flutter(double simTime, uint seed)
        => 0.65f + 0.45f * MathF.Sin((float)(simTime * 14.0 * MathF.PI * 2.0) + (seed % 11u));

    /// <summary>A folded wing (a bird at rest).</summary>
    public const float WingFolded = 0f;
}

/// <summary>
/// ⭐ CE-4623 (AU-4, AR-14) — the FLOCK in closed form: one entity (<c>FlockState</c>: Count, Seed, Mode) drawn as N birds, each bird a pure function of (seed, its index, sim time, the mode) in the
/// flock's frame — never integrated, so the same birds fly after a seek, a replay jump or on a node that joined late. A bird's index is its identity: when the count falls the
/// HIGHEST indices go and every other bird stays exactly where it was.
/// </summary>
public static class FlockBirds
{
    /// <summary>The most birds drawn from one flock (a count above it draws this many).</summary>
    public const int MaxDrawn = 400;

    private static uint H(uint seed, int i, uint salt)
    {
        uint x = seed * 0x9E3779B1u ^ (uint)i * 0x85EBCA6Bu ^ salt * 0xC2B2AE35u;
        x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
        return x;
    }

    private static float U(uint seed, int i, uint salt) => (H(seed, i, salt) & 0xFFFFFF) / 16777216f;

    /// <summary>The radius (m) of a flock of <paramref name="count"/> in the air.</summary>
    public static float Radius(int count) => 2.5f + 0.5f * MathF.Sqrt(MathF.Max(count, 1));

    private static float Sin(double cycles) => MathF.Sin((float)(cycles % 1.0 * Math.PI * 2.0));

    /// <summary>Bird <paramref name="i"/>'s place (m) in the flock's frame (x forward, y left, z up).</summary>
    public static Vector3 Offset(uint seed, int i, double t, FlockMode mode, int count)
    {
        float r = Radius(count);
        switch (mode)
        {
            case FlockMode.Landed:   // spread on the ground, pecking
            {
                float rad = r * 0.7f * MathF.Sqrt(U(seed, i, 1)), ang = U(seed, i, 2) * MathF.PI * 2f;
                return new Vector3(rad * MathF.Cos(ang), rad * MathF.Sin(ang), 0f);
            }
            case FlockMode.Perched:   // clustered in a crown
            {
                float rad = 1.6f * MathF.Sqrt(U(seed, i, 1)), ang = U(seed, i, 2) * MathF.PI * 2f;
                return new Vector3(rad * MathF.Cos(ang), rad * MathF.Sin(ang), U(seed, i, 3) * 1.5f);
            }
            default:   // a loose cloud, each bird drifting on its own slow cycles
                return new Vector3((U(seed, i, 1) - 0.5f) * r * 2.2f + Sin(t * 0.11 + U(seed, i, 4)) * r * 0.25f,
                                   (U(seed, i, 2) - 0.5f) * r * 2f + Sin(t * 0.17 + U(seed, i, 5)) * r * 0.2f,
                                   (U(seed, i, 3) - 0.5f) * r * 0.5f + Sin(t * 0.23 + U(seed, i, 6)) * 0.6f);
        }
    }

    /// <summary>Bird <paramref name="i"/>'s heading (radians) off the flock's: a few degrees either way in the air, any direction on the ground or a branch.</summary>
    public static float Yaw(uint seed, int i, double t, FlockMode mode)
        => mode == FlockMode.Flying ? 0.35f * Sin(t * 0.3 + U(seed, i, 7)) : U(seed, i, 8) * MathF.PI * 2f;

    /// <summary>Bird <paramref name="i"/>'s wing angle: its own beat in the air, folded at rest.</summary>
    public static float Wing(uint seed, int i, double t, FlockMode mode)
        => mode == FlockMode.Flying ? AnimalPoses.Flap(t, (uint)(H(seed, i, 9) % 1000u)) : 0f;
}
