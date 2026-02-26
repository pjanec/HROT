using Xunit;
using Fdp.Kernel;
using System;

namespace Fdp.Tests
{
    // Test components of various sizes
    [ComponentId(170)]
    public struct SmallComponent  // 12 bytes - single part
    {
        public float X, Y, Z;
    }
    
    [ComponentId(171)]
    public struct MediumComponent  // 128 bytes - 2 parts
    {
        public float X, Y, Z, W;
        public unsafe fixed float Data[28];  // Total: 4*4 + 28*4 = 128 bytes
    }
    
    [ComponentId(172)]
    public unsafe struct LargeComponent  // 256 bytes - 4 parts
    {
        public fixed byte Data[256];
    }
    
    [Collection("ComponentTests")]
    public class PartDescriptorTests
    {
        public PartDescriptorTests()
        {
            ComponentTypeRegistry.Clear();
        }
        
        [Fact]
        public void PartDescriptor_Empty_HasNoParts()
        {
            var desc = PartDescriptor.Empty();
            
            Assert.False(desc.HasAnyParts());
            Assert.False(desc.HasPart(0));
            Assert.False(desc.HasPart(1));
        }
        
        [Fact]
        public void PartDescriptor_All_HasAllParts()
        {
            var desc = PartDescriptor.All();
            
            Assert.True(desc.HasAnyParts());
            
            // Check many parts
            for (int i = 0; i < 256; i++)
            {
                Assert.True(desc.HasPart(i));
            }
        }
        
        [Fact]
        public void PartDescriptor_SetPart_Works()
        {
            var desc = PartDescriptor.Empty();
            
            desc.SetPart(5);
            desc.SetPart(10);
            
            Assert.True(desc.HasPart(5));
            Assert.True(desc.HasPart(10));
            Assert.False(desc.HasPart(0));
            Assert.False(desc.HasPart(1));
        }
        
        [Fact]
        public void PartDescriptor_ClearPart_Works()
        {
            var desc = PartDescriptor.All();
            
            desc.ClearPart(5);
            desc.ClearPart(10);
            
            Assert.False(desc.HasPart(5));
            Assert.False(desc.HasPart(10));
            Assert.True(desc.HasPart(0));
            Assert.True(desc.HasPart(1));
        }
        
        [Fact]
        public void PartDescriptor_UnionWith_CombinesParts()
        {
            var desc1 = PartDescriptor.Empty();
            desc1.SetPart(0);
            desc1.SetPart(1);
            
            var desc2 = PartDescriptor.Empty();
            desc2.SetPart(1);
            desc2.SetPart(2);
            
            desc1.UnionWith(desc2);
            
            Assert.True(desc1.HasPart(0));
            Assert.True(desc1.HasPart(1));
            Assert.True(desc1.HasPart(2));
        }
        
        [Fact]
        public void PartDescriptor_IntersectWith_FindsCommonParts()
        {
            var desc1 = PartDescriptor.Empty();
            desc1.SetPart(0);
            desc1.SetPart(1);
            desc1.SetPart(2);
            
            var desc2 = PartDescriptor.Empty();
            desc2.SetPart(1);
            desc2.SetPart(2);
            desc2.SetPart(3);
            
            desc1.IntersectWith(desc2);
            
            Assert.False(desc1.HasPart(0));
            Assert.True(desc1.HasPart(1));
            Assert.True(desc1.HasPart(2));
            Assert.False(desc1.HasPart(3));
        }
        
        [Fact]
        public void PartDescriptor_Equality_Works()
        {
            var desc1 = PartDescriptor.Empty();
            desc1.SetPart(5);
            
            var desc2 = PartDescriptor.Empty();
            desc2.SetPart(5);
            
            var desc3 = PartDescriptor.Empty();
            desc3.SetPart(6);
            
            Assert.True(desc1 == desc2);
            Assert.False(desc1 == desc3);
            Assert.True(desc1.Equals(desc2));
        }
    }
    
    [Collection("ComponentTests")]
    public class MultiPartComponentTests
    {
        public MultiPartComponentTests()
        {
            ComponentTypeRegistry.Clear();
        }
        
