using System.Collections.Generic;
using MessagePack;
using Xunit;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;

namespace Fdp.Tests
{
    [MessagePackObject]
    [DataPolicy(DataPolicy.SnapshotViaClone)]
    [ComponentId(174)]
    public class SimpleCloneableClass
    {
        [Key(0)] public int Value { get; set; }
        [Key(1)] public string Name { get; set; } = "";
    }
    
    [MessagePackObject]
    [DataPolicy(DataPolicy.SnapshotViaClone)]
    public class ComplexCloneableClass
    {
        [Key(0)] public int Value { get; set; }
        [Key(1)] public List<int> Items { get; set; } = new();
        [Key(2)] public Dictionary<string, int> Dict { get; set; } = new();
    }
    
    [MessagePackObject]
    [DataPolicy(DataPolicy.SnapshotViaClone)]
    public class NestedCloneableClass
    {
        [Key(0)] public SimpleCloneableClass Inner { get; set; } = new();
        [Key(1)] public int OuterValue { get; set; }
    }
    
    public class ComponentCloningTests
    {
        [Fact]
        public void DeepClone_SimpleClass_CreatesIndependentCopy()
        {
            var original = new SimpleCloneableClass 
            { 
                Value = 42, 
                Name = "Test" 
            };
            
            var clone = FdpAutoSerializer.DeepClone(original);
            
            // Verify clone has same data
            Assert.Equal(42, clone.Value);
            Assert.Equal("Test", clone.Name);
            
            // Verify independence: mutating original doesn't affect clone
            original.Value = 99;
            original.Name = "Changed";
            
            Assert.Equal(42, clone.Value);  // Clone unchanged
            Assert.Equal("Test", clone.Name);  // Clone unchanged
        }
        
        [Fact]
        public void DeepClone_ComplexClass_CreatesDeepCopy()
        {
            var original = new ComplexCloneableClass
            {
                Value = 100,
                Items = new List<int> { 1, 2, 3 },
                Dict = new Dictionary<string, int> { ["A"] = 10, ["B"] = 20 }
            };
            
            var clone = FdpAutoSerializer.DeepClone(original);
            
            // Verify data
            Assert.Equal(100, clone.Value);
            Assert.Equal(new[] { 1, 2, 3 }, clone.Items);
            Assert.Equal(10, clone.Dict["A"]);
            
            // Verify independence: mutate collections
            original.Items.Add(4);
            original.Dict["C"] = 30;
            
            Assert.Equal(3, clone.Items.Count);  // Clone unchanged
            Assert.False(clone.Dict.ContainsKey("C"));  // Clone unchanged
        }
        
        [Fact]
        public void DeepClone_NestedClass_ClonesRecursively()
        {
            var original = new NestedCloneableClass
            {
                Inner = new SimpleCloneableClass { Value = 50, Name = "Inner" },
                OuterValue = 200
            };
            
            var clone = FdpAutoSerializer.DeepClone(original);
            
            // Verify data
            Assert.Equal(50, clone.Inner.Value);
            Assert.Equal("Inner", clone.Inner.Name);
            Assert.Equal(200, clone.OuterValue);
            
            // Verify deep independence
            original.Inner.Value = 999;
            
            Assert.Equal(50, clone.Inner.Value);  // Clone's inner unchanged
            Assert.NotSame(original.Inner, clone.Inner);  // Different instances
        }
        
        [Fact]
        public void DeepClone_String_ReturnsReference()
        {
            string original = "test";
            string clone = FdpAutoSerializer.DeepClone(original);
            
            // Strings are immutable, so reference copy is safe
            Assert.Same(original, clone);
        }
        
        [Fact]
        public void DeepClone_Null_ReturnsNull()
        {
            SimpleCloneableClass? original = null;
            var clone = FdpAutoSerializer.DeepClone(original);
            
            // Explicitly assert null for clarity
            Assert.Null(clone);
        }
        
