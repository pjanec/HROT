using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Hrot.Core.Tkb;

namespace Hrot.UI.Common.Map3D;

/// <summary>⭐ CE-1087 (docs/DESIGN_Map_3D_Mode.md §6n N-1) — which weapon-system kit a type draws, from its class chain.</summary>
public enum WeaponKind : byte
{
    /// <summary>A heavy machine gun on a tripod (M2, DShK) — also the generic weapon system.</summary>
    Hmg,
    /// <summary>A six-barrel minigun on a pintle post (M134).</summary>
    Minigun,
    /// <summary>A guided-missile launch tube on a tripod (Kornet, TOW).</summary>
    Atgm,
    /// <summary>A shoulder-fired tube (Javelin, NLAW, LAW).</summary>
    Launcher,
    /// <summary>An automatic turret on a stand.</summary>
    Sentry,
    /// <summary>A baseplate, a bipod and a tube (60 / 81 / 120 mm, Indirect Fire IF-G).</summary>
    Mortar,
    /// <summary>A towed howitzer on two wheels with split trails (105 mm).</summary>
    TowedGun,
    /// <summary>⭐ CE-1112 (§6v AD-V2) — a shoulder-fired anti-aircraft missile tube with a grip stock (Stinger, Igla).</summary>
    Manpads,
    /// <summary>⭐ CE-1112 (§6v AD-V3) — a twin-barrel anti-aircraft gun on a two-wheel carriage (ZU-23-2).</summary>
    AaGun,
}

/// <summary>
/// ⭐⭐ CE-1087 … CE-1089 (docs/DESIGN_Map_3D_Mode.md §6n N-1, N-2, N-5) — what a WEAPON SYSTEM looks like: a STOWED kit (transport —
/// folded, lying) and a DEPLOYED kit (on its tripod, plate or trails, ready) per <see cref="WeaponKind"/>, with the same parts in the same
/// order so a deploy or a pack-up blends between them; where its gunner kneels or stands; how a carried one hangs on a man's back.
/// Parts are written in metres for the kind's own size (<see cref="DefaultSize"/>) and stored as fractions, like every kit, so a type with
/// its own size scales them. A deployed kit's turret traverses and its gun elevates (§3.11) about its pivots.
/// </summary>
public static class WeaponKits
{
    /// <summary>The kind of <paramref name="dis"/>: the most specific class in its chain that names one (a plain weapon system is an HMG).</summary>
    public static WeaponKind KindOf(DISEntityType dis)
    {
        var chain = EntityClassTable.Default.Chain(dis);
        for (int i = chain.Count - 1; i >= 0; i--)
            switch (chain[i].Name)
            {
                case "HmgWeaponSystem": return WeaponKind.Hmg;
                case "MinigunWeaponSystem": return WeaponKind.Minigun;
                case "AtgmWeaponSystem": return WeaponKind.Atgm;
                case "LauncherWeaponSystem": return WeaponKind.Launcher;
                case "SentryWeaponSystem": return WeaponKind.Sentry;
                case "Mortar": return WeaponKind.Mortar;
                case "TowedArtillery": return WeaponKind.TowedGun;
                case "ManpadsWeaponSystem": return WeaponKind.Manpads;     // ⭐ CE-1112
                case "AaGunWeaponSystem": return WeaponKind.AaGun;         // ⭐ CE-1112
            }
        return WeaponKind.Hmg;
    }

    /// <summary>A kind's size (length, width, height in metres, deployed) when the TKB gives none.</summary>
    public static Vector3 DefaultSize(WeaponKind kind) => kind switch
    {
        WeaponKind.Minigun => new Vector3(0.9f, 0.4f, 0.7f),
        WeaponKind.Atgm => new Vector3(1.3f, 0.9f, 1.1f),
        WeaponKind.Launcher => new Vector3(1.1f, 0.15f, 0.15f),
        WeaponKind.Sentry => new Vector3(1.0f, 0.8f, 1.3f),
        WeaponKind.Mortar => new Vector3(1.2f, 0.6f, 1.1f),
        WeaponKind.TowedGun => new Vector3(6.0f, 2.0f, 1.8f),
        WeaponKind.Manpads => new Vector3(1.55f, 0.18f, 0.22f),
        WeaponKind.AaGun => new Vector3(4.6f, 2.4f, 1.6f),
        _ => new Vector3(1.65f, 0.9f, 0.75f),
    };

    /// <summary>⭐ N-5 — the elevation a deployed gun is drawn laid at when nothing says better (radians): a mortar fires only high (60°),
    /// a howitzer sits at a common low-angle lay (25°); the others level.</summary>
    public static float DefaultElevation(WeaponKind kind) => kind switch
    {
        WeaponKind.Mortar => 60f * MathF.PI / 180f,
        WeaponKind.TowedGun => 25f * MathF.PI / 180f,
        _ => 0f,
    };

