using Fdp.Interfaces;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Definitions.Tkb;
using Hrot.Map.Common;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐⭐ <c>CE-4650</c> (docs/designs/weapons/DESIGN_Bows_And_Crossbows.md §9 NA-1, R-4281 — the FUN pack: content separate, engine shared) — the fun pack's TKB types. Today ONE: the <b>Native American warrior</b> (2016): the bow
    /// (<see cref="UrbanCombatTkbCatalog.Bow"/>, mount 0) and a thrown tomahawk (the throwing hatchet, <see cref="MunitionTkbCatalog.TkbThrowingHatchet"/>, CE-3247 — mount 1), no armour, the INSURGENT's template and temperament
    /// (<see cref="UrbanCombatTkbCatalog.Insurgent"/> — no descriptor is copied here), his wardrobe <see cref="OutfitCatalog.NativeWarrior"/>. On a horse he is the same type (703, the firing seat, R-4269). ⚠ No knife-range melee: the engine has no
    /// melee MOUNT (<c>Melee</c> is the blow of a man's fists and a dog's bite), so none is declared.
    /// </summary>
    public static class FunTkbCatalog
    {
        /// <summary>The Native American warrior — the next free person type after the archer (2013), crossbowman (2014) and archery target (2015), measured free on every lane 2026-11-17.</summary>
        public const int TkbNativeWarrior = 2016;

        public static void RegisterAll(ITkbDatabase tkb)
        {
            if (tkb == null) throw new System.ArgumentNullException(nameof(tkb));
            UrbanCombatTkbCatalog.Insurgent(tkb, "Native American warrior", TkbNativeWarrior, OutfitCatalog.NativeWarrior, UrbanCombatTkbCatalog.Bow(), Tomahawk());
            // ⭐ CE-4634 D-1 — the dragon is built with the vehicle builder (flight, turret, combat, seats); the product's TKB is always a TkbDatabase
            if (tkb is TkbDatabase db) DefineDragon(new NedTkbBuilder(db));
        }

        /// <summary>The dragon's DIS subcategory under the attack-helicopter category (1.2.0.20.<b>99</b>): the air orders resolve as for any attack
        /// helicopter (<c>EntityClassTable</c> 1.2.*.20); the look tells a dragon from a helicopter by it (the ui NOTE).</summary>
        public const byte DragonDisSubcategory = 99;

        /// <summary>
        /// ⭐⭐ <c>CE-4634</c> D-1 (docs/designs/fun/DESIGN_Dragon.md DR-1…DR-7, R-3112) — the DRAGON, TKB <see cref="TkbEntityTypes.Dragon"/>: a helicopter in
        /// disguise. ⭐ It FLIES as a rotorcraft (<c>RotorcraftModel</c> unchanged: hover, route, land on its four legs — wheel contacts — and the player's
        /// manual flight); its BREATH is a flamethrower (mount 0, the dragon fire <see cref="MunitionTkbCatalog.TkbDragonFire"/>: 50 m, 9°) on TURRET 0 — the
        /// NECK (±70°, −60…+15°), leaving from the mouth (the muzzle); seat 0 the RIDER (Driver, ridden astride — <c>Drive</c> boards him and flies the
        /// dragon), seat 1 the PILLION (a firing seat over the flanks and rear: his own weapon, PF-1). An aircraft to radar and air defence; fireproof
        /// (the flame's <c>Ignition</c> never lights an aircraft). Its sound a beast's roar (<see cref="SoundSourceClass.Roar"/>), its fire the flame's.
        /// Game of Thrones size: 20 m nose to tail, 26 m wingspan. Starter numbers.
        /// </summary>
        public static void DefineDragon(NedTkbBuilder builder)
        {
            const long type = TkbEntityTypes.Dragon;
            builder
                .DefineVehicle(type, "Dragon")
                .WithVisual(type, v => { v.SymbolCode = "SFAPMH--------"; v.ColorHex = "#7A1F1F"; v.ShowLabel = true; v.Scale = 1.4f; })
                .WithFaction(type, 1)
                .WithBodyGeometry(type, new BodyGeometryDto
                {
                    ReferencePoint = ReferencePointKind.CentreOfGravity,
                    Length = 20f, Width = 26f, Height = 6f,   // wings spread; the box bottom = its feet (−3)
                    GroundContacts =
                    {
                        Leg("fore left",   2.5f,  1.6f), Leg("fore right",  2.5f, -1.6f),
                        Leg("hind left",  -2.5f,  1.8f), Leg("hind right", -2.5f, -1.8f),
                    },
                })
                .WithDisType(type, new DISEntityType { Kind = 1, Domain = 2, Country = 0, Category = 20, Subcategory = DragonDisSubcategory })
                .WithFlight(type, LocomotionClass.Rotorcraft, new FlightParamsDto
                {
                    MaxSpeedMps = 35f, ClimbMps = 8f, TurnRateDeg = 60f, EnduranceSeconds = 14400f,
                    ContactRadiusM = 1f, BodyRadiusM = 4f, BodyHeightM = 6f,
                    HoverHeightM = 25f, TurnRadiusM = 30f, AccelMps2 = 5f,
                })
                .WithTurrets(type, new TurretDto
                {
                    PivotX = 5f, PivotZ = 1.5f, TrunnionX = 4f,   // the neck: from the shoulders, 4 m to the head
                    TraverseMinDeg = -70f, TraverseMaxDeg = 70f, ElevationMinDeg = -60f, ElevationMaxDeg = 15f,
                    TraverseRateDegPerSec = 90f, ElevationRateDegPerSec = 60f,
                })
                .WithCombat(type, c =>
                {
                    c.MaxHealth = 1500;
                    c.BlastSusceptibility = 0.6f;
                    c.ArmorFront = 10; c.ArmorSide = 6; c.ArmorRear = 6; c.ArmorTop = 6;   // scales: a rifle barely hurts, a 23 mm gun does
                    c.EyeHeight = 2f;
                    c.SensorRange = 3000;
                    c.Weapons.Add(new Hrot.Map.Definitions.Tkb.WeaponMount
                    {
                        WeaponType = "dragon breath", Category = WeaponCategory.Flamethrower, TurretIndex = 0,
                        Ammunition = 600, MagazineSize = 600, RateOfFire = 600, Range = MunitionTkbCatalog.DragonFireReachM, MuzzleVelocity = 40f,
                        FireOnMove = true,
                        MuzzleOffset = new System.Numerics.Vector3(1.5f, 0f, 0f),   // the mouth, at the end of the head
                        AmmoTkb = (Fdp.Core.TkbTypeId)MunitionTkbCatalog.TkbDragonFire,
                    });
                })
                .WithCarrier(type, DragonSeats())
                .WithSignatures(type, UrbanCombatTkbCatalog.Sounds(1500f, SoundSourceClass.Roar, UrbanCombatTkbCatalog.FlameRoarRange * 1.5f, SoundSourceClass.Flame))
                .WithRadarCrossSection(type, 20f)
                .WithBehavior(type);
        }

        /// <summary>⭐ DR-7 — seat 0 the RIDER at the base of the neck (Driver, ridden astride: he flies it, his hand is the breath — not a firing seat),
        /// seat 1 the PILLION behind him (a firing seat over both flanks and the rear: his own weapon, AI or player — the motorcycle's PF-1).</summary>
        public static CarrierDefDto DragonSeats()
        {
            var c = new CarrierDefDto { Exposure = PassengerExposure.Open };
            c.Seats.Add(new SeatDto { Role = SeatRole.Driver, Exposed = true, X = 3f, Z = 2.4f, Pose = SeatPose.Ride });
            c.Seats.Add(new SeatDto { Role = SeatRole.Passenger, Exposed = true, X = 1.4f, Z = 2.3f, Pose = SeatPose.Ride,
                                      TraverseMinDeg = 100f, TraverseMaxDeg = 260f });
            return c;
        }

        private static GroundContactPoint Leg(string name, float x, float y)
            => new() { Name = name, X = x, Y = y, Z = -3f, WheelRadius = 0.4f, StrutLength = 0.8f, Retractable = false, Kind = GroundContactKind.Wheel };

        /// <summary>⭐ NA-1 — the TOMAHAWK as a mount: the thrown hatchet, two of them, thrown at 15 m/s inside 25 m (the grenade's throw).</summary>
        public static WeaponMountDto Tomahawk() => new()
        {
            Category = WeaponCategory.Grenade, InitialAmmunition = 2, MuzzleVelocity = 15f, Range = 25f, AmmoGuid = MunitionTkbCatalog.TkbThrowingHatchet,
        };
    }
}
