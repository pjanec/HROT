using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Hrot.Core.Tkb;

namespace Hrot.UI.Common.Map3D;   // ⭐ CE-4671 LK-S1 — the namespace is kept with the move, so no presentation caller changes

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
    /// <summary>⭐ CE-1169 / CE-1179 — a rifle one man picks up and carries (backend's pick-up rifle, CE-3233).</summary>
    Rifle,
    /// <summary>⭐ CE-1169 / CE-1179 — a pistol one man picks up and carries.</summary>
    Pistol,
    /// <summary>⭐ CE-1179 (People Display U18) — an explosive charge: worn hidden, placed, or in a vehicle's cargo.</summary>
    Charge,
}

/// <summary>⭐ CE-1090 (§6n N-4) — what a shot throws besides its muzzle flash.</summary>
public enum FiringBlast : byte
{
    None = 0,
    /// <summary>Dust behind a recoilless tube.</summary>
    Backblast,
    /// <summary>A ground blast round a howitzer.</summary>
    Ground,
    /// <summary>⭐ CE-4618 — the puff of smoke a missile leaves at its canister's mouth (the radar SAM truck).</summary>
    Launch,
}

/// <summary>
/// ⭐⭐ CE-4671 LK-S1 (DESIGN_Vehicle_Appearance_State.md §7b) — what a shot THROWS besides its flash, and the type classification it rests on: moved out of
/// the map's <c>WeaponKits</c> (which forwards here) so the effect rules (<c>Hrot.Look.Effects.EffectRules</c>) and an external renderer share it.
/// </summary>
public static class FiringBlasts
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
                case "PickUpRifleWeaponSystem": return WeaponKind.Rifle;    // ⭐ CE-1169 / CE-1179
                case "PickUpPistolWeaponSystem": return WeaponKind.Pistol;
                case "ExplosiveChargeWeaponSystem": return WeaponKind.Charge;
            }
        return WeaponKind.Hmg;
    }
    /// <summary>⭐ CE-1112 (§6v AD-V2) — a weapon fired from the SHOULDER: held there while its bearer aims, throwing a backblast.</summary>
    public static bool IsShoulderFired(WeaponKind kind) => kind is WeaponKind.Launcher or WeaponKind.Atgm or WeaponKind.Manpads;
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
            VisualFamily.AirDefenceVehicle => FiringBlast.Launch,   // ⭐ CE-4618 — a SAM's booster smoke at the canister's mouth
            // ⭐ CE-4647 (air combat AC-15 step 4) — a fighter's missile leaving its rail: the booster's flash (the mount's own effect set) and the puff left at the rail (the shot's origin)
            VisualFamily.Jet or VisualFamily.TwinEngineJet when mount is { } m && (AirMissiles.IsIr(m.AmmoGuid) || AirMissiles.IsRadar(m.AmmoGuid)) => FiringBlast.Launch,
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
}
