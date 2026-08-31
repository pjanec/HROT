using System;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Core.Tkb
{
    /// <summary>
    /// The ONE shared source of the UrbanCombat TKB templates (types 1001-2003):
    /// CivilianPedestrian, CivilianCar, MilitaryAPC, InfantrySoldier, Insurgent.
    ///
    /// <para><b>Why this lives in Hrot.Core.</b> These templates used to live in
    /// <c>Fdp.Examples.Scenarios</c>, an assembly referenced by exactly TWO production projects
    /// (Hrot.Editor and HrotStrideApp.Game). SimHost, CGF and IG therefore <i>could not</i> resolve
    /// TkbTypes 1001-2003 even if they wanted to: a scenario referencing 1001 loaded in the Editor and
    /// failed on a cluster node. User ruling 2026-08-30: "if editor builds UrbanCombat stuff then
    /// everyone should, editor is the most advanced in that matter." Seeded from
    /// <c>HrotEnvironment.CreateTkb()</c>, so every host gets identical catalogue CONTENTS.</para>
    ///
    /// <para><b>There used to be TWO copies</b>, both inside <c>UrbanCombatNewScenario</c>: five private
    /// per-template methods used by the scenario's own run, and this public one used by the Editor. They
    /// were identical EXCEPT that the private copy omitted <see cref="StrideRenderModelDefDto"/> from all
    /// five templates - so entities spawned through the scenario's own path had no render model and no
    /// collider. THIS (the Editor's, richer) version is authoritative; the private copy was deleted and
    /// <c>UrbanCombatNewScenario</c> now forwards here. That is the "one unified TKB template source"
    /// the user asked for on 2026-08-31.</para>
    ///
    /// <para>⚠ <b>DEVELOPMENT DEFAULT, NOT THE END STATE.</b> User, 2026-08-31: "these exist as default
    /// for development now, real system would read everything from files synced to all nodes." So this
    /// class is a code-registered seed to make development and tests self-contained; the production
    /// system loads TKB content from files replicated across nodes. Do NOT grow it into the product's
    /// authoring surface, and do not treat its contents as a contract.</para>
    ///
    /// <para>📄 docs/DESIGN_Entity_Creation_Unification.md §3.3.</para>
    /// </summary>
    public static class UrbanCombatTkbCatalog
    {
        // ── TKB type codes — PUBLIC because UrbanCombatNewScenario's spawn calls need them, and
        //    this class is now the single source of them. ─────────────────────────────────────────
        /// <summary>TKB type code for the CivilianPedestrian template.</summary>
        public const int TkbCivilianPedestrian = 1001;
        /// <summary>TKB type code for the CivilianCar template.</summary>
        public const int TkbCivilianCar        = 1002;
        /// <summary>TKB type code for the MilitaryAPC template.</summary>
        public const int TkbMilitaryApc        = 2001;
        /// <summary>TKB type code for the InfantrySoldier template.</summary>
        public const int TkbInfantrySoldier    = 2002;
        /// <summary>TKB type code for the Insurgent template.</summary>
        public const int TkbInsurgent          = 2003;

        // ── Tuning constants — moved verbatim with the templates; used only here. ────────────────
        private const float CivilianVisionRange  = 30f;
        private const float CivilianHearingRange = 100f;
        private const float SoldierVisionRange   = 150f;
        private const float SoldierHearingRange  = 200f;

        private const float ApcMaxHealth     = 500f;
        private const float SoldierMaxHealth = 100f;

        private const int   RifleAmmo           = 30;
        private const float RifleMuzzleVelocity = 800f;
        private const int   RpgAmmo             = 1;
        private const float RpgMuzzleVelocity   = 300f;

        /// <summary>
        /// Registers all five UrbanCombat entity blueprints into <paramref name="tkb"/>.
        ///
        /// <para>⚠ <c>TkbDatabase.Register</c> THROWS on a duplicate name or type, so call this exactly
        /// once per database. <c>HrotEnvironment.CreateTkb()</c> already does; a host that uses
        /// <c>CreateTkb()</c> must NOT call this again.</para>
        /// </summary>
        public static void RegisterAll(ITkbDatabase tkb)
        {
            if (tkb == null) throw new ArgumentNullException(nameof(tkb));

            // CivilianPedestrian (1001)
            {
                var t = new TkbTemplate("CivilianPedestrian", TkbCivilianPedestrian);
                t.AddDescriptor(new TkbMasterDto { CustomName = "CivilianPedestrian" });
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel", SkeletonAssetRef = "Models/mannequinModel Skeleton", ShapeKind = CollisionShapeKind.Capsule, ShapeRadius = 0.3f, ShapeHeight = 1.7f });
                t.AddDescriptor(new VehicleParametersDto { Length = 0.6f, Width = 0.4f, MaxSpeedFwd = 2.0f, MaxAccel = 1.0f });
                t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierCivilian, BrainTier = 0, CanMove = true });
                t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = CivilianVisionRange, HearingRange = CivilianHearingRange, FieldOfViewDegrees = 360f });
                tkb.Register(t);
            }

            // CivilianCar (1002)
            {
                var t = new TkbTemplate("CivilianCar", TkbCivilianCar);
                t.AddDescriptor(new TkbMasterDto { CustomName = "CivilianCar" });
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/Box2x1x1", ShapeKind = CollisionShapeKind.OrientedBox, ShapeHeight = 1.5f });
                t.AddDescriptor(new VehicleParametersDto { Length = 4.5f, Width = 2.0f, MaxSpeedFwd = 25.0f, MaxAccel = 3.0f });
                t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierCivilian, BrainTier = 0, CanMove = true });
                tkb.Register(t);
            }

            // MilitaryAPC (2001)
            {
                var t = new TkbTemplate("MilitaryAPC", TkbMilitaryApc);
                t.AddDescriptor(new TkbMasterDto { CustomName = "MilitaryAPC" });
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/Box2x1x1", ShapeKind = CollisionShapeKind.OrientedBox, ShapeHeight = 2.5f });
                t.AddDescriptor(new VehicleParametersDto { Length = 7.0f, Width = 3.5f, MaxSpeedFwd = 12.0f, MaxAccel = 2.0f });
                t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierTactical, BrainTier = BehaviorConstants.BrainTierHsm, CanMove = true, CanInteract = true });
                t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = ApcMaxHealth });
                tkb.Register(t);
            }

            // InfantrySoldier (2002)
            {
                var t = new TkbTemplate("InfantrySoldier", TkbInfantrySoldier);
                t.AddDescriptor(new TkbMasterDto { CustomName = "InfantrySoldier" });
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel", SkeletonAssetRef = "Models/mannequinModel Skeleton", ShapeKind = CollisionShapeKind.Capsule, ShapeRadius = 0.3f, ShapeHeight = 1.8f });
                t.AddDescriptor(new VehicleParametersDto { Length = 0.6f, Width = 0.4f, MaxSpeedFwd = 2.0f, MaxAccel = 1.0f });
                t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierTactical, BrainTier = BehaviorConstants.BrainTierBTree, CanMove = true, CanShoot = true });
                t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = SoldierMaxHealth });
                t.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { InitialAmmunition = RifleAmmo, MuzzleVelocity = RifleMuzzleVelocity } } });
                t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = SoldierVisionRange, HearingRange = SoldierHearingRange, FieldOfViewDegrees = 360f });
                t.AddDescriptor(BuildMannequinAnimationDef());  // ST-011
                tkb.Register(t);
            }

            // Insurgent (2003)
            {
                var t = new TkbTemplate("Insurgent", TkbInsurgent);
                t.AddDescriptor(new TkbMasterDto { CustomName = "Insurgent" });
                t.AddDescriptor(new StrideRenderModelDefDto { ModelAssetRef = "Models/mannequinModel", SkeletonAssetRef = "Models/mannequinModel Skeleton", ShapeKind = CollisionShapeKind.Capsule, ShapeRadius = 0.3f, ShapeHeight = 1.8f });
                t.AddDescriptor(new VehicleParametersDto { Length = 0.6f, Width = 0.4f, MaxSpeedFwd = 2.0f, MaxAccel = 1.0f });
                t.AddDescriptor(new BehaviorProfileDto { SimTier = BehaviorConstants.SimTierTactical, BrainTier = BehaviorConstants.BrainTierBTree, CanMove = true, CanShoot = true });
                t.AddDescriptor(new CombatPlatformDefDto { MaxHealth = SoldierMaxHealth });
                t.AddDescriptor(new WeaponSuiteDto { Mounts = { new WeaponMountDto { InitialAmmunition = RpgAmmo, MuzzleVelocity = RpgMuzzleVelocity } } });
                t.AddDescriptor(new SensorCapabilitiesDto { VisionRange = SoldierVisionRange, HearingRange = SoldierHearingRange, FieldOfViewDegrees = 360f });
                t.AddDescriptor(BuildMannequinAnimationDef());  // ST-011
                tkb.Register(t);
            }
        }

        /// <summary>
        /// Builds the mannequin character-class animation descriptor (STR-P4-T2, DD-4 §2).
        ///
        /// <para>Carries the idle/walk/run locomotion clips (driven by the
        /// <c>StrideAnimationBackend</c>'s blend tree on slot 0 = Locomotion) and the
        /// three jump traversal montages (<c>Jump_Start</c>/<c>Jump_Loop</c>/<c>Jump_End</c>
        /// on slot 100 = FullBody). All six <see cref="MontageDefDto.AssetRef"/>s point at the
        /// template-seeded Stride asset URLs (<c>Animations/*</c>, §12). Shared by the
        /// InfantrySoldier (2002) and Insurgent (2003) humanoid templates — both use the
        /// mannequin model.</para>
        /// </summary>
        public static CharacterAnimationDefDto BuildMannequinAnimationDef()
        {
            return new CharacterAnimationDefDto
            {
                Slots = new[]
                {
                    new SlotDefDto { SlotId = 0,   Name = "Locomotion", BoneMask = new[] { "root" }, Mode = SlotCompositingMode.Override, Priority = 0 },
                    new SlotDefDto { SlotId = 100, Name = "FullBody",   BoneMask = new[] { "root" }, Mode = SlotCompositingMode.Override, Priority = 100 },
                },
                Montages = new[]
                {
                    // Locomotion clips — blended (not played as one-shots) by the backend's
                    // idle/walk/run blend tree; carried here so the AssetRefs resolve and the
                    // backend can look up the Stride AnimationClip URLs.
                    new MontageDefDto { Name = "Idle", AssetRef = "Animations/Idle", Slot = 0, DefaultBlendInTime = 0.2f, DefaultBlendOutTime = 0.2f, DurationSeconds = 1.0f, Sections = Array.Empty<string>(), Notifies = Array.Empty<MontageNotifyRefDto>() },
                    new MontageDefDto { Name = "Walk", AssetRef = "Animations/Walk", Slot = 0, DefaultBlendInTime = 0.2f, DefaultBlendOutTime = 0.2f, DurationSeconds = 1.0f, Sections = Array.Empty<string>(),
                        Notifies = new[]
                        {
                            new MontageNotifyRefDto { MarkerName = "Footstep_Left",  TimeSeconds = 0.25f, PayloadByte = 0 },
                            new MontageNotifyRefDto { MarkerName = "Footstep_Right", TimeSeconds = 0.75f, PayloadByte = 1 },
                        } },
                    new MontageDefDto { Name = "Run", AssetRef = "Animations/Run", Slot = 0, DefaultBlendInTime = 0.15f, DefaultBlendOutTime = 0.15f, DurationSeconds = 0.7f, Sections = Array.Empty<string>(),
                        Notifies = new[]
                        {
                            new MontageNotifyRefDto { MarkerName = "Footstep_Left",  TimeSeconds = 0.2f, PayloadByte = 0 },
                            new MontageNotifyRefDto { MarkerName = "Footstep_Right", TimeSeconds = 0.6f, PayloadByte = 1 },
                        } },

                    // Jump traversal montages (off-mesh-link discrete playback, §6.4).
                    new MontageDefDto { Name = "Jump_Start", AssetRef = "Animations/Jump_Start", Slot = 100, DefaultBlendInTime = 0.08f, DefaultBlendOutTime = 0.1f, DurationSeconds = 0.4f, Sections = new[] { "Launch" }, Notifies = Array.Empty<MontageNotifyRefDto>() },
                    new MontageDefDto { Name = "Jump_Loop",  AssetRef = "Animations/Jump_Loop",  Slot = 100, DefaultBlendInTime = 0.1f,  DefaultBlendOutTime = 0.1f, DurationSeconds = 0.6f, Sections = new[] { "Airborne" }, Notifies = Array.Empty<MontageNotifyRefDto>() },
                    new MontageDefDto { Name = "Jump_End",   AssetRef = "Animations/Jump_End",   Slot = 100, DefaultBlendInTime = 0.1f,  DefaultBlendOutTime = 0.12f, DurationSeconds = 0.5f, Sections = new[] { "Land" },
                        Notifies = new[]
                        {
                            new MontageNotifyRefDto { MarkerName = "Footstep_Left", TimeSeconds = 0.05f, PayloadByte = 0 },
                        } },
                },
                SupportedStances = new[] { StanceId.Standing, StanceId.Crouched },
                StanceTransitions = Array.Empty<StanceTransitionDto>(),
                AimConfig = null,
                NotifyMarkers = new[]
                {
                    new NotifyMarkerDefDto { Name = "Footstep_Left",  Hash = 0xC1D2E3F4u, Kind = AnimNotifyCategory.Footstep },
                    new NotifyMarkerDefDto { Name = "Footstep_Right", Hash = 0xD1E2F3A4u, Kind = AnimNotifyCategory.Footstep },
                },
            };
        }
    }
}
