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
    /// <para>⚠ The civilians' <c>DefaultBehaviorHash</c> stays empty until the behaviors lane registers <c>CivilianHsm</c>
    /// (<c>CE-2235</c>): a brain tier with nothing bound runs nothing (<c>BehaviorTkbTranslator</c> starts no default). The
    /// security forces carry no weapon yet — their baton, shield and launchers are B-5 (<c>CE-3223</c>).</para>
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

            Civilian(tkb, "CivilianAdultMale",   TkbCivilianAdultMale,   height: 1.78f, radius: 0.30f, runSpeed: 6.0f);
            Civilian(tkb, "CivilianAdultFemale", TkbCivilianAdultFemale, height: 1.65f, radius: 0.28f, runSpeed: 5.6f);
            Civilian(tkb, "CivilianMinor",       TkbCivilianChild,       height: 1.25f, radius: 0.22f, runSpeed: 4.5f);   // under 1.5 m: the 3-D map draws a child (CE-1099)
            Civilian(tkb, "CivilianElderly",     TkbCivilianElderly,     height: 1.68f, radius: 0.30f, runSpeed: 3.0f);

            Security(tkb, "SecurityShield",   TkbSecurityShield);
            Security(tkb, "SecurityBaton",    TkbSecurityBaton);
            Security(tkb, "SecurityLauncher", TkbSecurityLauncher);
        }

        private static void Civilian(ITkbDatabase tkb, string name, int type, float height, float radius, float runSpeed)
        {
            var t = new TkbTemplate(name, type);
            UrbanCombatTkbCatalog.Master(t, name, new DISEntityType { Kind = 3, Domain = 1, Category = DisCategoryCivilian });
            Person(t, height, radius, runSpeed);
            t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierCivilian, BrainTier = BehaviorConstants.BrainTierHsm,
                                                    CanMove = true, CanInteract = true });   // no weapon: picks one up (PersonCarrier)
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

        private static void Security(ITkbDatabase tkb, string name, int type)
        {
            var t = new TkbTemplate(name, type);
            UrbanCombatTkbCatalog.Master(t, name, new DISEntityType { Kind = 3, Domain = 1, Category = DisCategorySecurityForce });
            Person(t, height: 1.80f, radius: 0.30f, runSpeed: 5.5f);   // the class template's helmet and vest stay
            t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierTactical, BrainTier = BehaviorConstants.BrainTierBTree,
                                                    CanMove = true, CanShoot = true, CanInteract = true });
            t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = 150f, HearingRange = 300f, FieldOfViewDegrees = 360f });
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