    /// <summary>The kit of <paramref name="kind"/>, stowed or deployed.</summary>
    public static ShapeKit Kit(WeaponKind kind, bool deployed) => (deployed ? DeployedKits : StowedKits)[(int)kind];

    /// <summary>
    /// The kit part-way from stowed to deployed (<paramref name="t"/> 0..1 — a deploy goes up, a pack-up down): every part's centre, size,
    /// pitch and yaw interpolated; only a fully deployed kit articulates (its roles), a moving one is drawn as one piece.
    /// </summary>
    public static ShapeKit Blend(WeaponKind kind, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        if (t <= 0f) return Kit(kind, false);
        if (t >= 1f) return Kit(kind, true);
        var a = Kit(kind, false).Parts;
        var b = Kit(kind, true);
        var parts = new KitPart[a.Length];
        for (int i = 0; i < a.Length; i++)
            parts[i] = b.Parts[i] with
            {
                Centre = Vector3.Lerp(a[i].Centre, b.Parts[i].Centre, t),
                Extent = Vector3.Lerp(a[i].Extent, b.Parts[i].Extent, t),
                PitchDegrees = a[i].PitchDegrees + (b.Parts[i].PitchDegrees - a[i].PitchDegrees) * t,
                YawDegrees = a[i].YawDegrees + (b.Parts[i].YawDegrees - a[i].YawDegrees) * t,
                Role = PartRole.Hull,
            };
        return new ShapeKit(parts, b.Pivots);
    }

    /// <summary>How far a weapon is deployed (0 stowed … 1 deployed) at <paramref name="now"/>, from its replicated state: a deploy rises
    /// over its last <paramref name="seconds"/> before <paramref name="until"/>, a pack-up falls.</summary>
    public static float Deployment(Fdp.Toolkit.Combat.WeaponDeploy deploy, double until, double now, float seconds)
    {
        float left = seconds > 0f ? Math.Clamp((float)((until - now) / seconds), 0f, 1f) : 0f;
        return deploy switch
        {
            Fdp.Toolkit.Combat.WeaponDeploy.Deployed => 1f,
            Fdp.Toolkit.Combat.WeaponDeploy.Deploying => 1f - left,
            Fdp.Toolkit.Combat.WeaponDeploy.Packing => left,
            _ => 0f,
        };
    }

    /// <summary>⭐ N-2 — where the gunner of a <paramref name="kind"/> is (metres from the weapon's origin in its frame, turned with its
    /// traverse), which way he faces (radians from the weapon's forward), and his pose: KNEEL / STAND (the place is his feet) or ⭐ CE-1112
    /// (§6v AD-V4) SIT (the place is his hips, on the weapon's own seat). Null: nobody mans it on foot.</summary>
    public static (Vector3 At, float Facing, Fdp.Toolkit.Tkb.Domain.SeatPose Pose)? GunnerSpot(WeaponKind kind) => kind switch
    {
        WeaponKind.Hmg => (new Vector3(-0.95f, 0f, 0f), 0f, Kneel),            // behind the spade grips
        WeaponKind.Minigun => (new Vector3(-0.75f, 0f, 0f), 0f, Stand),        // standing behind it
        WeaponKind.Atgm => (new Vector3(-0.80f, -0.15f, 0f), 0f, Kneel),       // kneeling behind the sight
        WeaponKind.Launcher => (new Vector3(-0.30f, 0.30f, 0f), 0f, Kneel),
        WeaponKind.Manpads => (new Vector3(-0.30f, 0.30f, 0f), 0f, Kneel),
        WeaponKind.Mortar => (new Vector3(-0.15f, 0.55f, 0f), -0.35f, Kneel),  // beside the tube, at the sight
        WeaponKind.TowedGun => (new Vector3(-0.90f, 0.85f, 0f), 0f, Stand),    // left of the breech
        WeaponKind.AaGun => (new Vector3(-0.55f, 0.45f, 0.95f), 0f, Sit),      // ⭐ CE-1112 — on the left seat, at the sight
        _ => null,                                                              // a sentry gun mans itself
    };

    private const Fdp.Toolkit.Tkb.Domain.SeatPose Kneel = Fdp.Toolkit.Tkb.Domain.SeatPose.Kneel, Stand = Fdp.Toolkit.Tkb.Domain.SeatPose.Stand,
                                                   Sit = Fdp.Toolkit.Tkb.Domain.SeatPose.Sit;

