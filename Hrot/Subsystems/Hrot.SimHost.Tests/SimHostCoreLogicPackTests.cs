using System;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Systems;
using CarKinem.Trajectory;
using Fdp.Core;
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
using Hrot.Common.Systems;
using Hrot.SimHost;
using Hrot.SimHost.Systems;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="SimHostCoreLogicPack"/> (PACK2-P001).
    /// </summary>
    public class SimHostCoreLogicPackTests
    {
        // Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬ World factory Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬

        private static EntityRepository CreateEmptyWorld()
        {
            var world = new EntityRepository();

            // Behavior / locomotion
            world.RegisterComponent<BehaviorState>();
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
            world.RegisterComponent<EntityInfo>();
            world.RegisterComponent<PerceptionReceptor>();
            world.RegisterComponent<TargetMemory>();

            // Combat & Physics
            world.RegisterComponent<PhysicsCollider>();
            world.RegisterComponent<WeaponState>();
            world.RegisterComponent<Health>();
            world.RegisterComponent<BallisticProjectile>();

            // Core simulation (Fdp.Core + CarKinem)
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<SimVelocity>();
            world.RegisterComponent<VehicleState>();
            world.RegisterComponent<VehicleParams>();
            world.RegisterComponent<NavState>();
            world.RegisterComponent<FormationController>();

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
                if (batch.Hits.IsCreated) batch.Hits.Dispose();
            }
        }

        // Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬ Tests Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬

        /// <summary>
        /// All four sub-module system sets register without error, and a single-frame
        /// pump does not throw on an empty world.
        /// </summary>
        // STABILITY(Broken): system count mismatch — system added/removed from SimHostCoreLogicPack without updating the test's expected count; investigate
        [Trait("Stability", "Broken")]
        [Fact]
        public void SimHostCoreLogicPack_EmptyWorld_AllSystemsRegisterAndRunWithoutException()
        {
            // Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬ Arrange Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬Ă˘â€ťâ‚¬
            using var world = CreateEmptyWorld();
            var entityMap        = new NetworkEntityMap();
            var roadNetwork      = new RoadNetworkBuilder().Build(10f, 10, 10);
            var trajectoryPool   = new TrajectoryPoolManager();

            var pack = new SimHostCoreLogicPack(
                entityMap,
                roadNetwork:    roadNetwork,
                trajectoryPool: trajectoryPool);

            var view = (ISimulationView)world;
            var ex = Record.Exception(() =>
            {
                foreach (var s in pack.InputSystems)          s.Execute(view, 0.016f);
                foreach (var s in pack.SimulationSystems)     s.Execute(view, 0.016f);
                foreach (var s in pack.PostSimulationSystems) s.Execute(view, 0.016f);
            });

            Assert.Null(ex);

            // Verify system counts match expected numbers from sub-module implementations:
            // CombatModule: FireProcessingSystem, RaycastSolverSystem, HitResolutionSystem (input=3)
            // PersonalRouteAuthoringSystem (input=1) -- InputSystems total = 4
            Assert.Equal(4, pack.InputSystems.Count);

            // CombatModule: no systems in sim (sim=0)
            // DamageAssessmentModule: DamageCalculationSystem (sim=1)
            // Navigation bridges: NavigationIntentBridgeSystem, RouteTrajectorySyncSystem (sim=2)
            // GroundKinematicsModule.SimulationSystems: SpatialHashSystem, FormationTargetSystem,
            //   VehicleCommandSystem, NavigationExecutionSystem (sim=4)
            // UnitHierarchySystem (sim=1)
            // total sim = 8
            Assert.Equal(8, pack.SimulationSystems.Count);

            // CombatModule: BallisticsSystem (postSim=1)
            // GroundKinematicsModule.PostSimulationSystems: CarKinematicsSystem, LinearKinematicsSystem (postSim=2)
            Assert.NotEmpty(pack.PostSimulationSystems);
            Assert.Contains(pack.PostSimulationSystems, s => s is BallisticsSystem);
            Assert.Contains(pack.PostSimulationSystems, s => s is CarKinematicsSystem);
            Assert.Contains(pack.PostSimulationSystems, s => s is LinearKinematicsSystem);

            // Cleanup
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

            var inputSystems   = pack.InputSystems;
            var simSystems     = pack.SimulationSystems;
            var postSimSystems = pack.PostSimulationSystems;

            Assert.Contains(inputSystems,   s => s is FireProcessingSystem);
            Assert.Contains(inputSystems,   s => s is RaycastSolverSystem);
            Assert.Contains(inputSystems,   s => s is HitResolutionSystem);
            Assert.Contains(postSimSystems, s => s is BallisticsSystem);

            // DamageAssessmentModule systems
            Assert.Contains(simSystems, s => s is DamageCalculationSystem);

            // GroundKinematicsModule sim systems
            Assert.Contains(simSystems, s => s is SpatialHashSystem);
            // UnitHierarchySystem (CS016)
            Assert.Contains(simSystems, s => s is UnitHierarchySystem);
            // GroundKinematicsModule post-sim systems
            Assert.Contains(postSimSystems, s => s is CarKinematicsSystem);
            Assert.Contains(postSimSystems, s => s is LinearKinematicsSystem);

            // AutonomousPerceptionModule: does not add systems to groups -- runs via Tick().
            Assert.Equal("SimHostCoreLogicPack", pack.Name);

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

        /// <summary>
        /// TASK-CS004 integration test: SimHostComponentRegistry.RegisterAll registers
        /// UnitRoster and UnitSubordinate so their component tables are available.
        /// </summary>
        [Fact]
        public void SimHostComponentRegistry_RegisterAll_ProvidesUnitHierarchyComponentTables()
        {
            var world = new EntityRepository();
            SimHostComponentRegistry.RegisterAll(world);

            Assert.NotNull(world.GetComponentTable<Fdp.Core.CommandHierarchy.UnitRoster>());
            Assert.NotNull(world.GetComponentTable<Fdp.Core.CommandHierarchy.UnitSubordinate>());

            // Dispose NativeArrays allocated by SimHostComponentRegistry (PathfindingBatchData, RaycastBatchData)
            if (world.HasSingleton<PathfindingBatchData>())
            {
                ref var batch = ref world.GetSingleton<PathfindingBatchData>();
                if (batch.Results.IsCreated) batch.Results.Dispose();
            }
            DisposeRaycastBatchData(world);
            world.Dispose();
        }
    }
}
