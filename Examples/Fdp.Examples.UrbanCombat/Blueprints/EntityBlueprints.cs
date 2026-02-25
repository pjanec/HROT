using System.Numerics;
using CarKinem.Core;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Physics.Components;

namespace Fdp.Examples.UrbanCombat.Blueprints
{
    /// <summary>
    /// Factory methods for the five Urban Ambush entity blueprints.
    /// Each method creates an entity in the given <paramref name="world"/>, adds all required
    /// components (with default/zero values), and returns the <see cref="Entity"/> handle.
    /// Actual spawn positions are set by <c>ScenarioDirector</c> (BCS-P7-T7) —
    /// <see cref="SimTransform.Position"/> is <see cref="Vector3.Zero"/> here.
    /// </summary>
    /// <remarks>
    /// Blueprint component lists follow DESIGN.md §9.2 with two additions back-ported from
    /// BATCH-11/12/13:
    /// <list type="bullet">
    ///   <item><see cref="PreviousCapabilities"/> — required by <c>HsmDamageBridgeSystem</c>
    ///         for all entities that carry a brain HSM or BTree (added in BATCH-12).</item>
    ///   <item><see cref="HealthData"/> — kernel mirror required by <c>MissionDirectorSystem.HealthCritical</c>
    ///         for all damageable entities (added in BATCH-13).</item>
    /// </list>
    /// </remarks>
    public static class EntityBlueprints
    {
        // ── Blueprint IDs ────────────────────────────────────────────────────────────

        /// <summary>TKB type ID for <see cref="CivilianPedestrian"/>.</summary>
        public const int Id_CivilianPedestrian = 1001;

        /// <summary>TKB type ID for <see cref="CivilianCar"/>.</summary>
        public const int Id_CivilianCar = 1002;

        /// <summary>TKB type ID for <see cref="MilitaryAPC"/>.</summary>
        public const int Id_MilitaryAPC = 2001;

        /// <summary>TKB type ID for <see cref="InfantrySoldier"/>.</summary>
        public const int Id_InfantrySoldier = 2002;

        /// <summary>TKB type ID for <see cref="Insurgent"/>.</summary>
        public const int Id_Insurgent = 2003;

        // ── Faction IDs (convention per DESIGN.md §4.1) ─────────────────────────────

        private const byte FactionNeutral = 0;
        private const byte FactionBlue    = 1;
        private const byte FactionRed     = 2;

        // ── Blueprint: CivilianPedestrian (ID 1001) ─────────────────────────────────

        /// <summary>
        /// Creates a Tier-1 civilian pedestrian entity.
        /// Brain: hardcoded by <c>TrafficBrainSystem</c> (no HSM/BTree components).
        /// <para>Components: SimTransform, SimVelocity, SimTier(1), DoctrineState,
        /// ActorCapabilityState(CanMove), LocomotionChannel, VehicleState, VehicleParams(Pedestrian),
        /// NavState, PerceptionReceptor(vision=30, hear=100), TargetMemory,
        /// PhysicsCollider(r=0.4, layer=1)</para>
        /// </summary>
        public static Entity CivilianPedestrian(EntityRepository world)
        {
            var e = world.CreateEntity();

            world.AddComponent(e, new SimTransform());
            world.AddComponent(e, new SimVelocity());
            world.AddComponent(e, new SimTier { Value = 1 });
            world.AddComponent(e, new DoctrineState());
            world.AddComponent(e, new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove
            });
            world.AddComponent(e, new LocomotionChannel());
            world.AddComponent(e, new VehicleState());
            world.AddComponent(e, VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            world.AddComponent(e, new NavState());
            world.AddComponent(e, new PerceptionReceptor
            {
                VisionRange    = 30f,
                HearingRange   = 100f,
                FieldOfViewCos = 0f    // 360° — civilians are alert in all directions
            });
            world.AddComponent(e, new TargetMemory());
            world.AddComponent(e, new PhysicsCollider { Radius = 0.4f, CollisionLayer = 1 });

            return e;
        }

        // ── Blueprint: CivilianCar (ID 1002) ────────────────────────────────────────