    /// <summary>⭐ CE-1112 (§6v AD-V5) — a weapon MOUNTED in a vehicle's station stands on the vehicle, not on its own carriage: the
    /// ground-only parts of its kit (the ZU-23's jacks and wheels) are left out.</summary>
    public static KitPart[] Mounted(WeaponKind kind, KitPart[] parts)
        => kind == WeaponKind.AaGun && parts.Length == AaGunParts ? [parts[0], .. parts.AsSpan(AaGunFirstUpperPart)] : parts;

    private const int AaGunParts = 17, AaGunFirstUpperPart = 6;   // 1–3 the jacks, 4–5 the wheels

    /// <summary>⭐ CE-1112 (§6v AD-V2) — a weapon fired from the SHOULDER: held there while its bearer aims, throwing a backblast.</summary>
    public static bool IsShoulderFired(WeaponKind kind) => kind is WeaponKind.Launcher or WeaponKind.Atgm or WeaponKind.Manpads;

    /// <summary>The traverse pivot of a kind's deployed kit, metres in its frame (the gunner turns about it with the weapon).</summary>
    public static Vector3 TraversePivot(WeaponKind kind, Vector3 size)
    {
        var p = Kit(kind, true).Pivots.Turret;
        return new Vector3(p.X * size.X, p.Y * size.Y, 0f);
    }

    /// <summary>⭐ N-3 — a carried weapon's frame in the WORLD: slung across the carrier's back, its length running from the right hip up
    /// to the left shoulder, the middle of its stowed kit on his spine. <paramref name="carrierBody"/> is his figure's frame as drawn.</summary>
    public static Matrix4x4 OnBack(Matrix4x4 carrierBody, float carrierHeight, Vector3 weaponSize)
    {
        float h = carrierHeight / 1.8f;
        // the kit's length (+x) runs from his right hip up to his left shoulder: raised 60° (x → up), then turned a quarter about the
        // vertical (x → his left), so it lies flat across his back; its middle sits just behind the spine at mid-back height
        return Matrix4x4.CreateTranslation(0f, 0f, -weaponSize.Z / 2f)
               * Matrix4x4.CreateRotationY(-MathF.PI / 3f)
               * Matrix4x4.CreateRotationZ(MathF.PI / 2f)
               * Matrix4x4.CreateTranslation(-0.20f * h - weaponSize.Y / 2f, 0f, 1.15f * h)
               * carrierBody;
    }

    // ── ⭐ CE-1090 (§6n N-4) — launchers in hands, backblast, the gun's blast ────────────────────────────────────────────────────────

    /// <summary>How long after firing a launcher a soldier is drawn holding it on his shoulder (s, sim time).</summary>
    public const double ShoulderSeconds = 4.0;

    /// <summary>A launched round's mount: an ATGM, or anything firing a rocket or a missile (the RPG).</summary>
    public static bool IsLauncher(ITkbDatabase? tkb, Fdp.Toolkit.Tkb.Domain.WeaponMountDto? mount)
    {
        if (mount == null) return false;
        if (mount.Category is Fdp.Toolkit.Tkb.Domain.WeaponCategory.Atgm or Fdp.Toolkit.Tkb.Domain.WeaponCategory.Sam) return true;   // ⭐ CE-2187 (backend, cross-lane: a MANPADS is a launcher)
        return MunitionTkbCatalog.VisualOf(tkb, unchecked((long)mount.AmmoGuid)) is { Shape: Fdp.Toolkit.Tkb.Domain.MunitionShape.Rocket or Fdp.Toolkit.Tkb.Domain.MunitionShape.Missile };
    }

    /// <summary>The family and (for a weapon system) the kind of <paramref name="e"/>'s TKB type; Unknown with no type.</summary>
    public static (VisualFamily Family, WeaponKind Kind) TypeOf(EntityRepository world, ITkbDatabase? tkb, Entity e)
    {
        if (tkb == null || !world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()
            || !world.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(e)
            || !tkb.TryGetByType(world.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(e).TkbType, out var t) || t == null)
            return (VisualFamily.Unknown, WeaponKind.Hmg);
        var family = VisualFamilies.Of(t);
        return (family, family == VisualFamily.WeaponSystem ? KindOf(t.DisType) : WeaponKind.Hmg);
    }

