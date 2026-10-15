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

        /// <summary>⭐ M9 (Exposure Cycle §6) — a pickup: drives like a car, no armour, a 2.2 m eye in the bed, hostile. ⭐⭐ WS-4 (W-F, W-I;
        /// DESIGN_Weapon_Systems §5) — its weapon is no longer TYPE data: the bed is a STATION (a pintle, operated by the gunner seat,
        /// NeedsGunner) and a technical is this pickup with a weapon system and a gunner in its seats (<paramref name="weapon"/>,
        /// <paramref name="gunner"/> — <c>SeatDto.StartWith</c>, filled once on the creating node by <c>SeatPresets</c>).</summary>
        /// <param name="icon">an icon with embedded art (the HMMWV's until a pickup icon is drawn — CE1017's icon rail).</param>
        private static void Pickup(NedTkbBuilder builder, long type, string name, string icon, long weapon = 0, long gunner = 0,
                                   float traverseDeg = 0f, float elevationMinDeg = 0f, float elevationMaxDeg = 0f,
                                   float stationZ = PintleZ, SeatDto? gunnerSeat = null)
        {
            bool arc = traverseDeg > 0f;
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
                    c.BlastSusceptibility = 1f;   // ⭐ R-2019 D-M2 (AE3) — a soft skin (5 mm sheet): a near burst wrecks it
                    c.MobilityKillBelowFraction = 0.6f;
                    c.EyeHeight = 2.2f;   // standing in the bed
                    c.SensorRange = 2000;
                })
                .WithCarrier(type, new CarrierDefDto
                {
                    Exposure = Fdp.Toolkit.Behavior.Components.PassengerExposure.Open,   // a bed and a cab window: the crew is seen and hit
                    Seats =
                    {
                        new SeatDto { Role = Fdp.Toolkit.Behavior.Components.SeatRole.Driver },
                        // ⭐ CE-1108 — kneeling in the bed behind his pintle (the place is his feet; read only by the 3-D drawing): without
                        //   a place he was drawn at the pickup's reference point, inside the body
                        gunnerSeat ?? new SeatDto { Role = Fdp.Toolkit.Behavior.Components.SeatRole.Gunner, StartWith = gunner,
                                                    X = -2.0f, Z = BedFloorZ, Pose = Fdp.Toolkit.Tkb.Domain.SeatPose.Kneel },
                        // the bed pintle: the weapon's own pivot ends 1.9 m up, 1.3 m behind the reference point (the M9 pintle's place)
                        new SeatDto
                        {
                            Role = Fdp.Toolkit.Behavior.Components.SeatRole.Station, Accepts = MountKinds.Pintle, OperatedBy = 1, NeedsGunner = true,
                            X = -1.3f, Z = stationZ, StartWith = weapon,
                            TraverseMinDeg = arc ? -traverseDeg : null, TraverseMaxDeg = arc ? traverseDeg : null,
                            ElevationMinDeg = arc ? elevationMinDeg : null, ElevationMaxDeg = arc ? elevationMaxDeg : null,
                        },
                    },
                })
                .WithSignatures(type, new SignaturesDto { Acoustic = new AcousticSignatureDto
                {
                    MovingAudibleRange = 100f, MovingClass = SoundSourceClass.WheeledEngine,
                    FiringAudibleRange = 1000f, FiringClass = SoundSourceClass.HeavyWeapon,
                } })
                .WithFaction(type, 2)
                .WithBehavior(type)
                .WithDisType(type, new DISEntityType { Kind = 1, Domain = 1, Category = 6 });   // 1.1.*.6 small wheeled utility vehicle
        }

        /// <summary>
        /// ⭐⭐ ui handoff P1 (DESIGN_Weapon_Systems §5a; W-I) — the patrol HMMWV's seats: the three passenger seats it always had (0–2, the
        /// indices saved scenarios name), then a Gunner seat (3) that starts with a rifleman, EXPOSED in the roof ring while the passengers
        /// stay enclosed, and the roof-ring STATION (4) it operates — a pintle, all round (the weapon's own limits), NeedsGunner — that
        /// starts with an M2. The ring's pivot is 2.3 m up (the station 2.0 m + the weapon's 0.3 m pivot), over the rear seats.
        /// </summary>
        private static Fdp.Toolkit.Tkb.Domain.CarrierDefDto HmmwvSeats()
        {
            var dto = Fdp.Toolkit.Tkb.Domain.CarrierDefDto.PassengerSeats(3);
            dto.Seats.Add(new SeatDto
            {
                Role = Fdp.Toolkit.Behavior.Components.SeatRole.Gunner, StartWith = TkbEntityTypes.Infantry_Rifleman, Exposed = true,
                // ⭐ CE-1108 — STANDING in the hatch on the rear floor (the place is his feet): seated at 1.2 m only his head cleared the roof
                X = -0.4f, Z = 0.7f, Pose = Fdp.Toolkit.Tkb.Domain.SeatPose.Stand,
            });
            dto.Seats.Add(new SeatDto
            {
                Role = Fdp.Toolkit.Behavior.Components.SeatRole.Station, Accepts = MountKinds.Pintle, OperatedBy = 3, NeedsGunner = true,
                X = -0.4f, Z = HmmwvRingPivotZ - WeaponPivotZ, StartWith = TkbEntityTypes.WeaponSystem_M2Hmg,
            });
            return dto;
        }

        /// <summary>⭐ ui handoff P1 — the HMMWV roof ring's weapon pivot height (m): the roof at 1.83 m + the ring.</summary>
        private const float HmmwvRingPivotZ = 2.3f;

        /// <summary>⭐ WS-4 — the bed station's height: a weapon's pintle pivot (<see cref="WeaponPivotZ"/>) above it ends at the M9 pintle's 1.9 m.</summary>
        private const float WeaponPivotZ = 0.3f, PintleZ = 1.9f - WeaponPivotZ;
        /// <summary>⭐ CE-1112 — a pickup's bed floor above its reference point (m): where a man in the bed kneels and a ground gun stands.</summary>
        private const float BedFloorZ = 0.85f;

        /// <summary>
        /// ⭐⭐ Weapon systems WS-4 (W-F; DESIGN_Weapon_Systems §5) — a WEAPON SYSTEM type: an entity of its own (Gen.WeaponSystem) whose gun is
        /// its mount 0 and whose pintle / tripod is its own turret 0; manned through a Gunner seat (+ a loader's for a two-man weapon); seen,
        /// hit and destroyed — ⭐ CE-3203 (§5b) by <paramref name="breaks"/>: its own area, casing and health (a fragment hits it as EQUIPMENT,
        /// Building Interiors §3k.1). DIS 1.1.*.50.<paramref name="sub"/> (the WeaponSystem class). Every one has a BRAIN (§6: it runs only
        /// while the weapon controls itself, else it holds — and the Brain group must own its Health and deploy state, CE-523); an AUTOMATIC one
        /// (<c>NeedsOperator = false</c>) also sees (a sensor) and so fights on its own.
        /// </summary>
        private static void WeaponSystemType(NedTkbBuilder builder, long type, string name, byte sub, WeaponSystemDto ws, WeaponMount weapon,
                                             TurretDto? turret, WeaponBreaks breaks, float sensorRange = 0f, byte category = 50)
        {
            builder
                .DefineVehicle(type, name)
                .WithVisual(type, v => { v.SymbolCode = "SFGPEWM--------"; v.ColorHex = "#5A5F52"; v.Scale = 0.5f; })
                .WithCombat(type, c =>
                {
                    c.MaxHealth = breaks.Health;
                    c.BlastSusceptibility = breaks.BlastShare;   // ⭐ R-2019 D-M2 (AE3) — a weapon on a mount or tripod: no hull
                    c.PresentedAreaM2 = breaks.AreaM2;           // ⭐ CE-3203 — EQUIPMENT in a burst: its own area, not a man's
                    c.ArmorFront = c.ArmorSide = c.ArmorRear = c.ArmorTop = breaks.CasingMm;
                    if (sensorRange > 0f) { c.SensorRange = sensorRange; c.EyeHeight = 1f; }
                    c.Weapons.Add(weapon);
                })
                .WithWeaponSystem(type, ws)
                .WithDisType(type, new DISEntityType { Kind = 1, Domain = 1, Category = category, Subcategory = sub })   // ⭐ IF-1 — a mortar 10, a towed gun 5
                // ⭐ hidden from the Add Entity picker until the ui lane draws the class (WS-5: the stowed / deployed drawings and its
                //   picker entries — a weapon has no 3-D family yet, Map3D S1). It is created by presets and scenarios meanwhile.
                .HiddenFromPalette(type);
            if (turret != null) builder.WithTurrets(type, turret);
            if (ws.NeedsOperator)
            {
                var crew = new CarrierDefDto { Exposure = Fdp.Toolkit.Behavior.Components.PassengerExposure.Open };
                crew.Seats.Add(new SeatDto { Role = Fdp.Toolkit.Behavior.Components.SeatRole.Gunner });
                if (ws.Portable == WeaponPortability.TwoMan) crew.Seats.Add(new SeatDto());   // the loader
                builder.WithCarrier(type, crew);
            }
            else builder.WithFaction(type, 1);   // W-E — it fights on its own
            builder.WithBehavior(type);
        }

        /// <summary>
        /// ⭐ CE-3203 (DESIGN_Weapon_Systems §5b) — how a weapon type BREAKS: the area a burst's fragments see (m²), its casing (mm RHA, what a
        /// fragment must pierce), its health and its share of the blast. Sophisticated weapons (optics, seekers, electronics) have no casing
        /// and little health — one fragment breaks them; a steel gun takes several.
        /// </summary>
        private readonly record struct WeaponBreaks(float AreaM2, float CasingMm, float Health, float BlastShare = 1f);

        /// <summary>
        /// ⭐ <c>CE-2187</c> (DESIGN_Weapon_Systems §5c) — a MANPADS gunner: the rifleman (body, rifle as mount 0, sensor, injury) whose
        /// person-carrier carry station starts with <paramref name="launcher"/> (W-I: <c>SeatPresets</c> fills it once on the creating node).
        /// The launcher fires carried (<c>FireWhileCarried</c>); his weapon brain picks it only against an aircraft (its missile's
        /// <c>TargetDomain</c>), the rifle otherwise.
        /// </summary>
        private static void ManpadsGunner(NedTkbBuilder builder, long type, string name, long launcher, ushort country, byte faction = 1)
        {
            var carrier = Fdp.Toolkit.Tkb.Domain.CarrierDefDto.PersonCarrier();
            carrier.Seats[1] = carrier.Seats[1] with { StartWith = launcher };   // seat 1 = the carry station
            builder
                .DefineVehicle(type, name)
                .WithVisual(type, v => { v.IconName = "rifleman"; v.SymbolCode = "SFGPUCDM-------"; v.ModelPath = "models/soldier.obj"; v.ColorHex = "#556B2F"; v.Scale = 0.6f; })
                .WithPhysics(type, p =>
                {
                    p.Mass = 100; p.Length = 0.6f; p.Width = 0.4f; p.Height = 1.75f;
                    p.MaxSpeed = 2.5f; p.Acceleration = 1.0f; p.TurnRate = 90.0f; p.Mobility = TerrainMobility.Infantry;
                })
                .WithCombat(type, c =>
                {
                    c.ArmorFront = 5;
                    c.Weapons.Add(new WeaponMount { WeaponType = "M4_Carbine", Ammunition = 210, Range = 300, RateOfFire = 700, Penetration = 5, DamagePerHit = 25,
                        DispersionMils = RifleDispersionMils, AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.Tkb556Ball,
                        TracerAmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.Tkb556Tracer, TracerEvery = 5, Category = WeaponCategory.SmallArms });
                    c.SensorRange = 5000;   // the launcher's sight: he must see an aircraft as far as the missile reaches
                })
                .WithFaction(type, faction)
                .WithInjury(type, new Fdp.Toolkit.Tkb.Domain.InjuryDefDto())
                .WithCarrier(type, carrier)
                .WithBehavior(type)
                .WithDisType(type, new DISEntityType { Kind = 3, Domain = 1, Country = country, Category = 1 });
        }

        /// <summary>⭐ WS-4 — a pintle / tripod turret of a weapon system: all round, its pivot <see cref="WeaponPivotZ"/> above its base.</summary>
        private static TurretDto Pintle(float elevationMinDeg, float elevationMaxDeg, float traverseRate, float elevationRate, float trunnionX)
            => new() { PivotZ = WeaponPivotZ, TrunnionX = trunnionX, TraverseMinDeg = -180f, TraverseMaxDeg = 180f,
                       ElevationMinDeg = elevationMinDeg, ElevationMaxDeg = elevationMaxDeg,
                       TraverseRateDegPerSec = traverseRate, ElevationRateDegPerSec = elevationRate };

        /// <summary>⭐ WS-4 — the 12.7 mm heavy machine gun as the M9 technical carried it (penetration: the round's own, 25 mm).</summary>
        private static WeaponMount Hmg(string name) => new()
        {
            WeaponType = name, Ammunition = 300, Range = 1500, RateOfFire = 600, DamagePerHit = 60,
            DispersionMils = 4f, MagazineSize = 50, ReloadSeconds = 8f, FireOnMove = true,
            AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.TkbHmgAp, Category = WeaponCategory.MachineGun, TurretIndex = 0,
            MuzzleOffset = new System.Numerics.Vector3(1.6f, 0f, 0f),
        };

        /// <summary>⭐ WS-4 — the weapon systems (600–608): the separable weapons a host's station, a soldier or the ground holds.</summary>
        private static void RegisterWeaponSystems(NedTkbBuilder builder)
        {
            var heavy = new WeaponSystemDto { Portable = WeaponPortability.TwoMan, MountKinds = MountKinds.Ground | MountKinds.Pintle,
                                              DeploySeconds = 20f, PackSeconds = 15f, CarrySpeedFactor = 0.5f };
            WeaponSystemType(builder, TkbEntityTypes.WeaponSystem_M2Hmg, "M2 HMG", 1, heavy, Hmg("12.7mm_M2"), Pintle(-10f, 70f, 90f, 45f, 0.4f), new(0.35f, 1.5f, 80f));
            WeaponSystemType(builder, TkbEntityTypes.WeaponSystem_DshkHmg, "DShK HMG", 1, heavy, Hmg("12.7mm_DShK"), Pintle(-10f, 70f, 90f, 45f, 0.4f), new(0.35f, 1.5f, 80f));
            WeaponSystemType(builder, TkbEntityTypes.WeaponSystem_M134Minigun, "M134 minigun", 2,
                new WeaponSystemDto { Portable = WeaponPortability.Static, MountKinds = MountKinds.Door | MountKinds.Pintle },
                new WeaponMount { WeaponType = "7.62mm_M134", Ammunition = 3000, Range = 1000, RateOfFire = 3000, Penetration = 8, DamagePerHit = 30,
                    DispersionMils = 6f, MagazineSize = 1500, ReloadSeconds = 10f, FireOnMove = true, Category = WeaponCategory.MachineGun, TurretIndex = 0,
                    AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.Tkb762Ball, TracerAmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.Tkb762Tracer, TracerEvery = 5,
                    MuzzleOffset = new System.Numerics.Vector3(0.8f, 0f, 0f) },
                Pintle(-60f, 30f, 120f, 90f, 0.3f), new(0.25f, 1f, 40f));
            var shoulder = new WeaponSystemDto { Portable = WeaponPortability.OneMan, MountKinds = MountKinds.Ground | MountKinds.Carried,
                                                 FireWhileCarried = true, CarrySpeedFactor = 0.9f, DeploySeconds = 2f, PackSeconds = 2f };
            WeaponSystemType(builder, TkbEntityTypes.WeaponSystem_Javelin, "Javelin", 3, shoulder with { CarrySpeedFactor = 0.8f },
                new WeaponMount { WeaponType = "Javelin_ATGM", Ammunition = 2, Range = 2500, RateOfFire = 3, DamagePerHit = 1500,
                    AimSeconds = 4f, MagazineSize = 1, ReloadSeconds = 20f, FireOnMove = false,
                    AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.TkbJavelinMissile, Category = WeaponCategory.Atgm }, turret: null, new(0.3f, 0f, 20f));
            WeaponSystemType(builder, TkbEntityTypes.WeaponSystem_Nlaw, "NLAW", 4, shoulder,
                new WeaponMount { WeaponType = "NLAW", Ammunition = 1, Range = 800, RateOfFire = 3, DamagePerHit = 900,
                    AimSeconds = 2.5f, MagazineSize = 1, ReloadSeconds = 3f, FireOnMove = false,
                    AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.TkbNlawMissile, Category = WeaponCategory.Atgm }, turret: null, new(0.15f, 0f, 15f));
            WeaponSystemType(builder, TkbEntityTypes.WeaponSystem_Law, "M72 LAW", 4, shoulder with { MountKinds = MountKinds.Carried },
                new WeaponMount { WeaponType = "M72_LAW", Ammunition = 1, Range = 200, RateOfFire = 3, DamagePerHit = 600,
                    AimSeconds = 1.5f, MagazineSize = 1, ReloadSeconds = 3f, FireOnMove = false,
                    AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.TkbLawRocket }, turret: null, new(0.1f, 0f, 10f));
            WeaponSystemType(builder, TkbEntityTypes.WeaponSystem_KornetTripod, "Kornet (tripod)", 3, heavy with { DeploySeconds = 30f },
                new WeaponMount { WeaponType = "Kornet_ATGM", Ammunition = 4, Range = 5000, RateOfFire = 2, DamagePerHit = 2000,   // as the M9 technical carried it
                    AimSeconds = 1.5f, MagazineSize = 1, ReloadSeconds = 20f, FireOnMove = false,
                    AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.TkbKornetMissile, Category = WeaponCategory.Atgm, TurretIndex = 0,
                    MuzzleOffset = new System.Numerics.Vector3(0.6f, 0f, 0.2f) },
                Pintle(-10f, 20f, 40f, 20f, 0.3f), new(0.35f, 0f, 25f));
            WeaponSystemType(builder, TkbEntityTypes.WeaponSystem_TowTripod, "TOW (tripod)", 3, heavy with { DeploySeconds = 30f },
                new WeaponMount { WeaponType = "TOW_ATGM", Ammunition = 4, Range = 3750, RateOfFire = 2, DamagePerHit = 1800,
                    AimSeconds = 1.5f, MagazineSize = 1, ReloadSeconds = 15f, FireOnMove = false,
                    AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.TkbTowMissile, Category = WeaponCategory.Atgm, TurretIndex = 0,
                    MuzzleOffset = new System.Numerics.Vector3(0.7f, 0f, 0.2f) },
                Pintle(-10f, 20f, 30f, 15f, 0.3f), new(0.4f, 0f, 25f));
            WeaponSystemType(builder, TkbEntityTypes.WeaponSystem_SentryGun, "Sentry gun", 5,
                new WeaponSystemDto { NeedsOperator = false, Portable = WeaponPortability.Static, MountKinds = MountKinds.Ground | MountKinds.Pintle },
                new WeaponMount { WeaponType = "7.62mm_RWS", Ammunition = 1000, Range = 900, RateOfFire = 600, Penetration = 8, DamagePerHit = 30,
                    DispersionMils = 3f, MagazineSize = 200, ReloadSeconds = 6f, FireOnMove = true, Category = WeaponCategory.MachineGun, TurretIndex = 0,
                    AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.Tkb762Ball, MuzzleOffset = new System.Numerics.Vector3(0.7f, 0f, 0f) },
                Pintle(-20f, 60f, 120f, 60f, 0.2f), new(0.3f, 1f, 30f), sensorRange: 1200f);

            // ⭐⭐ Indirect fire IF-1 (I8, IF-G; docs/designs/indirect-fire/DESIGN_Indirect_Fire.md §4) — the tubes and the towed gun: weapon
            //   systems crewed through their Gunner seat (+ a loader's for a two-man tube), class Mortar 1.1.*.10 / TowedArtillery 1.1.*.5.
            //   Each fires at a POINT by a fire solution (Arc HighOnly / Both + the elevation limits) at its round's charges (I7). Public
            //   figures, rounded; the dispersion, rates and deploy times are starter values — tuning starts here.
            WeaponSystemType(builder, TkbEntityTypes.WeaponSystem_Mortar60, "60 mm mortar", 1,
                new WeaponSystemDto { Portable = WeaponPortability.OneMan, MountKinds = MountKinds.Ground, DeploySeconds = 20f, PackSeconds = 15f, CarrySpeedFactor = 0.8f },
                Laid(IndirectMount("60mm_M224", Hrot.Core.Tkb.MunitionTkbCatalog.Tkb60mmMortarHe, Hrot.Core.Tkb.MunitionTkbCatalog.Mortar60mmHeName,
                              WeaponCategory.Mortar, IndirectArc.HighOnly, 45f, 85f, rounds: 20, rangeM: 3490f, dispersionMils: 6f, reloadSeconds: 3f)),
                LayingTurret(45f, 85f, pivotZ: 0.3f), new(0.2f, 2f, 60f), category: 10);
            WeaponSystemType(builder, TkbEntityTypes.WeaponSystem_Mortar81, "81 mm mortar", 2,
                new WeaponSystemDto { Portable = WeaponPortability.TwoMan, MountKinds = MountKinds.Ground, DeploySeconds = 60f, PackSeconds = 45f, CarrySpeedFactor = 0.5f },
                Laid(IndirectMount("81mm_M252", Hrot.Core.Tkb.MunitionTkbCatalog.Tkb81mmMortarHe, Hrot.Core.Tkb.MunitionTkbCatalog.Mortar81mmHeName,
                              WeaponCategory.Mortar, IndirectArc.HighOnly, 45f, 85f, rounds: 30, rangeM: 5600f, dispersionMils: 5f, reloadSeconds: 3f)),
                LayingTurret(45f, 85f, pivotZ: 0.3f), new(0.3f, 2f, 80f), category: 10);
            WeaponSystemType(builder, TkbEntityTypes.WeaponSystem_Mortar120, "120 mm mortar", 3,
                new WeaponSystemDto { Portable = WeaponPortability.TwoMan, MountKinds = MountKinds.Ground, DeploySeconds = 90f, PackSeconds = 60f, CarrySpeedFactor = 0.4f },
                Laid(IndirectMount("120mm_M120", Hrot.Core.Tkb.MunitionTkbCatalog.Tkb120mmMortarHe, Hrot.Core.Tkb.MunitionTkbCatalog.Mortar120mmHeName,
                              WeaponCategory.Mortar, IndirectArc.HighOnly, 45f, 85f, rounds: 20, rangeM: 7200f, dispersionMils: 4f, reloadSeconds: 6f)),
                LayingTurret(45f, 85f, pivotZ: 0.3f), new(0.5f, 2f, 120f), category: 10);
            WeaponSystemType(builder, TkbEntityTypes.WeaponSystem_Howitzer105, "105 mm towed howitzer", 1,
                new WeaponSystemDto { Portable = WeaponPortability.Static, MountKinds = MountKinds.Ground },   // emplaced: no towing yet (IF-G)
                Laid(IndirectMount("105mm_M119", Hrot.Core.Tkb.MunitionTkbCatalog.Tkb105mmHowitzerHe, Hrot.Core.Tkb.MunitionTkbCatalog.Howitzer105HeName,
                              WeaponCategory.Howitzer, IndirectArc.Both, -5f, 70f, rounds: 40, rangeM: 11500f, dispersionMils: 3f, reloadSeconds: 8f)),
                LayingTurret(-5f, 70f, pivotZ: 1.0f), new(3.0f, 6f, 400f, BlastShare: 0.5f), category: 5);

            // ⭐⭐ CE-2187 / CE-2190 (DESIGN_Weapon_Systems §5c; Air Ops §7d AD1/AD5) — air defence. The MANPADS are the shoulder pattern
            //   (the Javelin's: carried, fires carried); the lock is the aim time. The ZU-23-2 is the M2's station pattern with a two-man
            //   ground crew: twin 23 mm, −10…+90°, a fast traverse; its rounds are aimed by AT2's lead (no proximity fuze). Public figures,
            //   rounded — tuning starts here.
            var manpads = shoulder with { CarrySpeedFactor = 0.85f };
            WeaponSystemType(builder, TkbEntityTypes.WeaponSystem_Stinger, "Stinger (MANPADS)", 6, manpads,
                new WeaponMount { WeaponType = "Stinger_SAM", Ammunition = 1, Range = 4800, RateOfFire = 3, DamagePerHit = 400,
                    AimSeconds = 4f, MagazineSize = 1, ReloadSeconds = 8f, FireOnMove = false,
                    AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.TkbStingerMissile, Category = WeaponCategory.Sam }, turret: null, new(0.15f, 0f, 15f));
            WeaponSystemType(builder, TkbEntityTypes.WeaponSystem_Igla, "Igla (MANPADS)", 6, manpads,
                new WeaponMount { WeaponType = "Igla_SAM", Ammunition = 1, Range = 5200, RateOfFire = 3, DamagePerHit = 300,
                    AimSeconds = 4f, MagazineSize = 1, ReloadSeconds = 8f, FireOnMove = false,
                    AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.TkbIglaMissile, Category = WeaponCategory.Sam }, turret: null, new(0.15f, 0f, 15f));
            WeaponSystemType(builder, TkbEntityTypes.WeaponSystem_Zu23, "ZU-23-2", 7, heavy with { DeploySeconds = 30f, PackSeconds = 20f, CarrySpeedFactor = 0.3f },
                new WeaponMount { WeaponType = "2A14_23mm", Ammunition = 100, Range = 2500, RateOfFire = 400,
                    DamagePerHit = Hrot.Core.Tkb.MunitionTkbCatalog.LibraryDamage(Hrot.Core.Tkb.MunitionTkbCatalog.Zu23ApiLibraryName),
                    DispersionMils = 3f, MagazineSize = 50, ReloadSeconds = 10f, FireOnMove = true, Category = WeaponCategory.MachineGun, TurretIndex = 0,
                    AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.Tkb23mmApi, MuzzleOffset = new System.Numerics.Vector3(1.8f, 0f, 0f),
                    Loadout = new() { (Hrot.Core.Tkb.MunitionTkbCatalog.Tkb23mmApi, 50), (Hrot.Core.Tkb.MunitionTkbCatalog.Tkb23mmHei, 50) } },   // one box each
                Pintle(-10f, 90f, 70f, 50f, 0.4f), new(1.0f, 3f, 120f));
        }

        /// <summary>
        /// ⭐⭐ ui handoff P2 (DESIGN_Indirect_Fire §3.9) — the LAYING turret of a mortar / towed gun: turret 0, all round (the crew shifts the
        /// baseplate or the trails), inside the mount's elevation window, at a crew's rates. It is what replicates the laid elevation
        /// and azimuth (<c>dtTurretPose</c>, W-9) — no field of its own. Starter rates: 30 °/s traverse, 15 °/s elevation — a
        /// full lay (180°, 0 → 85°) in ≈ 6 s, inside FM-4's 10 s lay time.
        /// </summary>
        private static TurretDto LayingTurret(float elevationMinDeg, float elevationMaxDeg, float pivotZ)
            => new() { PivotZ = pivotZ, TraverseMinDeg = -180f, TraverseMaxDeg = 180f,
                       ElevationMinDeg = elevationMinDeg, ElevationMaxDeg = elevationMaxDeg,
                       TraverseRateDegPerSec = 30f, ElevationRateDegPerSec = 15f };

        /// <summary>⭐ ui handoff P2 — the mount is laid by turret 0 (<see cref="LayingTurret"/>).</summary>
        private static WeaponMount Laid(WeaponMount mount) { mount.TurretIndex = 0; return mount; }

        /// <summary>⭐ IF-1 (I8) — an INDIRECT mount: a point-firing gun whose round flies the fire solution's charge and arc. Its damage is
        /// the round's library pair (the warhead does the work); it stands to fire.</summary>
        private static WeaponMount IndirectMount(string name, long ammo, string libraryName, WeaponCategory category, IndirectArc arc,
            float minElevationDeg, float maxElevationDeg, int rounds, float rangeM, float dispersionMils, float reloadSeconds)
            => new()
            {
                WeaponType = name, Ammunition = rounds, Range = rangeM, RateOfFire = 60f / reloadSeconds,
                DamagePerHit = Hrot.Core.Tkb.MunitionTkbCatalog.LibraryDamage(libraryName), DispersionMils = dispersionMils,
                AimSeconds = 2f, MagazineSize = 1, ReloadSeconds = reloadSeconds, FireOnMove = false,
                AmmoTkb = ammo, Category = category, Arc = arc, MinElevationDeg = minElevationDeg, MaxElevationDeg = maxElevationDeg,
            };

        /// <summary>
        /// ⭐⭐ IF-1 (I8, IF-G, FM-1) — the 155 mm SELF-PROPELLED HOWITZER (M109 class): a tracked vehicle with a turret (all round, −3…+75°), a
        /// brain (a Strike to it resolves to <c>SelfPropelledArtilleryStrike</c>, the behaviour lane's), and a preset crew (W-I: driver,
        /// gunner, commander, two loaders). Class SelfPropelledArtillery 1.1.*.4. Public figures, rounded.
        /// </summary>
        private static void DefineSelfPropelledHowitzer(NedTkbBuilder builder)
        {
            const long type = TkbEntityTypes.Sph_M109;
            builder
                .DefineVehicle(type, "M109 155 mm self-propelled howitzer")
                .WithVisual(type, v => { v.SymbolCode = "SFGPUCFHE------"; v.ColorHex = "#5A5F52"; v.Scale = 1.2f; v.ShowLabel = true; })
                .WithPhysics(type, p =>
                {
                    p.Mass = 27_500; p.Length = 6.19f; p.Width = 3.15f; p.Height = 3.25f;
                    p.MaxSpeed = 15f; p.MaxSpeedRev = 5f; p.Acceleration = 1.5f; p.TurnRate = 20f;
                    p.Mobility = TerrainMobility.Tracked;
                })
                .WithCombat(type, c =>
                {
                    c.ArmorFront = 30; c.ArmorSide = 25; c.ArmorRear = 20; c.ArmorTop = 20;
                    c.MaxHealth = 1200;
                    c.BlastSusceptibility = 0.25f;   // ⭐ R-2019 D-M2 (AE3) — aluminium hull, 25 mm sides
                    c.EyeHeight = 2.8f;
                    c.SensorRange = 2000;
                    var gun = IndirectMount("155mm_M284", Hrot.Core.Tkb.MunitionTkbCatalog.Tkb155mmHowitzerHe, Hrot.Core.Tkb.MunitionTkbCatalog.Howitzer155HeName,
                                            WeaponCategory.Howitzer, IndirectArc.Both, -3f, 75f, rounds: 39, rangeM: 18100f, dispersionMils: 3f, reloadSeconds: 10f);
                    gun.TurretIndex = 0;
                    gun.MuzzleOffset = new System.Numerics.Vector3(6.0f, 0f, 0f);
                    c.Weapons.Add(gun);
                })
                .WithTurrets(type, new TurretDto
                {
                    PivotX = -0.5f, PivotZ = 2.6f, TrunnionX = 0.8f,
                    TraverseMinDeg = -180f, TraverseMaxDeg = 180f, ElevationMinDeg = -3f, ElevationMaxDeg = 75f,
                    TraverseRateDegPerSec = 10f, ElevationRateDegPerSec = 5f,
                })
                .WithCarrier(type, new CarrierDefDto
                {
                    Exposure = Fdp.Toolkit.Behavior.Components.PassengerExposure.Enclosed,
                    Seats =
                    {
                        new SeatDto { Role = Fdp.Toolkit.Behavior.Components.SeatRole.Driver, StartWith = TkbEntityTypes.Infantry_Rifleman },
                        new SeatDto { Role = Fdp.Toolkit.Behavior.Components.SeatRole.Gunner, StartWith = TkbEntityTypes.Infantry_Rifleman },
                        new SeatDto { Role = Fdp.Toolkit.Behavior.Components.SeatRole.Commander, StartWith = TkbEntityTypes.Infantry_Officer },
                        new SeatDto { StartWith = TkbEntityTypes.Infantry_Rifleman },   // the loaders
                        new SeatDto { StartWith = TkbEntityTypes.Infantry_Rifleman },
                    },
                })
                .WithFaction(type, 1)
                .WithBehavior(type)
                .WithDisType(type, new DISEntityType { Kind = 1, Domain = 1, Country = UnitedStates, Category = 4, Subcategory = 1 });
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
                    c.BlastSusceptibility = 0f;   // ⭐ R-2019 D-M2 (AE3) — an MBT shrugs off blast; its fragments and direct hits still count
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
                    c.BlastSusceptibility = 0.1f;   // ⭐ R-2019 D-M2 (AE3) — IFV: blast damages optics and running gear only
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
                .WithCarrier(TkbEntityTypes.Truck_HMMWV, HmmwvSeats())   // ⭐ CE-2151 three beside the (abstract) driver; ⭐ ui P1 + the roof ring
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
                    c.BlastSusceptibility = 0f;   // ⭐ R-2019 D-M2 (AE3) — an MBT
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
            // ⭐⭐ WS-4 (W-F, W-I, STEER_Behaviors_To_Backend_Weapon_Systems_Crew_Presets) — the gun is an ENTITY now: a bare pickup (106) and two
            //   presets of it — 104 = + an M2 in the bed station + a rifleman in the gunner seat, 105 = + a Kornet (the old ±120° / −10…+20° arc,
            //   now the station's) + a rifleman. Orders go to the pickup; its weapon brain fires the station weapon (WS-3).
            RegisterWeaponSystems(builder);
            Pickup(builder, TkbEntityTypes.Pickup_Toyota, "Pickup (Toyota)", "hmmwv");
            Pickup(builder, TkbEntityTypes.Technical_Hmg, "Technical (HMG)", "hmmwv",
                weapon: TkbEntityTypes.WeaponSystem_M2Hmg, gunner: TkbEntityTypes.Infantry_Rifleman);
            Pickup(builder, TkbEntityTypes.Technical_Atgm, "Technical (ATGM)", "hmmwv",
                weapon: TkbEntityTypes.WeaponSystem_KornetTripod, gunner: TkbEntityTypes.Infantry_Rifleman,
                traverseDeg: 120f, elevationMinDeg: -10f, elevationMaxDeg: 20f);
            // ⭐ ui handoff P1 (DESIGN_Weapon_Systems §5a) — the other two bed weapons as presets of the same pickup; 106 stays bare
            Pickup(builder, TkbEntityTypes.Technical_Dshk, "Technical (DShK)", "hmmwv",
                weapon: TkbEntityTypes.WeaponSystem_DshkHmg, gunner: TkbEntityTypes.Infantry_Rifleman);
            Pickup(builder, TkbEntityTypes.Technical_Minigun, "Technical (minigun)", "hmmwv",
                weapon: TkbEntityTypes.WeaponSystem_M134Minigun, gunner: TkbEntityTypes.Infantry_Rifleman);
            // ⭐ CE-2190 (DESIGN_Weapon_Systems §5c, AD5) — the ZU-23-2 in the same bed (its own −10…+90°, all round)
            Pickup(builder, TkbEntityTypes.Technical_Zu23, "Technical (ZU-23-2)", "hmmwv",
                weapon: TkbEntityTypes.WeaponSystem_Zu23, gunner: TkbEntityTypes.Infantry_Rifleman,
                // ⭐ CE-1112 (ui, DESIGN_Map_3D_Mode.md §6v AD-V5 — a cross-lane edit to this preset's data, read only by the 3-D drawing):
                //   the ZU-23 stands ON the bed floor (not at an HMG's pintle height — its own trunnions are 1 m up), and its gunner SITS on
                //   the gun's left seat (WeaponKits.GunnerSpot(AaGun): 0.55 m behind its pivot, 0.45 m left, hips 0.95 m up)
                stationZ: BedFloorZ,
                gunnerSeat: new SeatDto { Role = Fdp.Toolkit.Behavior.Components.SeatRole.Gunner, StartWith = TkbEntityTypes.Infantry_Rifleman,
                                          X = -1.3f - 0.55f, Y = 0.45f, Z = BedFloorZ + 0.95f, Pose = Fdp.Toolkit.Tkb.Domain.SeatPose.Sit });
            
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

            // ⭐ CE-2187 (DESIGN_Weapon_Systems §5c) — the MANPADS gunners: a rifleman whose carry station starts with the launcher
            //   (W-I SeatPresets fills it once on the creating node) — the first soldier preset with a weapon on his back
            ManpadsGunner(builder, TkbEntityTypes.Infantry_StingerGunner, "Rifleman (Stinger)", TkbEntityTypes.WeaponSystem_Stinger, UnitedStates);
            ManpadsGunner(builder, TkbEntityTypes.Infantry_IglaGunner, "Rifleman (Igla)", TkbEntityTypes.WeaponSystem_Igla, Russia, faction: 2);
            
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
            //   carries its size in Body.Geometry. (⭐ CE-2185: both UH-60s now carry a brain and health, below.)
            //   Public figures, rounded; the gear's X is from the CG. DIS subcategory left 0 (generic) — not verified.
            //   📄 docs/DESIGN_Body_Geometry_And_Ground_Contact.md G4.
            DefineAircraft(builder, TkbEntityTypes.Heli_UH60, "UH-60A Black Hawk", "SFAPMHU-------", "#56604F",
                new DISEntityType { Kind = 1, Domain = 2, Country = UnitedStates, Category = 21 }, Uh60Geometry());
            // ⭐ CE-1083 (docs/DESIGN_Map_3D_Mode.md §6m C-3) — the same Black Hawk with its cabin doors off: Gen.Carrier OPEN, eight seats,
            //   four in each door on the cabin floor's edge (the drawn fuselage: ±1.15 m wide, its floor ~0.9 m under the CG), legs out.
            DefineAircraft(builder, TkbEntityTypes.Heli_UH60_DoorsOff, "UH-60A Black Hawk (doors off)", "SFAPMHU-------", "#56604F",
                new DISEntityType { Kind = 1, Domain = 2, Country = UnitedStates, Category = 21 }, Uh60Geometry());
            builder.WithCarrier(TkbEntityTypes.Heli_UH60_DoorsOff, DoorSeats());
            // ⭐ CE-2185 (AT4, R-2021, DESIGN_Air_Operations §7c) — both Black Hawks can be downed: the AH-64 class's numbers (health 800, a
            //   helicopter's blast susceptibility 0.8 — armour covers the crew, not the rotors). Source: the attack helicopter's entry
            //   (no public damage model for a UH-60 exists); no armour FACES yet — the A-10 / AH-64 have none either, and per-face armour
            //   for all four aircraft goes to the user as a proposal (the handoff's "report numbers, do not build").
            //   ⭐ And a BRAIN (`WithBehavior`): Health is in the Brain ownership group and only the Brain node applies damage
            //   (CE-523, `HrotOwnershipGroupsTests`) — a brainless type with Health keeps it with its creator and is never downed. The
            //   doors-off one's design already assumes one (Air Ops: the hold's release is "the brain's").
            foreach (var uh60 in new[] { TkbEntityTypes.Heli_UH60, TkbEntityTypes.Heli_UH60_DoorsOff })
                builder
                    .WithCombat(uh60, c =>
                    {
                        c.MaxHealth = 800;
                        c.BlastSusceptibility = 0.8f;
                    })
                    .WithBehavior(uh60);
            // ⭐ Air Ops slice 2 (docs/designs/air-attack/DESIGN_Air_Operations.md §5, §6) — it FLIES: a route takes it off its gear, rounds
            //   its corners at 60 m, holds 30 m over the ground and sets it down on a Ground waypoint. Public figures, rounded: cruise
            //   ~70 m/s, climb ~8 m/s; the collider is the fuselage, not the rotor disc. The plain UH-60 (400) stays a static body.
            builder.WithFlight(TkbEntityTypes.Heli_UH60_DoorsOff, Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft, new FlightParamsDto
            {
                MaxSpeedMps = 70f, ClimbMps = 8f, TurnRateDeg = 30f, EnduranceSeconds = 7200f,
                ContactRadiusM = 1f, BodyRadiusM = 1.5f, BodyHeightM = 3.5f,
                HoverHeightM = 30f, TurnRadiusM = 60f, AccelMps2 = 3f,   // R-3007: a heavy helicopter eases into its speed
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
            DefineAttackHelicopter(builder);   // ⭐ Air Ops slice 6a
            // ⭐⭐ CE-2188 (DESIGN_Missiles_And_Ammo §3c, AD3) — every crewed aircraft carries flares and chaff (the FPV drone none). Public
            //   loads, rounded; a salvo of two a second, decoys living 5 s — tuning starts here.
            foreach (var (aircraft, flares, chaff) in new[]
                     {
                         (TkbEntityTypes.Heli_UH60, 30, 30), (TkbEntityTypes.Heli_UH60_DoorsOff, 30, 30), (TkbEntityTypes.Heli_AH64, 60, 60),
                         (TkbEntityTypes.Jet_AttackA10, 120, 240), (TkbEntityTypes.Jet_F16, 60, 60), (TkbEntityTypes.Cargo_C130, 120, 120),
                     })
                builder.WithCountermeasures(aircraft, new Fdp.Toolkit.Tkb.Domain.CountermeasuresDto { Flares = flares, Chaff = chaff });
            DefineSelfPropelledHowitzer(builder);   // ⭐ Indirect fire IF-1 (I8)

            // ⭐ IF-1 (I8, FM-1) — the ARTILLERY BATTERY: a body-less commander unit (class ArtilleryUnit 11.1.*.4) whose roster is its guns;
            //   a Strike to it resolves to ArtilleryUnitStrike (the behaviour lane's battery play). Three M109s, placed by hand (no auto spawn).
            builder
                .DefineVehicle(TkbEntityTypes.Unit_ArtilleryBattery, "Artillery Battery")
                .WithVisual(TkbEntityTypes.Unit_ArtilleryBattery, v => { v.SymbolCode = "SFGPUCF---E----"; v.ColorHex = "#0000FF"; v.Scale = 1.2f; })
                .WithFaction(TkbEntityTypes.Unit_ArtilleryBattery, 1)
                .WithBehavior(TkbEntityTypes.Unit_ArtilleryBattery)
                .AsComposite(TkbEntityTypes.Unit_ArtilleryBattery, comp =>
                {
                    comp.Subordinates.Add(new TkbChildSlot { TkbType = TkbEntityTypes.Sph_M109, Count = 3, Designation = TacticalDesignation.Wingman });
                    comp.Echelon = "Company";   // a battery is a company-level echelon
                    comp.AutoCreateChildren = false;
                })
                .AsLandUnit(TkbEntityTypes.Unit_ArtilleryBattery, Hrot.Core.Tkb.UnitArm.Artillery, UnitedStates);   // 11.1.225.4.5.1

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
            // ⭐ CE-3148 — every obstacle states its DIS type (they drew the magenta unknown-type box): the parked car is a car
            //   (1.1.0.81, the civilian car's), the rest are cultural features (5 — no domain pinned: an unnamed level is a rail failure)
            var car = new DISEntityType { Kind = 1, Domain = 1, Category = 81 };
            var feature = new DISEntityType { Kind = 5 };
            RegisterObstacle(tkbDb, "Car",            TkbEntityTypes.Obstacle_Car,           4.5f, 1.8f, 1.5f, "car-body", car);
            RegisterObstacle(tkbDb, "Sandbag wall",   TkbEntityTypes.Obstacle_SandbagWall,   3.0f, 0.6f, 1.0f, "sandbags", feature);
            RegisterObstacle(tkbDb, "Concrete block", TkbEntityTypes.Obstacle_ConcreteBlock, 2.0f, 1.0f, 1.0f, "concrete", feature);
            RegisterObstacle(tkbDb, "Crate",          TkbEntityTypes.Obstacle_Crate,         1.2f, 1.0f, 1.0f, "fence-wood", feature);
        }

        private static void RegisterObstacle(TkbDatabase tkbDb, string name, long type, float length, float width, float height, string material,
                                             DISEntityType dis)
        {
            var t = new TkbTemplate(name, type);
            t.DisType = dis;   // ⭐ CE-3148 — the TYPE only: ⛔ no TkbMasterDto, which would list an obstacle in the Add Entity picker as a sided unit
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
            // ⭐ R-3007 — the drone IS its warhead (meeting something bursts it); it is nimble: 15 m/s² against a helicopter's 3
            var flight = new FlightParamsDto { AccelMps2 = 15f, DetonatesOnImpact = true };
            builder
                .DefineVehicle(type, "FPV drone")
                .WithVisual(type, v => { v.SymbolCode = "SHAPMFQ-------"; v.ColorHex = "#4A4A4A"; v.Scale = 0.3f; v.ShowLabel = true; })
                .WithFlight(type, LocomotionClass.Rotorcraft, flight)
                .WithCombat(type, c =>
                {
                    c.MaxHealth = 10;
                    c.BlastSusceptibility = 1f;   // ⭐ R-2019 D-M2 (AE3) — a drone: plastic and foam
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
                    c.BlastSusceptibility = 0.5f;   // ⭐ R-2019 D-M2 (AE3) — the titanium cockpit tub; wings and engines are soft
                    c.EyeHeight = 1f;
                    c.SensorRange = 5000;
                    c.Weapons.Add(new WeaponMount { WeaponType = "Mk-82 rack", Ammunition = 4, Range = 3000f, DamagePerHit = 1200,
                        AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.TkbMk82Bomb, Category = WeaponCategory.Bomb });
                    // ⭐ Air Ops slice 6a (AW-1, AW-2; §7a) — the GAU-8: FIXED forward in the nose (its arc is a 5° cone round the body's
                    //   forward, so the gun run points the plane), 1150 rounds, ~3900 rounds/min; HEI loaded, the penetrator for armour
                    //   (AmmoChoice, M8). Every round's numbers are the library's (MunitionTkbCatalog); dispersion 5 mils (80 % in ~5 mil).
                    c.Weapons.Add(new WeaponMount
                    {
                        WeaponType = "GAU-8 Avenger", Ammunition = 1150, Range = 1200f, RateOfFire = 3900, DispersionMils = 5f,
                        Penetration = Hrot.Core.Tkb.MunitionTkbCatalog.LibraryPenetration(Hrot.Core.Tkb.MunitionTkbCatalog.Gau8HeiLibraryName),
                        DamagePerHit = Hrot.Core.Tkb.MunitionTkbCatalog.LibraryDamage(Hrot.Core.Tkb.MunitionTkbCatalog.Gau8HeiLibraryName),
                        Category = WeaponCategory.Autocannon, FixedConeDeg = 5f, FireOnMove = true,
                        AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.TkbGau8Hei,
                        Loadout = new() { (Hrot.Core.Tkb.MunitionTkbCatalog.TkbGau8Hei, 575), (Hrot.Core.Tkb.MunitionTkbCatalog.TkbGau8Api, 575) },
                        MuzzleOffset = new System.Numerics.Vector3(7.5f, 0f, -0.6f),
                    });
                })
                .WithBehavior(type);
        }

        /// <summary>
        /// ⭐ Air Ops slice 6a (docs/designs/air-attack/DESIGN_Air_Operations.md §7a, AW-5) — an AH-64-class ATTACK HELICOPTER: it FLIES like the
        /// doors-off UH-60 (Rotorcraft, a little faster and more agile), carries a chin M134 on a turret (mount 0: ±110°, −60…+11°) and a
        /// fixed-forward Hydra-70 pod (mount 1: 38 rockets, an 8° cone — the pod points where the helicopter points; a held trigger or the
        /// action's round count is the ripple). DIS 1.2.225.20 (attack helicopter) so the air orders resolve. Public figures, rounded;
        /// the crew is the type itself (no seats). A brain (its behaviours are the behaviour lane's, slice 6). Friendly.
        /// </summary>
        private static void DefineAttackHelicopter(NedTkbBuilder builder)
        {
            const long type = TkbEntityTypes.Heli_AH64;
            DefineAircraft(builder, type, "AH-64 Apache (attack helicopter)", "SFAPMHA-------", "#56604F",
                new DISEntityType { Kind = 1, Domain = 2, Country = UnitedStates, Category = 20 },
                new Fdp.Toolkit.Tkb.Domain.BodyGeometryDto
                {
                    ReferencePoint = Fdp.Toolkit.Tkb.Domain.ReferencePointKind.CentreOfGravity,
                    Length = 17.73f, Width = 14.63f, Height = 3.87f,   // rotors turning; width = main-rotor diameter
                    BodyCentreX = -1.5f, BodyCentreZ = 0.6f,             // box bottom = the tyres (−1.33)
                    GroundContacts =
                    {
                        Wheel("main left",  1.8f,  1.0f, -1.33f, 0.32f, 0.8f, retractable: false),
                        Wheel("main right", 1.8f, -1.0f, -1.33f, 0.32f, 0.8f, retractable: false),
                        Wheel("tail",      -8.6f,  0f,   -1.33f, 0.18f, 0.6f, retractable: false),
                    },
                });
            builder
                .WithFlight(type, Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft, new FlightParamsDto
                {
                    MaxSpeedMps = 80f, ClimbMps = 12f, TurnRateDeg = 40f, EnduranceSeconds = 7200f,
                    ContactRadiusM = 1f, BodyRadiusM = 1.2f, BodyHeightM = 3.0f,
                    HoverHeightM = 30f, TurnRadiusM = 50f, AccelMps2 = 4f,
                })
                .WithTurrets(type, new TurretDto
                {
                    PivotX = 4.3f, PivotZ = -1.0f, TrunnionX = 0.2f,          // under the nose
                    TraverseMinDeg = -110f, TraverseMaxDeg = 110f, ElevationMinDeg = -60f, ElevationMaxDeg = 11f,
                    TraverseRateDegPerSec = 120f, ElevationRateDegPerSec = 90f,
                })
                .WithCombat(type, c =>
                {
                    c.MaxHealth = 800;
                    c.BlastSusceptibility = 0.8f;   // ⭐ R-2019 D-M2 (AE3) — a helicopter's armour covers the crew, not the rotors
                    c.EyeHeight = 1f;
                    c.SensorRange = 6000;
                    c.Weapons.Add(new WeaponMount
                    {
                        WeaponType = "7.62mm_M134_chin", Ammunition = 4000, Range = 1000, RateOfFire = 3000, Penetration = 8, DamagePerHit = 30,
                        DispersionMils = 6f, MagazineSize = 4000, ReloadSeconds = 10f, FireOnMove = true, Category = WeaponCategory.MachineGun, TurretIndex = 0,
                        AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.Tkb762Ball, TracerAmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.Tkb762Tracer, TracerEvery = 5,
                        MuzzleOffset = new System.Numerics.Vector3(0.8f, 0f, 0f),
                    });
                    c.Weapons.Add(new WeaponMount
                    {
                        WeaponType = "Hydra-70 pod", Ammunition = 38, Range = 4000f, RateOfFire = 300, DispersionMils = 10f,
                        Penetration = Hrot.Core.Tkb.MunitionTkbCatalog.LibraryPenetration(Hrot.Core.Tkb.MunitionTkbCatalog.Hydra70LibraryName),
                        DamagePerHit = Hrot.Core.Tkb.MunitionTkbCatalog.LibraryDamage(Hrot.Core.Tkb.MunitionTkbCatalog.Hydra70LibraryName),
                        Category = WeaponCategory.Rocket, FixedConeDeg = 8f, FireOnMove = true,
                        AmmoTkb = Hrot.Core.Tkb.MunitionTkbCatalog.TkbHydra70Rocket,
                        MuzzleOffset = new System.Numerics.Vector3(0.5f, 1.8f, -0.6f),   // the left stub wing
                    });
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

        /// <summary>⭐ CE-1083 — a doors-off Black Hawk's eight door seats: four a side on the cabin floor's edge, facing out, legs over the sill;
        /// ⭐ F4 — then each side's door gunner and door gun (seats 8–11).</summary>
        private static Fdp.Toolkit.Tkb.Domain.CarrierDefDto DoorSeats()
        {
            var dto = new Fdp.Toolkit.Tkb.Domain.CarrierDefDto { Exposure = Fdp.Toolkit.Behavior.Components.PassengerExposure.Open };
            foreach (float side in new[] { 1f, -1f })
                foreach (float x in new[] { 2.4f, 1.8f, 1.2f, 0.6f })
                    dto.Seats.Add(new Fdp.Toolkit.Tkb.Domain.SeatDto
                    {
                        X = x, Y = 1.05f * side, Z = -0.85f, FacingDegrees = 90f * side, Pose = Fdp.Toolkit.Tkb.Domain.SeatPose.SitLegsOut,
                    });
            // ⭐ Air Ops slice 6a F4 (DESIGN_Air_Operations §7a; Weapon Systems WS-4, W-I) — a door gun each side, forward of the cabin
            //   doors: a gunner seat (it starts with a soldier) and a DOOR station operated by it (it starts with an M134), its arc the
            //   side it faces. Appended after the eight riders (seats 8–11), so a rider's seat index is unchanged; the passenger list is
            //   12 wide for it (🔒 user, 2026-10-14). A door gunner shot ⇒ his gun falls silent (NeedsGunner, WS-3).
            foreach (float side in new[] { 1f, -1f })
            {
                int gunnerSeat = dto.Seats.Count;
                dto.Seats.Add(new Fdp.Toolkit.Tkb.Domain.SeatDto
                {
                    Role = Fdp.Toolkit.Behavior.Components.SeatRole.Gunner, StartWith = TkbEntityTypes.Infantry_Rifleman,
                    // ⭐ CE-1106 — in the door like the riders (|Y| 1.05, legs out): at 0.95 his body was inside the fuselage
                    X = 3.0f, Y = 1.05f * side, Z = -0.85f, FacingDegrees = 90f * side, Pose = Fdp.Toolkit.Tkb.Domain.SeatPose.SitLegsOut,
                });
                dto.Seats.Add(new Fdp.Toolkit.Tkb.Domain.SeatDto
                {
                    Role = Fdp.Toolkit.Behavior.Components.SeatRole.Station, Accepts = Fdp.Toolkit.Tkb.Domain.MountKinds.Door,
                    OperatedBy = gunnerSeat, NeedsGunner = true, StartWith = TkbEntityTypes.WeaponSystem_M134Minigun,
                    X = 3.0f, Y = 1.35f * side, Z = -0.7f, FacingDegrees = 90f * side,   // ⭐ CE-1106 — in front of him; its pivot (0.3 m up) at his chest
                    TraverseMinDeg = side > 0 ? 20f : -160f, TraverseMaxDeg = side > 0 ? 160f : -20f,
                });
            }
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
