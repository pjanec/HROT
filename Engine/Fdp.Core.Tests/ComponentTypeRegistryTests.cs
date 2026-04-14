using System;
using Xunit;
using Fdp.Kernel;
using System.Linq;

namespace Fdp.Tests
{
    public class ComponentTypeRegistryTests
    {
        // Unique types to avoid conflict with other tests
        [ComponentId(185)]
        private struct RegTestCompA { public int A; }
        [ComponentId(186)]
        private struct RegTestCompB { public int B; }

        [Fact]
        public void SetSnapshotable_UpdatesStatus()
        {
            int id = ComponentTypeRegistry.GetOrRegister<RegTestCompA>();
            
            // Default is true
            Assert.True(ComponentTypeRegistry.IsSnapshotable(id));

            ComponentTypeRegistry.SetSnapshotable(id, false);
            Assert.False(ComponentTypeRegistry.IsSnapshotable(id));
            
            ComponentTypeRegistry.SetSnapshotable(id, true);
            Assert.True(ComponentTypeRegistry.IsSnapshotable(id));
        }

        [Fact]
        public void GetSnapshotableTypeIds_ReturnsOnlySnapshotable()
        {
            int idA = ComponentTypeRegistry.GetOrRegister<RegTestCompA>();
            int idB = ComponentTypeRegistry.GetOrRegister<RegTestCompB>();
            
            ComponentTypeRegistry.SetSnapshotable(idA, true);
            ComponentTypeRegistry.SetSnapshotable(idB, false);
            
            var ids = ComponentTypeRegistry.GetSnapshotableTypeIds();
            
            Assert.Contains(idA, ids);
            Assert.DoesNotContain(idB, ids);
        }
    }
}
