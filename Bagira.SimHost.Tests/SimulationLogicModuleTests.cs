using Bagira.SimHost.Modules;
using CarKinem.Core;
using CarKinem.Formation;
using CarKinem.Road;
using CarKinem.Spatial;
using CarKinem.Trajectory;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Physics.Systems;
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

            // ── Core simulation components (Fdp.Kernel + CarKinem) ────────────
            world.RegisterComponent<SimTransform>();
            world.RegisterComponent<SimVelocity>();
            world.RegisterComponent<VehicleState>();
            world.RegisterComponent<VehicleParams>();
            world.RegisterComponent<NavState>();
            world.RegisterComponent<FormationRoster>();

            // GlobalTime singleton — ComponentSystem.DeltaTime reads this.
            world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });

            return world;
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

            // The SystemGroup is the "kernel" referenced in the test requirement.
            var group = new SystemGroup();
            group.Create(world);
            module.RegisterSystems(group);

            // ── Act + Assert ──────────────────────────────────────────────────
            // Calling Run() triggers SortSystems() which validates the dependency
            // graph (throws InvalidOperationException on cyclic dependencies),
            // then executes OnUpdate() for each system on the empty world.
            var exception = Record.Exception(() => group.Run());

            Assert.Null(exception);

            // Assert system count: 9 systems registered
            // (MissionAdapterSystem, ChannelArbitration, BTreeTick,
            //  LocomotionDispatcher, SpatialHash, FormationTarget,
            //  VehicleCommand, CarKinematics, LinearKinematics)
            Assert.Equal(9, group.SystemCount);

            // ── Cleanup ───────────────────────────────────────────────────────
            group.Dispose();
            roadNetwork.Dispose();
            trajectoryPool.Dispose();
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
            var group  = new SystemGroup();
            group.Create(world);
            module.RegisterSystems(group);

            var systems = group.GetSystems();
            Assert.Contains(systems, s => s is LinearKinematicsSystem);

            group.Dispose();
        }
    }
}
