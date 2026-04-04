using Hrot.SimHost.Systems;
using Fdp.Kernel;
using FDP.Toolkit.Behavior.Components;
using FDP.Toolkit.Behavior.Executors;
using FDP.Toolkit.Behavior.Modules;
using FDP.Toolkit.Behavior.Systems;
using FDP.Toolkit.Combat;
using FDP.Toolkit.Combat.Executors;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Navigation.Executors;
using FDP.Toolkit.Replication.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// Verifies that <see cref="ActionDispatchModule"/> (now in FDP.Toolkit.Behavior.Modules
    /// — CT-MOD1-E) registers the expected locomotion and weapon dispatcher systems.
    /// </summary>
    public class ActionDispatchModuleTests
    {
        private static ActionDispatchModule CreateDefaultModule(NetworkEntityMap? entityMap = null)
        {
            entityMap ??= new NetworkEntityMap();
            return new ActionDispatchModule(
                locoExecutors: new (ushort, IActionExecutor<LocomotionChannel>)[]
                {
                    (NavigationConstants.ActionIdMoveTo,        new MoveToExecutor()),
                    (NavigationConstants.ActionIdFollowRoute,   new FollowRouteExecutor()),
                    (NavigationConstants.ActionIdJoinFormation, new JoinFormationExecutor(vehicleAPI: null, entityMap)),
                },
                weaponExecutors: new (ushort, IActionExecutor<WeaponChannel>)[]
                {
                    (CombatConstants.ActionIdAimAndFire, new AimAndFireExecutor()),
                });
        }

        [Fact]
        public void ActionDispatchModule_RegistersLocoAndWeaponDispatchers()
        {
            // Arrange
            var repo   = new EntityRepository();
            var module = CreateDefaultModule();

            var group = new SystemGroup();
            group.Create(repo);

            // Act
            module.RegisterSystems(group);

            // Assert — exactly 2 systems: LocomotionDispatcher + WeaponDispatcher
            var systems = group.GetSystems();
            Assert.Equal(2, systems.Count);
            Assert.Contains(systems, s => s is LocomotionDispatcherSystem);
            Assert.Contains(systems, s => s is WeaponDispatcherSystem);

            group.Dispose();
            repo.Dispose();
        }

        [Fact]
        public void ActionDispatchModule_EmptyExecutorLists_StillRegistersDispatchers()
        {
            // Modules with no executors (e.g. headless test stubs) must still compile
            // and register both dispatcher systems.
            var repo   = new EntityRepository();
            var module = new ActionDispatchModule(
                locoExecutors:   System.Array.Empty<(ushort, IActionExecutor<LocomotionChannel>)>(),
                weaponExecutors: System.Array.Empty<(ushort, IActionExecutor<WeaponChannel>)>());

            var group = new SystemGroup();
            group.Create(repo);
            module.RegisterSystems(group);

            Assert.Equal(2, group.GetSystems().Count);

            group.Dispose();
            repo.Dispose();
        }
    }
}
