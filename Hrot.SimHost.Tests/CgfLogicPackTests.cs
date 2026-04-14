using System;
using CarKinem.Formation;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Modules;
using FDP.Toolkit.Behavior.Systems;
using FDP.Toolkit.CarKinem.Systems;
using FDP.Toolkit.Replication.Services;
using Hrot.CGF;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Unit tests for <see cref="CgfLogicPack"/> (PACK2-P001).
    /// </summary>
    public class CgfLogicPackTests
    {
        private static EntityRepository CreateEmptyWorld()
        {
            var world = new EntityRepository();

            // Behavior toolkit components
            world.RegisterComponent<FDP.Toolkit.Behavior.Components.DoctrineState>();
            world.RegisterComponent<FDP.Toolkit.Behavior.Components.LocomotionChannel>();
            world.RegisterComponent<FDP.Toolkit.Behavior.Components.WeaponChannel>();
            world.RegisterComponent<FDP.Toolkit.Behavior.Components.InteractionChannel>();
            world.RegisterComponent<FDP.Toolkit.Behavior.Components.ActorCapabilityState>();
            world.RegisterComponent<FDP.Toolkit.Behavior.Components.BrainBTreeState>();
            world.RegisterComponent<FDP.Toolkit.Behavior.Components.BrainBlackboard>();
            world.RegisterComponent<FDP.Toolkit.Behavior.Components.BrainHsm64>();
            world.RegisterComponent<FDP.Toolkit.Behavior.Components.BrainHsm128>();
            world.RegisterComponent<FDP.Toolkit.Behavior.Components.PreviousCapabilities>();
            world.RegisterComponent<FDP.Toolkit.Behavior.Components.PassengerBuffer>();
            world.RegisterComponent<FDP.Toolkit.Behavior.Components.IsEmbarkedTag>();

            world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });

            return world;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

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

            var pack    = new CgfLogicPack(doctrineRegistry, entityMap);
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
            // RouteContextSystem (1)
            // total = 13
            Assert.Equal(13, simGroup.SystemCount);

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

            var pack     = new CgfLogicPack(doctrineRegistry, entityMap);
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
            var pack = new CgfLogicPack(new DoctrineRegistry(), new NetworkEntityMap());
            Assert.Equal("CgfLogicPack", pack.Name);
        }
    }
}
