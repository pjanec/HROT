using System;
using Xunit;
using Fdp.ModuleHost.Network;
using Fdp.Kernel; // For component types

namespace Fdp.ModuleHost.Tests.Network
{
    public class DescriptorOwnershipMapTests
    {
        [ComponentId(102)]
        private struct TestPosition { }
        [ComponentId(103)]
        private struct TestVelocity { }

        [Fact]
        public void RegisterMapping_StoresComponentsCorrectly()
        {
            var map = new DescriptorOwnershipMap();
            
            map.RegisterMapping(
                descriptorTypeId: 1,
                typeof(TestPosition),
                typeof(TestVelocity)
            );
            
            var components = map.GetComponentsForDescriptor(1);
            
            Assert.Equal(2, components.Length);
            Assert.Contains(typeof(TestPosition), components);
            Assert.Contains(typeof(TestVelocity), components);
        }
        
        [Fact]
        public void GetComponentsForDescriptor_UnknownId_ReturnsEmpty()
        {
            var map = new DescriptorOwnershipMap();
            
            var components = map.GetComponentsForDescriptor(999);
            
            Assert.Empty(components);
        }
        
        [Fact]
        public void GetDescriptorForComponent_ReverseLookup_ReturnsCorrectId()
        {
            var map = new DescriptorOwnershipMap();
            
            map.RegisterMapping(
                descriptorTypeId: 1,
                typeof(TestPosition)
            );
            
            var descriptorId = map.GetDescriptorForComponent(typeof(TestPosition));
            
            Assert.Equal(1, descriptorId);
        }
        
        [Fact]
        public void GetDescriptorForComponent_UnknownType_ReturnsZero()
        {
            var map = new DescriptorOwnershipMap();
            
            var descriptorId = map.GetDescriptorForComponent(typeof(TestPosition));
            
            Assert.Equal(0, descriptorId);
        }
        
        [Fact]
        public void RegisterMapping_DuplicateId_Overwrites()
        {
            // Note: The instruction implementation of RegisterMapping blindly adds to internal dict.
            // If the implementation keeps appending components, this test might need adjustment based on actual implementation.
            // Let's assume standard dictionary behavior or replacement.
            // Checking implementation via viewing source might be prudent, but I'll write the test as instructed first.
            
            var map = new DescriptorOwnershipMap();
            
            map.RegisterMapping(1, typeof(TestPosition));
            map.RegisterMapping(1, typeof(TestVelocity)); // Same ID
            
            var components = map.GetComponentsForDescriptor(1);
            
            // Should have only Velocity (overwritten)
            Assert.Single(components);
            Assert.Contains(typeof(TestVelocity), components);
        }
    }
}
