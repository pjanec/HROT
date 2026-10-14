using Fdp.Core.CommandHierarchy;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Map.Common;

namespace Hrot.Map.Definitions.Tkb
{
    public static class NedTkbCatalog
    {
        /// <summary>⭐ AQ85 C (R-216) — the rifle's aim dispersion, mils (same as UrbanCombat's).</summary>
        public const float RifleDispersionMils = 6f;
        /// <summary>⭐ CE-1017 S0 — SISO-REF-010 country codes used by the built-in types; ⭐ CE-2138 — the codes and names live
        /// in <see cref="Hrot.Core.Tkb.CountryTable"/>.</summary>
        public const ushort UnitedStates = Hrot.Core.Tkb.CountryTable.UnitedStates;
        /// <summary>⭐ CE-1017 S0 — SISO-REF-010 Russia.</summary>
        public const ushort Russia = Hrot.Core.Tkb.CountryTable.Russia;

        /// <summary>⭐ M5 (DESIGN_Missiles_And_Ammo §2, §5) — a tank gun's loadout: its APFSDS, the HEAT-MP and the HE-FRAG.</summary>
        private static List<(long, int)> TankLoadout(long apfsds, int kinetic, int heat, int he)
            => new() { (apfsds, kinetic), (Hrot.Core.Tkb.MunitionTkbCatalog.TkbTankHeatMp, heat), (Hrot.Core.Tkb.MunitionTkbCatalog.TkbTankHeFrag, he) };

        /// <summary>⭐ M5 — a tank's coaxial machine gun: on the turret beside the gun, a 200-round belt (6 s to change), 1 in 5 traces;
        /// it fires on the move (a machine gun, D-L).</summary>
        private static WeaponMount Coax(System.Numerics.Vector3 muzzle) => new()
        {
            WeaponType = "7.62mm_coax", Ammunition = 2000, Range = 900, RateOfFire = 600, Penetration = 8, DamagePerHit = 30, DispersionMils = 3f,
            Category = WeaponCategory.MachineGun, TurretIndex = 0, MuzzleOffset = muzzle, MagazineSize = 200, ReloadSeconds = 6f,
            AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.Tkb762Ball, TracerAmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.Tkb762Tracer, TracerEvery = 5,
        };

        /// <summary>⭐ M9 (Exposure Cycle §6) — a technical: the pickup, its pintle <paramref name="turret"/> and <paramref name="weapon"/>.</summary>
        /// <param name="icon">an icon with embedded art (the HMMWV's until a pickup icon is drawn — CE1017's icon rail).</param>
        private static void Technical(NedTkbBuilder builder, long type, string name, string icon, TurretDto turret, WeaponMount weapon)
        {
            builder
                .DefineVehicle(type, name)
                .WithVisual(type, v => { v.IconName = icon; v.SymbolCode = "SHGPUCV--------"; v.ModelPath = "models/technical.obj"; v.ColorHex = "#C8C2B0"; v.Scale = 0.9f; })
                .WithPhysics(type, p =>
                {
                    p.Mass = 2_100; p.Length = 5.3f; p.Width = 1.9f; p.Height = 1.9f;
                    p.MaxSpeed = 25f; p.MaxSpeedRev = 6f; p.Acceleration = 3.5f; p.TurnRate = 35f;
                    p.Mobility = TerrainMobility.Wheeled;
                    p.Preset = CarKinem.Core.VehicleClass.PersonalCar;   // drives like a car, not the Wheeled ⇒ Truck preset
                })
                .WithCombat(type, c =>
                {
                    c.ArmorFront = 5; c.ArmorSide = 5; c.ArmorRear = 5; c.ArmorTop = 3;
                    c.MaxHealth = 300;
                    c.MobilityKillBelowFraction = 0.6f;
                    c.EyeHeight = 2.2f;   // standing in the bed
                    c.SensorRange = 2000;
                    c.Weapons.Add(weapon);
                })
                .WithTurrets(type, turret)
                .WithSignatures(type, new SignaturesDto { Acoustic = new AcousticSignatureDto
                {
                    MovingAudibleRange = 100f, MovingClass = SoundSourceClass.WheeledEngine,
                    FiringAudibleRange = 1000f, FiringClass = SoundSourceClass.HeavyWeapon,
                } })
                .WithFaction(type, 2)
                .WithBehavior(type)
                .WithDisType(type, new DISEntityType { Kind = 1, Domain = 1, Category = 6 });   // 1.1.*.6 small wheeled utility vehicle
        }

