using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Animals;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

// ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════
// ⭐ CE-4623 (docs/designs/animals/DESIGN_Animals.md §6.3 AU-1…AU-5; mockup animals-visuals.svg) — the ANIMALS' bodies: nine shape kits, the gaits, the flock's N birds, the coats.
// Every number is metres (x forward, y left, z up from the ground), turned into the fractions a <see cref="KitPart"/> carries by <see cref="MetreKit"/>, which also measures the
// kit's box — so a kit and its default size cannot disagree (rail), and the mockup (scripts/gen-animals-mockup.py) draws the SAME builder's numbers.
// ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════

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
public enum QuadGait : byte { Stand = 0, Walk, Trot, Canter, Gallop }

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
    public static float Amplitude(QuadGait g) => g switch { QuadGait.Walk => 0.40f, QuadGait.Trot => 0.55f, QuadGait.Canter => 0.70f, QuadGait.Gallop => 0.95f, _ => 0f };

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

/// <summary>⭐ CE-4623 — the coat an animal wears: a palette per species, picked by its seed (the entity's network id — the same on every host).</summary>
public static class AnimalCoats
{
    private static readonly Color[] Cattle = [new(140, 92, 56, 255), new(58, 54, 52, 255), new(182, 140, 96, 255), new(226, 222, 214, 255), new(96, 64, 42, 255)];
    private static readonly Color[] Sheep = [new(228, 224, 212, 255), new(214, 206, 184, 255), new(160, 150, 134, 255)];
    private static readonly Color[] Horse = [new(130, 82, 48, 255), new(150, 86, 44, 255), new(48, 44, 42, 255), new(160, 160, 158, 255), new(200, 164, 102, 255)];
    private static readonly Color[] Dog = [new(176, 128, 76, 255), new(52, 48, 46, 255), new(120, 84, 56, 255), new(204, 194, 170, 255)];
    private static readonly Color[] Cat = [new(200, 130, 60, 255), new(130, 130, 134, 255), new(40, 40, 44, 255), new(222, 220, 214, 255)];
    private static readonly Color[] Pig = [new(226, 170, 160, 255), new(214, 156, 148, 255), new(150, 112, 102, 255)];
    private static readonly Color[] Goat = [new(226, 222, 212, 255), new(140, 104, 72, 255), new(150, 148, 144, 255)];
    private static readonly Color[] Hen = [new(150, 90, 52, 255), new(226, 222, 212, 255), new(60, 52, 48, 255), new(176, 120, 64, 255)];
    private static readonly Color[] Bird = [new(60, 56, 52, 255), new(110, 84, 60, 255), new(130, 134, 140, 255), new(78, 70, 64, 255)];

    /// <summary>The palette of <paramref name="f"/>.</summary>
    public static Color[] Palette(VisualFamily f) => f switch
    {
        VisualFamily.Cattle => Cattle, VisualFamily.Sheep => Sheep, VisualFamily.Horse => Horse, VisualFamily.Dog => Dog, VisualFamily.Cat => Cat,
        VisualFamily.Pig => Pig, VisualFamily.Goat => Goat, VisualFamily.Poultry => Hen, _ => Bird,
    };

    /// <summary>The coat of an animal of <paramref name="f"/> with <paramref name="seed"/>.</summary>
    public static Color Of(VisualFamily f, long seed)
    {
        var p = Palette(f);
        uint h = (uint)(seed * 2654435761L) ^ (uint)(seed >> 32);
        return p[(int)((h >> 11) % (uint)p.Length)];
    }
}

/// <summary>A metre-built kit: parts are added in METRES (x forward, y left, z up from the ground) and turned into the fractions of a measured box — centred along x, the ground at z 0.</summary>
internal sealed class MetreKit
{
    private readonly record struct P(Vector3 C, Vector3 E, ColourRole Col, float Pitch, float Yaw, PartRole Role, Vector3? Pivot, Vector3? Pivot2);
    private readonly List<P> _p = new();

    public void Add(float cx, float cy, float cz, float lx, float ly, float lz, ColourRole col, float pitch = 0f, float yaw = 0f,
                    PartRole role = PartRole.Hull, Vector3? pivot = null, Vector3? pivot2 = null)
        => _p.Add(new P(new(cx, cy, cz), new(lx, ly, lz), col, pitch, yaw, role, pivot, pivot2));

