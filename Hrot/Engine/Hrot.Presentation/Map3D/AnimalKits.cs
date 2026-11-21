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

/// <summary>⭐ CE-4623 — the coat an animal wears: a palette per species, picked by its seed (the entity's network id — the same on every host).</summary>
public static class AnimalCoats
{
    private static readonly Color[] Cattle = [new(140, 92, 56, 255), new(58, 54, 52, 255), new(182, 140, 96, 255), new(226, 222, 214, 255), new(96, 64, 42, 255)];
    private static readonly Color[] Sheep = [new(228, 224, 212, 255), new(214, 206, 184, 255), new(160, 150, 134, 255)];
    private static readonly Color[] Horse = [new(130, 82, 48, 255), new(150, 86, 44, 255), new(48, 44, 42, 255), new(160, 160, 158, 255), new(200, 164, 102, 255)];
    private static readonly Color[] Dog = [new(176, 128, 76, 255), new(52, 48, 46, 255), new(120, 84, 56, 255), new(204, 194, 170, 255)];
    private static readonly Color[] Cat = [new(200, 130, 60, 255), new(130, 130, 134, 255), new(40, 40, 44, 255), new(222, 220, 214, 255)];
    private static readonly Color[] Rabbit = [new(130, 114, 96, 255), new(120, 84, 56, 255), new(190, 160, 112, 255)];   // grey-brown, brown, sandy
    private static readonly Color[] Pig = [new(226, 170, 160, 255), new(214, 156, 148, 255), new(150, 112, 102, 255)];
    private static readonly Color[] Goat = [new(226, 222, 212, 255), new(140, 104, 72, 255), new(150, 148, 144, 255)];
    private static readonly Color[] Hen = [new(150, 90, 52, 255), new(226, 222, 212, 255), new(60, 52, 48, 255), new(176, 120, 64, 255)];
    private static readonly Color[] Bird = [new(60, 56, 52, 255), new(110, 84, 60, 255), new(130, 134, 140, 255), new(78, 70, 64, 255)];