    /// <summary>
    /// ⭐ N-4 — what a shot throws besides its flash: a BACKBLAST behind a recoilless tube (a launcher fired by a man, or a launcher /
    /// ATGM weapon system on the ground or carried); a GROUND blast round a howitzer (towed or self-propelled). A launcher in a vehicle's
    /// turret (a Bradley's TOW) throws none.
    /// </summary>
    public static FiringBlast BlastOf(EntityRepository world, ITkbDatabase? tkb, Entity shooter, Fdp.Toolkit.Tkb.Domain.WeaponMountDto? mount)
    {
        var (family, kind) = TypeOf(world, tkb, shooter);
        return family switch
        {
            VisualFamily.SpArtillery => FiringBlast.Ground,
            VisualFamily.WeaponSystem when kind == WeaponKind.TowedGun => FiringBlast.Ground,
            VisualFamily.WeaponSystem when IsShoulderFired(kind) => FiringBlast.Backblast,
            VisualFamily.Person when IsLauncher(tkb, mount) => FiringBlast.Backblast,
            _ => FiringBlast.None,
        };
    }

    /// <summary>Where the backblast of a tube fired from <paramref name="at"/> toward <paramref name="aimAt"/> (zero: along
    /// <paramref name="rotation"/>'s heading) billows: ~2 m behind the shoulder, on the line of fire.</summary>
    public static Vector3 BackblastPoint(Vector3 at, Quaternion rotation, Vector3 aimAt)
    {
        var dir = aimAt != Vector3.Zero ? aimAt - at : Vector3.Transform(Vector3.UnitX, rotation);
        dir.Z = 0f;
        dir = dir.LengthSquared() > 1e-6f ? Vector3.Normalize(dir) : Vector3.UnitX;
        return at + new Vector3(0f, 0f, 1.3f) - dir * 2.2f;
    }

    // ── the kits, in metres for each kind's DefaultSize ───────────────────────────────────────────────────────────────────────────────

    /// <summary>A box from <paramref name="a"/> to <paramref name="b"/> (a leg, a trail), <paramref name="w"/> × <paramref name="h"/> across.</summary>
    private static KitPart Strut(Vector3 s, Vector3 a, Vector3 b, float w, float h, ColourRole c, PartRole role = PartRole.Hull)
    {
        var d = b - a;
        float length = d.Length();
        float yaw = MathF.Abs(d.X) < 1e-5f && MathF.Abs(d.Y) < 1e-5f ? 0f : MathF.Atan2(d.Y, d.X) * 180f / MathF.PI;
        float pitch = MathF.Atan2(-d.Z, MathF.Sqrt(d.X * d.X + d.Y * d.Y)) * 180f / MathF.PI;
        return new KitPart(PartShape.Box, (a + b) / 2f / s, new Vector3(length, w, h) / s, c, PartAxis.Z, pitch, yaw, role);
    }

    private static KitPart Block(Vector3 s, float cx, float cy, float cz, float ex, float ey, float ez, ColourRole c, PartRole role = PartRole.Hull,
                                 float pitch = 0f)
        => new(PartShape.Box, new Vector3(cx, cy, cz) / s, new Vector3(ex, ey, ez) / s, c, PartAxis.Z, pitch, 0f, role);

    /// <summary>A cylinder along x (a barrel, a tube) centred at (cx, cy, cz), <paramref name="length"/> long, <paramref name="diameter"/> across.</summary>
    private static KitPart Tube(Vector3 s, float cx, float cy, float cz, float length, float diameter, ColourRole c, PartRole role = PartRole.Hull,
                                float pitch = 0f)
        => new(PartShape.Cylinder, new Vector3(cx, cy, cz) / s, new Vector3(length, 0f, diameter) / s, c, PartAxis.X, pitch, 0f, role);

    private static KitPart Upright(Vector3 s, float cx, float cy, float cz, float height, float diameter, ColourRole c, PartRole role = PartRole.Hull)
        => new(PartShape.Cylinder, new Vector3(cx, cy, cz) / s, new Vector3(diameter, 0f, height) / s, c, PartAxis.Z, Role: role);

    private static KitPivots Pivots(Vector3 s, float turretX, float turretY, float trunnionX, float trunnionZ)
        => new(new Vector2(turretX / s.X, turretY / s.Y), new Vector2(trunnionX / s.X, trunnionZ / s.Z));

