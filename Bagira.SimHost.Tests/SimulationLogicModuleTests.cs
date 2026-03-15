using System;
using Bagira.SimHost.Modules;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Spatial;
using CarKinem.Trajectory;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Combat.Components;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Navigation.Systems;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Physics;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.CarKinem.Systems;
using FDP.Toolkit.Replication.Services;

namespace Bagira.SimHost.Tests
{
    /// <summary>
    /// Tests for <see cref="SimulationLogicModule"/> (TASK-S4.1).
    /// </summary>
    public class SimulationLogicModuleTests
    {
        // ── World factory ─────────────────────────────────────────────────────

        /// <summary>
        /// Creates an empty <see cref="EntityRepository"/> with every component type
        /// required by the systems registered in <see cref="SimulationLogicModule"/>.
        /// No entities are added — the test exercises topology / ordering only.
        /// </summary>
        private static EntityRepository CreateEmptyWorld()
        {
            var world = new EntityRepository();

            // ── Behavior toolkit components ───────────────────────────────────
            world.RegisterComponent<DoctrineState>();
            world.RegisterComponent<LocomotionChannel>();
            world.RegisterComponent<WeaponChannel>();
            world.RegisterComponent<InteractionChannel>();
            world.RegisterComponent<ActorCapabilityState>();
            world.RegisterComponent<BrainBTreeState>();
            world.RegisterComponent<BrainBlackboard>();

            // HSM brain tiers (for APC-style HSM doctrines)
            world.RegisterComponent<BrainHsm64>();
            world.RegisterComponent<BrainHsm128>();
            world.RegisterComponent<PreviousCapabilities>();
            world.RegisterComponent<PassengerBuffer>();
            world.RegisterComponent<IsEmbarkedTag>();

            // Perception
            world.RegisterComponent<Faction>();
            world.RegisterComponent<PerceptionReceptor>();
            world.RegisterComponent<TargetMemory>();

            // Combat & Physics
            world.RegisterComponent<PhysicsCollider>();
            world.RegisterComponent<WeaponState>();
            world.RegisterComponent<Health>();
            world.RegisterComponent<BallisticProjectile>();
            world.RegisterComponent<HealthData>();

            // ── Core simulation components (Fdp.Kernel + CarKinem) ────────────
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<SimVelocity>();
            world.RegisterComponent<VehicleState>();
            world.RegisterComponent<VehicleParams>();
            world.RegisterComponent<NavState>();
            world.RegisterComponent<FormationRoster>();

            // ── Navigation CQRS components (BATCH-01 + CT-MOD1-A) ─────────────
            world.RegisterComponent<NavigationIntent>();
            world.RegisterComponent<NavigationStatus>();
            world.RegisterComponent<FrustrationTicks>();

            // GlobalTime singleton — ComponentSystem.DeltaTime reads this.
            world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });

            var physicsModule = new PhysicsToolkitModule();
            physicsModule.Initialize(world);

