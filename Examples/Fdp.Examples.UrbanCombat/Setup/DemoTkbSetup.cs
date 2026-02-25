using CarKinem.Core;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Physics.Components;

namespace Fdp.Examples.UrbanCombat.Setup
{
    /// <summary>
    /// TKB blueprint registration for the five Urban Ambush entity types.
    /// Replaces the BATCH-14 <c>EntityBlueprints</c> factory methods, which called
    /// <c>world.AddComponent()</c> directly.  This class uses the <see cref="TkbTemplate"/> /
    /// <see cref="ITkbDatabase"/> pattern (as established by <c>TankTemplate.cs</c> in
    /// <c>Fdp.Examples.NetworkDemo</c>) so that every archetype can be retrieved and spawned
    /// by type ID anywhere in the pipeline (ScenarioDirector, tests, T7, …).
    /// </summary>
    /// <remarks>
    /// Component sets match DESIGN.md §9.2 verbatim, with two back-ports added in BATCH-12/13:
    /// <list type="bullet">
    ///   <item><see cref="PreviousCapabilities"/> on damageable entities (APC, Soldier, Insurgent)
    ///         — required by <c>HsmDamageBridgeSystem</c>.</item>
    ///   <item><see cref="HealthData"/> on damageable entities — required by
    ///         <c>MissionDirectorSystem.HealthCritical</c>.</item>
    /// </list>
    /// </remarks>
    public static class DemoTkbSetup
    {
        // ── Faction constants (DESIGN.md §4.1) ───────────────────────────────────────
        private const byte FactionNeutral = 0;
        private const byte FactionBlue    = 1;
        private const byte FactionRed     = 2;

        // ── Public entry point ────────────────────────────────────────────────────────

        /// <summary>
        /// Registers all five Urban Ambush entity templates with <paramref name="tkb"/>.
        /// Call once at application startup (e.g. from <see cref="HeadlessDemoApp.Initialize"/>).
        /// </summary>
        public static void RegisterAll(ITkbDatabase tkb)
        {
            RegisterCivilianPedestrian(tkb);
            RegisterCivilianCar(tkb);
            RegisterMilitaryAPC(tkb);
            RegisterInfantrySoldier(tkb);
            RegisterInsurgent(tkb);
        }

        // ── Template: CivilianPedestrian (ID 1001) ───────────────────────────────────

        /// <summary>Tier-1 civilian pedestrian. Brain: hardcoded by TrafficBrainSystem.</summary>
        private static void RegisterCivilianPedestrian(ITkbDatabase tkb)
        {
            var t = new TkbTemplate("CivilianPedestrian", tkbType: 1001);

            // Universal spatial primitives (Phase 0)
            t.AddComponent(new SimTransform());
            t.AddComponent(new SimVelocity());

            // Behaviour
            t.AddComponent(new SimTier { Value = 1 });
            t.AddComponent(new DoctrineState());
            t.AddComponent(new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });
            t.AddComponent(new LocomotionChannel());

            // Vehicle kinematics (Phase 0: VehicleState is motor-only, no Position/Forward)
            t.AddComponent(new VehicleState { Speed = 0, SteerAngle = 0, Accel = 0 });
            t.AddComponent(VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            t.AddComponent(new NavState());

            // Perception
            t.AddComponent(new PerceptionReceptor
            {
                VisionRange    = 30f,
                HearingRange   = 100f,
                FieldOfViewCos = 0f   // 360° awareness
            });
            t.AddComponent(new TargetMemory());

            // Physics
            t.AddComponent(new PhysicsCollider { Radius = 0.4f, CollisionLayer = 1 });

            tkb.Register(t);
        }

        // ── Template: CivilianCar (ID 1002) ─────────────────────────────────────────

        /// <summary>Tier-1 civilian car. Brain: hardcoded by TrafficBrainSystem (road-graph loop).</summary>
        private static void RegisterCivilianCar(ITkbDatabase tkb)
        {
            var t = new TkbTemplate("CivilianCar", tkbType: 1002);

            t.AddComponent(new SimTransform());
            t.AddComponent(new SimVelocity());

            t.AddComponent(new SimTier { Value = 1 });
            t.AddComponent(new DoctrineState());
            t.AddComponent(new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });
            t.AddComponent(new LocomotionChannel());

            t.AddComponent(new VehicleState { Speed = 0, SteerAngle = 0, Accel = 0 });
            t.AddComponent(VehiclePresets.GetPreset(VehicleClass.PersonalCar));
            t.AddComponent(new NavState());

            t.AddComponent(new PhysicsCollider { Radius = 2f, CollisionLayer = 1 });

            tkb.Register(t);
        }

        // ── Template: MilitaryAPC (ID 2001) ─────────────────────────────────────────

        /// <summary>
        /// Tier-2 military APC. Brain: HSM ("ConvoyEscort_HSM").
        /// Damageable: PreviousCapabilities + HealthData back-ported from BATCH-12/13.
        /// </summary>
        private static void RegisterMilitaryAPC(ITkbDatabase tkb)
        {
            var t = new TkbTemplate("MilitaryAPC", tkbType: 2001);

            t.AddComponent(new SimTransform());
            t.AddComponent(new SimVelocity());

            t.AddComponent(new SimTier { Value = 2 });
            t.AddComponent(new DoctrineState { BrainTier = 2 });

            // HSM brain
            t.AddComponent(new BrainHsm128());
            t.AddComponent(new BrainBlackboard());

            // Required by HsmDamageBridgeSystem (BATCH-12 back-port)
            t.AddComponent(new PreviousCapabilities
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanInteract
            });