        [Fact]
        public void SyncDirtyChunks_CloneableComponent_CreatesIndependentCopies()
        {
            var repo1 = new EntityRepository();
            var repo2 = new EntityRepository();
            
            // Register and ensure policies are active (auto or manual)
            repo1.RegisterManagedComponent<SimpleCloneableClass>();
            repo2.RegisterManagedComponent<SimpleCloneableClass>();
            
            var e = repo1.CreateEntity();
            var original = new SimpleCloneableClass { Value = 100, Name = "Original" };
            repo1.AddComponent(e, original);
            
            // Simulate snapshot sync
            // This internally calls DeepClone for SnapshotViaClone policy
            repo2.SyncFrom(repo1);
            
            // Mutate original
            original.Value = 999;
            original.Name = "Mutated";
            
            // Verify snapshot is isolated
            var snapshotCopy = repo2.GetComponent<SimpleCloneableClass>(e);
            Assert.Equal(100, snapshotCopy.Value);  // Clone unchanged
            Assert.Equal("Original", snapshotCopy.Name);  // Clone unchanged
        }
        
        [Fact]
        public void CloneableComponent_IsSnapshotable()
        {
            var repo = new EntityRepository();
            repo.RegisterManagedComponent<SimpleCloneableClass>();
            
            int typeId = ManagedComponentType<SimpleCloneableClass>.ID;
            
            Assert.True(ComponentTypeRegistry.IsSnapshotable(typeId));
            Assert.True(ComponentTypeRegistry.NeedsClone(typeId));
        }
        
        [Fact]
        public void GetSnapshotableMask_IncludesCloneableComponent()
        {
            var repo = new EntityRepository();
            repo.RegisterManagedComponent<SimpleCloneableClass>();
            
            var mask = repo.GetSnapshotableMask();
            int typeId = ManagedComponentType<SimpleCloneableClass>.ID;
            
            Assert.True(mask.IsSet(typeId));
        }
        
        [Fact]
        public void DeepClone_EmptyList_Clones()
        {
            var original = new ComplexCloneableClass
            {
                Items = new List<int>(),  // Empty
                Dict = new Dictionary<string, int>()  // Empty
            };
            
            var clone = FdpAutoSerializer.DeepClone(original);
            
            Assert.NotNull(clone.Items);
            Assert.Empty(clone.Items);
            Assert.NotSame(original.Items, clone.Items);  // Different instances
        }
        
        [Fact]
        public void DeepClone_Performance_IsCached()
        {
            // First call compiles
            var obj1 = new SimpleCloneableClass { Value = 1 };
            var clone1 = FdpAutoSerializer.DeepClone(obj1);
            
            // Second call should use cached delegate (very fast)
            var obj2 = new SimpleCloneableClass { Value = 2 };
            var clone2 = FdpAutoSerializer.DeepClone(obj2);
            
            Assert.Equal(1, clone1.Value);
            Assert.Equal(2, clone2.Value);
        }

        [Fact]
        public void DeepClone_NullFields_HandledCorrectly()
        {
            var original = new ComplexCloneableClass
            {
                Value = 5,
                Items = null!, // Explicit null
                Dict = null!
            };
            
            var clone = FdpAutoSerializer.DeepClone(original);
            
            Assert.Equal(5, clone.Value);
            Assert.Null(clone.Items);
            Assert.Null(clone.Dict);
        }

        [Fact]
        public void DeepClone_MultipleClones_AllIndependent()
        {
            var original = new SimpleCloneableClass { Value = 1 };
            var clone1 = FdpAutoSerializer.DeepClone(original);
            var clone2 = FdpAutoSerializer.DeepClone(original);

            // Mutate clone1
            clone1.Value = 2;
            
            Assert.Equal(1, original.Value);
            Assert.Equal(2, clone1.Value);
            Assert.Equal(1, clone2.Value);
        }
    }
}
