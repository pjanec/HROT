using System.Collections.Generic;
using MessagePack;
using Xunit;
using Fdp.Kernel;

namespace Fdp.Tests
{
    // Test component: Mutable class WITHOUT DataPolicy attribute
    [ComponentId(173)]
    [MessagePackObject]
    public class CombatHistory
    {
        [Key(0)] public int TotalDamage { get; set; }
        [Key(1)] public List<string> Events { get; set; } = new();
        
        public void RecordDamage(int amount, string source)
        {
            TotalDamage += amount;
            Events.Add($"{amount} from {source}");
        }
    }
    
    public class MutableClassRecordingTests
    {
        [Fact]
        public void MutableClass_NoAttribute_DoesNotCrash()
        {
            // Ensure clean slate for this type if possible, or just rely on robust registration
            // THE FIX: This should NOT throw
            var repo = new EntityRepository();
            repo.RegisterManagedComponent<CombatHistory>();
            
            var e = repo.CreateEntity();
            repo.AddComponent(e, new CombatHistory());
            
            Assert.True(repo.HasComponent<CombatHistory>(e));
        }
        
        [Fact]
        public void MutableClass_DefaultsToRecordable()
        {
            var repo = new EntityRepository();
            repo.RegisterManagedComponent<CombatHistory>();
            
            int typeId = ManagedComponentType<CombatHistory>.ID;
            
            Assert.True(ComponentTypeRegistry.IsRecordable(typeId));
            Assert.True(ComponentTypeRegistry.IsSaveable(typeId));
        }
        
        [Fact]
        public void MutableClass_DefaultsToNoSnapshot()
        {
            var repo = new EntityRepository();
            repo.RegisterManagedComponent<CombatHistory>();
            
            int typeId = ManagedComponentType<CombatHistory>.ID;
            
            // Should NOT be snapshotable (unsafe for background threads)
            Assert.False(ComponentTypeRegistry.IsSnapshotable(typeId));
            Assert.False(ComponentTypeRegistry.NeedsClone(typeId));
        }
        
        [Fact]
        public void GetRecordableMask_IncludesMutableClass()
        {
            var repo = new EntityRepository();
            repo.RegisterManagedComponent<CombatHistory>();
            
            var mask = repo.GetRecordableMask();
            int typeId = ManagedComponentType<CombatHistory>.ID;
            
            Assert.True(mask.IsSet(typeId));
        }
        
        [Fact]
        public void GetSnapshotableMask_ExcludesMutableClass()
        {
            var repo = new EntityRepository();
            repo.RegisterManagedComponent<CombatHistory>();
            
            var mask = repo.GetSnapshotableMask();
            int typeId = ManagedComponentType<CombatHistory>.ID;
            
            Assert.False(mask.IsSet(typeId));
        }
        
        [Fact]
        public void GetSaveableMask_IncludesMutableClass()
        {
            var repo = new EntityRepository();
            repo.RegisterManagedComponent<CombatHistory>();
            
            var mask = repo.GetSaveableMask();
            int typeId = ManagedComponentType<CombatHistory>.ID;
            
            Assert.True(mask.IsSet(typeId));
        }
        
        [Fact]
        public void MutableClass_CanMutateOnMainThread()
        {
            var repo = new EntityRepository();
            repo.RegisterManagedComponent<CombatHistory>();
            
            var e = repo.CreateEntity();
            var history = new CombatHistory();
            repo.AddComponent(e, history);
            
            // Mutate
            history.RecordDamage(50, "Dragon");
            
            // Verify mutation visible
            var retrieved = repo.GetComponent<CombatHistory>(e);
            Assert.Equal(50, retrieved.TotalDamage);
            Assert.Single(retrieved.Events);
        }
        
        [Fact]
        public void MutableClass_NotInBackgroundSnapshot()
        {
            var mainRepo = new EntityRepository();
            var snapshotRepo = new EntityRepository();
            
            mainRepo.RegisterManagedComponent<CombatHistory>();
            snapshotRepo.RegisterManagedComponent<CombatHistory>();
            
            var e = mainRepo.CreateEntity();
            mainRepo.AddComponent(e, new CombatHistory { TotalDamage = 100 });
            
            // Simulate background snapshot
            snapshotRepo.SyncFrom(mainRepo);
            
            // Should NOT have the component (not snapshotable)
            Assert.False(snapshotRepo.HasComponent<CombatHistory>(e));
        }
    }
}