    private static ShapeKit Hmg(bool deployed)
    {
        var s = DefaultSize(WeaponKind.Hmg);
        var pintle = new Vector3(0f, 0f, 0.45f);
        if (deployed)
            return new ShapeKit(new[]
            {
                Strut(s, new(0.55f, 0f, 0.02f), pintle, 0.05f, 0.05f, ColourRole.Dark),                    // tripod, front leg
                Strut(s, new(-0.60f, 0.40f, 0.02f), pintle, 0.05f, 0.05f, ColourRole.Dark),                // rear legs
                Strut(s, new(-0.60f, -0.40f, 0.02f), pintle, 0.05f, 0.05f, ColourRole.Dark),
                Block(s, 0f, 0f, 0.50f, 0.14f, 0.12f, 0.12f, ColourRole.Metal, PartRole.Turret),          // cradle on the pintle
                Block(s, -0.05f, 0f, 0.60f, 0.62f, 0.20f, 0.20f, ColourRole.Body, PartRole.Gun),          // receiver
                Tube(s, 0.71f, 0f, 0.62f, 0.90f, 0.06f, ColourRole.Metal, PartRole.Gun),                  // barrel
                Block(s, 0.05f, 0.19f, 0.58f, 0.26f, 0.10f, 0.18f, ColourRole.Body, PartRole.Gun),        // ammunition can
                Block(s, -0.40f, 0f, 0.62f, 0.08f, 0.18f, 0.10f, ColourRole.Dark, PartRole.Gun),          // spade grips
            }, Pivots(s, 0f, 0f, 0f, 0.60f));
        return new ShapeKit(new[]
        {
            Strut(s, new(0.10f, 0.10f, 0.03f), new(-0.62f, 0.12f, 0.03f), 0.05f, 0.05f, ColourRole.Dark),   // the legs folded together
            Strut(s, new(0.10f, 0.00f, 0.03f), new(-0.62f, 0.02f, 0.03f), 0.05f, 0.05f, ColourRole.Dark),
            Strut(s, new(0.10f, -0.10f, 0.03f), new(-0.62f, -0.08f, 0.03f), 0.05f, 0.05f, ColourRole.Dark),
            Block(s, -0.30f, 0f, 0.09f, 0.14f, 0.12f, 0.12f, ColourRole.Metal),
            Block(s, -0.05f, 0.30f, 0.11f, 0.62f, 0.20f, 0.20f, ColourRole.Body),                          // the gun lying beside it
            Tube(s, 0.71f, 0.30f, 0.11f, 0.90f, 0.06f, ColourRole.Metal),
            Block(s, -0.15f, -0.25f, 0.09f, 0.26f, 0.10f, 0.18f, ColourRole.Body),
            Block(s, -0.40f, 0.30f, 0.11f, 0.08f, 0.18f, 0.10f, ColourRole.Dark),
        }, Pivots(s, 0f, 0f, 0f, 0.60f));
    }

    private static ShapeKit Minigun(bool deployed)
    {
        var s = DefaultSize(WeaponKind.Minigun);
        float down = deployed ? 0f : 25f;   // stowed: the barrels dipped on their mount
        return new ShapeKit(new[]
        {
            Upright(s, 0f, 0f, 0.22f, 0.44f, 0.07f, ColourRole.Dark),                                      // pintle post
            Block(s, 0f, 0f, 0.48f, 0.14f, 0.12f, 0.10f, ColourRole.Metal, deployed ? PartRole.Turret : PartRole.Hull),
            Block(s, -0.05f, 0f, 0.55f, 0.36f, 0.18f, 0.18f, ColourRole.Body, deployed ? PartRole.Gun : PartRole.Hull, down),
            Tube(s, 0.32f, 0f, 0.55f - down / 100f, 0.55f, 0.13f, ColourRole.Metal, deployed ? PartRole.Gun : PartRole.Hull, down),   // the barrel cluster
            Block(s, -0.06f, 0f, 0.67f, 0.18f, 0.10f, 0.07f, ColourRole.Dark, deployed ? PartRole.Gun : PartRole.Hull, down),     // motor
            Block(s, -0.08f, 0.17f, 0.50f, 0.20f, 0.10f, 0.14f, ColourRole.Body, deployed ? PartRole.Turret : PartRole.Hull),     // feed chute box
            Block(s, -0.32f, 0f, 0.57f, 0.06f, 0.22f, 0.08f, ColourRole.Dark, deployed ? PartRole.Gun : PartRole.Hull, down),     // grips
        }, Pivots(s, 0f, 0f, 0f, 0.55f));
    }

