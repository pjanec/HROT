using Xunit;
using Fdp.Kernel;
using System.Threading.Tasks;
using System.Linq;

namespace Fdp.Tests
{
    public class ComponentTypeRegistryPolicyTests
    {
        // Test-only components with explicit IDs in the reserved test range (200–209).
        [ComponentId(200)] private struct RegistryTestComp0 { public int Value; }
        [ComponentId(201)] private struct RegistryTestComp1 { public float Value; }
        [ComponentId(202)] private struct RegistryTestComp2 { public double Value; }
        [ComponentId(203)] private struct RegistryTestComp3 { public long Value; }

        private int Register<T>() where T : unmanaged
        {
            return ComponentType<T>.ID;
        }

        [Fact]
        public void SetRecordable_StoresValue()
        {
            ComponentTypeRegistry.Clear();
            int id = Register<RegistryTestComp0>();
            
            ComponentTypeRegistry.SetRecordable(id, true);
            Assert.True(ComponentTypeRegistry.IsRecordable(id));
            
            ComponentTypeRegistry.SetRecordable(id, false);
            Assert.False(ComponentTypeRegistry.IsRecordable(id));
        }
        
        [Fact]
        public void SetSaveable_StoresValue()
        {
            ComponentTypeRegistry.Clear();
            int id = Register<RegistryTestComp0>();

            ComponentTypeRegistry.SetSaveable(id, true);
            Assert.True(ComponentTypeRegistry.IsSaveable(id));
            
            ComponentTypeRegistry.SetSaveable(id, false);
            Assert.False(ComponentTypeRegistry.IsSaveable(id));
        }
        
        [Fact]
        public void SetNeedsClone_StoresValue()
        {
            ComponentTypeRegistry.Clear();
            int id = Register<RegistryTestComp0>();

            ComponentTypeRegistry.SetNeedsClone(id, true);
            Assert.True(ComponentTypeRegistry.NeedsClone(id));
            
            ComponentTypeRegistry.SetNeedsClone(id, false);
            Assert.False(ComponentTypeRegistry.NeedsClone(id));
        }
        
        [Fact]
        public void GetRecordableTypeIds_ReturnsOnlyRecordable()
        {
            ComponentTypeRegistry.Clear();
            int id0 = Register<RegistryTestComp0>();
            int id1 = Register<RegistryTestComp1>();
            int id2 = Register<RegistryTestComp2>();
            
            ComponentTypeRegistry.SetRecordable(id0, true);
            ComponentTypeRegistry.SetRecordable(id1, false);
            ComponentTypeRegistry.SetRecordable(id2, true);
            
            var ids = ComponentTypeRegistry.GetRecordableTypeIds();
            Assert.Contains(id0, ids);
            Assert.DoesNotContain(id1, ids);
            Assert.Contains(id2, ids);
        }
        
        [Fact]
        public void GetSaveableTypeIds_ReturnsOnlySaveable()
        {
            ComponentTypeRegistry.Clear();
            int id0 = Register<RegistryTestComp0>();
            int id1 = Register<RegistryTestComp1>();
            int id2 = Register<RegistryTestComp2>();
            
            ComponentTypeRegistry.SetSaveable(id0, true);
            ComponentTypeRegistry.SetSaveable(id1, false);
            ComponentTypeRegistry.SetSaveable(id2, true);
            
            var ids = ComponentTypeRegistry.GetSaveableTypeIds();
            Assert.Contains(id0, ids);
            Assert.DoesNotContain(id1, ids);
            Assert.Contains(id2, ids);
        }
        
        [Fact]
        public void IsRecordable_OutOfRange_ReturnsFalse()
        {
            ComponentTypeRegistry.Clear();
            Assert.False(ComponentTypeRegistry.IsRecordable(999));
        }
        
        [Fact]
        public void IsSaveable_OutOfRange_ReturnsFalse()
        {
            ComponentTypeRegistry.Clear();
            Assert.False(ComponentTypeRegistry.IsSaveable(999));
        }
        
        [Fact]
        public void NeedsClone_OutOfRange_ReturnsFalse()
        {
            ComponentTypeRegistry.Clear();
            Assert.False(ComponentTypeRegistry.NeedsClone(999));
        }
        
        [Fact]
        public void Register_InitializesDefaultFlags()
        {
            ComponentTypeRegistry.Clear();
            
            // Register new type
            int id = Register<RegistryTestComp3>();
            
            // Verify defaults (based on ComponentTypeRegistry implementation)
            // Unmanaged/Managed defaults are usually: Snapshot=True, Record=True, Save=True, Clone=False
            Assert.True(ComponentTypeRegistry.IsSnapshotable(id));
            Assert.True(ComponentTypeRegistry.IsRecordable(id));
            Assert.True(ComponentTypeRegistry.IsSaveable(id));
            Assert.False(ComponentTypeRegistry.NeedsClone(id));
        }
        