    public ShapeKit Build(out Vector3 size, float minWidth = 0f) => Build(out size, out _, minWidth);

    /// <summary>Builds against a MEASURED box: <paramref name="sizeOverride"/> / <paramref name="shiftOverride"/> pin the fractions to another kit's box (the lying animal is drawn in the standing animal's box).</summary>
    public ShapeKit Build(out Vector3 size, out float shiftX, float minWidth = 0f, Vector3? sizeOverride = null, float? shiftOverride = null)
    {
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        foreach (var p in _p)
        {
            var rot = Matrix4x4.CreateRotationY(p.Pitch * MathF.PI / 180f) * Matrix4x4.CreateRotationZ(p.Yaw * MathF.PI / 180f);
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3(((i & 1) - 0.5f) * p.E.X, (((i >> 1) & 1) - 0.5f) * p.E.Y, (((i >> 2) & 1) - 0.5f) * p.E.Z);
                var w = Vector3.Transform(c, rot) + p.C;
                min = Vector3.Min(min, w); max = Vector3.Max(max, w);
            }
        }
        shiftX = shiftOverride ?? (min.X + max.X) / 2f;
        float width = MathF.Max(MathF.Max(max.Y, -min.Y) * 2f, minWidth);
        var sz = sizeOverride ?? new Vector3(max.X - min.X, width, max.Z);
        size = sz;
        float sx = shiftX;
        var parts = new KitPart[_p.Count];
        for (int i = 0; i < parts.Length; i++)
        {
            var p = _p[i];
            Vector3 F(Vector3 m) => new((m.X - sx) / sz.X, m.Y / sz.Y, m.Z / sz.Z);
            parts[i] = new KitPart(PartShape.Box, F(p.C), p.E / sz, p.Col, PartAxis.Z, p.Pitch, p.Yaw, p.Role, SwingPivot: p.Pivot is { } a ? F(a) : null,
                                   KneePivot: p.Pivot2 is { } b ? F(b) : null);
        }
        return new ShapeKit(parts, default);
    }
}

/// <summary>⭐ CE-4623 — the horns of a species.</summary>
internal enum HornKind : byte { None, Short, Back }

/// <summary>⭐ CE-4623 — one four-legged species, in metres: the torso box (<c>X0</c> rump … <c>X1</c> chest, <c>Z0</c> its belly, <c>BodyH</c> its depth), the neck (a rise in degrees, a length), the head (<c>HeadDown</c> degrees
/// below level, a snout), the legs and the tail (<c>TailDeg</c> from hanging down toward the rear: 0 down, 90 level, 150 up).</summary>
internal readonly record struct QuadSpec(
    float X0, float X1, float BodyW, float Z0, float BodyH,
    float NeckUp, float NeckL, float NeckW, float NeckT,
    float HeadDown, float HeadL, float HeadW, float HeadH, float SnoutL, float SnoutW, ColourRole SnoutCol,
    float LegT, float FrontX, float HindX, float LegY, float HoofH,
    float TailL, float TailDeg, ColourRole TailCol,
    ColourRole TorsoCol = ColourRole.Body, ColourRole HeadCol = ColourRole.Body, ColourRole LegCol = ColourRole.Body,
    HornKind Horns = HornKind.None, bool Udder = false, bool Mane = false, bool Beard = false, bool FloppyEars = false, bool UprightEars = false, bool CurlyTail = false);