        public static void RegisterAll(TkbDatabase tkbDb)
        {
            var builder = new NedTkbBuilder(tkbDb);
            
            // M1 Abrams
            builder
                .DefineVehicle(TkbEntityTypes.Tank_M1Abrams, "M1 Abrams")
                .WithVisual(TkbEntityTypes.Tank_M1Abrams, v =>
                {
                    v.IconName = "m1_abrams";   // CE-1017 S0
                    v.SymbolCode = "SFGPUCIZ-------";
                    v.ModelPath = "models/m1_abrams.obj";
                    v.ColorHex = "#2E4057";
                    v.Scale = 1.2f;
                    v.ShowLabel = true;
                })
                .WithPhysics(TkbEntityTypes.Tank_M1Abrams, p =>
                {
                    p.Mass = 61_000; // kg
                    p.Length = 7.93f;
                    p.Width = 3.66f;
                    p.Height = 2.44f;
                    p.MaxSpeed = 20.0f; // m/s
                    p.MaxSpeedRev = 12.0f; // m/s
                    p.Acceleration = 2.5f;
                    p.TurnRate = 15.0f;
                    p.Mobility = TerrainMobility.Tracked;
                })
                .WithCombat(TkbEntityTypes.Tank_M1Abrams, c =>
                {
                    c.ArmorFront = 600; // mm RHA
                    c.ArmorSide = 350;
                    c.ArmorRear = 200;
                    // ⭐ M6 (DESIGN_Armour_Hit_Model §2, D-F) — the roof, the composite front against a shaped charge, and the health
                    //   STATED (the value front × 5 gave it, so no fight moves on that number)
                    c.ArmorTop = 60; c.ArmorFrontCe = 900; c.ArmorSideCe = 450;
                    c.MaxHealth = 3000;
                    c.EyeHeight = 2.5f;   // ⭐ M5 — the gunner's sight / the commander's hatch
                    c.Weapons.Add(new WeaponMount
                    {
                        WeaponType = "120mm_M256",
                        Ammunition = 42,
                        Range = 3000,
                        RateOfFire = 6,
                        Penetration = 650,     // CE-3071 — mm RHA (design §9 calibration)
                        DamagePerHit = 1200,
                        Category = WeaponCategory.TankGun, TurretIndex = 0,   // ⭐ WL-1
                        MuzzleOffset = new System.Numerics.Vector3(5.3f, 0f, 0f),   // ⭐ E4 — the 120 mm L/44 barrel from its trunnion
                        AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.Tkb120mmApfsds,   // ⭐ LR-1 (R-271)
                        // ⭐ M5 (DESIGN_Missiles_And_Ammo §2) — the 42 rounds as a loadout the weapon brain picks from: APFSDS for armour,
                        //   HEAT-MP, HE-FRAG for men and soft targets
                        Loadout = TankLoadout(Hrot.Core.Tkb.MunitionTkbCatalog.Tkb120mmApfsds, 20, 10, 12),
                    });
                    c.Weapons.Add(Coax(new System.Numerics.Vector3(1.0f, 0.5f, 0.1f)));   // ⭐ M5 — the coaxial M240
                    c.SensorRange = 8000;
                })
                // ⭐ WL-1 (DESIGN_Weapon_Layers W-9) — the turret: pivot aft of centre, −10…+20° gun, 40°/s traverse.
                .WithTurrets(TkbEntityTypes.Tank_M1Abrams, new TurretDto { PivotX = -0.55f, PivotZ = 1.9f, TrunnionX = 1.65f,
                    ElevationMinDeg = -10f, ElevationMaxDeg = 20f, TraverseRateDegPerSec = 40f, ElevationRateDegPerSec = 10f })
                .WithFaction(TkbEntityTypes.Tank_M1Abrams, 1)
                .WithBehavior(TkbEntityTypes.Tank_M1Abrams)
                .WithDisType(TkbEntityTypes.Tank_M1Abrams, new DISEntityType { Kind = 1, Domain = 1, Country = UnitedStates, Category = 1 });
            
            // Bradley IFV
            builder
                .DefineVehicle(TkbEntityTypes.IFV_Bradley, "M2 Bradley IFV")
                .WithVisual(TkbEntityTypes.IFV_Bradley, v =>
                {
                    v.IconName = "m2_bradley";   // CE-1017 S0
                    v.SymbolCode = "SFGPUCI--------";
                    v.ModelPath = "models/bradley.obj";
                    v.ColorHex = "#2E4057";
                    v.Scale = 1.0f;
                })
                .WithPhysics(TkbEntityTypes.IFV_Bradley, p =>
                {
                    p.Mass = 27_000;
                    p.Length = 6.55f;
                    p.Width = 3.6f;
                    p.Height = 2.98f;
                    p.MaxSpeed = 18.0f;
                    p.Acceleration = 3.0f;
                    p.TurnRate = 20.0f;
                    p.Mobility = TerrainMobility.Tracked;
                })
                .WithCombat(TkbEntityTypes.IFV_Bradley, c =>
                {
                    c.ArmorFront = 100;
                    c.ArmorSide = 60;
                    c.ArmorRear = 40;
                    // ⭐ M6 — aluminium + spaced steel: the roof, the spaced front against a shaped charge, the health as before (100 × 5)
                    c.ArmorTop = 30; c.ArmorFrontCe = 150; c.ArmorSideCe = 80;
                    c.MaxHealth = 500;
                    c.EyeHeight = 2.7f;   // ⭐ M5
                    c.Weapons.Add(new WeaponMount { WeaponType = "25mm_M242", Ammunition = 300, Range = 2500, RateOfFire = 200, Penetration = 60, DamagePerHit = 60,
                        AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.Tkb25mmApdsT,   // ⭐ LR-1
                        Category = WeaponCategory.Autocannon, TurretIndex = 0,   // ⭐ WL-1 — the 25 mm and the TOW share one turret
                        MuzzleOffset = new System.Numerics.Vector3(2.6f, 0f, 0f) });   // ⭐ E4

                    c.Weapons.Add(new WeaponMount { WeaponType = "TOW_ATGM", Ammunition = 7, Range = 3750, RateOfFire = 2, Penetration = 800, DamagePerHit = 2000,
                        AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.TkbTowMissile,   // ⭐ LR-1
                        AimSeconds = 1.5f,   // ⭐ M5 (§5) — the gunner's lay before the launch; the TOW then flies command-guided (MissileDto)
                        Category = WeaponCategory.Atgm, TurretIndex = 0,
                        MuzzleOffset = new System.Numerics.Vector3(0.4f, 1.0f, 0.35f) });   // ⭐ E4 — the launcher on the turret's left
                    c.SensorRange = 5000;
                })
                .WithTurrets(TkbEntityTypes.IFV_Bradley, new TurretDto { PivotX = -0.33f, PivotZ = 2.3f, TrunnionX = 0.65f,
                    ElevationMinDeg = -10f, ElevationMaxDeg = 60f, TraverseRateDegPerSec = 60f, ElevationRateDegPerSec = 30f })
                .WithFaction(TkbEntityTypes.IFV_Bradley, 1)
                .WithCarrier(TkbEntityTypes.IFV_Bradley, Fdp.Toolkit.Tkb.Domain.CarrierDefDto.PassengerSeats(6))   // ⭐ CE-2151 — six dismounts; the crew stay abstract (R-2009 B5)
                .WithBehavior(TkbEntityTypes.IFV_Bradley)
                .WithDisType(TkbEntityTypes.IFV_Bradley, new DISEntityType { Kind = 1, Domain = 1, Country = UnitedStates, Category = 2 });
            
            // HMMWV
            builder
                .DefineVehicle(TkbEntityTypes.Truck_HMMWV, "HMMWV")
                .WithVisual(TkbEntityTypes.Truck_HMMWV, v =>
                {
                    v.IconName = "hmmwv";   // CE-1017 S0
                    v.SymbolCode = "SFGPUUS--------";
                    v.ModelPath = "models/hmmwv.obj";
                    v.ColorHex = "#3E5641";
                    v.Scale = 0.9f;
                })
                .WithPhysics(TkbEntityTypes.Truck_HMMWV, p =>
                {
                    p.Mass = 2_400;
                    p.Length = 4.57f;
                    p.Width = 2.16f;
                    p.Height = 1.83f;
                    p.MaxSpeed = 25.0f;
                    p.Acceleration = 4.0f;
                    p.TurnRate = 30.0f;
                    p.Mobility = TerrainMobility.Wheeled;
                })
                .WithFaction(TkbEntityTypes.Truck_HMMWV, 1)
                .WithCarrier(TkbEntityTypes.Truck_HMMWV, Fdp.Toolkit.Tkb.Domain.CarrierDefDto.PassengerSeats(3))   // ⭐ CE-2151 — three beside the (abstract) driver
                .WithBehavior(TkbEntityTypes.Truck_HMMWV)
                .WithDisType(TkbEntityTypes.Truck_HMMWV, new DISEntityType { Kind = 1, Domain = 1, Country = UnitedStates, Category = 6 });   // CE-1017 S0: 6 = small wheeled utility vehicle (SISO-REF-010); 3 read "armored utility vehicle"
            
            // T-72 (OPFOR)
            builder
                .DefineVehicle(TkbEntityTypes.Tank_T72, "T-72")
                .WithVisual(TkbEntityTypes.Tank_T72, v =>
                {
                    v.IconName = "t72";   // CE-1017 S0
                    v.SymbolCode = "SHGPUCIZ-------"; // Hostile
                    v.ModelPath = "models/t72.obj";
                    v.ColorHex = "#8B0000";
                    v.Scale = 1.1f;
                })
                .WithPhysics(TkbEntityTypes.Tank_T72, p =>
                {
                    p.Mass = 41_000;
                    p.Length = 6.95f;
                    p.Width = 3.59f;
                    p.Height = 2.23f;
                    p.MaxSpeed = 17.0f;
                    p.MaxSpeedRev = 10.0f;
                    p.Acceleration = 2.0f;
                    p.TurnRate = 12.0f;
                    p.Mobility = TerrainMobility.Tracked;
                })
                .WithCombat(TkbEntityTypes.Tank_T72, c =>
                {
                    c.ArmorFront = 500;
                    c.ArmorSide = 250;
                    c.ArmorRear = 150;
                    // ⭐ M6 — the roof, Kontakt ERA on the front and sides against a shaped charge, the health as before (500 × 5)
                    c.ArmorTop = 40; c.ArmorFrontCe = 700; c.ArmorSideCe = 350;
                    c.MaxHealth = 2500;
                    c.EyeHeight = 2.2f;   // ⭐ M5
                    c.Weapons.Add(new WeaponMount { WeaponType = "125mm_2A46", Ammunition = 39, Range = 2800, RateOfFire = 8, Penetration = 600, DamagePerHit = 1100,
                        AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.Tkb125mmApfsds,   // ⭐ LR-1
                        Category = WeaponCategory.TankGun, TurretIndex = 0,   // ⭐ WL-1
                        MuzzleOffset = new System.Numerics.Vector3(6.0f, 0f, 0f),   // ⭐ E4 — the 125 mm L/48 barrel
                        Loadout = TankLoadout(Hrot.Core.Tkb.MunitionTkbCatalog.Tkb125mmApfsds, 18, 9, 12) });   // ⭐ M5 — the 39 as a loadout
                    c.Weapons.Add(Coax(new System.Numerics.Vector3(1.0f, 0.5f, 0.1f)));   // ⭐ M5 — the coaxial PKT (7.62 × 51 stands in)
                    c.SensorRange = 6000;
                })
                .WithTurrets(TkbEntityTypes.Tank_T72, new TurretDto { PivotX = -0.3f, PivotZ = 1.75f, TrunnionX = 1.2f,
                    ElevationMinDeg = -6f, ElevationMaxDeg = 14f, TraverseRateDegPerSec = 24f, ElevationRateDegPerSec = 6f })
                .WithFaction(TkbEntityTypes.Tank_T72, 2)
                .WithBehavior(TkbEntityTypes.Tank_T72)
                .WithDisType(TkbEntityTypes.Tank_T72, new DISEntityType { Kind = 1, Domain = 1, Country = Russia, Category = 1 });

            // ⭐⭐ M9 (docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md §6, D-K) — the TECHNICALS: an unarmoured pickup
            //   (5 mm all round, 300 hp, a mobility kill below 60 %) with one weapon on a pintle in the bed. No armour, so speed and
            //   surprise are the protection: shoot and scoot (the behaviour lane's BasicVehicleSop). The gunner is the vehicle (D-K).
            Technical(builder, TkbEntityTypes.Technical_Hmg, "Technical (HMG)", "hmmwv",
                new TurretDto { PivotX = -1.3f, PivotZ = 1.9f, TrunnionX = 0.4f,                       // a pintle: all round, up to +70° (drones)
                    TraverseMinDeg = -180f, TraverseMaxDeg = 180f, ElevationMinDeg = -10f, ElevationMaxDeg = 70f,
                    TraverseRateDegPerSec = 90f, ElevationRateDegPerSec = 45f },
                new WeaponMount { WeaponType = "12.7mm_DShK", Ammunition = 300, Range = 1500, RateOfFire = 600, DamagePerHit = 60,   // penetration: the round's own (25 mm)
                    DispersionMils = 4f, MagazineSize = 50, ReloadSeconds = 8f, FireOnMove = true,
                    AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.TkbHmgAp, Category = WeaponCategory.MachineGun, TurretIndex = 0,
                    MuzzleOffset = new System.Numerics.Vector3(1.6f, 0f, 0f) });
            Technical(builder, TkbEntityTypes.Technical_Atgm, "Technical (ATGM)", "hmmwv",
                new TurretDto { PivotX = -1.3f, PivotZ = 1.9f, TrunnionX = 0.3f,
                    TraverseMinDeg = -120f, TraverseMaxDeg = 120f, ElevationMinDeg = -10f, ElevationMaxDeg = 20f,
                    TraverseRateDegPerSec = 40f, ElevationRateDegPerSec = 20f },
                new WeaponMount { WeaponType = "Kornet_ATGM", Ammunition = 4, Range = 5000, RateOfFire = 2, DamagePerHit = 2000,   // penetration: the missile's own (1100 mm tandem)
                    AimSeconds = 1.5f, MagazineSize = 1, ReloadSeconds = 20f, FireOnMove = false,   // halts for the flight (command guidance)
                    AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.TkbKornetMissile, Category = WeaponCategory.Atgm, TurretIndex = 0,
                    MuzzleOffset = new System.Numerics.Vector3(0.6f, 0f, 0.2f) });
            
            // Infantry Rifleman
            builder
                .DefineVehicle(TkbEntityTypes.Infantry_Rifleman, "Rifleman")
                .WithVisual(TkbEntityTypes.Infantry_Rifleman, v =>
                {
                    v.IconName = "rifleman";   // CE-1017 S0
                    v.SymbolCode = "SFGPUCI--------";
                    v.ModelPath = "models/soldier.obj";
                    v.ColorHex = "#556B2F";
                    v.Scale = 0.6f;
                })
                .WithPhysics(TkbEntityTypes.Infantry_Rifleman, p =>
                {
                    p.Mass = 100;
                    p.Length = 0.6f;
                    p.Width = 0.4f;
                    p.Height = 1.75f;
                    p.MaxSpeed = 2.5f; // Walking
                    p.Acceleration = 1.0f;
                    p.TurnRate = 90.0f;
                    p.Mobility = TerrainMobility.Infantry;
                })
                .WithCombat(TkbEntityTypes.Infantry_Rifleman, c =>
                {
                    c.ArmorFront = 5; // Body armor
                    c.Weapons.Add(new WeaponMount { WeaponType = "M4_Carbine", Ammunition = 210, Range = 300, RateOfFire = 700, Penetration = 5, DamagePerHit = 25,
                        DispersionMils = RifleDispersionMils,   // ⭐ AQ85 C — the first opt-in (~50 % at 100 m standing still)
                        AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.Tkb556Ball, TracerAmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.Tkb556Tracer, TracerEvery = 5,   // ⭐ LR-1 — 1 in 5 traces
                        Category = WeaponCategory.SmallArms });
                    c.SensorRange = 500;
                })
                .WithFaction(TkbEntityTypes.Infantry_Rifleman, 1)
                .WithInjury(TkbEntityTypes.Infantry_Rifleman, new Fdp.Toolkit.Tkb.Domain.InjuryDefDto())   // ⭐ CE-2155 — wounded, incapacitated, bleeds out (R-2011)
                .WithCarrier(TkbEntityTypes.Infantry_Rifleman, Fdp.Toolkit.Tkb.Domain.CarrierDefDto.PersonCarrier())   // ⭐ CE-2152 — carries or drags one casualty (R-2011 C1)
                .WithBehavior(TkbEntityTypes.Infantry_Rifleman)
                .WithDisType(TkbEntityTypes.Infantry_Rifleman, new DISEntityType { Kind = 3, Domain = 1, Country = UnitedStates, Category = 1 });
            
            // Tank Platoon (Composite)
            builder
                .DefineVehicle(TkbEntityTypes.Unit_TankPlatoon, "Tank Platoon")
                .WithVisual(TkbEntityTypes.Unit_TankPlatoon, v =>
                {
                    v.IconName = "tank_platoon";   // CE-1017 S0
                    v.SymbolCode = "SFGPUCIZ--H----"; // Platoon echelon
                    v.ColorHex = "#0000FF";
                    v.Scale = 1.5f;
                })
                .WithFaction(TkbEntityTypes.Unit_TankPlatoon, 1)
                .WithBehavior(TkbEntityTypes.Unit_TankPlatoon)
                .AsComposite(TkbEntityTypes.Unit_TankPlatoon, comp =>
                {
                    comp.Subordinates.Add(new TkbChildSlot { TkbType = TkbEntityTypes.Tank_M1Abrams, Count = 4, Designation = TacticalDesignation.Wingman });
                    comp.Echelon = "Platoon";
                    comp.AutoCreateChildren = false; // Manual creation
                })
                .AsLandUnit(TkbEntityTypes.Unit_TankPlatoon, Hrot.Core.Tkb.UnitArm.Armor, UnitedStates);   // ⭐ CE-2139 — 11.1.225.2.4.1
            
            // Infantry Squad (Composite)
            builder
                .DefineVehicle(TkbEntityTypes.Unit_InfantrySquad, "Infantry Squad")
                .WithVisual(TkbEntityTypes.Unit_InfantrySquad, v =>
                {
                    v.IconName = "infantry_squad";   // CE-1017 S0
                    v.SymbolCode = "SFGPUCI---H----"; // Squad echelon
                    v.ColorHex = "#0000FF";
                    v.Scale = 1.2f;
                })
                .WithFaction(TkbEntityTypes.Unit_InfantrySquad, 1)
                .WithBehavior(TkbEntityTypes.Unit_InfantrySquad)   // ⭐ CE-2125 (R-272) — the squad's body-less commander runs the squad SOP and drills
                .AsComposite(TkbEntityTypes.Unit_InfantrySquad, comp =>
                {
                    comp.Subordinates.Add(new TkbChildSlot { TkbType = TkbEntityTypes.Infantry_Officer, Count = 1, Designation = TacticalDesignation.SquadLeader });
                    comp.Subordinates.Add(new TkbChildSlot { TkbType = TkbEntityTypes.Infantry_Rifleman, Count = 9, Designation = TacticalDesignation.Wingman });
                    comp.Echelon = "Squad";
                    comp.AutoCreateChildren = false;
                })
                .AsLandUnit(TkbEntityTypes.Unit_InfantrySquad, Hrot.Core.Tkb.UnitArm.Infantry, UnitedStates);   // ⭐ CE-2139 — 11.1.225.1.2.1

            // Tank Platoon (Auto-Spawning)
            builder
                .DefineVehicle(TkbEntityTypes.Unit_TankPlatoon_Auto, "Tank Platoon (Auto Spawn)")
                .WithVisual(TkbEntityTypes.Unit_TankPlatoon_Auto, v =>
                {
                    v.IconName = "tank_platoon";   // CE-1017 S0
                    v.SymbolCode = "SFGPUCIZ--H----"; // Platoon echelon
                    v.ColorHex = "#0000FF";
                    v.Scale = 1.5f;
                })
                .WithFaction(TkbEntityTypes.Unit_TankPlatoon_Auto, 1)
                .WithBehavior(TkbEntityTypes.Unit_TankPlatoon_Auto)
                .AsComposite(TkbEntityTypes.Unit_TankPlatoon_Auto, comp =>
                {
                    comp.Subordinates.Add(new TkbChildSlot { TkbType = TkbEntityTypes.Tank_M1Abrams, Count = 4, Designation = TacticalDesignation.Wingman });
                    comp.Echelon = "Platoon";
                    comp.AutoCreateChildren = true; // The engine will now auto-spawn 4x M1 Abrams when this is created
                })
                .AsLandUnit(TkbEntityTypes.Unit_TankPlatoon_Auto, Hrot.Core.Tkb.UnitArm.Armor, UnitedStates, variant: 2);   // ⭐ CE-2139 — the auto-spawning variant

            // ⭐⭐ CE-1041 — AIRCRAFT. CG-referenced bodies resting on their gear: the gear positions are what landing and parking
            //   use (BodyGeometry.RestingPose). ⛔ No WithPhysics — that adds VehicleParametersDto, i.e. CAR kinematics; an aircraft
            //   carries its size in Body.Geometry. ⛔ No WithBehavior — there is no flight model for a brain to drive yet.
            //   Public figures, rounded; the gear's X is from the CG. DIS subcategory left 0 (generic) — not verified.
            //   📄 docs/DESIGN_Body_Geometry_And_Ground_Contact.md G4.
            DefineAircraft(builder, TkbEntityTypes.Heli_UH60, "UH-60A Black Hawk", "SFAPMHU-------", "#56604F",
                new DISEntityType { Kind = 1, Domain = 2, Country = UnitedStates, Category = 21 }, Uh60Geometry());
            // ⭐ CE-1083 (docs/DESIGN_Map_3D_Mode.md §6m C-3) — the same Black Hawk with its cabin doors off: Gen.Carrier OPEN, eight seats,
            //   four in each door on the cabin floor's edge (the drawn fuselage: ±1.15 m wide, its floor ~0.9 m under the CG), legs out.
            DefineAircraft(builder, TkbEntityTypes.Heli_UH60_DoorsOff, "UH-60A Black Hawk (doors off)", "SFAPMHU-------", "#56604F",
                new DISEntityType { Kind = 1, Domain = 2, Country = UnitedStates, Category = 21 }, Uh60Geometry());
            builder.WithCarrier(TkbEntityTypes.Heli_UH60_DoorsOff, DoorSeats());
            // ⭐ Air Ops slice 2 (docs/designs/air-attack/DESIGN_Air_Operations.md §5, §6) — it FLIES: a route takes it off its gear, rounds
            //   its corners at 60 m, holds 30 m over the ground and sets it down on a Ground waypoint. Public figures, rounded: cruise
            //   ~70 m/s, climb ~8 m/s; the collider is the fuselage, not the rotor disc. The plain UH-60 (400) stays a static body.
            builder.WithFlight(TkbEntityTypes.Heli_UH60_DoorsOff, Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft, new FlightParamsDto
            {
                MaxSpeedMps = 70f, ClimbMps = 8f, TurnRateDeg = 30f, EnduranceSeconds = 7200f,
                ContactRadiusM = 1f, BodyRadiusM = 1.5f, BodyHeightM = 3.5f,
                HoverHeightM = 30f, TurnRadiusM = 60f,
            });
            DefineAircraft(builder, TkbEntityTypes.Jet_F16, "F-16C Fighting Falcon", "SFAPMFF-------", "#8A929C",
                new DISEntityType { Kind = 1, Domain = 2, Country = UnitedStates, Category = 1 },
                new Fdp.Toolkit.Tkb.Domain.BodyGeometryDto
                {
                    ReferencePoint = Fdp.Toolkit.Tkb.Domain.ReferencePointKind.CentreOfGravity,
                    Length = 15.06f, Width = 9.96f, Height = 4.88f,
                    BodyCentreX = 0.50f, BodyCentreZ = 0.54f,          // box bottom = the tyres (−1.90)
                    GroundContacts =
                    {
                        Wheel("nose",        3.4f,  0f,    -1.90f, 0.24f, 1.2f, retractable: true),
                        Wheel("main left",  -0.6f,  1.18f, -1.90f, 0.36f, 1.0f, retractable: true),
                        Wheel("main right", -0.6f, -1.18f, -1.90f, 0.36f, 1.0f, retractable: true),
                    },
                });
            DefineAircraft(builder, TkbEntityTypes.Cargo_C130, "C-130H Hercules", "SFAPMFC-------", "#7A8076",
                new DISEntityType { Kind = 1, Domain = 2, Country = UnitedStates, Category = 4 },
                new Fdp.Toolkit.Tkb.Domain.BodyGeometryDto
                {
                    ReferencePoint = Fdp.Toolkit.Tkb.Domain.ReferencePointKind.CentreOfGravity,
                    Length = 29.79f, Width = 40.41f, Height = 11.66f,
                    BodyCentreX = 1.50f, BodyCentreZ = 2.83f,          // box bottom = the tyres (−3.00)
                    GroundContacts =
                    {
                        Wheel("nose",        9.4f,  0f,    -3.00f, 0.50f, 1.6f, retractable: true),
                        Wheel("main left",  -0.3f,  2.18f, -3.00f, 0.60f, 1.2f, retractable: true),
                        Wheel("main right", -0.3f, -2.18f, -3.00f, 0.60f, 1.2f, retractable: true),
                    },
                });
            DefineFpvDrone(builder);   // ⭐ M7
            DefineAttackPlane(builder);   // ⭐ Air Ops slice 4

            // Tactical graphic: area overlay
            var areaTemplate = new TkbTemplate("TacGraphic_Area", TkbEntityTypes.TacGraphic_Area);
            // TKB-014 (Phase 6): ECS components will be injected by translators.
            tkbDb.Register(areaTemplate);

            // Tactical graphic: route entity (ROUTES1-T003)
            // RoutePlan (managed) is added by RouteTkbExtensions.ApplyRoutePlanToBlueprint()
            // in Phase 6 via translator.
            var routeTemplate = new TkbTemplate("TacGraphic_Route", TkbEntityTypes.TacGraphic_Route);
            // TKB-014 (Phase 6): ECS components will be injected by translators.
            tkbDb.Register(routeTemplate);

            // ⭐ Buildings Stage 5b — a terrain door (📄 docs/DESIGN_Building_Interiors.md §3j). A bare template like the two above:
            //   its state (DoorState) and key (TerrainObjectKey) arrive with the creation request on the creator and through the
            //   EntityDoorState descriptor everywhere else — nothing for a TKB translator to inject.
            tkbDb.Register(new TkbTemplate("Door", TkbEntityTypes.Door));

            RegisterObstacles(tkbDb);
        }

