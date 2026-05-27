using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// Verifies that <see cref="NavigationTestWorldFactory.Create"/> registers every
    /// navigation component that Phase 1+ systems and tests depend on.
    /// </summary>
    public class NavigationTestWorldFactoryTests
    {
        [Fact]
        public void NavigationTestWorldFactory_RegistersAllNavComponents()
        {
            using var world = NavigationTestWorldFactory.Create();
            var entity = world.CreateEntity();

            // Core legacy components (Phase 0).
            world.AddComponent(entity, new NavigationIntent());
            world.AddComponent(entity, new NavigationStatus());

            // Phase 1 corridor + crowd components (Debt-02 fix).
            world.AddComponent(entity, new NavigationCorridorMuscle());
            world.AddComponent(entity, new NavigationCorridorPreview());
            world.AddComponent(entity, new NavigationPathDetailsBuffer());
            world.AddComponent(entity, new CrowdAgent());
            world.AddComponent(entity, new NavAgentProfile());

            // If any RegisterComponent call was missing, AddComponent would throw.
            // Reaching here means all registrations are present.
            Assert.True(world.HasComponent<NavigationCorridorMuscle>(entity));
            Assert.True(world.HasComponent<NavigationCorridorPreview>(entity));
            Assert.True(world.HasComponent<NavigationPathDetailsBuffer>(entity));
            Assert.True(world.HasComponent<CrowdAgent>(entity));
            Assert.True(world.HasComponent<NavAgentProfile>(entity));
        }
    }
}