/// <summary>⭐ CE-4623 (AU-2) — the animals' shape kits and their sizes: <see cref="For"/> / <see cref="SizeOf"/> by family; the lying quadrupeds, the perched bird, the hen.</summary>
public static class AnimalKits
{
    // ── the species table (metres) — the mockup's numbers (scripts/gen-animals-mockup.py SPECIES) ───────────────────────────────────────────────────────────────────────
    internal static readonly QuadSpec CattleSpec = new(-1.15f, 0.55f, 0.70f, 0.60f, 0.72f, 5f, 0.55f, 0.40f, 0.42f, 40f, 0.52f, 0.30f, 0.30f, 0.12f, 0.26f, ColourRole.Skin,
                                                       0.15f, 0.35f, -0.85f, 0.24f, 0.08f, 0.80f, 0f, ColourRole.Dark, Horns: HornKind.Short, Udder: true, UprightEars: false, FloppyEars: true);
    internal static readonly QuadSpec HorseSpec = new(-0.85f, 0.55f, 0.55f, 0.85f, 0.62f, 50f, 0.65f, 0.22f, 0.30f, 50f, 0.55f, 0.18f, 0.22f, 0.10f, 0.16f, ColourRole.Dark,
                                                      0.10f, 0.38f, -0.65f, 0.18f, 0.07f, 0.70f, 12f, ColourRole.Dark, Mane: true, UprightEars: true, LegCol: ColourRole.Body);
    internal static readonly QuadSpec SheepSpec = new(-0.45f, 0.30f, 0.42f, 0.32f, 0.42f, 30f, 0.20f, 0.16f, 0.18f, 45f, 0.22f, 0.14f, 0.16f, 0.05f, 0.11f, ColourRole.Dark,
                                                      0.06f, 0.18f, -0.32f, 0.14f, 0.05f, 0.12f, 20f, ColourRole.Light, TorsoCol: ColourRole.Light, HeadCol: ColourRole.Dark, LegCol: ColourRole.Dark, FloppyEars: true);
    internal static readonly QuadSpec DogSpec = new(-0.40f, 0.25f, 0.20f, 0.28f, 0.24f, 40f, 0.16f, 0.12f, 0.14f, 15f, 0.20f, 0.11f, 0.12f, 0.10f, 0.08f, ColourRole.Dark,
                                                    0.06f, 0.18f, -0.30f, 0.08f, 0.04f, 0.35f, 140f, ColourRole.Body, FloppyEars: true);
    internal static readonly QuadSpec CatSpec = new(-0.22f, 0.14f, 0.14f, 0.14f, 0.15f, 45f, 0.08f, 0.09f, 0.09f, 15f, 0.12f, 0.10f, 0.09f, 0.03f, 0.05f, ColourRole.Skin,
                                                    0.035f, 0.10f, -0.17f, 0.045f, 0.02f, 0.34f, 160f, ColourRole.Body, UprightEars: true);
    internal static readonly QuadSpec PigSpec = new(-0.55f, 0.45f, 0.50f, 0.22f, 0.45f, 0f, 0.15f, 0.30f, 0.30f, 15f, 0.35f, 0.25f, 0.26f, 0.10f, 0.14f, ColourRole.Skin,
                                                    0.09f, 0.28f, -0.35f, 0.17f, 0.05f, 0.12f, 120f, ColourRole.Body, FloppyEars: true, CurlyTail: true);
    internal static readonly QuadSpec GoatSpec = new(-0.45f, 0.32f, 0.32f, 0.45f, 0.38f, 45f, 0.30f, 0.14f, 0.16f, 35f, 0.24f, 0.12f, 0.16f, 0.06f, 0.09f, ColourRole.Dark,
                                                     0.06f, 0.22f, -0.30f, 0.12f, 0.05f, 0.12f, 150f, ColourRole.Body, Horns: HornKind.Back, Beard: true, LegCol: ColourRole.Dark, UprightEars: true);

    internal static QuadSpec SpecOf(VisualFamily f) => f switch
    {
        VisualFamily.Cattle => CattleSpec, VisualFamily.Horse => HorseSpec, VisualFamily.Sheep => SheepSpec, VisualFamily.Dog => DogSpec,
        VisualFamily.Cat => CatSpec, VisualFamily.Pig => PigSpec, _ => GoatSpec,
    };

    /// <summary>The withers (the point the neck pitches about) of a spec, in metres.</summary>
    internal static Vector3 Withers(in QuadSpec s) => new(s.X1 - 0.08f, 0f, s.Z0 + s.BodyH * 0.85f);

