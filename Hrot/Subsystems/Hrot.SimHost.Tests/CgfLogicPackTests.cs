using System;
using CarKinem.Formation;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Modules;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.CarKinem.Systems;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb;
using Hrot.CGF;
using Hrot.CGF.Systems;
using Hrot.Common.Systems;
using Hrot.Core.Network;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="CgfLogicPack"/> (PACK2-P001) and TASK-C003 wiring.
    /// </summary>
    public class CgfLogicPackTests
    {
        private static EntityRepository CreateEmptyWorld()
        {
            var world = new EntityRepository();

            // Behavior toolkit components
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.LocomotionChannel>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.WeaponChannel>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.InteractionChannel>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.ActorCapabilityState>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.BrainBTreeState>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.BrainBlackboard>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.BrainHsm64>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.BrainHsm128>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.PreviousCapabilities>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>();
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();

            world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });

            return world;
        }

        // -- Helpers for C003 CreateEntityRequestSystem tests --

        private const long  C003ValidTkbType = 42L;
        private const ulong C003ValidDisType = 0x0100_0000_0000_0001UL;
        private const int   C003LocalNodeId  = 9;

        private static TkbDatabase CreateTkb()
        {
            var db = new TkbDatabase();
            db.Register(new TkbTemplate("TestVehicle", C003ValidTkbType));
            return db;
        }

        private static EntityRepository CreateWorldForRequestSystem()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NetworkIdentity>();
            repo.RegisterComponent<NetworkOwnership>();
            repo.RegisterComponent<TkbIdentity>();
            repo.RegisterComponent<GhostStateTracker>();
            repo.RegisterEvent<Fdp.Toolkit.Lifecycle.Events.ConstructionOrder>();
            repo.RegisterEvent<Fdp.Toolkit.Lifecycle.Events.DestructionOrder>();
            return repo;
        }

        private static EntityCreationRequest MakeValidRequest() =>
            new EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = C003LocalNodeId,
                TkbType            = C003ValidTkbType,
                DisType            = C003ValidDisType,
            };

        // -- Tests (PACK2-P001 existing, updated for new scenarioSource param) --

        /// <summary>
        /// All three sub-module system sets register without error and run on an
        /// empty world without throwing.
        /// </summary>
        [Fact]
        public void CgfLogicPack_EmptyWorld_AllSystemsRegisterAndRunWithoutException()
        {
            using var world   = CreateEmptyWorld();
            var behaviorRegistry = new BehaviorRegistry();
            var entityMap        = new NetworkEntityMap();
            var scenarioSource   = new ScenarioEntityCreationRequestSource();

            var pack    = new CgfLogicPack(behaviorRegistry, entityMap, scenarioSource,
                new TacticalIntentMapperRegistry());
            var view = (ISimulationView)world;
            var ex = Record.Exception(() =>
            {
                foreach (var s in pack.InputSystems)      s.Execute(view, 0.016f);
                foreach (var s in pack.SimulationSystems) s.Execute(view, 0.016f);
            });
            Assert.Null(ex);

            // InputSystems: MissionControlExecutionSystem (1), BehaviorIngressSystem (1),
            //               DebugStatePatchSystem (1, behav-diag-1) = 3
            // SimulationSystems: 16 + TacticalIntentResolutionSystem + UnitHierarchySystem
            //                    + TraceBufferLifecycleSystem (behav-diag-1) = 18
            Assert.Equal(3,  pack.InputSystems.Count);
            Assert.Equal(18, pack.SimulationSystems.Count);
        }

        /// <summary>
        /// Verifies that systems belonging to each of the three sub-modules are
        /// in the simulation group.
        /// </summary>
        [Fact]
        public void CgfLogicPack_ContainsSystemsFromAllThreeSubModules()
        {
            using var world      = CreateEmptyWorld();
            var behaviorRegistry = new BehaviorRegistry();
            var entityMap        = new NetworkEntityMap();
            var scenarioSource   = new ScenarioEntityCreationRequestSource();

            var pack     = new CgfLogicPack(behaviorRegistry, entityMap, scenarioSource,
                new TacticalIntentMapperRegistry());
            // MissionControlModule systems in InputSystems + SimulationSystems
            Assert.Contains(pack.InputSystems,      s => s is BehaviorIngressSystem);
            Assert.Contains(pack.SimulationSystems, s => s is MissionDirectorSystem);

            // CognitiveRuntimeModule systems
            Assert.Contains(pack.SimulationSystems, s => s is ChannelArbitrationSystem);
            Assert.Contains(pack.SimulationSystems, s => s is BTreeTickSystem);

            // ActionDispatchModule systems
            Assert.Contains(pack.SimulationSystems, s => s is LocomotionDispatcherSystem);
            Assert.Contains(pack.SimulationSystems, s => s is WeaponDispatcherSystem);
        }

        /// <summary>
        /// Verifies the module Name property.
        /// </summary>
        [Fact]
        public void CgfLogicPack_Name_IsCgfLogicPack()
        {
            var pack = new CgfLogicPack(
                new BehaviorRegistry(),
                new NetworkEntityMap(),
                new ScenarioEntityCreationRequestSource(),
                new TacticalIntentMapperRegistry());
            Assert.Equal("CgfLogicPack", pack.Name);
        }

        // -- Tests (TASK-C003) --

        /// <summary>
        /// C003 success condition 4: CgfLogicPack rejects null scenarioSource.
        /// </summary>
        [Fact]
        public void CgfLogicPack_NullScenarioSource_ThrowsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() =>
                new CgfLogicPack(
                    new BehaviorRegistry(),
                    new NetworkEntityMap(),
                    scenarioSource: null!,
                    mapperRegistry: new TacticalIntentMapperRegistry()));

            Assert.Equal("scenarioSource", ex.ParamName);
        }

        /// <summary>
        /// C003 success condition 1: requests from the NED stub source reach
        /// SpawnEntityCommand when CreateEntityRequestSystem uses the composite.
        /// </summary>
        [Fact]
        public void C003_NedRequestsProcessed_ViaCompositeSource()
        {
            var repo    = CreateWorldForRequestSystem();
            var tkb     = CreateTkb();
            var nedStub = new StubRequestSource();
            var scenarioSource = new ScenarioEntityCreationRequestSource();

            var composite = new CompositeEntityCreationRequestSource(
                new IEntityCreationRequestSource[] { nedStub, scenarioSource });

            var system = new CreateEntityRequestSystem(
                composite, new StubAckSink(), tkb,
                new StubIdAllocator(startId: 100), C003LocalNodeId);

            nedStub.Enqueue(MakeValidRequest());

            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            Assert.NotEmpty(commands);
        }

        /// <summary>
        /// C003 success condition 2: requests from the scenario source reach
        /// SpawnEntityCommand when CreateEntityRequestSystem uses the composite.
        /// </summary>
        [Fact]
        public void C003_ScenarioRequestsProcessed_ViaCompositeSource()
        {
            var repo    = CreateWorldForRequestSystem();
            var tkb     = CreateTkb();
            var nedStub = new StubRequestSource();
            var scenarioSource = new ScenarioEntityCreationRequestSource();

            var composite = new CompositeEntityCreationRequestSource(
                new IEntityCreationRequestSource[] { nedStub, scenarioSource });

            var system = new CreateEntityRequestSystem(
                composite, new StubAckSink(), tkb,
                new StubIdAllocator(startId: 200), C003LocalNodeId);

            scenarioSource.Enqueue(MakeValidRequest());

            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            Assert.NotEmpty(commands);
        }

        /// <summary>
        /// C003 success condition 3: requests from BOTH sources reach SpawnEntityCommand
        /// in the same tick when CreateEntityRequestSystem uses the composite.
        /// </summary>
        [Fact]
        public void C003_BothSourcesProcessed_SameTick()
        {
            var repo    = CreateWorldForRequestSystem();
            var tkb     = CreateTkb();
            var nedStub = new StubRequestSource();
            var scenarioSource = new ScenarioEntityCreationRequestSource();

            var composite = new CompositeEntityCreationRequestSource(
                new IEntityCreationRequestSource[] { nedStub, scenarioSource });

            var system = new CreateEntityRequestSystem(
                composite, new StubAckSink(), tkb,
                new StubIdAllocator(startId: 300), C003LocalNodeId);

            nedStub.Enqueue(MakeValidRequest());
            scenarioSource.Enqueue(MakeValidRequest());

            system.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            var commands = ((ISimulationView)repo).ReadManagedEvents<SpawnEntityCommand>();
            Assert.Equal(2, commands.Count);
        }

        // â”€â”€ S306: Two-group overload routes systems correctly â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// S306-SC1/SC2/SC3: The two-group overload places <see cref="MissionControlExecutionSystem"/>
        /// and <see cref="BehaviorIngressSystem"/> in the Input group, and all remaining
        /// systems in the Simulation group.
        /// </summary>
        [Fact]
        public void CgfLogicPack_TwoGroupOverload_RoutesSystemsCorrectly()
        {
            using var world      = CreateEmptyWorld();
            var behaviorRegistry = new BehaviorRegistry();
            var entityMap        = new NetworkEntityMap();
            var scenarioSource   = new ScenarioEntityCreationRequestSource();

            var pack       = new CgfLogicPack(behaviorRegistry, entityMap, scenarioSource,
                new TacticalIntentMapperRegistry());
            // SC1: MissionControlExecutionSystem is in InputSystems.
            Assert.Contains(pack.InputSystems, s => s is MissionControlExecutionSystem);
            // SC2: BehaviorIngressSystem is in InputSystems.
            Assert.Contains(pack.InputSystems, s => s is BehaviorIngressSystem);
            // SC3: MissionDirectorSystem is in SimulationSystems.
            Assert.Contains(pack.SimulationSystems, s => s is MissionDirectorSystem);
            // MissionAdapterSystem stays in SimulationSystems.
            Assert.Contains(pack.SimulationSystems, s => s is MissionAdapterSystem);

            // InputSystems: MissionControlExecutionSystem + BehaviorIngressSystem
            //               + DebugStatePatchSystem (behav-diag-1) = 3
            Assert.Equal(3,  pack.InputSystems.Count);
            // SimulationSystems: total 21 - 3 = 18 (incl. TraceBufferLifecycleSystem behav-diag-1)
            Assert.Equal(18, pack.SimulationSystems.Count);
        }

        /// <summary>
        /// S306-SC4: The existing single-group overload still adds all 15 systems to the same
        /// group (no regression).
        /// </summary>
        [Fact]
        public void CgfLogicPack_SingleGroupOverload_StillAddsAllSystemsToOneGroup()
        {
            using var world      = CreateEmptyWorld();
            var behaviorRegistry = new BehaviorRegistry();
            var entityMap        = new NetworkEntityMap();
            var scenarioSource   = new ScenarioEntityCreationRequestSource();

            var pack     = new CgfLogicPack(behaviorRegistry, entityMap, scenarioSource,
                new TacticalIntentMapperRegistry());
            // Total systems across both phases equals 21
            //   (split: 3 input + 18 sim — adds DebugStatePatchSystem + TraceBufferLifecycleSystem from behav-diag-1).
            Assert.Equal(21, pack.InputSystems.Count + pack.SimulationSystems.Count);
        }
    }
}