        /// <summary>
        /// Creates a Tier-1 civilian car entity (follows road graph, no perception).
        /// <para>Components: SimTransform, SimVelocity, SimTier(1), DoctrineState,
        /// ActorCapabilityState(CanMove), LocomotionChannel, VehicleState,
        /// VehicleParams(PersonalCar), NavState, PhysicsCollider(r=2, layer=1)</para>
        /// </summary>
        public static Entity CivilianCar(EntityRepository world)
        {
            var e = world.CreateEntity();

            world.AddComponent(e, new SimTransform());
            world.AddComponent(e, new SimVelocity());
            world.AddComponent(e, new SimTier { Value = 1 });
            world.AddComponent(e, new DoctrineState());
            world.AddComponent(e, new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove
            });
            world.AddComponent(e, new LocomotionChannel());
            world.AddComponent(e, new VehicleState());
            world.AddComponent(e, VehiclePresets.GetPreset(VehicleClass.PersonalCar));
            world.AddComponent(e, new NavState());
            world.AddComponent(e, new PhysicsCollider { Radius = 2f, CollisionLayer = 1 });

            return e;
        }

        // ── Blueprint: MilitaryAPC (ID 2001) ─────────────────────────────────────────

        /// <summary>
        /// Creates a Tier-2 military APC with an HSM brain.
        /// <para>Components: SimTransform, SimVelocity, SimTier(2), DoctrineState(BrainTier=2),
        /// BrainHsm128, BrainBlackboard, PreviousCapabilities,
        /// ActorCapabilityState(CanMove|CanInteract), LocomotionChannel, InteractionChannel,
        /// VehicleState, VehicleParams(Tank), NavState, Health(500), HealthData(500,500),
        /// PhysicsCollider(r=3.5, layer=1), PassengerBuffer, Faction(TeamId=1)</para>
        /// </summary>
        public static Entity MilitaryAPC(EntityRepository world)
        {
            var e = world.CreateEntity();

            world.AddComponent(e, new SimTransform());
            world.AddComponent(e, new SimVelocity());
            world.AddComponent(e, new SimTier { Value = 2 });
            world.AddComponent(e, new DoctrineState { BrainTier = 2 });
            world.AddComponent(e, new BrainHsm128());
            world.AddComponent(e, new BrainBlackboard());
            world.AddComponent(e, new PreviousCapabilities   // Required by HsmDamageBridgeSystem
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanInteract
            });
            world.AddComponent(e, new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanInteract
            });
            world.AddComponent(e, new LocomotionChannel());
            world.AddComponent(e, new InteractionChannel());
            world.AddComponent(e, new VehicleState());
            world.AddComponent(e, VehiclePresets.GetPreset(VehicleClass.Tank));
            world.AddComponent(e, new NavState());
            world.AddComponent(e, new Health  { Current = 500f, Max = 500f });
            world.AddComponent(e, new HealthData { Current = 500f, Max = 500f });
            world.AddComponent(e, new PhysicsCollider { Radius = 3.5f, CollisionLayer = 1 });
            world.AddComponent(e, new PassengerBuffer());
            world.AddComponent(e, new Faction { FactionId = FactionBlue });

            return e;
        }

        // ── Blueprint: InfantrySoldier (ID 2002) ─────────────────────────────────────

        /// <summary>
        /// Creates a Tier-2 infantry soldier with a BTree brain.
        /// <para>Components: SimTransform, SimVelocity, SimTier(2), DoctrineState,
        /// BrainBTreeState, BrainBlackboard, PreviousCapabilities,
        /// ActorCapabilityState(CanMove|CanShoot), LocomotionChannel, WeaponChannel,
        /// InteractionChannel, VehicleState, VehicleParams(Pedestrian), NavState,
        /// Health(100), HealthData(100,100), WeaponState(ammo=30, rate=5Hz→cooldown=12ticks,
        /// range=200, damage=25), PerceptionReceptor(vision=150, hear=200), TargetMemory,
        /// PhysicsCollider(r=0.4, layer=1), Faction(TeamId=1)</para>
        /// </summary>
        public static Entity InfantrySoldier(EntityRepository world)
        {
            var e = world.CreateEntity();

            world.AddComponent(e, new SimTransform());
            world.AddComponent(e, new SimVelocity());
            world.AddComponent(e, new SimTier { Value = 2 });
            world.AddComponent(e, new DoctrineState());
            world.AddComponent(e, new BrainBTreeState());
            world.AddComponent(e, new BrainBlackboard());
            world.AddComponent(e, new PreviousCapabilities   // Required by HsmDamageBridgeSystem
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot
            });
            world.AddComponent(e, new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot
            });
            world.AddComponent(e, new LocomotionChannel());
            world.AddComponent(e, new WeaponChannel());
            world.AddComponent(e, new InteractionChannel());
            world.AddComponent(e, new VehicleState());
            world.AddComponent(e, VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            world.AddComponent(e, new NavState());
            world.AddComponent(e, new Health    { Current = 100f, Max = 100f });
            world.AddComponent(e, new HealthData { Current = 100f, Max = 100f });
            world.AddComponent(e, new WeaponState
            {
                Ammo                   = 30,
                MuzzleVelocity         = 800f,  // Rifle muzzle velocity (m/s)
                // Fire rate 5Hz → cooldown = round(60fps / 5Hz) = 12 ticks
                CooldownTicksRemaining = 0
            });
            world.AddComponent(e, new PerceptionReceptor
            {
                VisionRange    = 150f,
                HearingRange   = 200f,
                FieldOfViewCos = 0f   // 360° combat awareness
            });
            world.AddComponent(e, new TargetMemory());
            world.AddComponent(e, new PhysicsCollider { Radius = 0.4f, CollisionLayer = 1 });
            world.AddComponent(e, new Faction { FactionId = FactionBlue });

            return e;
        }

        // ── Blueprint: Insurgent (ID 2003) ────────────────────────────────────────────

        /// <summary>
        /// Creates a Tier-2 insurgent (RPG operator) with a BTree brain.
        /// Same structure as <see cref="InfantrySoldier"/> but Red faction and RPG weapon stats
        /// (ammo=1, range=300m, damage=500, rate=0.1Hz).
        /// <para>Faction(TeamId=2), WeaponState(ammo=1, range=300, damage=500, rate=0.1Hz→cooldown=600ticks)</para>
        /// </summary>
        public static Entity Insurgent(EntityRepository world)
        {
            var e = world.CreateEntity();

            world.AddComponent(e, new SimTransform());
            world.AddComponent(e, new SimVelocity());
            world.AddComponent(e, new SimTier { Value = 2 });
            world.AddComponent(e, new DoctrineState());
            world.AddComponent(e, new BrainBTreeState());
            world.AddComponent(e, new BrainBlackboard());
            world.AddComponent(e, new PreviousCapabilities   // Required by HsmDamageBridgeSystem
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot
            });
            world.AddComponent(e, new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot
            });
            world.AddComponent(e, new LocomotionChannel());
            world.AddComponent(e, new WeaponChannel());
            world.AddComponent(e, new InteractionChannel());
            world.AddComponent(e, new VehicleState());
            world.AddComponent(e, VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            world.AddComponent(e, new NavState());
            world.AddComponent(e, new Health    { Current = 100f, Max = 100f });
            world.AddComponent(e, new HealthData { Current = 100f, Max = 100f });
            world.AddComponent(e, new WeaponState
            {
                Ammo                   = 1,
                MuzzleVelocity         = 300f,  // RPG projectile speed (m/s)
                // Fire rate 0.1Hz → cooldown = round(60fps / 0.1Hz) = 600 ticks
                CooldownTicksRemaining = 0
            });
            world.AddComponent(e, new PerceptionReceptor
            {
                VisionRange    = 150f,
                HearingRange   = 200f,
                FieldOfViewCos = 0f
            });
            world.AddComponent(e, new TargetMemory());
            world.AddComponent(e, new PhysicsCollider { Radius = 0.4f, CollisionLayer = 1 });
            world.AddComponent(e, new Faction { FactionId = FactionRed });

            return e;
        }
    }
}