    /// <summary>The standing quadruped of a species; <paramref name="lying"/> the same animal lying down (belly on the ground, legs folded under).</summary>
    internal static ShapeKit BuildQuad(in QuadSpec s0, bool lying, out Vector3 size, out float shiftX, Vector3? sizeOverride = null, float? shiftOverride = null)
    {
        var s = s0;
        if (lying) s = s with { Z0 = s.LegT * 0.7f, NeckUp = s.NeckUp + 25f };
        var k = new MetreKit();
        // the torso (a sheep's is wool: the lighter shade), a paler belly strip under it
        float tcx = (s.X0 + s.X1) / 2f;
        k.Add(tcx, 0f, s.Z0 + s.BodyH / 2f, s.X1 - s.X0, s.BodyW, s.BodyH, s.TorsoCol);
        var w = Withers(s);
        // the neck: from the withers forward and up by NeckUp; it, the mane and the head pitch about the withers (grazing, alert)
        float up = s.NeckUp * MathF.PI / 180f;
        var d = new Vector3(MathF.Cos(up), 0f, MathF.Sin(up));
        var nc = w + d * s.NeckL / 2f;
        k.Add(nc.X, 0f, nc.Z, s.NeckL, s.NeckW, s.NeckT, s.HeadCol == ColourRole.Dark ? ColourRole.Light : ColourRole.Body, pitch: -s.NeckUp, role: PartRole.AnimalHead, pivot: w);
        if (s.Mane) k.Add(nc.X, 0f, nc.Z + s.NeckT / 2f + 0.03f, s.NeckL, 0.05f, 0.09f, ColourRole.Dark, pitch: -s.NeckUp, role: PartRole.AnimalHead, pivot: w);
        var n = w + d * s.NeckL;   // the neck's end: the head turns about it too (AnimalSkull — its own pivot)
        float hd = s.HeadDown * MathF.PI / 180f;
        var h = new Vector3(MathF.Cos(hd), 0f, -MathF.Sin(hd));
        var skull = PartRole.AnimalHead;   // the head pitches with the neck; the head itself tilts back by KneePivot (see BodyTransform's AnimalHead case)
        var hc = n + h * s.HeadL / 2f + new Vector3(0f, 0f, -s.HeadH * 0.15f);
        k.Add(hc.X, 0f, hc.Z, s.HeadL, s.HeadW, s.HeadH, s.HeadCol, pitch: s.HeadDown, role: skull, pivot: w, pivot2: n);
        var sc = n + h * (s.HeadL + s.SnoutL / 2f - 0.01f) + new Vector3(0f, 0f, -s.HeadH * 0.2f);
        k.Add(sc.X, 0f, sc.Z, s.SnoutL, s.SnoutW, s.HeadH * 0.7f, s.SnoutCol, pitch: s.HeadDown, role: skull, pivot: w, pivot2: n);
        var ears = n + h * (s.HeadL * 0.12f) + new Vector3(0f, 0f, s.HeadH * 0.35f);
        foreach (float sy in new[] { 1f, -1f })
        {
            if (s.UprightEars) k.Add(ears.X, sy * (s.HeadW / 2f - 0.02f), ears.Z + 0.05f, 0.035f, 0.04f, 0.10f, s.HeadCol, role: skull, pivot: w, pivot2: n);
            else if (s.FloppyEars) k.Add(ears.X - 0.02f, sy * (s.HeadW / 2f + 0.05f), ears.Z, 0.10f, 0.07f, 0.035f, s.HeadCol == ColourRole.Dark ? ColourRole.Dark : ColourRole.Body, pitch: 25f, role: skull, pivot: w, pivot2: n);
            if (s.Horns == HornKind.Short) k.Add(ears.X + 0.02f, sy * (s.HeadW / 2f - 0.01f), ears.Z + 0.08f, 0.04f, 0.04f, 0.14f, ColourRole.Bone, role: skull, pivot: w, pivot2: n);
            else if (s.Horns == HornKind.Back) k.Add(ears.X - 0.04f, sy * (s.HeadW / 2f - 0.02f), ears.Z + 0.09f, 0.035f, 0.035f, 0.20f, ColourRole.Bone, pitch: -40f, role: skull, pivot: w, pivot2: n);
        }
        if (s.Beard) k.Add(n.X + h.X * s.HeadL * 0.95f, 0f, n.Z + h.Z * s.HeadL * 0.95f - 0.10f, 0.04f, 0.04f, 0.12f, ColourRole.Light, role: skull, pivot: w, pivot2: n);
        // the legs: a box and a hoof, both swinging about the hip (QuadFrontLeft, QuadFrontRight, QuadHindLeft, QuadHindRight in that order)
        float hipZ = s.Z0 + 0.03f;
        float[] xs = [s.FrontX, s.FrontX, s.HindX, s.HindX];
        float[] ys = [s.LegY, -s.LegY, s.LegY, -s.LegY];
        for (int i = 0; i < 4; i++)
        {
            if (lying)
            {
                // folded under the animal: a short thigh-and-shin lying along its flank, no swing
                k.Add(xs[i] + (i < 2 ? 0.12f : -0.02f), ys[i] * 0.85f, s.LegT * 0.5f, 0.34f, s.LegT, s.LegT, s.LegCol);
                continue;
            }
            var role = (PartRole)((int)PartRole.QuadFrontLeft + i);
            var pivot = new Vector3(xs[i], ys[i], hipZ);
            float len = hipZ - s.HoofH;
            k.Add(xs[i], ys[i], s.HoofH + len / 2f, s.LegT, s.LegT, len, s.LegCol, role: role, pivot: pivot);
            k.Add(xs[i], ys[i], s.HoofH / 2f, s.LegT * 1.1f, s.LegT * 1.1f, s.HoofH, ColourRole.Dark, role: role, pivot: pivot);
        }
        // the tail: hanging by TailDeg from straight down toward the rear
        var root = new Vector3(s.X0, 0f, s.Z0 + s.BodyH * 0.85f);
        float th = s.TailDeg * MathF.PI / 180f;
        var td = new Vector3(-MathF.Sin(th), 0f, -MathF.Cos(th));
        var tc = root + td * s.TailL / 2f;
        k.Add(tc.X, 0f, tc.Z, 0.05f, 0.05f, s.TailL, s.TailCol, pitch: s.TailDeg);
        if (s.CurlyTail) k.Add(root.X - 0.07f, 0f, root.Z + 0.08f, 0.07f, 0.05f, 0.05f, s.TailCol);
        if (s.Udder && !lying) k.Add(s.X0 + 0.55f, 0f, s.Z0 - 0.03f, 0.30f, 0.22f, 0.12f, ColourRole.Skin);
        return k.Build(out size, out shiftX, 0f, sizeOverride, shiftOverride);
    }

