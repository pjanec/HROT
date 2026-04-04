using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using CarKinem.Core;
using CarKinem.Road;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Examples.Common;
using Fdp.Examples.Common.Constants;
using Fdp.Examples.Common.Helpers;
using Fdp.Interfaces;
using Fdp.Kernel;
using Fdp.Toolkit.Tkb;
using Fbt;
using Fbt.Runtime;
using Fbt.Serialization;
using Fhsm.Compiler;
using Fhsm.Kernel;
using Fhsm.Kernel.Attributes;
using Fhsm.Kernel.Data;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Executors;
using FDP.Toolkit.Behavior.Systems;
using FDP.Toolkit.CarKinem.Systems;
using FDP.Toolkit.Combat;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Combat.Contracts;
using FDP.Toolkit.Combat.Events;
using FDP.Toolkit.Combat.Executors;
using FDP.Toolkit.Combat.Systems;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Perception.Systems;
using FDP.Toolkit.Physics;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Physics.Systems;
using FDP.Toolkit.Replication.Services;
using FDP.Kernel.Logging;
using FDP.Toolkit.Vis2D;
using ModuleHost.Core;
using ModuleHost.Core.Abstractions;

namespace Fdp.Examples.Scenarios.Integrated
{
    // ── APC HSM action delegates ──────────────────────────────────────────────
    // Defined at assembly scope so Fhsm.SourceGen can scan them and generate
    // Fdp.Examples.Scenarios.Generated.HsmActionRegistrar.RegisterAll().

    /// <summary>
    /// HSM action delegates for the ConvoyEscort_HSM (UrbanCombatNewScenario).
    /// Mirror of <c>Fdp.Examples.UrbanCombat.Brains.ApcHsmActions</c> but self-contained
    /// in this assembly so the scenario has no dependency on the legacy UrbanCombat project.
    /// </summary>
    internal static unsafe class UrbanCombatApcBrainActions
    {
        /// <summary>
        /// Activity action for the Cruising state.
        /// Writes <see cref="NavigationConstants.ActionIdFollowRoute"/> to the
        /// <see cref="LocomotionChannel"/> so the APC follows its road-graph route.
        /// </summary>
        [HsmAction]
        public static void Activity_Cruise(void* instance, void* context, HsmCommandWriter* writer)
        {
            var bridge = (HsmKernelBridge*)context;
            var repo   = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;

            ref var loco     = ref repo.GetComponentRW<LocomotionChannel>(bridge->Self);
            var     doctrine =     repo.GetComponent<DoctrineState>(bridge->Self);

            loco.ActiveAction       = NavigationConstants.ActionIdFollowRoute;
            loco.DoctrineInstanceId = doctrine.InstanceId;
        }

        /// <summary>
        /// OnEntry action for the Disabled state.
        /// Clears <see cref="LocomotionChannel"/> and writes
        /// <see cref="BehaviorConstants.ActionIdEjectPassengers"/> to <see cref="InteractionChannel"/>.
        /// </summary>
        [HsmAction]
        public static void OnEnter_Disabled(void* instance, void* context, HsmCommandWriter* writer)
        {
            var bridge = (HsmKernelBridge*)context;
            var repo   = (EntityRepository)GCHandle.FromIntPtr(bridge->WorldHandle).Target!;

            var doctrine = repo.GetComponent<DoctrineState>(bridge->Self);

            if (repo.HasComponent<LocomotionChannel>(bridge->Self))
            {
                ref var loco = ref repo.GetComponentRW<LocomotionChannel>(bridge->Self);
                loco.ActiveAction = 0;
            }

            if (repo.HasComponent<InteractionChannel>(bridge->Self))
            {
                ref var interact = ref repo.GetComponentRW<InteractionChannel>(bridge->Self);
                interact.ActiveAction       = BehaviorConstants.ActionIdEjectPassengers;
                interact.DoctrineInstanceId = doctrine.InstanceId;
                unchecked { interact.ActionInstanceId++; }
            }
        }
    }

