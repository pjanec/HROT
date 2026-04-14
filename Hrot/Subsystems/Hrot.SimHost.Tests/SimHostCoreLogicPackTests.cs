using System;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Kernel;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.CarKinem.Systems;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Systems;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Physics.Systems;
using Fdp.Toolkit.Replication.Services;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="SimHostCoreLogicPack"/> (PACK2-P001).
    /// </summary>
    public class SimHostCoreLogicPackTests
    {
        // ── World factory ─────────────────────────────────────────────────────

        private static EntityRepository CreateEmptyWorld()
        {
            var world = new EntityRepository();

            // Behavior / locomotion
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

            // Perception
            world.RegisterComponent<Faction>();
            world.RegisterComponent<PerceptionReceptor>();
            world.RegisterComponent<TargetMemory>();

            // Combat & Physics
            world.RegisterComponent<PhysicsCollider>();
            world.RegisterComponent<WeaponState>();
            world.RegisterComponent<Health>();
            world.RegisterComponent<BallisticProjectile>();

            // Core simulation (Fdp.Kernel + CarKinem)
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<SimVelocity>();
            world.RegisterComponent<VehicleState>();
            world.RegisterComponent<VehicleParams>();
            world.RegisterComponent<NavState>();
            world.RegisterComponent<FormationRoster>();

            // Navigation CQRS
            world.RegisterComponent<NavigationIntent>();
            world.RegisterComponent<NavigationStatus>();
            world.RegisterComponent<FrustrationTicks>();

            // GlobalTime singleton
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
        /// All four sub-module system sets register without error, and a single-frame
        /// pump does not throw on an empty world.
        /// </summary>
        [Fact]
        public void SimHostCoreLogicPack_EmptyWorld_AllSystemsRegisterAndRunWithoutException()
        {
            // ── Arrange ───────────────────────────────────────────────────────
            using var world = CreateEmptyWorld();
            var entityMap        = new NetworkEntityMap();
            var roadNetwork      = new RoadNetworkBuilder().Build(10f, 10, 10);
            var trajectoryPool   = new TrajectoryPoolManager();

            var pack = new SimHostCoreLogicPack(
                entityMap,
                roadNetwork:    roadNetwork,
                trajectoryPool: trajectoryPool);

            var inputGroup   = new SystemGroup();
            inputGroup.Create(world);
            var simGroup     = new SystemGroup();
            simGroup.Create(world);
            var postSimGroup = new SystemGroup();
            postSimGroup.Create(world);

            pack.RegisterSystems(inputGroup, simGroup, postSimGroup);

            // ── Act + Assert ──────────────────────────────────────────────────
            var ex = Record.Exception(() =>
            {
                inputGroup.Run();
                simGroup.Run();
                postSimGroup.Run();
            });

            Assert.Null(ex);

            // Verify system counts match expected numbers from sub-module implementations:
            // CombatModule: FireProcessingSystem, RaycastSolverSystem, HitResolutionSystem (input=3)
            // PersonalRouteAuthoringSystem (input=1) → inputGroup total = 4
            Assert.Equal(4, inputGroup.SystemCount);

            // CombatModule: PerceptionBroadphaseSystem, ThreatEvaluationAdapterSystem, DamageSystem (sim=3)
            // DamageAssessmentModule: DamageCalculationSystem (sim=1)
            // Navigation bridges: NavigationIntentBridgeSystem, RouteTrajectorySyncSystem (sim=2)
            // GroundKinematicsModule: SpatialHashSystem, FormationTargetSystem, VehicleCommandSystem,
            //   CarKinematicsSystem, NavigationExecutionSystem, LinearKinematicsSystem (sim=6)
            // total sim = 12
            Assert.Equal(12, simGroup.SystemCount);

            // CombatModule: BallisticsSystem (postSim=1)
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
        /// Verifies that systems belonging to each of the four sub-modules are present
        /// in the appropriate groups.
        /// </summary>
        [Fact]
        public void SimHostCoreLogicPack_ContainsSystemsFromAllFourSubModules()
        {
            using var world      = CreateEmptyWorld();
            var entityMap        = new NetworkEntityMap();
            var pack             = new SimHostCoreLogicPack(entityMap);

            var inputGroup   = new SystemGroup();
            inputGroup.Create(world);
            var simGroup     = new SystemGroup();
            simGroup.Create(world);
            var postSimGroup = new SystemGroup();
            postSimGroup.Create(world);

            pack.RegisterSystems(inputGroup, simGroup, postSimGroup);

            // CombatModule systems
            var inputSystems   = inputGroup.GetSystems();
            var simSystems     = simGroup.GetSystems();
            var postSimSystems = postSimGroup.GetSystems();

            Assert.Contains(inputSystems,   s => s is FireProcessingSystem);
            Assert.Contains(inputSystems,   s => s is RaycastSolverSystem);
            Assert.Contains(inputSystems,   s => s is HitResolutionSystem);
            Assert.Contains(postSimSystems, s => s is BallisticsSystem);

            // DamageAssessmentModule systems
            Assert.Contains(simSystems, s => s is DamageCalculationSystem);

            // GroundKinematicsModule systems
            Assert.Contains(simSystems, s => s is SpatialHashSystem);
            Assert.Contains(simSystems, s => s is CarKinematicsSystem);
            Assert.Contains(simSystems, s => s is LinearKinematicsSystem);

            // AutonomousPerceptionModule: does not add systems to groups — runs via Tick().
            // Verify the module's Name property is correct (indirectly via RegisterSystems no-op).
            Assert.Equal("SimHostCoreLogicPack", pack.Name);

            inputGroup.Dispose();
            simGroup.Dispose();
            postSimGroup.Dispose();
            DisposeRaycastBatchData(world);
        }

        /// <summary>
        /// Verify that <see cref="SimHostCoreLogicPack"/> exposes accessible shared resources
        /// (TrajectoryPool and FormationTemplates) for composition-root wiring.
        /// </summary>
        [Fact]
        public void SimHostCoreLogicPack_ExposesSharedResources()
        {
            var entityMap = new NetworkEntityMap();
            var pack      = new SimHostCoreLogicPack(entityMap);

            Assert.NotNull(pack.TrajectoryPool);
            Assert.NotNull(pack.FormationTemplates);
        }
    }
}
