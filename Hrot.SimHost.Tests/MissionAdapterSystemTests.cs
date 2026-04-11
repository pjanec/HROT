using Hrot.SimHost.Modules;
using Hrot.SimHost.Network;
using Fdp.Kernel;
using FDP.Toolkit.Behavior;
using FDP.Toolkit.Behavior.Systems;
using FDP.Toolkit.Replication.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Verifies SimulationLogicModule role-conditional mission system registration.
    /// </summary>
    public class MissionDirectorSystemRegistrationTests
    {
        private static EntityRepository CreateWorld()
        {
            var world = new EntityRepository();
            world.SetSingletonUnmanaged(new GlobalTime { DeltaTime = 0.016f, TimeScale = 1.0f });
            return world;
        }

        [Fact]
        public void RegisterSystems_IncludesMissionDirectorSystem()
        {
            using var world = CreateWorld();
            var inputGroup = new SystemGroup();
            inputGroup.Create(world);
            var simGroup = new SystemGroup();
            simGroup.Create(world);
            var postSimGroup = new SystemGroup();
            postSimGroup.Create(world);

            var module = new SimulationLogicModule(
                new DoctrineRegistry(),
                new NetworkEntityMap(),
                role: NodeRole.Brain);
            module.RegisterSystems(inputGroup, simGroup, postSimGroup);

            var systems = simGroup.GetSystems();
            Assert.Contains(systems, system => system is MissionDirectorSystem);
        }

        [Fact]
        public void RegisterSystems_BrainRole_IncludesMissionAdapterSystem()
        {
            using var world = CreateWorld();
            var inputGroup = new SystemGroup();
            inputGroup.Create(world);
            var simGroup = new SystemGroup();
            simGroup.Create(world);
            var postSimGroup = new SystemGroup();
            postSimGroup.Create(world);

            var module = new SimulationLogicModule(
                new DoctrineRegistry(),
                new NetworkEntityMap(),
                role: NodeRole.Brain);
            module.RegisterSystems(inputGroup, simGroup, postSimGroup);

            var systems = simGroup.GetSystems();
            Assert.Contains(systems, system => system.GetType().Name == "MissionAdapterSystem");
        }

        [Fact]
        public void RegisterSystems_MuscleGroundRole_DoesNotIncludeMissionAdapterSystem()
        {
            using var world = CreateWorld();
            var inputGroup = new SystemGroup();
            inputGroup.Create(world);
            var simGroup = new SystemGroup();
            simGroup.Create(world);
            var postSimGroup = new SystemGroup();
            postSimGroup.Create(world);

            var module = new SimulationLogicModule(
                new DoctrineRegistry(),
                new NetworkEntityMap(),
                role: NodeRole.MuscleGround);
            module.RegisterSystems(inputGroup, simGroup, postSimGroup);

            var systems = simGroup.GetSystems();
            Assert.DoesNotContain(systems, system => system.GetType().Name == "MissionAdapterSystem");
        }
    }
}