        /// <summary>
        /// ⭐ CE-3136 P-7a (O4, R-242) — the four starter STATIC OBSTACLES: terrain made of a wall-library material
        /// (📄 docs/DESIGN_Peek_And_Fire.md §9). ⚠ The sizes and the car-body material are starter values to tune (§3c).
        /// ⭐ CE-1037 — public so a host with a smaller catalogue (the editor test harness) registers THESE, never a copy.
        /// </summary>
        public static void RegisterObstacles(TkbDatabase tkbDb)
        {
            RegisterObstacle(tkbDb, "Car",            TkbEntityTypes.Obstacle_Car,           4.5f, 1.8f, 1.5f, "car-body");
            RegisterObstacle(tkbDb, "Sandbag wall",   TkbEntityTypes.Obstacle_SandbagWall,   3.0f, 0.6f, 1.0f, "sandbags");
            RegisterObstacle(tkbDb, "Concrete block", TkbEntityTypes.Obstacle_ConcreteBlock, 2.0f, 1.0f, 1.0f, "concrete");
            RegisterObstacle(tkbDb, "Crate",          TkbEntityTypes.Obstacle_Crate,         1.2f, 1.0f, 1.0f, "fence-wood");
        }

        private static void RegisterObstacle(TkbDatabase tkbDb, string name, long type, float length, float width, float height, string material)
        {
            var t = new TkbTemplate(name, type);
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.StaticObstacleDto { Length = length, Width = width, Height = height, Material = material });
            tkbDb.Register(t);
        }

