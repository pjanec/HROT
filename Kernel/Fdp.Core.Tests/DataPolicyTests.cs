using System;
using Xunit;
using Fdp.Kernel;

namespace Fdp.Tests
{
    public class DataPolicyTests
    {
        // ━━━ Test Types ━━━
        
        // 1. Struct (Default: All Enabled)
        [ComponentId(177)]
        private struct NormalStruct { public int Value; }
        
        // 2. Record (Default: All Enabled)
        [ComponentId(178)]
        private record NormalRecord(int Value);
        
        // 3. Class (Default: NoSnapshot + Record + Save)
        [ComponentId(179)]
        private class MutableClass { public int Value; }
        
        // 4. Attributes
        [DataPolicy(DataPolicy.NoSnapshot)]
        [ComponentId(180)]
        private struct NoSnapshotStruct { public int Value; }
        
        [DataPolicy(DataPolicy.SnapshotViaClone)]
        [ComponentId(181)]
        private class CloneableClass { public int Value; }
        
        [DataPolicy(DataPolicy.NoRecord)]
        [ComponentId(182)]
        private struct NoRecordStruct { public int Value; }
        
        [DataPolicy(DataPolicy.NoSave)]
        [ComponentId(183)]
        private struct NoSaveStruct { public int Value; }
        
        [DataPolicy(DataPolicy.Transient)] // NoSnapshot | NoRecord | NoSave
        [ComponentId(184)]
        private class TransientClass { public int Value; }

        [Fact]
        public void UnmanagedStruct_DefaultsTo_AllEnabled()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NormalStruct>();
            
            int id = ComponentType<NormalStruct>.ID;
            
            Assert.True(ComponentTypeRegistry.IsSnapshotable(id));
            Assert.True(ComponentTypeRegistry.IsRecordable(id));
            Assert.True(ComponentTypeRegistry.IsSaveable(id));
            Assert.False(ComponentTypeRegistry.NeedsClone(id));
        }

        [Fact]
        public void ManagedRecord_DefaultsTo_AllEnabled()
        {
            var repo = new EntityRepository();
            repo.RegisterManagedComponent<NormalRecord>();
            
            int id = ManagedComponentType<NormalRecord>.ID;
            
            Assert.True(ComponentTypeRegistry.IsSnapshotable(id));
            Assert.True(ComponentTypeRegistry.IsRecordable(id));
            Assert.True(ComponentTypeRegistry.IsSaveable(id));
            Assert.False(ComponentTypeRegistry.NeedsClone(id));
        }

        [Fact]
        public void MutableClass_DefaultsTo_NoSnapshot_ButRecordableAndSaveable()
        {
            var repo = new EntityRepository();
            repo.RegisterManagedComponent<MutableClass>();
            
            int id = ManagedComponentType<MutableClass>.ID;
            
            Assert.False(ComponentTypeRegistry.IsSnapshotable(id), "Mutable class should NOT be snapshotable by default");
            Assert.True(ComponentTypeRegistry.IsRecordable(id), "Mutable class SHOULD be recordable by default");
            Assert.True(ComponentTypeRegistry.IsSaveable(id), "Mutable class SHOULD be saveable by default");
            Assert.False(ComponentTypeRegistry.NeedsClone(id));
        }

        [Fact]
        public void Attribute_NoSnapshot_Respected()
        {
            var repo = new EntityRepository();
            repo.RegisterComponent<NoSnapshotStruct>();
            
            int id = ComponentType<NoSnapshotStruct>.ID;
            
            Assert.False(ComponentTypeRegistry.IsSnapshotable(id));
            Assert.True(ComponentTypeRegistry.IsRecordable(id));
            Assert.True(ComponentTypeRegistry.IsSaveable(id));
        }

        [Fact]
        public void Attribute_SnapshotViaClone_EnablesSnapshotAndClone()
        {
            var repo = new EntityRepository();
            repo.RegisterManagedComponent<CloneableClass>();
            
            int id = ManagedComponentType<CloneableClass>.ID;
            
            Assert.True(ComponentTypeRegistry.IsSnapshotable(id), "SnapshotViaClone should enable Snapshot");
            Assert.True(ComponentTypeRegistry.NeedsClone(id), "SnapshotViaClone should enable NeedsClone");
        }

        [Fact]
        public void Attribute_Transient_DisablesAll()
        {
            var repo = new EntityRepository();
            repo.RegisterManagedComponent<TransientClass>();
            
            int id = ManagedComponentType<TransientClass>.ID;
            
            Assert.False(ComponentTypeRegistry.IsSnapshotable(id));
            Assert.False(ComponentTypeRegistry.IsRecordable(id));
            Assert.False(ComponentTypeRegistry.IsSaveable(id));
        }

        [Fact]
        public void Override_ExplicitArgument_WinsOverConvention()
        {
            var repo = new EntityRepository();
            // Force mutable class to be snapshotable (unsafe but allowed override)
            repo.RegisterManagedComponent<MutableClass>(DataPolicy.Default);
            
            int id = ManagedComponentType<MutableClass>.ID;
            Assert.True(ComponentTypeRegistry.IsSnapshotable(id));
        }

        [Fact]
        public void Override_ExplicitArgument_WinsOverAttribute()
        {
            var repo = new EntityRepository();
            // Force Transient class to be Recordable (but still NoSnapshot)
            // Wait, we need to pass a specific policy.
            // If we want NoSnapshot + Record + NoSave.
            repo.RegisterManagedComponent<TransientClass>(DataPolicy.NoSnapshot | DataPolicy.NoSave);
            
            int id = ManagedComponentType<TransientClass>.ID;
            
            Assert.False(ComponentTypeRegistry.IsSnapshotable(id));
            Assert.True(ComponentTypeRegistry.IsRecordable(id)); // Enabled!
            Assert.False(ComponentTypeRegistry.IsSaveable(id));
        }
    }
}