    private static ShapeKit Atgm(bool deployed)
    {
        var s = DefaultSize(WeaponKind.Atgm);
        var head = new Vector3(0f, 0f, 0.60f);
        if (deployed)
            return new ShapeKit(new[]
            {
                Strut(s, new(0.45f, 0f, 0.02f), head, 0.05f, 0.05f, ColourRole.Dark),
                Strut(s, new(-0.40f, 0.38f, 0.02f), head, 0.05f, 0.05f, ColourRole.Dark),
                Strut(s, new(-0.40f, -0.38f, 0.02f), head, 0.05f, 0.05f, ColourRole.Dark),
                Block(s, 0f, 0f, 0.66f, 0.18f, 0.16f, 0.14f, ColourRole.Metal, PartRole.Turret),          // traverse head
                Block(s, -0.08f, -0.22f, 0.78f, 0.30f, 0.16f, 0.24f, ColourRole.Body, PartRole.Gun),      // guidance / sight unit
                Tube(s, 0.10f, 0.02f, 0.86f, 1.20f, 0.15f, ColourRole.Body, PartRole.Gun),               // launch tube
                Block(s, -0.30f, 0.12f, 0.92f, 0.18f, 0.10f, 0.12f, ColourRole.Glass, PartRole.Gun),      // thermal sight
            }, Pivots(s, 0f, 0f, 0f, 0.78f));
        return new ShapeKit(new[]
        {
            Strut(s, new(0.30f, 0.30f, 0.03f), new(-0.40f, 0.30f, 0.03f), 0.05f, 0.05f, ColourRole.Dark),
            Strut(s, new(0.30f, 0.22f, 0.03f), new(-0.40f, 0.22f, 0.03f), 0.05f, 0.05f, ColourRole.Dark),
            Strut(s, new(0.30f, 0.14f, 0.03f), new(-0.40f, 0.14f, 0.03f), 0.05f, 0.05f, ColourRole.Dark),
            Block(s, -0.05f, 0.22f, 0.12f, 0.18f, 0.16f, 0.14f, ColourRole.Metal),
            Block(s, -0.30f, -0.25f, 0.12f, 0.30f, 0.16f, 0.24f, ColourRole.Body),
            Tube(s, 0.10f, -0.05f, 0.08f, 1.20f, 0.15f, ColourRole.Body),
            Block(s, 0.40f, -0.30f, 0.06f, 0.18f, 0.10f, 0.12f, ColourRole.Glass),
        }, Pivots(s, 0f, 0f, 0f, 0.78f));
    }

    private static ShapeKit Launcher(bool deployed)
    {
        var s = DefaultSize(WeaponKind.Launcher);
        // a tube lying on the ground, its sight unit on its side (the same stowed and deployed: it is fired from the shoulder)
        return new ShapeKit(new[]
        {
            Tube(s, 0f, 0f, 0.075f, 1.10f, 0.13f, ColourRole.Body),
            Block(s, -0.12f, 0.12f, 0.08f, 0.26f, 0.14f, 0.13f, ColourRole.Dark),      // command / sight unit
            Tube(s, 0.53f, 0f, 0.075f, 0.06f, 0.16f, ColourRole.Dark),                 // front end cap
            Tube(s, -0.53f, 0f, 0.075f, 0.06f, 0.16f, ColourRole.Dark),                // rear end cap
        }, default);
    }

    /// <summary>⭐ CE-1112 (§6v AD-V2) — a Stinger / Igla: a slim tube, its grip stock and sight under it, the battery unit under the front,
    /// the folded antenna on top; lying the same stowed and deployed (it is fired from the shoulder).</summary>
    private static ShapeKit Manpads(bool deployed)
    {
        var s = DefaultSize(WeaponKind.Manpads);
        return new ShapeKit(new[]
        {
            Tube(s, 0f, 0f, 0.11f, 1.52f, 0.09f, ColourRole.Body),                     // the launch tube
            Block(s, -0.12f, 0f, 0.04f, 0.30f, 0.06f, 0.08f, ColourRole.Dark),          // the grip stock under it
            Upright(s, 0.30f, 0f, 0.04f, 0.08f, 0.07f, ColourRole.Dark),               // the battery / coolant unit
            Block(s, 0.05f, 0.07f, 0.15f, 0.14f, 0.05f, 0.08f, ColourRole.Glass),       // the sight on its left
            Block(s, 0.20f, 0f, 0.17f, 0.30f, 0.12f, 0.02f, ColourRole.Dark),           // the folded antenna
            Tube(s, 0.75f, 0f, 0.11f, 0.04f, 0.11f, ColourRole.Dark),                   // front cap
        }, default);
    }