    /// <summary>
    /// DEM1-D010 — UrbanCombatNew: Grand Integration Demo (all toolkits).
    ///
    /// <para>Spawns 14 entities across 5 entity types on a city road network, and drives a
    /// sequential ambush narrative through 5 sequential latches:</para>
    /// <list type="number">
    ///   <item><term>AmbushFired</term><description>Insurgent's WeaponChannel shows AimAndFire.</description></item>
    ///   <item><term>ApcHalted</term><description>APC LocomotionChannel.ActiveAction == 0 (hit, MobilityLost).</description></item>
    ///   <item><term>InsurgentHit</term><description>Insurgent health below maximum (soldiers' fire landed).</description></item>
    ///   <item><term>InsurgentKilled</term><description>Insurgent entity no longer alive.</description></item>
    ///   <item><term>MissionResumed</term><description>Log emitted, EvaluateTick returns true.</description></item>
    /// </list>
    ///
    /// <para><b>System pipeline order:</b> DoctrineIngress → FireProcessing → RaycastSolver →
    /// HitResolution → MissionDirector → ChannelArbitration → BTreeTick → HsmTick →
    /// Damage → HsmDamageBridge → AudioPerception →
    /// WeaponDispatcher → InteractionDispatcher → LocomotionDispatcher →
    /// SpatialHash → CarKinematics → LinearKinematics → Ballistics</para>
    ///
    /// <para><b>Damage → MobilityLost chain (PACK-M001 / PACK-M002):</b>
    /// <c>DamageSystem</c> reduces HP; <c>HealthApplicationSystem</c> strips <c>CanMove</c>
    /// on any non-lethal hit; <c>HsmDamageBridgeSystem</c> (now in <c>CognitiveRuntimeModule</c>)
    /// enqueues <c>MobilityLost</c> into the Brain HSM the same frame.</para>
    ///
    /// <para><b>HSM lifecycle:</b> The ConvoyEscort_HSM has two states: Cruising (initial) →
    /// Disabled (on MobilityLost).  OnEnter_Disabled stops the APC and ejects soldiers.
    /// There is no recovery transition — the APC stays Disabled for the rest of the scenario.</para>
    ///
    /// <para><b>InfantryCombat BTree:</b> Uses an aggressive Selector[Condition_HasTarget →
    /// Action_AimAndFire, Action_HoldPosition] BTree identical in structure to the Insurgent's
    /// Ambush BTree.  Each soldier's TargetMemory is pre-seeded with the Insurgent entity so
    /// they engage immediately after disembarkation.</para>
    ///
    /// <para><b>Tick budget:</b> 600 ticks at 1/60 s each (10 s simulated time).
    /// Expected completion: ~50–80 ticks for the full ambush narrative to resolve.</para>
    /// </summary>
    public sealed class UrbanCombatNewScenario : IScenario
    {
        // ── Scenario identity ─────────────────────────────────────────────────

        /// <inheritdoc/>
        public string ScenarioName => ScenarioNames.UrbanCombat;   // "urbancombat"

        // ── TKB type IDs (DEM1-D010 §9.2) ────────────────────────────────────
        private const int TkbCivilianPedestrian = 1001;
        private const int TkbCivilianCar        = 1002;
        private const int TkbMilitaryApc        = 2001;
        private const int TkbInfantrySoldier    = 2002;
        private const int TkbInsurgent          = 2003;

        // ── Doctrine IDs (must match DoctrineIds in FDP.Toolkit.Behavior) ────
        private const int DoctrineWanderCivil   = 1001;
        private const int DoctrineConvoyEscort  = 2001;
        private const int DoctrineInfantryCombat = 2002;
        private const int DoctrineAmbush        = 2003;

        // ── Faction IDs ───────────────────────────────────────────────────────
        private const byte FactionNeutral = 0;
        private const byte FactionBlue    = 1;
        private const byte FactionRed     = 2;

        // ── Sensor ranges (m) ─────────────────────────────────────────────────
        private const float CivilianVisionRange  = 30f;
        private const float CivilianHearingRange = 100f;
        private const float SoldierVisionRange   = 150f;
        private const float SoldierHearingRange  = 200f;

        // ── Collider radii (m) ────────────────────────────────────────────────
        private const float HumanoidRadius = 0.4f;
        private const float CarRadius      = 2.0f;
        private const float ApcRadius      = 3.5f;

        // ── Health ────────────────────────────────────────────────────────────
        private const float ApcMaxHealth     = 500f;
        private const float SoldierMaxHealth = 100f;

        // ── Weapon stats ──────────────────────────────────────────────────────
        private const int   RifleAmmo           = 30;
        private const float RifleMuzzleVelocity = 800f;
        private const int   RpgAmmo             = 1;
        private const float RpgMuzzleVelocity   = 300f;

        // ── Spawn positions ───────────────────────────────────────────────────
        private static readonly Vector3[] CivilianPositions =
        {
            new Vector3(-35f,  40f, 0f),
            new Vector3( 30f, -45f, 0f),
            new Vector3(-20f,  50f, 0f),
            new Vector3( 45f,  30f, 0f),
            new Vector3(  0f, -35f, 0f),
        };

        private static readonly Vector3[] CarPositions =
        {
            new Vector3(  0f,  60f, 0f),
            new Vector3(  0f, -60f, 0f),
            new Vector3( 60f,   0f, 0f),
        };

        private static readonly Vector3 ApcSpawnPos      = new Vector3(0f, -80f, 0f);
        private static readonly Vector3 InsurgentSpawnPos = new Vector3(60f, 20f, 0f);

        // ── APC HSM state index constants (BFS order: root=0, Cruising=1, Disabled=2) ──
        private const ushort ApcHsmCruisingIndex = 1;
        private const ushort ApcHsmDisabledIndex = 2;

        // ── Inline BTree JSON ─────────────────────────────────────────────────