            return world;
        }

        private static void DisposeRaycastBatchData(EntityRepository world)
        {
            if (world.HasSingleton<RaycastBatchData>())
            {
                ref var batch = ref world.GetSingleton<RaycastBatchData>();
                if (batch.Requests.IsCreated) batch.Requests.Dispose();
                if (batch.Hits.IsCreated) batch.Hits.Dispose();
            }
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Instantiates <see cref="SimulationLogicModule"/> with minimal dummy parameters,
        /// registers all systems into an empty <see cref="SystemGroup"/>, and ticks the
        /// group once. Verifies:
        /// <list type="bullet">
        ///   <item>All 9 systems can be added without compile or runtime error.</item>
        ///   <item>The system topology has no cyclic dependencies (topological sort succeeds).</item>
        ///   <item>A single kernel update on an empty world does not throw.</item>
        /// </list>
        /// </summary>
        [Fact]
        public void SimulationLogicModule_EmptyWorld_AllSystemsRegisterAndUpdateWithoutException()
        {
            // ── Arrange ───────────────────────────────────────────────────────
            using var world = CreateEmptyWorld();

            var doctrineRegistry = new DoctrineRegistry();
            var entityMap        = new NetworkEntityMap();

            // Minimal CarKinem dependencies — empty road network and fresh pool.
            var roadNetwork     = new RoadNetworkBuilder().Build(10f, 10, 10);
            var trajectoryPool  = new TrajectoryPoolManager();

            var module = new SimulationLogicModule(
                doctrineRegistry,
                entityMap,
                vehicleAPI:              null,
                roadNetwork:             roadNetwork,
                trajectoryPool:          trajectoryPool,
                formationTemplateManager: null);   // defaults to new FormationTemplateManager()

            var inputGroup = new SystemGroup();
            inputGroup.Create(world);
            var simGroup = new SystemGroup();
            simGroup.Create(world);
            var postSimGroup = new SystemGroup();
            postSimGroup.Create(world);

            module.RegisterSystems(inputGroup, simGroup, postSimGroup);

            // ── Act + Assert ──────────────────────────────────────────────────
            // Calling Run() triggers SortSystems() which validates the dependency
            // graph (throws InvalidOperationException on cyclic dependencies),
            // then executes OnUpdate() for each system on the empty world.
            var exception = Record.Exception(() =>
            {
                inputGroup.Run();
                simGroup.Run();
                postSimGroup.Run();
            });

            Assert.Null(exception);

            Assert.Equal(3, inputGroup.SystemCount);
            Assert.Equal(19, simGroup.SystemCount);  // -1 vs pre-MOD1-BATCH-08: LosRequestBatchingSystem removed from CombatModule (CT-MOD1-N); it now runs only inside AutonomousPerceptionModule on the background thread.
            Assert.Equal(1, postSimGroup.SystemCount);

            // ── Cleanup ───────────────────────────────────────────────────────
            inputGroup.Dispose();
            simGroup.Dispose();
            postSimGroup.Dispose();
            roadNetwork.Dispose();
            trajectoryPool.Dispose();
            DisposeRaycastBatchData(world);
        }

        /// <summary>
        /// Verifies that the <see cref="LinearKinematicsSystem"/> is present in the
        /// group (requirement: must not be omitted per TASK-S4.1 spec).
        /// </summary>
        [Fact]
        public void SimulationLogicModule_ContainsLinearKinematicsSystem()
        {
            using var world = CreateEmptyWorld();
            var module = new SimulationLogicModule(new DoctrineRegistry(), new NetworkEntityMap());
            var inputGroup = new SystemGroup();
            inputGroup.Create(world);
            var simGroup = new SystemGroup();
            simGroup.Create(world);
            var postSimGroup = new SystemGroup();
            postSimGroup.Create(world);
            module.RegisterSystems(inputGroup, simGroup, postSimGroup);

            var systems = simGroup.GetSystems();
            Assert.Contains(systems, s => s is LinearKinematicsSystem);

            inputGroup.Dispose();
            simGroup.Dispose();
            postSimGroup.Dispose();
            DisposeRaycastBatchData(world);
        }

        [Fact]
        public void SimulationLogicModule_CombatEntity_TenFramePumpHasNoException()
        {
            using var world = CreateEmptyWorld();
            var module = new SimulationLogicModule(new DoctrineRegistry(), new NetworkEntityMap());

            var inputGroup = new SystemGroup();
            inputGroup.Create(world);
            var simGroup = new SystemGroup();
            simGroup.Create(world);
            var postSimGroup = new SystemGroup();
            postSimGroup.Create(world);
            module.RegisterSystems(inputGroup, simGroup, postSimGroup);

            var entity = world.CreateEntity();
            world.AddComponent(entity, new SimTransform());
            world.AddComponent(entity, new SimVelocity());
            world.AddComponent(entity, new PerceptionReceptor { VisionRange = 500f, HearingRange = 250f, FieldOfViewCos = 0f });
            world.AddComponent(entity, new TargetMemory());
            world.AddComponent(entity, new WeaponState { Ammo = 1, MuzzleVelocity = 800f, CooldownTicksRemaining = 0 });
            world.AddComponent(entity, new Health { Current = 100f, Max = 100f });
            world.AddComponent(entity, new HealthData { Current = 100f, Max = 100f });
            world.AddComponent(entity, new PhysicsCollider { Radius = 1.0f, CollisionLayer = 1 });
            world.AddComponent(entity, new Faction { FactionId = 1 });
            world.AddComponent(entity, new BrainBTreeState());
            world.AddComponent(entity, new BrainBlackboard());
            world.AddComponent(entity, new DoctrineState());
            world.AddComponent(entity, new LocomotionChannel());
            world.AddComponent(entity, new WeaponChannel());
            world.AddComponent(entity, new InteractionChannel());
            world.AddComponent(entity, new ActorCapabilityState());

            Exception? exception = null;
            for (int i = 0; i < 10; i++)
            {
                world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });
                exception = Record.Exception(() =>
                {
                    inputGroup.Run();
                    simGroup.Run();
                    postSimGroup.Run();
                });
                if (exception != null) break;
            }

            Assert.Null(exception);

            inputGroup.Dispose();
            simGroup.Dispose();
            postSimGroup.Dispose();
            DisposeRaycastBatchData(world);
        }
    }

    // ── Role-conditional sub-module tests (DB-MOD1-08) ────────────────────────

    /// <summary>
    /// Verifies that <see cref="SimulationLogicModule"/> only creates the sub-modules
    /// appropriate for the supplied <see cref="NodeRole"/>.
    /// </summary>
    public class SimulationLogicModule_RoleConditionalTests
    {
        private static EntityRepository CreateEmptyWorld()
        {
            var world = new EntityRepository();
            world.RegisterComponent<DoctrineState>();
            world.RegisterComponent<LocomotionChannel>();
            world.RegisterComponent<WeaponChannel>();
            world.RegisterComponent<InteractionChannel>();
            world.RegisterComponent<ActorCapabilityState>();
            world.RegisterComponent<BrainBTreeState>();
            world.RegisterComponent<BrainBlackboard>();
            world.RegisterComponent<BrainHsm64>();
            world.RegisterComponent<BrainHsm128>();
            world.RegisterComponent<PreviousCapabilities>();
            world.RegisterComponent<PassengerBuffer>();
            world.RegisterComponent<IsEmbarkedTag>();
            world.RegisterComponent<Faction>();
            world.RegisterComponent<PerceptionReceptor>();
            world.RegisterComponent<TargetMemory>();
            world.RegisterComponent<PhysicsCollider>();
            world.RegisterComponent<WeaponState>();
            world.RegisterComponent<Health>();
            world.RegisterComponent<BallisticProjectile>();
            world.RegisterComponent<HealthData>();
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<SimVelocity>();
            world.RegisterComponent<VehicleState>();
            world.RegisterComponent<VehicleParams>();
            world.RegisterComponent<NavState>();
            world.RegisterComponent<FormationRoster>();
            world.RegisterComponent<NavigationIntent>();
            world.RegisterComponent<NavigationStatus>();
            world.RegisterComponent<FrustrationTicks>();
            world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });
            var physicsModule = new PhysicsToolkitModule();
            physicsModule.Initialize(world);
            return world;
        }

        private static void DisposeRaycastBatchData(EntityRepository world)
        {
            if (world.HasSingleton<RaycastBatchData>())
            {
                ref var batch = ref world.GetSingleton<RaycastBatchData>();
                if (batch.Requests.IsCreated) batch.Requests.Dispose();
                if (batch.Hits.IsCreated) batch.Hits.Dispose();
            }
        }

        [Fact]
        public void SimulationLogicModule_BrainRole_DoesNotRegisterGroundKinematics()
        {
            using var world = CreateEmptyWorld();
            var module = new SimulationLogicModule(
                new DoctrineRegistry(), new NetworkEntityMap(),
                role: NodeRole.Brain);

            var inputGroup   = new SystemGroup(); inputGroup.Create(world);
            var simGroup     = new SystemGroup(); simGroup.Create(world);
            var postSimGroup = new SystemGroup(); postSimGroup.Create(world);

            module.RegisterSystems(inputGroup, simGroup, postSimGroup);

            // Brain role must not include LinearKinematicsSystem (in GroundKinematicsModule).
            Assert.DoesNotContain(simGroup.GetSystems(), s => s is LinearKinematicsSystem);

            // Brain role must not register NavigationIntentBridgeSystem (needs GroundKinematics).
            Assert.DoesNotContain(simGroup.GetSystems(), s => s is NavigationIntentBridgeSystem);

            // Brain role must not expose a TrajectoryPool or FormationTemplates.
            Assert.Null(module.TrajectoryPool);
            Assert.Null(module.FormationTemplates);

            inputGroup.Dispose();
            simGroup.Dispose();
            postSimGroup.Dispose();
            DisposeRaycastBatchData(world);
        }

        [Fact]
        public void SimulationLogicModule_MuscleGroundRole_DoesNotRegisterCognitiveModules()
        {
            using var world = CreateEmptyWorld();
            var module = new SimulationLogicModule(
                new DoctrineRegistry(), new NetworkEntityMap(),
                role: NodeRole.MuscleGround);

            var inputGroup   = new SystemGroup(); inputGroup.Create(world);
            var simGroup     = new SystemGroup(); simGroup.Create(world);
            var postSimGroup = new SystemGroup(); postSimGroup.Create(world);

            module.RegisterSystems(inputGroup, simGroup, postSimGroup);

            // MuscleGround must include LinearKinematicsSystem (ground movement).
            Assert.Contains(simGroup.GetSystems(), s => s is LinearKinematicsSystem);

            // MuscleGround must include NavigationIntentBridgeSystem.
            Assert.Contains(simGroup.GetSystems(), s => s is NavigationIntentBridgeSystem);

            // TrajectoryPool and FormationTemplates are available on MuscleGround.
            Assert.NotNull(module.TrajectoryPool);
            Assert.NotNull(module.FormationTemplates);

            inputGroup.Dispose();
            simGroup.Dispose();
            postSimGroup.Dispose();
            DisposeRaycastBatchData(world);
        }

        [Fact]
        public void SimulationLogicModule_ImageGeneratorRole_RegistersNoSystems()
        {
            using var world = CreateEmptyWorld();
            var module = new SimulationLogicModule(
                new DoctrineRegistry(), new NetworkEntityMap(),
                role: NodeRole.ImageGenerator);

            var inputGroup   = new SystemGroup(); inputGroup.Create(world);
            var simGroup     = new SystemGroup(); simGroup.Create(world);
            var postSimGroup = new SystemGroup(); postSimGroup.Create(world);

            module.RegisterSystems(inputGroup, simGroup, postSimGroup);

            Assert.Equal(0, inputGroup.SystemCount);
            Assert.Equal(0, simGroup.SystemCount);
            Assert.Equal(0, postSimGroup.SystemCount);

            Assert.Null(module.TrajectoryPool);
            Assert.Null(module.FormationTemplates);

            inputGroup.Dispose();
            simGroup.Dispose();
            postSimGroup.Dispose();
            DisposeRaycastBatchData(world);
        }
    }
}