        [Fact]
        public void GetPartCount_SmallComponent_SinglePart()
        {
            int count = MultiPartComponent.GetPartCount<SmallComponent>();
            
            Assert.Equal(1, count);
        }
        
        [Fact]
        public void GetPartCount_MediumComponent_TwoParts()
        {
            int count = MultiPartComponent.GetPartCount<MediumComponent>();
            
            Assert.Equal(2, count);  // 128 bytes / 64 = 2
        }
        
        [Fact]
        public void GetPartCount_LargeComponent_FourParts()
        {
            int count = MultiPartComponent.GetPartCount<LargeComponent>();
            
            Assert.Equal(4, count);  // 256 bytes / 64 = 4
        }
        
        [Fact]
        public void IsMultiPart_DetectsCorrectly()
        {
            Assert.False(MultiPartComponent.IsMultiPart<SmallComponent>());
            Assert.True(MultiPartComponent.IsMultiPart<MediumComponent>());
            Assert.True(MultiPartComponent.IsMultiPart<LargeComponent>());
        }
        
        [Fact]
        public void CreateFullDescriptor_SetsCorrectParts()
        {
            var desc = MultiPartComponent.CreateFullDescriptor<MediumComponent>();
            
            Assert.True(desc.HasPart(0));
            Assert.True(desc.HasPart(1));
            Assert.False(desc.HasPart(2));  // Only 2 parts
        }
        
        [Fact]
        public void GetPartOffset_CalculatesCorrectly()
        {
            Assert.Equal(0, MultiPartComponent.GetPartOffset(0));
            Assert.Equal(64, MultiPartComponent.GetPartOffset(1));
            Assert.Equal(128, MultiPartComponent.GetPartOffset(2));
            Assert.Equal(192, MultiPartComponent.GetPartOffset(3));
        }
        
        [Fact]
        public void GetPartSize_ReturnsCorrectSizes()
        {
            // Small component: 12 bytes total
            Assert.Equal(12, MultiPartComponent.GetPartSize<SmallComponent>(0));
            Assert.Equal(0, MultiPartComponent.GetPartSize<SmallComponent>(1));
            
            // Medium component: 128 bytes total (2 x 64)
            Assert.Equal(64, MultiPartComponent.GetPartSize<MediumComponent>(0));
            Assert.Equal(64, MultiPartComponent.GetPartSize<MediumComponent>(1));
            Assert.Equal(0, MultiPartComponent.GetPartSize<MediumComponent>(2));
        }
        
        [Fact]
        public void GetChangedParts_NoChanges_ReturnsEmpty()
        {
            var comp1 = new SmallComponent { X = 1, Y = 2, Z = 3 };
            var comp2 = new SmallComponent { X = 1, Y = 2, Z = 3 };
            
            var desc = MultiPartComponent.GetChangedParts(comp1, comp2);
            
            Assert.False(desc.HasAnyParts());
        }
        
        [Fact]
        public void GetChangedParts_SmallChange_DetectsPart()
        {
            var comp1 = new SmallComponent { X = 1, Y = 2, Z = 3 };
            var comp2 = new SmallComponent { X = 999, Y = 2, Z = 3 };
            
            var desc = MultiPartComponent.GetChangedParts(comp1, comp2);
            
            Assert.True(desc.HasPart(0));  // Only 1 part for small component
        }
        
        [Fact]
        public unsafe void GetChangedParts_MultiPart_DetectsCorrectParts()
        {
            var comp1 = new LargeComponent();
            var comp2 = new LargeComponent();
            
            // Initialize both
            for (int i = 0; i < 256; i++)
            {
                comp1.Data[i] = (byte)i;
                comp2.Data[i] = (byte)i;
            }
            
            // Change only part 2 (bytes 128-191)
            comp2.Data[150] = 99;
            
            var desc = MultiPartComponent.GetChangedParts(comp1, comp2);
            
            Assert.False(desc.HasPart(0));  // Part 0 unchanged
            Assert.False(desc.HasPart(1));  // Part 1 unchanged
            Assert.True(desc.HasPart(2));   // Part 2 changed
            Assert.False(desc.HasPart(3));  // Part 3 unchanged
        }
        
