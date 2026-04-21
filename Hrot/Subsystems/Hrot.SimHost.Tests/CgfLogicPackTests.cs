using System;
using CarKinem.Formation;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Modules;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.CarKinem.Systems;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb;
using Hrot.CGF;
using Hrot.CGF.Systems;
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
            world.RegisterComponent<Fdp.Toolkit.Behavior.Components.DoctrineState>();
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
            var doctrineRegistry = new DoctrineRegistry();
            var entityMap        = new NetworkEntityMap();
            var scenarioSource   = new ScenarioEntityCreationRequestSource();

            var pack    = new CgfLogicPack(doctrineRegistry, entityMap, scenarioSource);
            var simGroup = new SystemGroup();
            simGroup.Create(world);

            pack.RegisterSystems(simGroup);

            var ex = Record.Exception(() => simGroup.Run());
            Assert.Null(ex);

            // MissionControlExecutionSystem (1), MissionAdapterSystem (1)
            // MissionControlModule: DoctrineIngressSystem, MissionDirectorSystem (2)
            // CognitiveRuntimeModule: ChannelArbitrationSystem, HsmDamageBridgeSystem,
            //   BTreeTickSystem, HsmTickSystem<BrainHsm128>, HsmTickSystem<BrainHsm64> (5)
            // ActionDispatchModule: LocomotionDispatcherSystem, WeaponDispatcherSystem,
            //   InteractionDispatcherSystem (3)
            // HealthApplicationSystem (1), RouteContextSystem (1)
            // total = 14
            Assert.Equal(14, simGroup.SystemCount);

            simGroup.Dispose();
        }

        /// <summary>
        /// Verifies that systems belonging to each of the three sub-modules are
        /// in the simulation group.
        /// </summary>
        [Fact]
        public void CgfLogicPack_ContainsSystemsFromAllThreeSubModules()
        {
            using var world      = CreateEmptyWorld();
            var doctrineRegistry = new DoctrineRegistry();
            var entityMap        = new NetworkEntityMap();
            var scenarioSource   = new ScenarioEntityCreationRequestSource();

            var pack     = new CgfLogicPack(doctrineRegistry, entityMap, scenarioSource);
            var simGroup = new SystemGroup();
            simGroup.Create(world);

            pack.RegisterSystems(simGroup);

            var systems = simGroup.GetSystems();

            // MissionControlModule systems
            Assert.Contains(systems, s => s is DoctrineIngressSystem);
            Assert.Contains(systems, s => s is MissionDirectorSystem);

            // CognitiveRuntimeModule systems
            Assert.Contains(systems, s => s is ChannelArbitrationSystem);
            Assert.Contains(systems, s => s is BTreeTickSystem);

            // ActionDispatchModule systems
            Assert.Contains(systems, s => s is LocomotionDispatcherSystem);
            Assert.Contains(systems, s => s is WeaponDispatcherSystem);

            simGroup.Dispose();
        }

        /// <summary>
        /// Verifies the module Name property.
        /// </summary>
        [Fact]
        public void CgfLogicPack_Name_IsCgfLogicPack()
        {
            var pack = new CgfLogicPack(
                new DoctrineRegistry(),
                new NetworkEntityMap(),
                new ScenarioEntityCreationRequestSource());
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
                    new DoctrineRegistry(),
                    new NetworkEntityMap(),
                    scenarioSource: null!));

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
    }
}