        [Fact]
        public void Clear_ResetsAllFlags()
        {
            ComponentTypeRegistry.Clear(); // Clear first to ensure 0 start
            int id = Register<RegistryTestComp0>();

            ComponentTypeRegistry.SetRecordable(id, true);
            ComponentTypeRegistry.SetSaveable(id, true);
            ComponentTypeRegistry.SetNeedsClone(id, true);
            
            ComponentTypeRegistry.Clear();
            
            var recordableIds = ComponentTypeRegistry.GetRecordableTypeIds();
            var saveableIds = ComponentTypeRegistry.GetSaveableTypeIds();
            
            Assert.Empty(recordableIds);
            Assert.Empty(saveableIds);
            
            // Also IsRecordable(0) should be false (or safe)
            Assert.False(ComponentTypeRegistry.IsRecordable(id));
        }

        [Fact]
        public void FlagIndependence_SettingOneFlag_DoesNotAffectOthers()
        {
            ComponentTypeRegistry.Clear();
            int id = Register<RegistryTestComp0>();

            // Setup initial state
            ComponentTypeRegistry.SetRecordable(id, true);
            ComponentTypeRegistry.SetSaveable(id, false);
            ComponentTypeRegistry.SetNeedsClone(id, false);

            Assert.True(ComponentTypeRegistry.IsRecordable(id));
            Assert.False(ComponentTypeRegistry.IsSaveable(id));
            Assert.False(ComponentTypeRegistry.NeedsClone(id));

            // Change one flag
            ComponentTypeRegistry.SetSaveable(id, true);
            
            // Verify others unchanged
            Assert.True(ComponentTypeRegistry.IsRecordable(id));
            Assert.True(ComponentTypeRegistry.IsSaveable(id));
            Assert.False(ComponentTypeRegistry.NeedsClone(id));
        }

        [Fact]
        public void LargeTypeIds_HandleGracefully()
        {
            // We can't easily force a "Use" of a large ID without registering enough types.
            // But we can check that querying a large ID returns false safely.
            ComponentTypeRegistry.Clear();
            int largeId = 10000;

            Assert.False(ComponentTypeRegistry.IsRecordable(largeId));
            Assert.False(ComponentTypeRegistry.IsSaveable(largeId));
            Assert.False(ComponentTypeRegistry.NeedsClone(largeId));
        }
        
        [Fact]
        public void Interaction_Snapshotable_Setting()
        {
            ComponentTypeRegistry.Clear();
            int id = Register<RegistryTestComp0>();
            
            ComponentTypeRegistry.SetSnapshotable(id, true);
            Assert.True(ComponentTypeRegistry.IsSnapshotable(id));
            
            // Snapshotable shouldn't change Recordable/Saveable on its own
            // (Assuming implementation keeps them separate)
            var originalRec = ComponentTypeRegistry.IsRecordable(id);
            var originalSave = ComponentTypeRegistry.IsSaveable(id);
            
            ComponentTypeRegistry.SetSnapshotable(id, !ComponentTypeRegistry.IsSnapshotable(id));
            
            Assert.Equal(originalRec, ComponentTypeRegistry.IsRecordable(id));
            Assert.Equal(originalSave, ComponentTypeRegistry.IsSaveable(id));
        }

        [Fact]
        public void GetSnapshotableTypeIds_Correctness()
        {
            ComponentTypeRegistry.Clear();
            int id0 = Register<RegistryTestComp0>();
            int id1 = Register<RegistryTestComp1>();
            int id2 = Register<RegistryTestComp2>();

            ComponentTypeRegistry.SetSnapshotable(id0, true);
            ComponentTypeRegistry.SetSnapshotable(id1, false);
            ComponentTypeRegistry.SetSnapshotable(id2, true);

            var ids = ComponentTypeRegistry.GetSnapshotableTypeIds();
            Assert.Contains(id0, ids);
            Assert.DoesNotContain(id1, ids);
            Assert.Contains(id2, ids);
        }

        [Fact]
        public void ThreadSafety_ConcurrentAccess_DoesNotCrash()
        {
            ComponentTypeRegistry.Clear();
            
            // Register a base set to work with
            int max = 100;
            for(int i=0; i<max; i++) {
                // Registering Managed types is easier loop-wise if we don't have enough unmanaged types
                ComponentTypeRegistry.GetOrRegister<RegistryTestComp0>(); // Idempotent
                // We need distinct types.
                // Let's just use concurrent reads primarily.
            }
            // Actually, let's register one type and pound it.
            int id = Register<RegistryTestComp0>();

            Parallel.For(0, 1000, i => 
            {
                // Concurrent reads
                bool b = ComponentTypeRegistry.IsRecordable(id);
                // Concurrent writes to same index
                // Note: List<T> is NOT thread safe for writes usually, BUT Set* methods use a lock (_lock).
                // So this should be safe.
                ComponentTypeRegistry.SetRecordable(id, i % 2 == 0);
            });
            
            // Pass if no exception
            Assert.True(true);
        }
    }
}

