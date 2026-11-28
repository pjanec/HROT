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
    /// <summary>⭐ CE-6007 (showcase world, DESIGN_City_Battle_Showcase V-1b) — a bulldozer (class <c>Bulldozer</c>, a D9): two track units on a high-drive
    /// chassis, a hood, an open ROPS cab, a lifted blade on push arms with hydraulic rams, a rear ripper. ⛔ Not armoured-looking.</summary>
    Bulldozer,
    /// <summary>⭐ CE-4618 (docs/designs/air-attack/DESIGN_Radar_SAM.md §2a, §5 U-1) — the radar SAM truck (class <c>AirDefenceVehicle</c>, DIS <c>1.1.*.28.1</c>): an 8×8 wheeled
    /// carrier, a cab, a launcher of four canisters on a turntable (turret 0 — it turns and elevates through <c>TurretPose</c>) and a search-radar mast whose
    /// antenna spins while the radar radiates (<see cref="RadarActivity"/>).</summary>
    AirDefenceVehicle,
    /// <summary>⭐ CE-4621 (docs/designs/vehicle-tactics/DESIGN_Motorcycle.md §2a, U-2) — the motorcycle (class <c>Motorcycle</c>, DIS <c>1.1.*.80.1</c>, type 115): TWO wheels (they roll), a
    /// tank, a seat for two (the riders are men drawn seated at the seats' places, <see cref="Fdp.Toolkit.Tkb.Domain.SeatPose.Ride"/>); it BANKS into a turn (a drawing only).</summary>
    Motorcycle,
    // ── ⭐ CE-4623 (docs/designs/animals/DESIGN_Animals.md §6.1, AU-2) — the animals: nine families, keyed on the CLASS names backend declares (`Cattle` `Sheep` `Horse` `Dog` `Cat` `Bird`
    //    `Poultry` `Pig` `Goat`). Appended (a byte value others may store). A flock (type 707) is ONE entity of the Bird family drawn as N birds (<see cref="FlockBirds"/>). ──
    /// <summary>⭐ CE-4623 — cattle (class <c>Cattle</c>, DIS <c>3.1.*.200</c>, type 700).</summary>
    Cattle,
    /// <summary>⭐ CE-4623 — sheep (class <c>Sheep</c>, <c>3.1.*.201</c>, type 701).</summary>
    Sheep,
    /// <summary>⭐ CE-4623 — a horse: herd, riding or war (class <c>Horse</c>, <c>3.1.*.202</c>, types 702 / 703 / 704); the riding horse carries a rider (<see cref="BlockFigure.Mounted"/>).</summary>
    Horse,
    /// <summary>⭐ CE-4623 — a dog (class <c>Dog</c>, <c>3.1.*.203</c>, type 705).</summary>
    Dog,
    /// <summary>⭐ CE-4623 — a cat (class <c>Cat</c>, <c>3.1.*.204</c>, type 706).</summary>
    Cat,
    /// <summary>⭐ CE-4623 — a bird (class <c>Bird</c>, <c>3.2.*.205</c>): a lone bird (type 708) or a FLOCK (type 707, drawn as N birds).</summary>
    Bird,
    /// <summary>⭐ CE-4623 — a hen (class <c>Poultry</c>, <c>3.1.*.206</c>, type 709).</summary>
    Poultry,
    /// <summary>⭐ CE-4623 — a pig (class <c>Pig</c>, <c>3.1.*.207</c>, type 710).</summary>
    Pig,
    /// <summary>⭐ CE-4623 — a goat (class <c>Goat</c>, <c>3.1.*.208</c>, type 711).</summary>
    Goat,
    /// <summary>⭐ CE-4624 (docs/designs/minefields/DESIGN_Minefields.md §3a, MS-U4) — the mine-clearing vehicle (class <c>MineClearer</c>, DIS <c>1.1.*.9.2</c>, TKB 116 <c>Engineer_MineClearer</c>):
    /// a tracked engineer carrier with a line-charge launcher at the rear and a FLAIL drum on two arms in front that spins while the flail is on (<see cref="FlailActivity"/>).
    /// ⛔ Appended (a byte value others may store), AFTER the animals. ✅ Bound (2026-11-16) to terrain's class: <c>["MineClearer"]</c> in <see cref="VisualFamilies.FamilyOfClass"/>, type 116 through the real catalogue; the kit is scaled to the TKB's 9.0 × 4.4 × 3.2 m box.</summary>
    MineClearer,
    /// <summary>⭐ CE-4647 (docs/designs/air-attack/DESIGN_Air_Combat.md §4d, AC-15) — a twin-engine fighter (class <c>TwinEngineFighter</c>, the MiG-29, type 409, DIS <c>1.2.*.1.&lt;sub&gt;</c>): two canted fins,
    /// two nozzles, intakes under the wings. More specific than <c>Fighter</c>/<c>Jet</c>, so the MiG is not an F-16. ⛔ Appended (a byte value others may store).</summary>
    TwinEngineJet,
    /// <summary>⭐ CE-4649 part D (docs/designs/animals/DESIGN_Animals.md §4c, AN-21e) — a rabbit (class <c>Rabbit</c>, DIS <c>3.1.*.209</c>, TKB 715): a small hopping quadruped with long upright ears and a white tail.
    /// ⛔ Appended (a byte value others may store), AFTER <see cref="TwinEngineJet"/> — so it is NOT inside the <c>Cattle..Goat</c> range: <see cref="VisualFamilies.IsAnimal"/> names it.</summary>
    Rabbit,
    /// <summary>⭐ CE-4634 (docs/designs/fun/DESIGN_Dragon.md §8, D-4) — a dragon (class <c>Dragon</c>, DIS <c>1.2.*.20.99</c>, TKB 411): a winged quadruped that flies, lands
    /// and carries riders, its neck on turret 0 and its fire from the mouth. ⛔ Appended (a byte value others may store); NOT an animal (<see cref="VisualFamilies.IsAnimal"/>): it is a combat aircraft.</summary>
    Dragon,
    /// <summary>⭐ CE-6502 (docs/designs/engineering/DESIGN_Bridge_Laying.md §13 U-6) — the ASSAULT BRIDGE LAYER (class <c>AssaultBridgeLayer</c>, DIS <c>1.1.*.9.3</c>, TKB 118, an
    /// M104 Wolverine-like): a tank chassis carrying its folded scissor span on the roof — the span drawn only while it carries one (<see cref="Hrot.UI.Common.Map3D.BridgeLayerLook"/>).
    /// ⛔ Appended (a byte value others may store).</summary>
    BridgeLayer,
    /// <summary>⭐ CE-6502 — the FLOATING-BRIDGE ENGINEER (class <c>FloatingBridgeEngineer</c>, DIS <c>1.1.*.7.1</c>, TKB 119, an Improved Ribbon Bridge transporter): an 8×8 truck with a
    /// folded ribbon bay on its bed — the bay drawn only while it carries one. ⛔ Appended.</summary>
    FloatingBridge,
    /// <summary>⭐ CE-6503 (docs/designs/engineering/DESIGN_Bridge_Laying.md §13.4) — a LAID assault bridge (TKB 8825, a cultural feature): the TERRAIN draws its deck and ramps
    /// (the walkables units stand on); this family draws the bridge ON it — the side girders, the scissor hinge, the end plates, the two treadways. Sized by the entity's
    /// <c>ObstacleShape</c>. ⛔ Appended.</summary>
    LaidAssaultBridge,
    /// <summary>⭐ CE-6503 — a LAID floating bridge (TKB 8826): the terrain's deck, and here the pontoon bays in the water under it, the joints between bays and the curbs. ⛔ Appended.</summary>
    LaidFloatingBridge,
    /// <summary>⭐ SD-S5d (DESIGN_Simulated_Devices §3.10a D-163) — a DEVICE (a <c>Gen.Device</c> type): its shape is its kind's (<c>Hrot.Look.Devices.DeviceLooks</c>).</summary>
    Device,
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
        ["Bus"] = VisualFamily.WheeledCar, ["CargoTruck"] = VisualFamily.WheeledCar,   // ⭐ CE-11042 — drawn as before (sized per type) until they get kits of their own
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
        ["TwinEngineFighter"] = VisualFamily.TwinEngineJet,   // ⭐ CE-4647 — the MiG-29 (DIS 1.2.*.1.200, type 409): more specific than Fighter / Jet, so it is not an F-16
        ["AssaultBridgeLayer"] = VisualFamily.BridgeLayer,          // ⭐ CE-6502 — 1.1.*.9.3, TKB 118: more specific than LargeTrackedUtilityVehicle
        ["FloatingBridgeEngineer"] = VisualFamily.FloatingBridge,   // ⭐ CE-6502 — 1.1.*.7.1, TKB 119: more specific than the large wheeled utility vehicle
        ["MineClearer"] = VisualFamily.MineClearer,     // ⭐ CE-4624 — the mine-clearing vehicle (1.1.*.9.2, TKB 116): bound to terrain's class
        ["Bulldozer"] = VisualFamily.Bulldozer,         // ⭐ CE-6007 — more specific than LargeTrackedUtilityVehicle (which has no family of its own)
        ["Motorcycle"] = VisualFamily.Motorcycle,       // ⭐ CE-4621 — the motorcycle (DIS 1.1.*.80, type 115)
        ["AirDefenceVehicle"] = VisualFamily.AirDefenceVehicle,   // ⭐ CE-4618 — the radar SAM truck (DIS 1.1.*.28, type 114)
        // ⭐ CE-4623 (AU-2, AR-19) — the animals: each class is more specific than `Person` (Kind 3), so a cow is never drawn as a man (rail)
        ["Cattle"] = VisualFamily.Cattle, ["Sheep"] = VisualFamily.Sheep, ["Horse"] = VisualFamily.Horse, ["Dog"] = VisualFamily.Dog, ["Cat"] = VisualFamily.Cat,
        ["Bird"] = VisualFamily.Bird, ["Poultry"] = VisualFamily.Poultry, ["Pig"] = VisualFamily.Pig, ["Goat"] = VisualFamily.Goat, ["Rabbit"] = VisualFamily.Rabbit, ["Dragon"] = VisualFamily.Dragon,
    };

    /// <summary>The family of <paramref name="t"/> — its DIS type's. ⭐ CE-2139 (C3): a unit is <see cref="VisualFamily.Unit"/>
    /// because its DIS Kind is the unit Kind (11), no longer because it carries a <see cref="TkbCompositionDef"/>.</summary>
    /// <para>⭐ CE-1086 (§6n N-7) — a <see cref="VisualFamily.Jet"/> whose class chain names <c>Uav</c> and that FLIES AS A ROTORCRAFT
    /// (<c>Gen.Locomotion</c>) is a <see cref="VisualFamily.Drone"/> — a quadcopter, not a 15 m fighter. A fixed-wing UAV stays a jet.</para>
    public static VisualFamily Of(TkbTemplate t)
    {
        // ⭐ CE-6503 — the two laid-bridge obstacles share the cultural-feature DIS type (5.0.0.0.0) with every other obstacle: they are told apart by their TKB type
        if ((long)t.TkbType == Hrot.Map.Common.TkbEntityTypes.Obstacle_AssaultBridge) return VisualFamily.LaidAssaultBridge;
        if ((long)t.TkbType == Hrot.Map.Common.TkbEntityTypes.Obstacle_FloatingBridge) return VisualFamily.LaidFloatingBridge;
        if (Hrot.Look.Devices.DeviceLooks.IsDevice(t)) return VisualFamily.Device;   // ⭐ SD-S5d (D-163) — a device type, whatever its DIS class
        var family = Of(t.DisType);
        if (family == VisualFamily.Jet && t.GetDescriptor<Fdp.Toolkit.Tkb.Domain.LocomotionDto>()?.Class == Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft
            && EntityClassTable.Default.ChainContains(t.DisType, "Uav"))   // ⭐ CE-2394 — no LINQ
            return VisualFamily.Drone;
        return family;
    }

    /// <summary>⭐ CE-4623 (DESIGN_Animals §4a, AR-19) — true for the nine animal families: they take the animal kits and gaits, never the man's, and never burn.</summary>
    public static bool IsAnimal(VisualFamily f) => (f >= VisualFamily.Cattle && f <= VisualFamily.Goat) || f == VisualFamily.Rabbit;

    /// <summary>⭐ CE-4623 — the four-legged families (<see cref="PartRole.QuadFrontLeft"/> …): everything animal but the birds.</summary>
    public static bool IsQuadruped(VisualFamily f) => f is VisualFamily.Cattle or VisualFamily.Sheep or VisualFamily.Horse or VisualFamily.Dog or VisualFamily.Cat
                                                          or VisualFamily.Pig or VisualFamily.Goat or VisualFamily.Rabbit;

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
        VisualFamily.MineClearer or VisualFamily.BridgeLayer => "_tank",   // ⭐ CE-6502 — the bridge layer is a tracked body on the 2-D picker too
        VisualFamily.FloatingBridge => "_wheeled",   // ⭐ CE-6502 — a wheeled body   // ⭐ CE-4624 — a tracked body on the 2-D picker; its own glyph (assets/icons/_mineclearer, the mockup's) is a follow-up asset
        VisualFamily.Motorcycle => "_motorcycle",   // ⭐ CE-4621 — its own picker glyph (assets/icons/_motorcycle)
        VisualFamily.Cattle => "_cattle", VisualFamily.Sheep => "_sheep", VisualFamily.Horse => "_horse", VisualFamily.Dog => "_dog", VisualFamily.Cat => "_cat",
        VisualFamily.Bird => "_bird", VisualFamily.Poultry => "_poultry", VisualFamily.Pig => "_pig", VisualFamily.Goat => "_goat", VisualFamily.Rabbit => "_cat",   /* no _rabbit glyph yet: the closest small animal */   // ⭐ CE-4623 — the animals' picker glyphs (assets/icons/_<name>)
        VisualFamily.WheeledCar or VisualFamily.WheeledUtility or VisualFamily.AirDefenceVehicle => "_wheeled",   // ⭐ CE-4618 — a SAM truck is a wheeled body on the 2-D picker too
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