    // ── the birds ────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A bird (flying or perched): body, head, beak, tail, feet, and the wings — spread (<see cref="PartRole.WingLeft"/> / <see cref="PartRole.WingRight"/>, flapping) or folded to its sides.</summary>
    internal static ShapeKit BuildBird(bool flying, out Vector3 size, Vector3? sizeOverride = null)
    {
        var k = new MetreKit();
        k.Add(0f, 0f, 0.085f, 0.22f, 0.08f, 0.08f, ColourRole.Body);                        // the body
        k.Add(0.13f, 0f, 0.105f, 0.07f, 0.06f, 0.06f, ColourRole.Body);                     // the head
        k.Add(0.18f, 0f, 0.10f, 0.045f, 0.02f, 0.02f, ColourRole.Amber);                    // the beak
        k.Add(-0.15f, 0f, 0.09f, 0.11f, 0.05f, 0.015f, ColourRole.Dark, pitch: 8f);         // the tail
        foreach (float sy in new[] { 1f, -1f })
        {
            k.Add(0f, sy * 0.02f, 0.025f, 0.012f, 0.012f, 0.05f, ColourRole.Amber);          // a foot
            if (flying)
            {
                var root = new Vector3(0.02f, sy * 0.04f, 0.095f);
                k.Add(0.02f, sy * (0.04f + 0.13f), 0.095f, 0.10f, 0.26f, 0.012f, ColourRole.Body, role: sy > 0 ? PartRole.WingLeft : PartRole.WingRight, pivot: root);
            }
            else k.Add(-0.01f, sy * 0.045f, 0.09f, 0.17f, 0.012f, 0.055f, ColourRole.Dark);   // a wing folded along the flank
        }
        return k.Build(out size, out _, 0.56f, sizeOverride, sizeOverride != null ? 0f : null);
    }