        /// <summary>⭐ CE-1041 — an aircraft: master, visual, DIS, faction and its body geometry — no car kinematics, no brain.</summary>
        /// <summary>
        /// ⭐⭐ M7 (docs/designs/vehicle-tactics/DESIGN_FPV_Drone.md §2) — the FPV DRONE: a Rotorcraft (Motion Domains MD-H, moved by the
        /// Air module's <c>RotorcraftModel</c>), 30 m/s, climbs 8 m/s, a 10-minute battery; a 0.3 m body with 10 hp; its weapon is its
        /// WARHEAD (munition 6016, reach = its contact radius — it does not shoot, it flies into the target: <c>DroneImpactSystem</c>);
        /// heard as rotors (~300 m), sees 1500 m; a brain (its behaviour, <c>FpvAttack</c>, is the behaviour lane's). Hostile.
        /// </summary>
        private static void DefineFpvDrone(NedTkbBuilder builder)
        {
            const long type = TkbEntityTypes.Drone_Fpv;
            var flight = new FlightParamsDto();
            builder
                .DefineVehicle(type, "FPV drone")
                .WithVisual(type, v => { v.SymbolCode = "SHAPMFQ-------"; v.ColorHex = "#4A4A4A"; v.Scale = 0.3f; v.ShowLabel = true; })
                .WithFlight(type, LocomotionClass.Rotorcraft, flight)
                .WithCombat(type, c =>
                {
                    c.MaxHealth = 10;
                    c.EyeHeight = 0.1f;   // the camera on its nose
                    c.SensorRange = 1500;
                    c.Weapons.Add(new WeaponMount { WeaponType = "FPV_warhead", Ammunition = 1, Range = flight.ContactRadiusM, DamagePerHit = 450,
                        AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.TkbFpvWarhead });   // penetration: the warhead's own (400 mm)
                })
                .WithSignatures(type, new SignaturesDto { Acoustic = new AcousticSignatureDto
                    { MovingAudibleRange = 300f, MovingClass = SoundSourceClass.Rotor, DetonationAudibleRange = 800f, DetonationClass = SoundSourceClass.Explosion } })
                .WithFaction(type, 2)
                .WithBehavior(type)
                .WithDisType(type, new DISEntityType { Kind = 1, Domain = 2, Category = 50 });   // air, 50 = unmanned (generic country)
        }