        private const string AmbushJson = """
            {
                "TreeName": "Ambush_BT",
                "Version": 1,
                "Root": {
                    "Type": "Selector",
                    "Children": [
                        {
                            "Type": "Sequence",
                            "Children": [
                                { "Type": "Condition", "Action": "Condition_HasTarget"  },
                                { "Type": "Action",    "Action": "Action_AimAndFire"    }
                            ]
                        },
                        { "Type": "Action", "Action": "Action_HoldPosition" }
                    ]
                }
            }
            """;

        // InfantryCombat uses the same aggressive structure as Ambush so soldiers
        // engage the insurgent immediately after disembarkation.
        private const string InfantryCombatJson = """
            {
                "TreeName": "InfantryCombat_BT",
                "Version": 1,
                "Root": {
                    "Type": "Selector",
                    "Children": [
                        {
                            "Type": "Sequence",
                            "Children": [
                                { "Type": "Condition", "Action": "Condition_HasTarget"  },
                                { "Type": "Action",    "Action": "Action_AimAndFire"    }
                            ]
                        },
                        { "Type": "Action", "Action": "Action_HoldPosition" }
                    ]
                }
            }
            """;

        // ── Sequential latch flags (public for test assertions) ──────────────

        private bool _latchAmbushFired     = false;
        private bool _latchApcHalted       = false;
        private bool _latchInsurgentHit    = false;
        private bool _latchInsurgentKilled = false;

        /// <summary>True once the Insurgent's WeaponChannel.ActiveAction == AimAndFire.</summary>
        public bool LatchAmbushFired     => _latchAmbushFired;
        /// <summary>True once the APC's LocomotionChannel.ActiveAction == 0 (MobilityLost processed).</summary>
        public bool LatchApcHalted       => _latchApcHalted;
        /// <summary>True once the Insurgent's health dropped below maximum.</summary>
        public bool LatchInsurgentHit    => _latchInsurgentHit;
        /// <summary>True once the Insurgent entity is no longer alive.</summary>
        public bool LatchInsurgentKilled => _latchInsurgentKilled;

        // ── Entity handles ────────────────────────────────────────────────────

        private Entity   _apc;
        private Entity   _insurgent;
        private Entity[] _soldiers = Array.Empty<Entity>();

        // ── Infrastructure ────────────────────────────────────────────────────

        private readonly TkbDatabase        _tkb             = new TkbDatabase();
        private readonly DoctrineRegistry   _doctrineRegistry = new DoctrineRegistry();
        private readonly NetworkEntityMap   _entityMap        = new NetworkEntityMap();

        private PhysicsToolkitModule? _physicsModule;
        private RoadNetworkBlob?      _road;
        private TrajectoryPoolManager? _trajectoryPool;

        private long _nextNetId = 1L;

        // ── IScenario.Configure ───────────────────────────────────────────────

        /// <inheritdoc/>
        public void Configure(EntityRepository world, ModuleHostKernel kernel)
        {
            // DEM1-D010: Register HSM action delegates generated for this assembly.
            // Fhsm.SourceGen scans [HsmAction] methods in Fdp.Examples.Scenarios and
            // generates Fdp.Examples.Scenarios.Generated.HsmActionRegistrar.
            Fdp.Examples.Scenarios.Generated.HsmActionRegistrar.RegisterAll();

            // 1. Register all component types.
            RegisterComponents(world);

            // 2. Register TKB entity blueprints.
            RegisterTkbTemplates();

            // 3. Register doctrine definitions.
            RegisterDoctrines();

            // 4. Create road network.
            _road = DemoRoadGraphFactory.CreateCityIntersection();

            // 5. Initialise physics (persistent NativeArrays — disposed in OnShutdown).
            _physicsModule = new PhysicsToolkitModule();
            _physicsModule.Initialize(world);

            // 6. Seed GlobalTime so DeltaTime is available on tick 0.
            // ScenarioSubsystem will overwrite this each tick, but the initial value
            // ensures any system that reads DeltaTime on the first tick gets a sane result.
            world.SetSingleton(new GlobalTime { DeltaTime = 1f / 60f, TimeScale = 1f });

            // 7. Build and register the system module.
            _trajectoryPool = new TrajectoryPoolManager();
            var systems = BuildSystems(world);
            kernel.RegisterModule(new UrbanCombatModule("UrbanCombatModule", systems));

            // 8. Spawn all 14 entities.
            SpawnAll(world);
        }

        // ── IScenario.EvaluateTick ────────────────────────────────────────────

