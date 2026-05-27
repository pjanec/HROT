using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Navigation.Fake;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// Unit tests for <see cref="NavigationFakesModule"/>.
    /// </summary>
    public class NavigationFakesModuleTests
    {
        [Fact]
        public void Module_DefaultConstructor_ProvidersNotNull()
        {
            var module = new NavigationFakesModule();
            Assert.NotNull(module.Navmesh);
            Assert.NotNull(module.Crowd);
            Assert.NotNull(module.Volumetric);
            Assert.NotNull(module.PathRegistry);
        }

        [Fact]
        public void Module_FromMap_MapPropertySet()
        {
            var map    = NavTestMaps.LoadCorridor();
            var module = new NavigationFakesModule(map);
            Assert.Same(map, module.Map);
        }

        [Fact]
        public void Module_SharedRegistry_BrainAndMuscleShareSameStore()
        {
            var module = new NavigationFakesModule();

            var waypoints = new NavWaypoint[]
            {
                new NavWaypoint { Position = new Vector3(0, 0, 0) },
                new NavWaypoint { Position = new Vector3(1, 0, 0) },
            };

            // Write via Muscle test-API.
            module.PathRegistry.Muscle.RegisterOrReplace(42, waypoints, 10f, 1u, 0, 0);

            // Read via IPathRegistry interface (Brain view).
            IPathRegistry registry = module.PathRegistry;
            var buf = new NavWaypoint[4];
            Assert.True(registry.TryGetWaypoints(42, buf.AsSpan(), out int count));
            Assert.Equal(2, count);
        }

        [Fact]
        public void Module_RegisterProviders_NavmeshSingletonAvailable()
        {
            var world  = NavigationTestWorldFactory.Create();
            var module = new NavigationFakesModule(NavTestMaps.LoadCorridor());
            module.RegisterProviders(world);

            var provider = world.GetSingletonManaged<INavmeshProvider>();
            Assert.NotNull(provider);
            Assert.Same(module.Navmesh, provider);
        }
    }
}