        /// <summary>
        /// ⭐ Air Ops slice 4 (docs/designs/air-attack/DESIGN_Air_Operations.md §6, §8) — an A-10 class attack aircraft: it FLIES (FixedWing:
        /// cruise 150 m/s, never under its 60 m/s stall, banks to 60° ⇒ turns of ~1.3 km), flies routes 300 m over the ground, and drops
        /// four Mk-82s (munition 6022, a Bomb mount) at the Release profile's cue. Public figures, rounded; its gear for the 3-D map (it
        /// does not land in v1). A brain (its behaviour is the behaviour lane's, slice 5). Friendly.
        /// </summary>
        private static void DefineAttackPlane(NedTkbBuilder builder)
        {
            const long type = TkbEntityTypes.Jet_AttackA10;
            DefineAircraft(builder, type, "A-10C Thunderbolt II", "SFAPMFA-------", "#7A8076",
                new DISEntityType { Kind = 1, Domain = 2, Country = UnitedStates, Category = 2 },
                new Fdp.Toolkit.Tkb.Domain.BodyGeometryDto
                {
                    ReferencePoint = Fdp.Toolkit.Tkb.Domain.ReferencePointKind.CentreOfGravity,
                    Length = 16.26f, Width = 17.53f, Height = 4.47f,
                    BodyCentreX = 0f, BodyCentreZ = 0.5f,
                    GroundContacts =
                    {
                        Wheel("nose",        4.2f,  0f,    -1.75f, 0.30f, 1.0f, retractable: true),
                        Wheel("main left",  -0.5f,  2.6f,  -1.75f, 0.45f, 1.0f, retractable: true),
                        Wheel("main right", -0.5f, -2.6f,  -1.75f, 0.45f, 1.0f, retractable: true),
                    },
                });
            builder
                .WithFlight(type, Fdp.Toolkit.Tkb.Domain.LocomotionClass.FixedWing, new FlightParamsDto
                {
                    MaxSpeedMps = 200f, CruiseSpeedMps = 150f, StallSpeedMps = 60f, MaxBankDeg = 60f, ClimbMps = 30f, TurnRateDeg = 10f,
                    EnduranceSeconds = 7200f, ContactRadiusM = 2f, BodyRadiusM = 2f, BodyHeightM = 4.5f, HoverHeightM = 300f,
                })
                .WithCombat(type, c =>
                {
                    c.MaxHealth = 600;
                    c.EyeHeight = 1f;
                    c.SensorRange = 5000;
                    c.Weapons.Add(new WeaponMount { WeaponType = "Mk-82 rack", Ammunition = 4, Range = 3000f, DamagePerHit = 1200,
                        AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.TkbMk82Bomb, Category = WeaponCategory.Bomb });
                })
                .WithBehavior(type);
        }