        /// <inheritdoc/>
        public bool EvaluateTick(uint tick, EntityRepository world)
        {
            // ── Latch 1: Insurgent fires (WeaponChannel.ActiveAction == AimAndFire) ──
            if (!_latchAmbushFired)
            {
                if (world.IsAlive(_insurgent) &&
                    world.GetComponent<WeaponChannel>(_insurgent).ActiveAction == CombatConstants.ActionIdAimAndFire)
                {
                    _latchAmbushFired = true;
                }
            }

            // ── Latch 2: APC halted (LocomotionChannel.ActiveAction == 0 after MobilityLost) ──
            if (!_latchApcHalted && _latchAmbushFired)
            {
                bool apcAlive = world.IsAlive(_apc);
                bool halted   = !apcAlive   // APC destroyed (extreme edge case)
                                || world.GetComponent<LocomotionChannel>(_apc).ActiveAction == 0;
                if (halted)
                    _latchApcHalted = true;
            }

            // ── Latch 3: Insurgent hit (health < max, or already dead) ──────────
            if (!_latchInsurgentHit && _latchApcHalted)
            {
                if (!world.IsAlive(_insurgent))
                {
                    // Insurgent died — set both latch 3 and 4 simultaneously.
                    _latchInsurgentHit    = true;
                    _latchInsurgentKilled = true;
                }
                else
                {
                    float hp = world.GetComponent<FDP.Toolkit.Combat.Components.Health>(_insurgent).Current;
                    if (hp < SoldierMaxHealth)
                        _latchInsurgentHit = true;
                }
            }

            // ── Latch 4: Insurgent killed ─────────────────────────────────────────
            if (!_latchInsurgentKilled && _latchInsurgentHit)
            {
                if (!world.IsAlive(_insurgent))
                    _latchInsurgentKilled = true;
            }

            // ── Latch 5 / Success: mission resumed (log + return true) ────────────
            if (_latchInsurgentKilled)
            {
                // DEM1-D010: APC has no HSM recovery transition (stays Disabled).
                // "Mission Resumed" reflects scenario narrative completion — the threat
                // is neutralised.  The Latch5 test checks for this string in the log.
                FdpLog<UrbanCombatNewScenario>.Info(
                    $"[urbancombat] Phase 5 PASSED tick={tick} Mission Resumed " +
                    $"apc_alive={world.IsAlive(_apc)} insurgent_alive={world.IsAlive(_insurgent)}");
                return true;
            }

            // ── Timeout guard ─────────────────────────────────────────────────────
            if (tick > 600)
            {
                throw new ScenarioFailureException(5,
                    $"Grand demo timed out. Latches: " +
                    $"ambush={_latchAmbushFired}, " +
                    $"halt={_latchApcHalted}, " +
                    $"hit={_latchInsurgentHit}, " +
                    $"killed={_latchInsurgentKilled}");
            }

            return false;
        }

        /// <inheritdoc/>
        public void ConfigureVisuals(MapCanvas? canvas, EntityRepository world) { }

        // ── IScenario.OnShutdown ──────────────────────────────────────────────

        /// <inheritdoc/>
        public void OnShutdown()
        {
            _trajectoryPool?.Dispose();
            _physicsModule?.Dispose();
            _road?.Dispose();
        }

        // ── Private helpers — components ──────────────────────────────────────

        private static void RegisterComponents(EntityRepository world)
        {
            // Fdp.Kernel spatial primitives
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<SimVelocity>();

            // FDP.Toolkit.Behavior
            world.RegisterComponent<DoctrineState>();
            world.RegisterComponent<SimTier>();
            world.RegisterComponent<BrainBlackboard>();
            world.RegisterComponent<BrainBTreeState>();
            world.RegisterComponent<BrainHsm128>();
            world.RegisterComponent<ActorCapabilityState>();
            world.RegisterComponent<PreviousCapabilities>();
            world.RegisterComponent<LocomotionChannel>();
            world.RegisterComponent<WeaponChannel>();
            world.RegisterComponent<InteractionChannel>();
            world.RegisterComponent<PassengerBuffer>();
            world.RegisterComponent<IsEmbarkedTag>();
            world.RegisterComponent<MissionPlanQueue>();

            // FDP.Toolkit.Perception
            world.RegisterComponent<Faction>();
            world.RegisterComponent<PerceptionReceptor>();
            world.RegisterComponent<TargetMemory>();

            // FDP.Toolkit.Physics
            world.RegisterComponent<PhysicsCollider>();

            // FDP.Toolkit.Combat
            world.RegisterComponent<FDP.Toolkit.Combat.Components.Health>();
            world.RegisterComponent<WeaponState>();
            world.RegisterComponent<BallisticProjectile>();

            // FDP.Toolkit.CarKinem
            world.RegisterComponent<VehicleState>();
            world.RegisterComponent<VehicleParams>();
            world.RegisterComponent<NavState>();

            // Events
            world.RegisterEvent<WeaponFireIntent>();
            world.RegisterEvent<HitEvent>();
        }

        // ── Private helpers — TKB templates ──────────────────────────────────

        private void RegisterTkbTemplates()
        {
            RegisterCivilianPedestrian();
            RegisterCivilianCar();
            RegisterMilitaryApc();
            RegisterInfantrySoldier();
            RegisterInsurgent();
        }