            t.AddComponent(new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanInteract
            });

            t.AddComponent(new LocomotionChannel());
            t.AddComponent(new InteractionChannel());

            t.AddComponent(new VehicleState { Speed = 0, SteerAngle = 0, Accel = 0 });
            t.AddComponent(VehiclePresets.GetPreset(VehicleClass.Tank));
            t.AddComponent(new NavState());

            // Health — damageable (BATCH-13 back-port)
            t.AddComponent(new Health { Current = 500f, Max = 500f });
            t.AddComponent(new HealthData { Current = 500f, Max = 500f });

            t.AddComponent(new PhysicsCollider { Radius = 3.5f, CollisionLayer = 1 });
            t.AddComponent(new PassengerBuffer());
            t.AddComponent(new Faction { FactionId = FactionBlue });

            tkb.Register(t);
        }

        // ── Template: InfantrySoldier (ID 2002) ─────────────────────────────────────

        /// <summary>
        /// Tier-2 infantry soldier. Brain: BTree ("InfantryCombat_BT").
        /// Rifle stats: ammo=30, 5 Hz fire rate.
        /// </summary>
        private static void RegisterInfantrySoldier(ITkbDatabase tkb)
        {
            var t = new TkbTemplate("InfantrySoldier", tkbType: 2002);

            t.AddComponent(new SimTransform());
            t.AddComponent(new SimVelocity());

            t.AddComponent(new SimTier { Value = 2 });
            t.AddComponent(new DoctrineState());

            // BTree brain
            t.AddComponent(new BrainBTreeState());
            t.AddComponent(new BrainBlackboard());

            // Required by HsmDamageBridgeSystem (BATCH-12 back-port)
            t.AddComponent(new PreviousCapabilities
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot
            });

            t.AddComponent(new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot
            });

            t.AddComponent(new LocomotionChannel());
            t.AddComponent(new WeaponChannel());
            t.AddComponent(new InteractionChannel());

            t.AddComponent(new VehicleState { Speed = 0, SteerAngle = 0, Accel = 0 });
            t.AddComponent(VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            t.AddComponent(new NavState());

            // Health — damageable (BATCH-13 back-port)
            t.AddComponent(new Health { Current = 100f, Max = 100f });
            t.AddComponent(new HealthData { Current = 100f, Max = 100f });

            // Rifle: ammo=30, muzzle=800 m/s, 5 Hz → cooldown = 60/5 = 12 ticks
            t.AddComponent(new WeaponState
            {
                Ammo                   = 30,
                MuzzleVelocity         = 800f,
                CooldownTicksRemaining = 0
            });

            t.AddComponent(new PerceptionReceptor
            {
                VisionRange    = 150f,
                HearingRange   = 200f,
                FieldOfViewCos = 0f
            });
            t.AddComponent(new TargetMemory());

            t.AddComponent(new PhysicsCollider { Radius = 0.4f, CollisionLayer = 1 });
            t.AddComponent(new Faction { FactionId = FactionBlue });

            tkb.Register(t);
        }

        // ── Template: Insurgent (ID 2003) ────────────────────────────────────────────

        /// <summary>
        /// Tier-2 insurgent RPG operator. Brain: BTree ("Ambush_BT").
        /// Same structure as InfantrySoldier but Red faction and RPG stats (ammo=1, 0.1 Hz).
        /// </summary>
        private static void RegisterInsurgent(ITkbDatabase tkb)
        {
            var t = new TkbTemplate("Insurgent", tkbType: 2003);

            t.AddComponent(new SimTransform());
            t.AddComponent(new SimVelocity());

            t.AddComponent(new SimTier { Value = 2 });
            t.AddComponent(new DoctrineState());

            // BTree brain
            t.AddComponent(new BrainBTreeState());
            t.AddComponent(new BrainBlackboard());

            // Required by HsmDamageBridgeSystem (BATCH-12 back-port)
            t.AddComponent(new PreviousCapabilities
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot
            });

            t.AddComponent(new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot
            });

            t.AddComponent(new LocomotionChannel());
            t.AddComponent(new WeaponChannel());
            t.AddComponent(new InteractionChannel());

            t.AddComponent(new VehicleState { Speed = 0, SteerAngle = 0, Accel = 0 });
            t.AddComponent(VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            t.AddComponent(new NavState());

            // Health — damageable (BATCH-13 back-port)
            t.AddComponent(new Health { Current = 100f, Max = 100f });
            t.AddComponent(new HealthData { Current = 100f, Max = 100f });

            // RPG: ammo=1, muzzle=300 m/s, 0.1 Hz → cooldown = 60/0.1 = 600 ticks
            t.AddComponent(new WeaponState
            {
                Ammo                   = 1,
                MuzzleVelocity         = 300f,
                CooldownTicksRemaining = 0
            });

            t.AddComponent(new PerceptionReceptor
            {
                VisionRange    = 150f,
                HearingRange   = 200f,
                FieldOfViewCos = 0f
            });
            t.AddComponent(new TargetMemory());

            t.AddComponent(new PhysicsCollider { Radius = 0.4f, CollisionLayer = 1 });
            t.AddComponent(new Faction { FactionId = FactionRed });

            tkb.Register(t);
        }
    }
}