        /// <summary>The UH-60's body (CE-1041): CG-referenced, resting on its tyres.</summary>
        private static Fdp.Toolkit.Tkb.Domain.BodyGeometryDto Uh60Geometry() => new Fdp.Toolkit.Tkb.Domain.BodyGeometryDto
                {
                    ReferencePoint = Fdp.Toolkit.Tkb.Domain.ReferencePointKind.CentreOfGravity,
                    Length = 19.76f, Width = 16.36f, Height = 5.13f,   // rotors turning; width = main-rotor diameter
                    BodyCentreX = -1.70f, BodyCentreZ = 0.815f,        // box bottom = the tyres (−1.75)
                    GroundContacts =
                    {
                        Wheel("main left",  2.6f,  1.35f, -1.75f, 0.38f, 0.9f, retractable: false),
                        Wheel("main right", 2.6f, -1.35f, -1.75f, 0.38f, 0.9f, retractable: false),
                        Wheel("tail",      -6.2f,  0f,    -1.75f, 0.20f, 0.7f, retractable: false),
                    },
                };

        /// <summary>⭐ CE-1083 — a doors-off Black Hawk's eight door seats: four a side on the cabin floor's edge, facing out, legs over the sill.</summary>
        private static Fdp.Toolkit.Tkb.Domain.CarrierDefDto DoorSeats()
        {
            var dto = new Fdp.Toolkit.Tkb.Domain.CarrierDefDto { Exposure = Fdp.Toolkit.Behavior.Components.PassengerExposure.Open };
            foreach (float side in new[] { 1f, -1f })
                foreach (float x in new[] { 2.4f, 1.8f, 1.2f, 0.6f })
                    dto.Seats.Add(new Fdp.Toolkit.Tkb.Domain.SeatDto
                    {
                        X = x, Y = 1.05f * side, Z = -0.85f, FacingDegrees = 90f * side, Pose = Fdp.Toolkit.Tkb.Domain.SeatPose.SitLegsOut,
                    });
            return dto;
        }

        private static void DefineAircraft(NedTkbBuilder builder, long type, string name, string symbol, string colour,
                                           DISEntityType dis, Fdp.Toolkit.Tkb.Domain.BodyGeometryDto geometry)
            => builder
                .DefineVehicle(type, name)
                .WithVisual(type, v => { v.SymbolCode = symbol; v.ColorHex = colour; v.ShowLabel = true; })
                .WithFaction(type, 1)
                .WithBodyGeometry(type, geometry)
                .WithDisType(type, dis);

        private static Fdp.Toolkit.Tkb.Domain.GroundContactPoint Wheel(string name, float x, float y, float z, float radius,
                                                                       float strut, bool retractable)
            => new() { Name = name, X = x, Y = y, Z = z, WheelRadius = radius, StrutLength = strut, Retractable = retractable,
                       Kind = Fdp.Toolkit.Tkb.Domain.GroundContactKind.Wheel };
    }
}