        private void RegisterCivilianPedestrian()
        {
            var t = new TkbTemplate("CivilianPedestrian", TkbCivilianPedestrian);
            t.AddComponent(new SimTransform());
            t.AddComponent(new SimVelocity());
            t.AddComponent(new SimTier  { Value = BehaviorConstants.SimTierCivilian });
            t.AddComponent(new DoctrineState());
            t.AddComponent(new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });
            t.AddComponent(new LocomotionChannel());
            t.AddComponent(new VehicleState { Speed = 0, SteerAngle = 0, Accel = 0 });
            t.AddComponent(VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            t.AddComponent(new NavState());
            t.AddComponent(new PerceptionReceptor
            {
                VisionRange    = CivilianVisionRange,
                HearingRange   = CivilianHearingRange,
                FieldOfViewCos = 0f,
            });
            t.AddComponent(new TargetMemory());
            t.AddComponent(new PhysicsCollider
            {
                Radius         = HumanoidRadius,
                CollisionLayer = PhysicsConstants.EntityCollisionLayer,
            });
            _tkb.Register(t);
        }

        private void RegisterCivilianCar()
        {
            var t = new TkbTemplate("CivilianCar", TkbCivilianCar);
            t.AddComponent(new SimTransform());
            t.AddComponent(new SimVelocity());
            t.AddComponent(new SimTier  { Value = BehaviorConstants.SimTierCivilian });
            t.AddComponent(new DoctrineState());
            t.AddComponent(new ActorCapabilityState { Capabilities = ActorCapabilities.CanMove });
            t.AddComponent(new LocomotionChannel());
            t.AddComponent(new VehicleState { Speed = 0, SteerAngle = 0, Accel = 0 });
            t.AddComponent(VehiclePresets.GetPreset(VehicleClass.PersonalCar));
            t.AddComponent(new NavState());
            t.AddComponent(new PhysicsCollider
            {
                Radius         = CarRadius,
                CollisionLayer = PhysicsConstants.EntityCollisionLayer,
            });
            _tkb.Register(t);
        }

