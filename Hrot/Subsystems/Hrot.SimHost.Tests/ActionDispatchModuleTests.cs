using Hrot.SimHost.Systems;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Behavior.Modules;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Executors;
using Fdp.Toolkit.Replication.Services;
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
            var module = CreateDefaultModule();

            // Assert — exactly 3 systems: LocomotionDispatcher + WeaponDispatcher + InteractionDispatcher
            Assert.Equal(3, module.SimulationSystems.Count);
            Assert.Contains(module.SimulationSystems, s => s is LocomotionDispatcherSystem);
            Assert.Contains(module.SimulationSystems, s => s is WeaponDispatcherSystem);
            Assert.Contains(module.SimulationSystems, s => s is InteractionDispatcherSystem);
        }

        [Fact]
        public void ActionDispatchModule_EmptyExecutorLists_StillRegistersDispatchers()
        {
            // Modules with no executors (e.g. headless test stubs) must still compile
            // and register both dispatcher systems.
            var module = new ActionDispatchModule(
                locoExecutors:   System.Array.Empty<(ushort, IActionExecutor<LocomotionChannel>)>(),
                weaponExecutors: System.Array.Empty<(ushort, IActionExecutor<WeaponChannel>)>());

            Assert.Equal(3, module.SimulationSystems.Count);
        }
    }
}