    /// <summary>
    /// ⭐ CE-1112 (§6v AD-V3) — the ZU-23-2: two barrels with flash hiders over a cradle, an ammunition box and a seat each side, on a turret
    /// ring. STOWED on its two wheels, barrels level forward; DEPLOYED lowered onto three jacks with the wheels lifted. Same 17 parts.
    /// </summary>
    private static ShapeKit AaGun(bool deployed)
    {
        var s = DefaultSize(WeaponKind.AaGun);
        float up = deployed ? 0f : 0.15f;                                    // the carriage rides higher on its wheels for towing
        PartRole turret = deployed ? PartRole.Turret : PartRole.Hull, gun = deployed ? PartRole.Gun : PartRole.Hull;
        var front = deployed ? new Vector3(1.45f, 0f, 0.02f) : new Vector3(1.00f, 0f, 0.40f);   // jacks down / folded along the carriage
        var rearL = deployed ? new Vector3(-1.25f, 1.05f, 0.02f) : new Vector3(-0.80f, 0.30f, 0.40f);
        var rearR = deployed ? new Vector3(-1.25f, -1.05f, 0.02f) : new Vector3(-0.80f, -0.30f, 0.40f);
        float wheelZ = deployed ? 0.70f : 0.36f;                             // lifted / on the ground
        var axle = new Vector3(0f, 0f, 0.45f + up);
        return new ShapeKit(new[]
        {
            Block(s, 0f, 0f, 0.45f + up, 1.70f, 1.10f, 0.22f, ColourRole.Body),                                    // carriage platform
            Strut(s, axle + new Vector3(0.6f, 0f, 0f), front, 0.10f, 0.10f, ColourRole.Dark),                       // front jack
            Strut(s, axle + new Vector3(-0.5f, 0.35f, 0f), rearL, 0.10f, 0.10f, ColourRole.Dark),                   // rear jacks
            Strut(s, axle + new Vector3(-0.5f, -0.35f, 0f), rearR, 0.10f, 0.10f, ColourRole.Dark),
            new KitPart(PartShape.Cylinder, new Vector3(0f, 0.95f, wheelZ) / s, new Vector3(0f, 0.20f, 0.72f) / s, ColourRole.Dark, PartAxis.Y),
            new KitPart(PartShape.Cylinder, new Vector3(0f, -0.95f, wheelZ) / s, new Vector3(0f, 0.20f, 0.72f) / s, ColourRole.Dark, PartAxis.Y),
            Upright(s, 0f, 0f, 0.65f + up, 0.18f, 0.90f, ColourRole.Metal, turret),                                // turret ring
            Block(s, 0.05f, 0f, 0.98f + up, 1.10f, 0.40f, 0.30f, ColourRole.Body, gun),                            // cradle and receivers
            Tube(s, 1.90f, 0.11f, 1.02f + up, 2.60f, 0.06f, ColourRole.Metal, gun),                               // left barrel
            Tube(s, 1.90f, -0.11f, 1.02f + up, 2.60f, 0.06f, ColourRole.Metal, gun),                              // right barrel
            Tube(s, 3.25f, 0.11f, 1.02f + up, 0.18f, 0.10f, ColourRole.Dark, gun),                                // flash hiders
            Tube(s, 3.25f, -0.11f, 1.02f + up, 0.18f, 0.10f, ColourRole.Dark, gun),
            Block(s, 0.05f, 0.36f, 1.05f + up, 0.36f, 0.22f, 0.30f, ColourRole.Body, gun),                         // ammunition boxes
            Block(s, 0.05f, -0.36f, 1.05f + up, 0.36f, 0.22f, 0.30f, ColourRole.Body, gun),
            Block(s, -0.55f, 0.45f, 0.86f + up, 0.32f, 0.30f, 0.06f, ColourRole.Dark, turret),                     // seats
            Block(s, -0.55f, -0.45f, 0.86f + up, 0.32f, 0.30f, 0.06f, ColourRole.Dark, turret),
            Block(s, -0.25f, 0.30f, 1.28f + up, 0.14f, 0.10f, 0.16f, ColourRole.Glass, gun),                       // sight
        }, Pivots(s, 0f, 0f, 0.05f, 1.02f));
    }

    private static ShapeKit Sentry(bool deployed)
    {
        var s = DefaultSize(WeaponKind.Sentry);
        float down = deployed ? 0f : 20f;
        return new ShapeKit(new[]
        {
            Block(s, 0f, 0f, 0.10f, 0.80f, 0.70f, 0.20f, ColourRole.Dark),                                 // base
            Upright(s, 0f, 0f, 0.45f, 0.50f, 0.16f, ColourRole.Metal),                                     // post
            Block(s, 0f, 0f, 0.85f, 0.46f, 0.40f, 0.30f, ColourRole.Body, deployed ? PartRole.Turret : PartRole.Hull),   // turret
            Tube(s, 0.48f, 0f, 0.88f, 0.62f, 0.05f, ColourRole.Metal, deployed ? PartRole.Gun : PartRole.Hull, down),     // barrel
            Block(s, 0.14f, 0f, 1.05f, 0.14f, 0.22f, 0.10f, ColourRole.Glass, deployed ? PartRole.Gun : PartRole.Hull),   // sensor head
        }, Pivots(s, 0f, 0f, 0.10f, 0.88f));
    }