    /// <summary>The palette of <paramref name="f"/>.</summary>
    public static Color[] Palette(VisualFamily f) => f switch
    {
        VisualFamily.Cattle => Cattle, VisualFamily.Sheep => Sheep, VisualFamily.Horse => Horse, VisualFamily.Dog => Dog, VisualFamily.Cat => Cat,
        VisualFamily.Pig => Pig, VisualFamily.Goat => Goat, VisualFamily.Rabbit => Rabbit, VisualFamily.Poultry => Hen, _ => Bird,
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
    public ShapeKit Build(out Vector3 size, out float shiftX, float minWidth = 0f, Vector3? sizeOverride = null, float? shiftOverride = null, float originAhead = 0f)
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
        shiftX = shiftOverride ?? ((min.X + max.X) / 2f - originAhead);   // originAhead: the kit is drawn this far AHEAD of its box's centre (a horse's saddle sits where its TKB seat says)
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
    HornKind Horns = HornKind.None, bool Udder = false, bool Mane = false, bool Beard = false, bool FloppyEars = false, bool UprightEars = false, bool CurlyTail = false, bool Rabbit = false);

/// <summary>⭐ CE-4623 (AU-2) — the animals' shape kits and their sizes: <see cref="For"/> / <see cref="SizeOf"/> by family; the lying quadrupeds, the perched bird, the hen.</summary>
public static class AnimalKits
{
    // ── the species table (metres) — the mockup's numbers (scripts/gen-animals-mockup.py SPECIES) ───────────────────────────────────────────────────────────────────────
    internal static readonly QuadSpec CattleSpec = new(-1.15f, 0.55f, 0.70f, 0.60f, 0.72f, 5f, 0.55f, 0.40f, 0.42f, 40f, 0.52f, 0.30f, 0.30f, 0.12f, 0.26f, ColourRole.Skin,
                                                       0.15f, 0.35f, -0.85f, 0.24f, 0.08f, 0.80f, 0f, ColourRole.Dark, Horns: HornKind.Short, Udder: true, UprightEars: false, FloppyEars: true);
    internal static readonly QuadSpec HorseSpec = new(-0.85f, 0.55f, 0.55f, 0.70f, 0.61f, 50f, 0.65f, 0.22f, 0.30f, 50f, 0.55f, 0.18f, 0.22f, 0.10f, 0.16f, ColourRole.Dark,
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

    // ⭐ AN-21e — the rabbit: ~0.40 m long, ~0.15 m wide, ~0.3 m tall to the ear tips; a round body, the haunches higher, big hind legs, long upright ears, a white puff of a tail (docs/designs/animals/DESIGN_Animals.md §4c)
    internal static readonly QuadSpec RabbitSpec = new(-0.19f, 0.12f, 0.14f, 0.045f, 0.10f, 20f, 0.04f, 0.08f, 0.08f, 30f, 0.07f, 0.075f, 0.075f, 0.025f, 0.045f, ColourRole.Light,
                                                       0.03f, 0.08f, -0.09f, 0.055f, 0.012f, 0.02f, 110f, ColourRole.Bone, LegCol: ColourRole.Body, Rabbit: true);

    internal static QuadSpec SpecOf(VisualFamily f) => f switch
    {
        VisualFamily.Cattle => CattleSpec, VisualFamily.Horse => HorseSpec, VisualFamily.Sheep => SheepSpec, VisualFamily.Dog => DogSpec,
        VisualFamily.Cat => CatSpec, VisualFamily.Pig => PigSpec, VisualFamily.Rabbit => RabbitSpec, _ => GoatSpec,
    };

    /// <summary>The withers (the point the neck pitches about) of a spec, in metres.</summary>
    internal static Vector3 Withers(in QuadSpec s) => new(s.X1 - 0.08f, 0f, s.Z0 + s.BodyH * 0.85f);

    /// <summary>The standing quadruped of a species; <paramref name="lying"/> the same animal lying down (belly on the ground, legs folded under).</summary>
    internal static ShapeKit BuildQuad(in QuadSpec s0, bool lying, out Vector3 size, out float shiftX, Vector3? sizeOverride = null, float? shiftOverride = null, float originAhead = 0f)
    {
        var s = s0;
        if (lying) s = s with { Z0 = s.LegT * 0.7f, NeckUp = s.NeckUp + 25f };
        var k = new MetreKit();
        // the torso (a sheep's is wool: the lighter shade), a paler belly strip under it
        float tcx = (s.X0 + s.X1) / 2f;
        if (!s.Rabbit) k.Add(tcx, 0f, s.Z0 + s.BodyH / 2f, s.X1 - s.X0, s.BodyW, s.BodyH, s.TorsoCol);
        else
        {
            // ⭐ HUNCHED (the rabbit sits): low shoulders, the back stepping up to big rounded haunches (heights shrink when crouched)
            float hs = lying ? 0.75f : 1f, z0 = s.Z0;
            k.Add(0.045f, 0f, z0 + 0.05f * hs, 0.15f, 0.11f, 0.10f * hs, s.TorsoCol);                 // the chest and shoulders: low
            k.Add(-0.06f, 0f, z0 + 0.075f * hs, 0.10f, 0.13f, 0.15f * hs, s.TorsoCol);               // the back rising
            k.Add(-0.115f, 0f, z0 + 0.09f * hs - 0.015f, 0.15f, 0.15f, 0.20f * hs, s.TorsoCol);      // the haunches: the highest, roundest part
        }
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
            if (s.Rabbit)
            {
                // two LONG ears (~0.10 m): upright, leaning a little back; laid back along the neck when the rabbit lies / crouches
                if (lying) k.Add(ears.X - 0.05f, sy * 0.035f, ears.Z + 0.015f, 0.02f, 0.018f, 0.10f, s.HeadCol, pitch: -80f, role: skull, pivot: w, pivot2: n);
                else k.Add(ears.X - 0.015f, sy * 0.035f, ears.Z + 0.05f, 0.02f, 0.018f, 0.11f, s.HeadCol, pitch: -18f, role: skull, pivot: w, pivot2: n);
            }
            else if (s.UprightEars) k.Add(ears.X, sy * (s.HeadW / 2f - 0.02f), ears.Z + 0.05f, 0.035f, 0.04f, 0.10f, s.HeadCol, role: skull, pivot: w, pivot2: n);
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
                float ll = s.Rabbit ? 0.13f : 0.34f;
                k.Add(xs[i] + (i < 2 ? ll * 0.35f : -ll * 0.06f), ys[i] * 0.85f, s.LegT * 0.5f, ll, s.LegT, s.LegT, s.LegCol);
                continue;
            }
            var role = (PartRole)((int)PartRole.QuadFrontLeft + i);
            var pivot = new Vector3(xs[i], ys[i], hipZ);
            float len = hipZ - s.HoofH;
            k.Add(xs[i], ys[i], s.HoofH + len / 2f, s.LegT, s.LegT, len, s.LegCol, role: role, pivot: pivot);
            if (s.Rabbit && i >= 2) k.Add(xs[i] + 0.05f, ys[i], s.HoofH / 2f, 0.13f, s.LegT * 1.1f, s.HoofH, s.LegCol, role: role, pivot: pivot);   // the hind foot: a long FLAT foot on the ground, the leg folded above it
            else k.Add(xs[i], ys[i], s.HoofH / 2f, s.LegT * 1.1f, s.LegT * 1.1f, s.HoofH, s.Rabbit ? s.LegCol : ColourRole.Dark, role: role, pivot: pivot);
        }
        // the tail: hanging by TailDeg from straight down toward the rear
        var root = new Vector3(s.X0, 0f, s.Z0 + s.BodyH * 0.85f);
        float th = s.TailDeg * MathF.PI / 180f;
        var td = new Vector3(-MathF.Sin(th), 0f, -MathF.Cos(th));
        var tc = root + td * s.TailL / 2f;
        k.Add(tc.X, 0f, tc.Z, 0.05f, 0.05f, s.TailL, s.TailCol, pitch: s.TailDeg);
        if (s.Rabbit)
        {
            k.Add(s.X0 - 0.005f, 0f, s.Z0 + (lying ? 0.10f : 0.13f), 0.07f, 0.07f, 0.07f, ColourRole.Bone);   // the white puff of a tail, at the rear of the haunch: seen from behind and the side
        }
        if (s.CurlyTail) k.Add(root.X - 0.07f, 0f, root.Z + 0.08f, 0.07f, 0.05f, 0.05f, s.TailCol);
        if (s.Udder && !lying) k.Add(s.X0 + 0.55f, 0f, s.Z0 - 0.03f, 0.30f, 0.22f, 0.12f, ColourRole.Skin);
        return k.Build(out size, out shiftX, 0f, sizeOverride, shiftOverride, originAhead);
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

