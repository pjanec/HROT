using Fdp.Kernel;
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
    }
}
