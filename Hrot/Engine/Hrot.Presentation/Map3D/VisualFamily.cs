using System.Linq;
using Fdp.Core;
using Fdp.Interfaces;
using Hrot.Core.Tkb;
using Hrot.Map.Definitions.Tkb;

namespace Hrot.UI.Common.Map3D;

/// <summary>What kind of thing a TKB type is, for drawing it — an icon glyph in 2-D, a shape kit in 3-D.</summary>
public enum VisualFamily : byte
{
    Unknown = 0,
    Person,
    Tank,
    Afv,
    WheeledCar,
    WheeledUtility,
    /// <summary>Rotary wing — DIS platform/air categories 20–25.</summary>
    Helicopter,
    /// <summary>Fast fixed wing — DIS platform/air 1, 2, 6, 7, 40, 47, 50.</summary>
    Jet,
    /// <summary>Large fixed wing — DIS platform/air 3, 4, 5, 8, 57 (bomber, cargo/tanker, patrol, AEW, commercial).</summary>
    CargoPlane,
    /// <summary>A composite unit: no body of its own, its members draw themselves.</summary>
    Unit,
    /// <summary>⭐ CE-1086 (§6n N-7) — a multirotor drone: a <c>Uav</c> that flies as a <c>Rotorcraft</c> (the FPV drone).</summary>
    Drone,
    /// <summary>⭐ CE-1087 (§6n N-1) — a separable weapon (class <c>WeaponSystem</c>, <c>Mortar</c>, <c>TowedArtillery</c>); its kind picks the kit
    /// (<c>WeaponKits.KindOf</c>).</summary>
    WeaponSystem,
    /// <summary>⭐ CE-1089 (§6n N-5) — a self-propelled howitzer: a tank hull, a big turret, a long barrel.</summary>
    SpArtillery,
    /// <summary>⭐ CE-1092 (§6n N-8) — an attack plane (class <c>AttackAircraft</c>, the A-10): straight wings, rear engines, twin fins, pylons.</summary>
    AttackJet,
    /// <summary>⭐ CE-1103 (§6s) — an attack helicopter (class <c>AttackHelicopter</c>, the AH-64): a slim fuselage, a tandem canopy, stub
    /// wings with rocket pods and missile rails, a chin gun on a turret.</summary>
    AttackHelicopter,
}

/// <summary>
/// ⭐ CE-1162 (civilian U-1, docs/DESIGN_Civilians_And_Crowd_Control.md §8 U1-a) — how a PERSON is dressed and armed, from his CLASS
/// (R-280, the one table): a soldier (Infantry), a security-force officer (SecurityForce), or anyone else in clothes.
/// </summary>
public enum PersonKit : byte { Civilian = 0, Soldier, Security }

/// <summary>
/// ⭐ CE-1033 S1 — THE one answer to "what kind of thing is this type" (<c>docs/DESIGN_Map_3D_Mode.md</c> §3.5, M7), extracted
/// from <c>EntityTypeCatalog.FallbackIconName</c> so the Add Entity icons and the 3-D shape kits cannot disagree. Keyed on
/// what the type IS — its DIS type, or being a composite — so a TKB without art still gets a meaningful picture.
/// <para>⭐ CE-2138 (C2, rule K1, R-279 / R-280 (behaviors)) — the DIS half is now <see cref="EntityClassTable"/>: the family is
/// that of the MOST SPECIFIC class in the type's chain that has one (<see cref="FamilyOfClass"/>), so the 3-D model, the
/// picker and the behaviours read one table. ⭐ CE-2139 (C3) — units have their own DIS Kind, so the composite check is gone.</para>
/// </summary>
public static class VisualFamilies
{
    /// <summary>⭐ CE-2138 — the classes that decide a family; a type takes the first one found walking its chain from the
    /// most specific class up. Every name here is a class of <see cref="EntityClassTable.Default"/> (rail).</summary>
    public static IReadOnlyDictionary<string, VisualFamily> FamilyOfClass { get; } = new Dictionary<string, VisualFamily>
    {
        ["Unit"] = VisualFamily.Unit,
        ["Person"] = VisualFamily.Person,
        ["Tank"] = VisualFamily.Tank,
        ["Afv"] = VisualFamily.Afv,
        ["Car"] = VisualFamily.WheeledCar,
        ["WheeledUtility"] = VisualFamily.WheeledUtility,
        ["Helicopter"] = VisualFamily.Helicopter,
        ["Jet"] = VisualFamily.Jet,
        ["CargoPlane"] = VisualFamily.CargoPlane,
        ["WeaponSystem"] = VisualFamily.WeaponSystem,   // ⭐ CE-1087 — and its subclasses (HMG, Minigun, ATGM, Launcher, Sentry)
        ["Mortar"] = VisualFamily.WeaponSystem,         // ⭐ CE-1087 — a tube is a weapon system (Indirect Fire IF-G)
        ["TowedArtillery"] = VisualFamily.WeaponSystem,
        ["SelfPropelledArtillery"] = VisualFamily.SpArtillery,   // ⭐ CE-1089 — its own kit (supersedes IF-1 I8's interim tank kit, merged 2026-10-15)
        ["AttackAircraft"] = VisualFamily.AttackJet,    // ⭐ CE-1092 — more specific than Jet, so the A-10 is not an F-16
        ["AttackHelicopter"] = VisualFamily.AttackHelicopter,   // ⭐ CE-1103 — more specific than Helicopter, so the AH-64 is not a UH-60
    };