    private static ShapeKit Mortar(bool deployed)
    {
        var s = DefaultSize(WeaponKind.Mortar);
        // the tube is modelled LEVEL from the baseplate (its trunnion) and laid by the gun elevation (N-5); the bipod is drawn for the
        // default 60° lay, its collar ~0.6 m up the tube
        if (deployed)
            return new ShapeKit(new[]
            {
                Upright(s, -0.35f, 0f, 0.03f, 0.06f, 0.50f, ColourRole.Dark),                              // baseplate
                Tube(s, 0.25f, 0f, 0.06f, 1.20f, 0.09f, ColourRole.Body, PartRole.Gun),                    // tube (laid by elevation)
                Strut(s, new(0.25f, 0.28f, 0.00f), new(-0.05f, 0.05f, 0.58f), 0.04f, 0.04f, ColourRole.Dark),   // bipod
                Strut(s, new(0.25f, -0.28f, 0.00f), new(-0.05f, -0.05f, 0.58f), 0.04f, 0.04f, ColourRole.Dark),
                Block(s, -0.02f, 0.11f, 0.55f, 0.10f, 0.08f, 0.12f, ColourRole.Glass),                    // sight
            }, Pivots(s, -0.35f, 0f, -0.35f, 0.06f));
        return new ShapeKit(new[]
        {
            Upright(s, -0.35f, 0f, 0.03f, 0.06f, 0.50f, ColourRole.Dark),
            Tube(s, 0.25f, 0f, 0.06f, 1.20f, 0.09f, ColourRole.Body),
            Strut(s, new(0.55f, 0.08f, 0.03f), new(-0.05f, 0.08f, 0.03f), 0.04f, 0.04f, ColourRole.Dark),   // bipod folded along the tube
            Strut(s, new(0.55f, -0.08f, 0.03f), new(-0.05f, -0.08f, 0.03f), 0.04f, 0.04f, ColourRole.Dark),
            Block(s, 0.05f, 0.18f, 0.05f, 0.10f, 0.08f, 0.10f, ColourRole.Glass),
        }, Pivots(s, -0.35f, 0f, -0.35f, 0.06f));
    }

    private static ShapeKit TowedGun(bool deployed)
    {
        var s = DefaultSize(WeaponKind.TowedGun);
        // trails spread wide and staked into the ground when deployed; closed and up for towing when stowed
        float trailY = deployed ? 1.00f : 0.12f, trailZ = deployed ? 0.05f : 0.30f;
        return new ShapeKit(new[]
        {
            new KitPart(PartShape.Cylinder, new Vector3(0f, 0.85f, 0.45f) / s, new Vector3(0f, 0.25f, 0.90f) / s, ColourRole.Dark, PartAxis.Y),
            new KitPart(PartShape.Cylinder, new Vector3(0f, -0.85f, 0.45f) / s, new Vector3(0f, 0.25f, 0.90f) / s, ColourRole.Dark, PartAxis.Y),
            Block(s, 0f, 0f, 0.55f, 0.90f, 1.50f, 0.25f, ColourRole.Body),                                  // axle and carriage
            Block(s, 0.35f, 0f, 0.95f, 0.08f, 1.60f, 0.80f, ColourRole.Body),                               // shield
            Block(s, -0.20f, 0f, 1.10f, 1.40f, 0.35f, 0.30f, ColourRole.Body, deployed ? PartRole.Gun : PartRole.Hull),    // cradle / recoil
            Tube(s, 1.60f, 0f, 1.15f, 3.30f, 0.14f, ColourRole.Metal, deployed ? PartRole.Gun : PartRole.Hull),            // barrel
            Tube(s, 3.30f, 0f, 1.15f, 0.25f, 0.22f, ColourRole.Dark, deployed ? PartRole.Gun : PartRole.Hull),             // muzzle brake
            Strut(s, new(-0.30f, 0.25f, 0.45f), new(-2.90f, trailY, trailZ), 0.16f, 0.18f, ColourRole.Body),  // trails
            Strut(s, new(-0.30f, -0.25f, 0.45f), new(-2.90f, -trailY, trailZ), 0.16f, 0.18f, ColourRole.Body),
        }, Pivots(s, 0f, 0f, -0.20f, 1.15f));
    }

    private static readonly ShapeKit[] StowedKits = Build(false);
    private static readonly ShapeKit[] DeployedKits = Build(true);

    private static ShapeKit[] Build(bool deployed) =>
    [
        Hmg(deployed), Minigun(deployed), Atgm(deployed), Launcher(deployed), Sentry(deployed), Mortar(deployed), TowedGun(deployed),
        Manpads(deployed), AaGun(deployed),
    ];
}

/// <summary>⭐ CE-1090 (§6n N-4) — what a shot throws besides its muzzle flash.</summary>
public enum FiringBlast : byte
{
    None = 0,
    /// <summary>Dust behind a recoilless tube.</summary>
    Backblast,
    /// <summary>A ground blast round a howitzer.</summary>
    Ground,
}