    /// <summary>
    /// ⭐ How far ahead of its box's centre a species' kit is drawn (m). A horse's TKB box runs nose to tail and backend puts the rider's hips at x 0.15 — over the saddle only if the body sits this much forward of the
    /// box's centre (the head and neck are what pull the box's centre ahead of the torso). 📌 Finding for backend: honest seat x for the 703 / 704 kit would be about −0.15.
    /// </summary>
    public static float OriginAhead(VisualFamily f) => f == VisualFamily.Horse ? 0.30f : 0f;

    static AnimalKits()
    {
        foreach (var f in new[] { VisualFamily.Cattle, VisualFamily.Sheep, VisualFamily.Horse, VisualFamily.Dog, VisualFamily.Cat, VisualFamily.Pig, VisualFamily.Goat, VisualFamily.Rabbit })
        {
            var spec = SpecOf(f);
            var standing = BuildQuad(spec, false, out var size, out var shift, originAhead: OriginAhead(f));   // the box is the STANDING animal's, measured from its parts — never typed
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
         : (mode == AnimalMode.Rest || mode == AnimalMode.Crouch && f == VisualFamily.Rabbit) && VisualFamilies.IsQuadruped(f) ? _quads[f].Lying   // a rabbit freezes CROUCHED (AN-21b): the low kit, ears laid back
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