    /// <summary>The family of <paramref name="t"/> — its DIS type's. ⭐ CE-2139 (C3): a unit is <see cref="VisualFamily.Unit"/>
    /// because its DIS Kind is the unit Kind (11), no longer because it carries a <see cref="TkbCompositionDef"/>.</summary>
    /// <para>⭐ CE-1086 (§6n N-7) — a <see cref="VisualFamily.Jet"/> whose class chain names <c>Uav</c> and that FLIES AS A ROTORCRAFT
    /// (<c>Gen.Locomotion</c>) is a <see cref="VisualFamily.Drone"/> — a quadcopter, not a 15 m fighter. A fixed-wing UAV stays a jet.</para>
    public static VisualFamily Of(TkbTemplate t)
    {
        var family = Of(t.DisType);
        if (family == VisualFamily.Jet && t.GetDescriptor<Fdp.Toolkit.Tkb.Domain.LocomotionDto>()?.Class == Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft
            && EntityClassTable.Default.Chain(t.DisType).Any(c => c.Name == "Uav"))
            return VisualFamily.Drone;
        return family;
    }

    /// <summary>The family of a DIS type, from its class chain (<see cref="VisualFamily.Unknown"/> when no class decides one).</summary>
    public static VisualFamily Of(DISEntityType dis)
    {
        var chain = EntityClassTable.Default.Chain(dis);
        for (int i = chain.Count - 1; i >= 0; i--)
            if (FamilyOfClass.TryGetValue(chain[i].Name, out var family)) return family;
        return VisualFamily.Unknown;
    }

    /// <summary>The fallback icon glyph of a family (null ⇒ the tool's glyph) — the Add Entity picker's S5 names.</summary>
    public static string? IconName(VisualFamily family) => family switch
    {
        VisualFamily.Unit => "_unit",
        VisualFamily.Person => "_person",
        VisualFamily.Tank or VisualFamily.SpArtillery => "_tank",   // ⭐ CE-1089 — a self-propelled gun is a tracked, turreted body on the 2-D map too
        VisualFamily.Afv => "_afv",
        VisualFamily.WheeledCar or VisualFamily.WheeledUtility => "_wheeled",
        _ => null,
    };

    /// <summary>
    /// ⭐ CE-1162 (§8 U1-a) — a person's kit from the most specific class in his chain that names one: <c>SecurityForce</c> ⇒ police,
    /// <c>Infantry</c> ⇒ soldier, else clothes. ⛔ SUPERSEDED: a hard-coded <c>DIS Category == 1</c>, which drew a police officer
    /// (category 3, CE-3220) in civilian clothes.
    /// </summary>
    public static PersonKit PersonKitOf(TkbTemplate t)
    {
        var chain = EntityClassTable.Default.Chain(t.DisType);
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            if (chain[i].Name == "SecurityForce") return PersonKit.Security;
            if (chain[i].Name == "Infantry") return PersonKit.Soldier;
        }
        return PersonKit.Civilian;
    }

    /// <summary>True when the person kit carries a rifle: a soldier, or a security-force officer whose type has a HELD weapon — a
    /// grenade is thrown, not carried in the hands, so the B-5 shield and baton men (stun grenades only) stay unarmed and the launcher man
    /// (a baton launcher) is armed. ⭐ CE-1162 — from the class, not a DIS category.</summary>
    public static bool IsSoldier(TkbTemplate t) => PersonKitOf(t) switch
    {
        PersonKit.Soldier => true,
        PersonKit.Security => t.GetDescriptor<Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto>()?.Mounts
                                  .Any(m => !m.Hands && m.Category != Fdp.Toolkit.Tkb.Domain.WeaponCategory.Grenade) == true,   // ⭐ B-8: empty hands arm nobody
        _ => false,
    };
}
