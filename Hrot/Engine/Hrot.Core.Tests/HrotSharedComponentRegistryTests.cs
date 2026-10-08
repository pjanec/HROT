using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Xunit;

namespace Hrot.Map.Common.Tests
{
    /// <summary>
    /// Verifies that <see cref="HrotSharedComponentRegistry.RegisterAll"/> registers
    /// all expected shared components, including <see cref="PartMetadata"/> which is
    /// required for personal route / hierarchical entity linking.
    /// </summary>
    public class HrotSharedComponentRegistryTests
    {
        [Fact]
        public void RegisterAll_DoesNotThrow()
        {
            using var world = new EntityRepository();
            // Must not throw any "Component not registered" exception.
            HrotSharedComponentRegistry.RegisterAll(world);
        }

        [Fact]
        public void RegisterAll_PartMetadata_IsRegistered()
        {
            using var world = new EntityRepository();
            HrotSharedComponentRegistry.RegisterAll(world);

            // Creating an entity and adding PartMetadata must succeed without
            // the "Component PartMetadata is not registered" InvalidOperationException
            // that was the root cause of the Shift+Right-Click crash in PersonalRouteAuthoringSystem.
            var entity = world.CreateEntity();
            var meta   = new PartMetadata { ParentEntity = Entity.Null };

            // AddUnmanagedComponent throws InvalidOperationException when not registered.
            world.AddComponent(entity, meta);

            Assert.True(world.HasComponent<PartMetadata>(entity));
        }

        /// <summary>
        /// ⭐ R-140 D2 — <c>NetworkSpawningSystem.ProcessSpawn</c> stamps <see cref="Fdp.Toolkit.Scenario.ScenarioIgnoreTag"/> on every
        /// transient entity it materialises, on every host. 🔴 No host registered it (only the rails did, each for itself), so the first
        /// door entity spawned in a cluster killed the CGF process (bt-doors live run, 2026-10-08).
        /// </summary>
        [Fact]
        public void RegisterAll_TheTransientSpawnTag_IsRegistered()
        {
            using var world = new EntityRepository();
            HrotSharedComponentRegistry.RegisterAll(world);

            var entity = world.CreateEntity();
            world.AddComponent(entity, new Fdp.Toolkit.Scenario.ScenarioIgnoreTag());
            Assert.True(world.HasComponent<Fdp.Toolkit.Scenario.ScenarioIgnoreTag>(entity));
        }
    }
}