        private void RegisterMilitaryApc()
        {
            var t = new TkbTemplate("MilitaryAPC", TkbMilitaryApc);
            t.AddComponent(new SimTransform());
            t.AddComponent(new SimVelocity());
            t.AddComponent(new SimTier  { Value = BehaviorConstants.SimTierTactical });
            t.AddComponent(new DoctrineState { BrainTier = BehaviorConstants.BrainTierHsm });
            t.AddComponent(new BrainHsm128());
            t.AddComponent(new BrainBlackboard());
            t.AddComponent(new PreviousCapabilities
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanInteract,
            });
            t.AddComponent(new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanInteract,
            });
            t.AddComponent(new LocomotionChannel());
            t.AddComponent(new InteractionChannel());
            t.AddComponent(new VehicleState { Speed = 0, SteerAngle = 0, Accel = 0 });
            t.AddComponent(VehiclePresets.GetPreset(VehicleClass.Tank));
            t.AddComponent(new NavState());
            t.AddComponent(new FDP.Toolkit.Combat.Components.Health
            {
                Current = ApcMaxHealth,
                Max     = ApcMaxHealth,
            });
            t.AddComponent(new PhysicsCollider
            {
                Radius         = ApcRadius,
                CollisionLayer = PhysicsConstants.EntityCollisionLayer,
            });
            t.AddComponent(new PassengerBuffer());
            t.AddComponent(new Faction { FactionId = FactionBlue });
            _tkb.Register(t);
        }

        private void RegisterInfantrySoldier()
        {
            var t = new TkbTemplate("InfantrySoldier", TkbInfantrySoldier);
            t.AddComponent(new SimTransform());
            t.AddComponent(new SimVelocity());
            t.AddComponent(new SimTier  { Value = BehaviorConstants.SimTierTactical });
            t.AddComponent(new DoctrineState { BrainTier = BehaviorConstants.BrainTierBTree });
            t.AddComponent(new BrainBTreeState());
            t.AddComponent(new BrainBlackboard());
            t.AddComponent(new PreviousCapabilities
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot,
            });
            t.AddComponent(new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot,
            });
            t.AddComponent(new LocomotionChannel());
            t.AddComponent(new WeaponChannel());
            t.AddComponent(new InteractionChannel());
            t.AddComponent(new VehicleState { Speed = 0, SteerAngle = 0, Accel = 0 });
            t.AddComponent(VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            t.AddComponent(new NavState());
            t.AddComponent(new FDP.Toolkit.Combat.Components.Health
            {
                Current = SoldierMaxHealth,
                Max     = SoldierMaxHealth,
            });
            t.AddComponent(new WeaponState
            {
                Ammo                   = RifleAmmo,
                MuzzleVelocity         = RifleMuzzleVelocity,
                CooldownTicksRemaining = 0,
            });
            t.AddComponent(new PerceptionReceptor
            {
                VisionRange    = SoldierVisionRange,
                HearingRange   = SoldierHearingRange,
                FieldOfViewCos = 0f,
            });
            t.AddComponent(new TargetMemory());
            t.AddComponent(new PhysicsCollider
            {
                Radius         = HumanoidRadius,
                CollisionLayer = PhysicsConstants.EntityCollisionLayer,
            });
            t.AddComponent(new Faction { FactionId = FactionBlue });
            _tkb.Register(t);
        }

        private void RegisterInsurgent()
        {
            var t = new TkbTemplate("Insurgent", TkbInsurgent);
            t.AddComponent(new SimTransform());
            t.AddComponent(new SimVelocity());
            t.AddComponent(new SimTier  { Value = BehaviorConstants.SimTierTactical });
            t.AddComponent(new DoctrineState { BrainTier = BehaviorConstants.BrainTierBTree });
            t.AddComponent(new BrainBTreeState());
            t.AddComponent(new BrainBlackboard());
            t.AddComponent(new PreviousCapabilities
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot,
            });
            t.AddComponent(new ActorCapabilityState
            {
                Capabilities = ActorCapabilities.CanMove | ActorCapabilities.CanShoot,
            });
            t.AddComponent(new LocomotionChannel());
            t.AddComponent(new WeaponChannel());
            t.AddComponent(new InteractionChannel());
            t.AddComponent(new VehicleState { Speed = 0, SteerAngle = 0, Accel = 0 });
            t.AddComponent(VehiclePresets.GetPreset(VehicleClass.Pedestrian));
            t.AddComponent(new NavState());
            t.AddComponent(new FDP.Toolkit.Combat.Components.Health
            {
                Current = SoldierMaxHealth,
                Max     = SoldierMaxHealth,
            });
            t.AddComponent(new WeaponState
            {
                Ammo                   = RpgAmmo,
                MuzzleVelocity         = RpgMuzzleVelocity,
                CooldownTicksRemaining = 0,
            });
            t.AddComponent(new PerceptionReceptor
            {
                VisionRange    = SoldierVisionRange,
                HearingRange   = SoldierHearingRange,
                FieldOfViewCos = 0f,
            });
            t.AddComponent(new TargetMemory());
            t.AddComponent(new PhysicsCollider
            {
                Radius         = HumanoidRadius,
                CollisionLayer = PhysicsConstants.EntityCollisionLayer,
            });
            t.AddComponent(new Faction { FactionId = FactionRed });
            _tkb.Register(t);
        }

        // ── Private helpers — doctrines ───────────────────────────────────────

        private void RegisterDoctrines()
        {
            // ── Civilian doctrines (BrainTier=0; TrafficBrainSystem absent — civilians static) ──
            _doctrineRegistry.Register(DoctrineWanderCivil, "WanderCivil",
                new DoctrineDefinition { Name = "WanderCivil", BrainTier = 0 });

            // ── APC: HSM ConvoyEscort ─────────────────────────────────────────
            _doctrineRegistry.Register(DoctrineConvoyEscort, "ConvoyEscort",
                new DoctrineDefinition
                {
                    Name          = "ConvoyEscort",
                    BrainTier     = BehaviorConstants.BrainTierHsm,
                    HsmDefinition = BuildApcHsm(),
                });

            // ── InfantrySoldier: aggressive InfantryCombat BTree ─────────────
            var infantryReg = new ActionRegistry<BrainBlackboard, BTreeContext>();
            infantryReg.Register("Condition_HasTarget", BTreeNodes.Condition_HasTarget);
            infantryReg.Register("Action_AimAndFire",   BTreeNodes.Action_AimAndFire);
            infantryReg.Register("Action_HoldPosition", BTreeNodes.Action_HoldPosition);
            var infantryBlob = TreeCompiler.CompileFromJson(InfantryCombatJson);
            _doctrineRegistry.Register(DoctrineInfantryCombat, "InfantryCombat",
                new DoctrineDefinition
                {
                    Name             = "InfantryCombat",
                    BrainTier        = BehaviorConstants.BrainTierBTree,
                    BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(infantryBlob, infantryReg),
                });

            // ── Insurgent: Ambush BTree ───────────────────────────────────────
            var ambushReg = new ActionRegistry<BrainBlackboard, BTreeContext>();
            ambushReg.Register("Condition_HasTarget", BTreeNodes.Condition_HasTarget);
            ambushReg.Register("Action_AimAndFire",   BTreeNodes.Action_AimAndFire);
            ambushReg.Register("Action_HoldPosition", BTreeNodes.Action_HoldPosition);
            var ambushBlob = TreeCompiler.CompileFromJson(AmbushJson);
            _doctrineRegistry.Register(DoctrineAmbush, "Ambush",
                new DoctrineDefinition
                {
                    Name             = "Ambush",
                    BrainTier        = BehaviorConstants.BrainTierBTree,
                    BTreeInterpreter = new Interpreter<BrainBlackboard, BTreeContext>(ambushBlob, ambushReg),
                });
        }

        private static unsafe HsmDefinitionBlob BuildApcHsm()
        {
            var builder = new HsmBuilder("ConvoyEscort_HSM");

            builder.Event("MobilityLost", BehaviorConstants.EventId_MobilityLost);

            builder
                .RegisterAction("Activity_Cruise")
                .RegisterAction("OnEnter_Disabled");

            var cruising = builder.State("Cruising")
                .Activity("Activity_Cruise")
                .Initial();

            var disabled = builder.State("Disabled")
                .OnEntry("OnEnter_Disabled");

            cruising.On(BehaviorConstants.EventId_MobilityLost).GoTo(disabled);

            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);

            var errors = HsmGraphValidator.Validate(graph);
            if (errors.Count > 0)
                throw new InvalidOperationException(
                    $"APC HSM validation: {string.Join(", ", errors.Select(e => e.Message))}");

            var flattened = HsmFlattener.Flatten(graph);
            return HsmEmitter.Emit(flattened);
        }

        // ── Private helpers — system pipeline ────────────────────────────────

        private ComponentSystem[] BuildSystems(EntityRepository world)
        {
            var weaponSys = new WeaponDispatcherSystem();
            weaponSys.RegisterExecutor(CombatConstants.ActionIdAimAndFire, new AimAndFireExecutor());

            var interactSys = new InteractionDispatcherSystem();
            interactSys.RegisterExecutor(BehaviorConstants.ActionIdEjectPassengers, new EjectPassengersExecutor());

            var systems = new ComponentSystem[]
            {
                // ── Input equivalent ──
                new DoctrineIngressSystem(_doctrineRegistry),
                new FireProcessingSystem(),
                new RaycastSolverSystem(),
                new HitResolutionSystem(),

                // ── Simulation ────────
                new MissionDirectorSystem(),
                new ChannelArbitrationSystem(),
                new BTreeTickSystem(_doctrineRegistry),
                new HsmTickSystem<BrainHsm128>(_doctrineRegistry),
                new DamageSystem(),
                new HsmDamageBridgeSystem(),
                new AudioPerceptionSystem(),
                weaponSys,
                interactSys,
                new LocomotionDispatcherSystem(),

                // ── PostSim equivalent ──
                new SpatialHashSystem(),
                new CarKinematicsSystem(_road ?? throw new InvalidOperationException("Road not initialized"), _trajectoryPool!),
                new LinearKinematicsSystem(),
                new BallisticsSystem(),
            };

            foreach (var sys in systems)
                sys.Create(world);

            return systems;
        }

        // ── Private helpers — entity spawning ────────────────────────────────

        private unsafe void SpawnAll(EntityRepository world)
        {
            // 1. Civilian pedestrians
            for (int i = 0; i < 5; i++)
                SpawnEntity(world, TkbCivilianPedestrian, CivilianPositions[i], 0f, DoctrineWanderCivil);

            // 2. Civilian cars
            for (int i = 0; i < 3; i++)
                SpawnEntity(world, TkbCivilianCar, CarPositions[i], 0f, DoctrineWanderCivil);

            // 3. Military APC (heading north — π/2 yaw in ENU XY)
            _apc = SpawnEntity(world, TkbMilitaryApc, ApcSpawnPos, MathF.PI / 2f, DoctrineConvoyEscort);

            // Pre-initialise the APC HSM brain so HsmKernel.Update processes it correctly.
            // Without this, BrainHsm128.Header.MachineId == 0 and ValidateInstance rejects it.
            if (_doctrineRegistry.TryGetDefinition(DoctrineConvoyEscort, out var convoyDef)
                && convoyDef.HsmDefinition != null)
            {
                ref var brain = ref world.GetComponentRW<BrainHsm128>(_apc);
                brain.State.Header.MachineId  = convoyDef.HsmDefinition.Header.StructureHash;
                brain.State.Header.Phase      = InstancePhase.RTC;
                brain.State.ActiveLeafIds[0]  = ApcHsmCruisingIndex;
            }

            // 4. Infantry soldiers — spawn co-located with APC, then embark
            _soldiers = new Entity[4];
            for (int i = 0; i < 4; i++)
                _soldiers[i] = SpawnEntity(world, TkbInfantrySoldier, ApcSpawnPos, 0f, DoctrineInfantryCombat);

            EmbarkSoldiers(world, _apc, _soldiers);

            // 5. Insurgent (south-east corner, stationary)
            _insurgent = SpawnEntity(world, TkbInsurgent, InsurgentSpawnPos, 0f, DoctrineAmbush);

            // 6. Pre-seed insurgent TargetMemory with APC
            if (world.HasComponent<TargetMemory>(_insurgent))
            {
                ref var mem = ref world.GetComponentRW<TargetMemory>(_insurgent);
                var apcPos  = world.GetComponent<SimTransform>(_apc).Position;
                TargetMemory.AddOrUpdateTarget(ref mem,
                    entityId:   (long)_apc.PackedValue,
                    posX:       apcPos.X,
                    posY:       apcPos.Y,
                    scoreBoost: 100f,
                    tick:       0u);
            }

            // 7. Pre-seed each soldier's TargetMemory with insurgent so they engage
            //    immediately after disembarkation (InfantryCombat BTree uses Condition_HasTarget).
            foreach (var soldier in _soldiers)
            {
                if (!world.HasComponent<TargetMemory>(soldier))
                    continue;

                ref var mem      = ref world.GetComponentRW<TargetMemory>(soldier);
                var insurgentPos = world.GetComponent<SimTransform>(_insurgent).Position;
                TargetMemory.AddOrUpdateTarget(ref mem,
                    entityId:   (long)_insurgent.PackedValue,
                    posX:       insurgentPos.X,
                    posY:       insurgentPos.Y,
                    scoreBoost: 100f,
                    tick:       0u);
            }
        }

        private unsafe Entity SpawnEntity(
            EntityRepository world,
            int tkbTypeId,
            Vector3 position,
            float yawRadians,
            int doctrineId)
        {
            var template = _tkb.GetByType(tkbTypeId)
                ?? throw new InvalidOperationException($"TKB type {tkbTypeId} not registered.");

            var entity = world.CreateEntity();
            template.ApplyTo(world, entity);

            ref var tf   = ref world.GetComponentRW<SimTransform>(entity);
            tf.Position  = position;
            tf.Rotation  = SimMath.FromYaw(yawRadians);

            ref var doctrine = ref world.GetComponentRW<DoctrineState>(entity);
            doctrine.ActiveDoctrineHash = doctrineId;
            unchecked { doctrine.InstanceId++; }

            if (_doctrineRegistry.TryGetDefinition(doctrineId, out var def))
                doctrine.BrainTier = def.BrainTier;

            _entityMap.Register(_nextNetId++, entity);

            return entity;
        }

        private static void EmbarkSoldiers(EntityRepository world, Entity apc, Entity[] soldiers)
        {
            ref var buffer = ref world.GetComponentRW<PassengerBuffer>(apc);

            foreach (var soldier in soldiers)
            {
                buffer.Passengers[buffer.Count] = soldier;
                buffer.Count++;

                ref var caps = ref world.GetComponentRW<ActorCapabilityState>(soldier);
                caps.Capabilities &= ~(ActorCapabilities.CanMove | ActorCapabilities.CanShoot);

                world.AddComponent(soldier, new IsEmbarkedTag { VehicleEntity = apc });
            }
        }

        // ── BTree node delegates (inline; no dependency on legacy UrbanCombat assembly) ──

        private static class BTreeNodes
        {
            public static NodeStatus Condition_HasTarget(
                ref BrainBlackboard blackboard,
                ref BehaviorTreeState state,
                ref BTreeContext ctx,
                int paramIndex)
            {
                if (!ctx.World.HasComponent<TargetMemory>(ctx.Self))
                    return NodeStatus.Failure;

                var tm = ctx.World.GetComponent<TargetMemory>(ctx.Self);
                return tm.Count > 0 ? NodeStatus.Success : NodeStatus.Failure;
            }

            public static unsafe NodeStatus Action_AimAndFire(
                ref BrainBlackboard blackboard,
                ref BehaviorTreeState state,
                ref BTreeContext ctx,
                int paramIndex)
            {
                if (!ctx.World.HasComponent<WeaponChannel>(ctx.Self))  return NodeStatus.Failure;
                if (!ctx.World.HasComponent<TargetMemory>(ctx.Self))   return NodeStatus.Failure;

                var mem = ctx.World.GetComponent<TargetMemory>(ctx.Self);
                if (mem.Count == 0) return NodeStatus.Failure;

                var targetEntity = new Entity((ulong)mem.EntityIds[0]);

                ref var channel = ref ctx.World.GetComponentRW<WeaponChannel>(ctx.Self);

                fixed (byte* ptr = channel.Params)
                    *(AimAndFireParams*)ptr = new AimAndFireParams { Target = targetEntity, CooldownTicks = 0 };

                bool needsReactivation =
                    channel.ActiveAction != CombatConstants.ActionIdAimAndFire
                    || channel.Status == NodeStatus.Failure;

                if (needsReactivation)
                    unchecked { channel.ActionInstanceId++; }

                channel.ActiveAction = CombatConstants.ActionIdAimAndFire;
                return NodeStatus.Running;
            }

            public static NodeStatus Action_HoldPosition(
                ref BrainBlackboard blackboard,
                ref BehaviorTreeState state,
                ref BTreeContext ctx,
                int paramIndex)
            {
                return NodeStatus.Running;
            }
        }

        // ── Inner: UrbanCombatModule ──────────────────────────────────────────

        /// <summary>
        /// Minimal <see cref="IEcsModule"/> that drives all scenario systems sequentially
        /// each kernel tick (after the kernel's own SwapBuffers pass).
        /// Uses <c>ExecutionPolicy.Synchronous()</c> so the kernel passes the live
        /// <see cref="EntityRepository"/> directly (no serialisation overhead).
        /// </summary>
        private sealed class UrbanCombatModule : IEcsModule
        {
            private readonly ComponentSystem[] _systems;

            public string Name { get; }
            public ExecutionPolicy Policy         => ExecutionPolicy.Synchronous();
            public IReadOnlyList<Type>? WatchComponents => null;
            public IReadOnlyList<Type>? WatchEvents     => null;

            public UrbanCombatModule(string name, ComponentSystem[] systems)
            {
                Name     = name;
                _systems = systems;
            }

            public void RegisterSystems(ISystemRegistry registry) { }

            public void Tick(ISimulationView view, float deltaTime)
            {
                foreach (var sys in _systems)
                    sys.Run();
            }

            public IReadOnlyList<Type>? GetRequiredComponents() => null;
        }
    }
}
