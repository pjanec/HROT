using System;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// ⭐ <c>CE-3220</c> (civilians B-2, CV-1/CV-2/CV-11, R-3013; docs/DESIGN_Civilians_And_Crowd_Control.md §7.2) — the civilian family
    /// and the security-force people. A civilian is one family whose panic, riot or hostility is a MODE of one HSM, never a type.
    /// <para>⭐ Each is a person: health, injury, an unarmoured body, the person poses, can carry one casualty or a load, footsteps, and
    /// hears 100 m / sees 30 m (CV-2). DIS Kind 3 / Domain 1 with Category <see cref="DisCategoryCivilian"/> or
    /// <see cref="DisCategorySecurityForce"/>, which the class table names <c>Civilian</c> / <c>SecurityForce</c> (R-280).</para>
    /// <para>⭐ The civilians' <c>DefaultBehaviorHash</c> is <c>CivilianHsm</c> (<c>CE-2235</c>, behaviors): <c>BehaviorTkbTranslator</c>
    /// starts it on the brain owner, so a civilian placed with no behaviour lives his routine. The
    /// security forces carry the non-lethal loadouts of B-5 (<c>CE-3223</c>): stun grenades, a baton-round launcher.</para>
    /// <para>⛔ The old <c>CivilianPedestrian</c> (1001) and <c>CivilianChild</c> (1003) are retired only after the neutral-target safety
    /// gate (<c>CE-2233</c>) lands: these civilians have health, and a soldier may otherwise shoot them.</para>
    /// </summary>
    public static class CivilianTkbCatalog
    {
        public const int TkbCivilianAdultMale   = 1101;
        public const int TkbCivilianAdultFemale = 1102;
        public const int TkbCivilianChild       = 1103;
        public const int TkbCivilianElderly     = 1104;

        public const int TkbSecurityShield   = 1201;
        public const int TkbSecurityBaton    = 1202;
        public const int TkbSecurityLauncher = 1203;

        /// <summary>DIS life-form category for a civilian (HROT's own value, like <c>Infantry (HROT)</c> = 1).</summary>
        public const byte DisCategoryCivilian = 2;
        /// <summary>DIS life-form category for a security-force person (HROT's own value).</summary>
        public const byte DisCategorySecurityForce = 3;

        private const float CivilianMaxHealth = 100f;

        public static void RegisterAll(ITkbDatabase tkb)
        {
            if (tkb == null) throw new ArgumentNullException(nameof(tkb));

            Civilian(tkb, "CivilianAdultMale",   TkbCivilianAdultMale,   height: 1.78f, radius: 0.30f, runSpeed: 6.0f, AgeBand.Adult,   PersonSex.Male);
            Civilian(tkb, "CivilianAdultFemale", TkbCivilianAdultFemale, height: 1.65f, radius: 0.28f, runSpeed: 5.6f, AgeBand.Adult,   PersonSex.Female);
            Civilian(tkb, "CivilianMinor",       TkbCivilianChild,       height: 1.25f, radius: 0.22f, runSpeed: 4.5f, AgeBand.Child,   PersonSex.Unspecified);   // under 1.5 m: the 3-D map draws a child (CE-1099)
            Civilian(tkb, "CivilianElderly",     TkbCivilianElderly,     height: 1.68f, radius: 0.30f, runSpeed: 3.0f, AgeBand.Elderly, PersonSex.Unspecified);

            // ⭐ CE-3223 (B-5, CV-9) — the non-lethal loadouts: the shield and baton men carry stun grenades (the baton's blow is a behaviours
            //   melee node, D5); the launcher man a 40 mm baton-round launcher. Tear gas waits for the cloud (CE-3219 part 3).
            Security(tkb, "SecurityShield",   TkbSecurityShield,   StunGrenades());
            Security(tkb, "SecurityBaton",    TkbSecurityBaton,    StunGrenades());
            Security(tkb, "SecurityLauncher", TkbSecurityLauncher, BatonLauncher(), StunGrenades(), TearGasGrenades());   // ⭐ CE-3219 part 3 — and gas
        }

        private static void Civilian(ITkbDatabase tkb, string name, int type, float height, float radius, float runSpeed, AgeBand age, PersonSex sex)
        {
            var t = new TkbTemplate(name, type);
            UrbanCombatTkbCatalog.Master(t, name, new DISEntityType { Kind = 3, Domain = 1, Category = DisCategoryCivilian });
            Person(t, height, radius, runSpeed);
            t.AddDescriptor(new PersonDto { Age = age, Sex = sex });   // ⭐ F-4 (design §7.6) — the outfits read who the person is
            t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierCivilian, BrainTier = BehaviorConstants.BrainTierHsm,
                                                    CanMove = true, CanInteract = true,   // no weapon: picks one up (PersonCarrier)
                                                    // ⭐ CE-2238 part 2 (behaviors, cross-lane; design §5i.2a) — his HANDS (B-8) throw through the weapon
                                                    //    channel, which the dispatcher fails without CanShoot; it gives him no weapon (H-3 reads the hands)
                                                    CanShoot = true,
                                                    // ⭐ CE-2235 (behaviors, the cross-lane edit backend OK'd; design §5d) — his one behaviour
                                                    DefaultBehaviorHash = BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.CivilianHsm) });
            // the class template (bodies.json "Soldier") gives a helmet and a vest; a civilian wears neither
            t.AddDescriptor(new BodyZonesDto { Zones =
            {
                new BodyZoneDto { Zone = "Head",  ArmourSide = 0, ArmourRear = 0, ArmourTop = 0 },
                new BodyZoneDto { Zone = "Torso", ArmourFront = 0, ArmourRear = 0 },
            } });
            t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = UrbanCombatTkbCatalog.CivilianVisionRange,
                                                        HearingRange = UrbanCombatTkbCatalog.CivilianHearingRange, FieldOfViewDegrees = 360f });
            tkb.Register(t);
        }

        private static WeaponMountDto StunGrenades() => new()
        {
            InitialAmmunition = 2, MuzzleVelocity = 15f, Range = 25f, AmmoGuid = MunitionTkbCatalog.TkbStunGrenade, Category = WeaponCategory.Grenade,
        };

        /// <summary>⭐ <c>CE-3219</c> part 3 — two tear-gas grenades, thrown like the stun grenade (their cloud drifts on the wind).</summary>
        private static WeaponMountDto TearGasGrenades() => new()
        {
            InitialAmmunition = 2, MuzzleVelocity = 15f, Range = 25f, AmmoGuid = MunitionTkbCatalog.TkbTearGasGrenade, Category = WeaponCategory.Grenade,
        };

        private static WeaponMountDto BatonLauncher() => new()
        {
            InitialAmmunition = 20, MagazineSize = 1, ReloadSeconds = 2.5f, MuzzleVelocity = 80f, Range = 50f, DamagePerHit = 5f,
            DispersionMils = 6f, AmmoGuid = MunitionTkbCatalog.TkbBatonRound, Category = WeaponCategory.SmallArms,
        };

        private static void Security(ITkbDatabase tkb, string name, int type, params WeaponMountDto[] mounts)
        {
            var t = new TkbTemplate(name, type);
            UrbanCombatTkbCatalog.Master(t, name, new DISEntityType { Kind = 3, Domain = 1, Category = DisCategorySecurityForce });
            Person(t, height: 1.80f, radius: 0.30f, runSpeed: 5.5f);   // the class template's helmet and vest stay
            t.AddDescriptor(new PersonDto { Age = AgeBand.Adult });
            t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierTactical, BrainTier = BehaviorConstants.BrainTierBTree,
                                                    CanMove = true, CanShoot = true, CanInteract = true });
            t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = 150f, HearingRange = 300f, FieldOfViewDegrees = 360f });
            var suite = new WeaponSuiteDto();
            foreach (var m in mounts) suite.Mounts.Add(m);
            t.AddDescriptor(suite);
            tkb.Register(t);
        }

        /// <summary>The body every person here shares: the mannequin, a pedestrian mover, health, injury, a person carrier, the person
        /// poses and footsteps.</summary>
        private static void Person(TkbTemplate t, float height, float radius, float runSpeed)
        {
            t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel", SkeletonAssetRef = "Models/mannequinModel Skeleton",
                                                         ShapeKind = CollisionShapeKind.Capsule, ShapeRadius = radius, ShapeHeight = height });
            t.AddDescriptor(new VehicleParametersDto { VehicleClass = VehicleClass.Pedestrian, Length = radius * 2f, Width = radius * 1.4f,
                                                      MaxSpeedFwd = runSpeed, MaxAccel = 2.5f });
            t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = CivilianMaxHealth });
            t.AddDescriptor(new InjuryDefDto());
            t.AddDescriptor(CarrierDefDto.PersonCarrier());
            t.AddDescriptor(UrbanCombatTkbCatalog.Sounds(UrbanCombatTkbCatalog.FootstepsRange, SoundSourceClass.Footsteps));
            t.AddDescriptor(UrbanCombatTkbCatalog.BuildMannequinAnimationDef());
        }
    }
}