    /// <summary>A hen: a plump body on two yellow legs (<see cref="PartRole.LegLeft"/> / <see cref="PartRole.LegRight"/> — they step like a man's), a fanned tail, a red comb, a beak, and wings that hang at the sides and open in a flutter.</summary>
    internal static ShapeKit BuildHen(out Vector3 size)
    {
        var k = new MetreKit();
        k.Add(0f, 0f, 0.24f, 0.30f, 0.17f, 0.19f, ColourRole.Body);                         // the body
        k.Add(0.07f, 0f, 0.20f, 0.14f, 0.15f, 0.09f, ColourRole.Light);                     // the breast
        k.Add(-0.17f, 0f, 0.33f, 0.05f, 0.14f, 0.20f, ColourRole.Dark, pitch: -25f);        // the tail fan
        var neckBase = new Vector3(0.12f, 0f, 0.30f);
        k.Add(0.13f, 0f, 0.36f, 0.07f, 0.07f, 0.14f, ColourRole.Body, role: PartRole.AnimalHead, pivot: neckBase);                 // the neck
        k.Add(0.16f, 0f, 0.44f, 0.09f, 0.06f, 0.07f, ColourRole.Body, role: PartRole.AnimalHead, pivot: neckBase);                 // the head
        k.Add(0.215f, 0f, 0.43f, 0.04f, 0.025f, 0.025f, ColourRole.Amber, role: PartRole.AnimalHead, pivot: neckBase);             // the beak
        k.Add(0.16f, 0f, 0.485f, 0.07f, 0.02f, 0.04f, ColourRole.Red, role: PartRole.AnimalHead, pivot: neckBase);                 // the comb
        k.Add(0.20f, 0f, 0.395f, 0.015f, 0.02f, 0.04f, ColourRole.Red, role: PartRole.AnimalHead, pivot: neckBase);                // the wattle
        foreach (float sy in new[] { 1f, -1f })
        {
            var hip = new Vector3(0f, sy * 0.05f, 0.15f);
            k.Add(0f, sy * 0.05f, 0.075f, 0.025f, 0.025f, 0.15f, ColourRole.Amber, role: sy > 0 ? PartRole.LegLeft : PartRole.LegRight, pivot: hip);
            var root = new Vector3(0.02f, sy * 0.092f, 0.32f);
            k.Add(0.0f, sy * 0.095f, 0.26f, 0.20f, 0.02f, 0.12f, ColourRole.Dark, role: sy > 0 ? PartRole.WingLeft : PartRole.WingRight, pivot: root);
        }
        return k.Build(out size);
    }

    // ── the table ───────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly Dictionary<VisualFamily, (ShapeKit Standing, ShapeKit Lying, Vector3 Size)> _quads = new();
    private static readonly (ShapeKit Kit, Vector3 Size) _hen, _flying, _perched;

    static AnimalKits()
    {
        foreach (var f in new[] { VisualFamily.Cattle, VisualFamily.Sheep, VisualFamily.Horse, VisualFamily.Dog, VisualFamily.Cat, VisualFamily.Pig, VisualFamily.Goat })
        {
            var spec = SpecOf(f);
            var standing = BuildQuad(spec, false, out var size, out var shift);   // the box is the STANDING animal's, measured from its parts — never typed
            var lying = BuildQuad(spec, true, out _, out _, size, shift);          // the lying animal is drawn in the same box
            _quads[f] = (standing, lying, size);
        }
        var hen = BuildHen(out var henSize); _hen = (hen, henSize);
        var fl = BuildBird(true, out var flSize); _flying = (fl, flSize);
        var pe = BuildBird(false, out var peSize, flSize); _perched = (pe, peSize);
    }

    /// <summary>The kit of <paramref name="f"/> (the standing quadruped, the hen, the flying bird).</summary>
    public static ShapeKit For(VisualFamily f) => f switch
    {
        VisualFamily.Poultry => _hen.Kit,
        VisualFamily.Bird => _flying.Kit,
        _ => _quads[f].Standing,
    };

    /// <summary>The kit of <paramref name="f"/> in <paramref name="mode"/>: a resting quadruped lies down, a landed or perched bird folds its wings.</summary>
    public static ShapeKit For(VisualFamily f, AnimalMode mode, bool perched = false)
        => f == VisualFamily.Bird ? (perched ? _perched.Kit : _flying.Kit)
         : mode == AnimalMode.Rest && VisualFamilies.IsQuadruped(f) ? _quads[f].Lying
         : For(f);

    /// <summary>The kit's own box (m) — what the TKB's size replaces when it states one.</summary>
    public static Vector3 SizeOf(VisualFamily f) => f switch
    {
        VisualFamily.Poultry => _hen.Size,
        VisualFamily.Bird => _flying.Size,
        _ => _quads[f].Size,
    };

    /// <summary>A perched bird's box.</summary>
    public static Vector3 PerchedSize => _perched.Size;

    /// <summary>A family's default colour (the coat palette's first entry — the TKB's own, or a seeded coat, wins).</summary>
    public static Color DefaultColour(VisualFamily f) => AnimalCoats.Palette(f)[0];
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