        [Fact]
        public unsafe void CopyParts_CopiesOnlySpecifiedParts()
        {
            var source = new LargeComponent();
            var dest = new LargeComponent();
            
            // Initialize source with unique values
            for (int i = 0; i < 256; i++)
            {
                source.Data[i] = (byte)(i + 100);
                dest.Data[i] = 0;
            }
            
            // Copy only part 1 (bytes 64-127)
            var desc = PartDescriptor.Empty();
            desc.SetPart(1);
            
            MultiPartComponent.CopyParts(ref dest, source, desc);
            
            // Verify part 0 not copied
            Assert.Equal(0, dest.Data[0]);
            Assert.Equal(0, dest.Data[63]);
            
            // Verify part 1 copied
            Assert.Equal(164, dest.Data[64]);   // 64 + 100
            Assert.Equal(227, dest.Data[127]);  // 127 + 100
            
            // Verify part 2 not copied
            Assert.Equal(0, dest.Data[128]);
        }
        
        [Fact]
        public void CopyParts_SmallComponent_WorksCorrectly()
        {
            var source = new SmallComponent { X = 10, Y = 20, Z = 30 };
            var dest = new SmallComponent { X = 0, Y = 0, Z = 0 };
            
            var desc = PartDescriptor.Empty();
            desc.SetPart(0);
            
            MultiPartComponent.CopyParts(ref dest, source, desc);
            
            Assert.Equal(10, dest.X);
            Assert.Equal(20, dest.Y);
            Assert.Equal(30, dest.Z);
        }
        
        [Fact]
        public void CopyParts_EmptyDescriptor_CopiesNothing()
        {
            var source = new SmallComponent { X = 10, Y = 20, Z = 30 };
            var dest = new SmallComponent { X = 1, Y = 2, Z = 3 };
            
            var desc = PartDescriptor.Empty();
            
            MultiPartComponent.CopyParts(ref dest, source, desc);
            
            // Destination unchanged
            Assert.Equal(1, dest.X);
            Assert.Equal(2, dest.Y);
            Assert.Equal(3, dest.Z);
        }
    }
    
    [Collection("ComponentTests")]
    public class MultiPartIntegrationTests
    {
        public MultiPartIntegrationTests()
        {
            ComponentTypeRegistry.Clear();
        }
        
        [Fact]
        public unsafe void Integration_DeltaUpdates()
        {
            // Simulate network delta updates
            var serverState = new LargeComponent();
            var clientState = new LargeComponent();
            
            // Initialize both to same state
            for (int i = 0; i < 256; i++)
            {
                serverState.Data[i] = (byte)i;
                clientState.Data[i] = (byte)i;
            }
            
            // Server modifies part 2
            serverState.Data[150] = 99;
            serverState.Data[151] = 88;
            
            // Detect changes
            var changedParts = MultiPartComponent.GetChangedParts(clientState, serverState);
            
            // Only part 2 should be marked
            Assert.True(changedParts.HasPart(2));
            Assert.False(changedParts.HasPart(0));
            Assert.False(changedParts.HasPart(1));
            Assert.False(changedParts.HasPart(3));
            
            // Apply delta to client
            MultiPartComponent.CopyParts(ref clientState, serverState, changedParts);
            
            // Verify client now matches server
            for (int i = 0; i < 256; i++)
            {
                Assert.Equal(serverState.Data[i], clientState.Data[i]);
            }
        }
        
        [Fact]
        public void Integration_WithEntityRepository()
        {
            using var repo = new EntityRepository();
            repo.RegisterComponent<SmallComponent>();
            
            var entity = repo.CreateEntity();
            
            var initial = new SmallComponent { X = 1, Y = 2, Z = 3 };
            repo.AddComponent(entity, initial);
            
            // Simulate update
            var updated = new SmallComponent { X = 10, Y = 2, Z = 3 };
            
            // Detect what changed
            var changedParts = MultiPartComponent.GetChangedParts(initial, updated);
            
            // Apply update
            ref var current = ref repo.GetComponentRW<SmallComponent>(entity);
            MultiPartComponent.CopyParts(ref current, updated, changedParts);
            
            Assert.Equal(10, current.X);
            Assert.Equal(2, current.Y);
        }
    }
}
