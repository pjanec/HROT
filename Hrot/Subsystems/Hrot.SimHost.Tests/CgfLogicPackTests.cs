using System;
using CarKinem.Formation;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost;
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
            // HealthApplicationSystem (1), CgfThreatEvaluationSystem (1), RouteContextSystem (1)
            // total = 15
            Assert.Equal(15, simGroup.SystemCount);

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
            Assert.Contains(systems, s => s.IsOrWraps<DoctrineIngressSystem>());
            Assert.Contains(systems, s => s.IsOrWraps<MissionDirectorSystem>());

            // CognitiveRuntimeModule systems
            Assert.Contains(systems, s => s.IsOrWraps<ChannelArbitrationSystem>());
            Assert.Contains(systems, s => s.IsOrWraps<BTreeTickSystem>());

            // ActionDispatchModule systems
            Assert.Contains(systems, s => s.IsOrWraps<LocomotionDispatcherSystem>());
            Assert.Contains(systems, s => s.IsOrWraps<WeaponDispatcherSystem>());

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

        // â”€â”€ S306: Two-group overload routes systems correctly â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// S306-SC1/SC2/SC3: The two-group overload places <see cref="MissionControlExecutionSystem"/>
        /// and <see cref="DoctrineIngressSystem"/> in the Input group, and all remaining
        /// systems in the Simulation group.
        /// </summary>
        [Fact]
        public void CgfLogicPack_TwoGroupOverload_RoutesSystemsCorrectly()
        {
            using var world      = CreateEmptyWorld();
            var doctrineRegistry = new DoctrineRegistry();
            var entityMap        = new NetworkEntityMap();
            var scenarioSource   = new ScenarioEntityCreationRequestSource();

            var pack       = new CgfLogicPack(doctrineRegistry, entityMap, scenarioSource);
            var inputGroup = new SystemGroup();
            var simGroup2  = new SystemGroup();
            inputGroup.Create(world);
            simGroup2.Create(world);

            pack.RegisterSystems(inputGroup, simGroup2);

            var inputSystems = inputGroup.GetSystems();
            var simSystems   = simGroup2.GetSystems();

            // SC1: MissionControlExecutionSystem is in inputGroup.
            Assert.Contains(inputSystems, s => s.IsOrWraps<MissionControlExecutionSystem>());
            // SC2: DoctrineIngressSystem is in inputGroup.
            Assert.Contains(inputSystems, s => s.IsOrWraps<DoctrineIngressSystem>());
            // SC3: MissionDirectorSystem is in simGroup.
            Assert.Contains(simSystems, s => s.IsOrWraps<MissionDirectorSystem>());
            // MissionAdapterSystem stays in simGroup.
            Assert.Contains(simSystems, s => s.IsOrWraps<MissionAdapterSystem>());

            // inputGroup: MissionControlExecutionSystem + DoctrineIngressSystem = 2
            Assert.Equal(2, inputGroup.SystemCount);
            // simGroup: total 15 - 2 = 13
            Assert.Equal(13, simGroup2.SystemCount);

            inputGroup.Dispose();
            simGroup2.Dispose();
        }

        /// <summary>
        /// S306-SC4: The existing single-group overload still adds all 15 systems to the same
        /// group (no regression).
        /// </summary>
        [Fact]
        public void CgfLogicPack_SingleGroupOverload_StillAddsAllSystemsToOneGroup()
        {
            using var world      = CreateEmptyWorld();
            var doctrineRegistry = new DoctrineRegistry();
            var entityMap        = new NetworkEntityMap();
            var scenarioSource   = new ScenarioEntityCreationRequestSource();

            var pack     = new CgfLogicPack(doctrineRegistry, entityMap, scenarioSource);
            var simGroup = new SystemGroup();
            simGroup.Create(world);

            pack.RegisterSystems(simGroup);

            Assert.Equal(15, simGroup.SystemCount);

            simGroup.Dispose();
        }

        /// <summary>
        /// S306-SC5: Passing null to either parameter of the two-group overload throws
        /// <see cref="ArgumentNullException"/>.
        /// </summary>
        [Fact]
        public void CgfLogicPack_TwoGroupOverload_NullInputGroup_Throws()
        {
            using var world = CreateEmptyWorld();
            var pack = new CgfLogicPack(new DoctrineRegistry(), new NetworkEntityMap(),
                new ScenarioEntityCreationRequestSource());
            var simGroup = new SystemGroup();
            simGroup.Create(world);

            var ex = Assert.Throws<ArgumentNullException>(() =>
                pack.RegisterSystems(null!, simGroup));
            Assert.Equal("inputGroup", ex.ParamName);

            simGroup.Dispose();
        }

        /// <summary>
        /// S306-SC5: Passing null simGroup to the two-group overload throws
        /// <see cref="ArgumentNullException"/>.
        /// </summary>
        [Fact]
        public void CgfLogicPack_TwoGroupOverload_NullSimGroup_Throws()
        {
            using var world = CreateEmptyWorld();
            var pack = new CgfLogicPack(new DoctrineRegistry(), new NetworkEntityMap(),
                new ScenarioEntityCreationRequestSource());
            var inputGroup = new SystemGroup();
            inputGroup.Create(world);

            var ex = Assert.Throws<ArgumentNullException>(() =>
                pack.RegisterSystems(inputGroup, null!));
            Assert.Equal("simGroup", ex.ParamName);

            inputGroup.Dispose();
        }
    }
}
