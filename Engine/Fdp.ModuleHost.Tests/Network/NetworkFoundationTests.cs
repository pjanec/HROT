using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Network;
using Fdp.ModuleHost.Network.Interfaces;
using Xunit;

namespace Fdp.ModuleHost.Tests.Network
{
    public class NetworkFoundationTests
    {
        // 3. Composite Key Packing Tests
        [Fact]
        public void PackKey_WithSimpleValues_ReturnsCorrectKey()
        {
            long typeId = 123;
            long instanceId = 456;
            long packed = OwnershipExtensions.PackKey(typeId, instanceId);
            
            // Expected: typeId in upper 32 bits, instanceId in lower 32 bits
            // 123l << 32 = 528280977408
            // 528280977408 | 456 = 528280977864
            long expected = 528280977864;
            
            Assert.Equal(expected, packed);
        }

        [Fact]
        public void UnpackKey_RoundTrip_RestoresOriginalValues()
        {
            long typeId = 98765;
            long instanceId = 12345;
            long packed = OwnershipExtensions.PackKey(typeId, instanceId);
            
            var (unpackedType, unpackedInstance) = OwnershipExtensions.UnpackKey(packed);
            
            Assert.Equal(typeId, unpackedType);
            Assert.Equal(instanceId, unpackedInstance);
        }

        [Fact]
        public void PackKey_WithMaxValues_RoundTripsCorrectly()
        {
            long typeId = 2147483647; 
            long instanceId = 4294967295;

            long packed = OwnershipExtensions.PackKey(typeId, instanceId);
            var (unpackedType, unpackedInstance) = OwnershipExtensions.UnpackKey(packed);
            
            Assert.Equal(typeId, unpackedType);
            Assert.Equal(instanceId, unpackedInstance);
        }

        [Fact]
        public void PackKey_WithZeroValues_ReturnsZero()
        {
            long packed = OwnershipExtensions.PackKey(0, 0);
            Assert.Equal(0, packed);
        }

        // 4. DescriptorAuthorityChanged Event Tests
        [Fact]
        public void DescriptorAuthorityChanged_Construction_StoresValues()
        {
            var evt = new DescriptorAuthorityChanged
            {
                Entity = new Entity(100, 1),
                DescriptorTypeId = 5,
                IsNowOwner = true,
                NewOwnerId = 10
            };
            
            Assert.Equal(100, evt.Entity.Index);
            Assert.Equal(1, evt.Entity.Generation);
            Assert.Equal(5, evt.DescriptorTypeId);
            Assert.True(evt.IsNowOwner);
            Assert.Equal(10, evt.NewOwnerId);
        }
        
        [Fact]
        public void DescriptorAuthorityChanged_DefaultIsInvalid()
        {
            var evt = new DescriptorAuthorityChanged();
            Assert.Equal(Entity.Null, evt.Entity);
            Assert.False(evt.IsNowOwner);
            Assert.Equal(0, evt.DescriptorTypeId);
        }

        // 5. Interface Mock Tests
        private class MockStrategy : IOwnershipDistributionStrategy
        {
            public int? ReturnValue { get; set; }
            public int? GetInitialOwner(long descriptorTypeId, DISEntityType entityType, int masterNodeId, long instanceId)
            {
                return ReturnValue;
            }
        }



        // 7. DISEntityType Tests
        [Fact]
        public void DISEntityType_Equality_WorksCorrectly()
        {
            var t1 = new DISEntityType { Kind = 1, Domain = 2, Country = 3 };
            var t2 = new DISEntityType { Kind = 1, Domain = 2, Country = 3 };
            var t3 = new DISEntityType { Kind = 1, Domain = 2, Country = 4 };
            
            // Default struct equality (ValueType.Equals) usually matches fields
            Assert.Equal(t1, t2);
            Assert.NotEqual(t1, t3);
        }

        [Fact]
        public void DISEntityType_HashCode_IsConsistent()
        {
            var t1 = new DISEntityType { Kind = 1, Domain = 2 };
            var t2 = new DISEntityType { Kind = 1, Domain = 2 };
            
            Assert.Equal(t1.GetHashCode(), t2.GetHashCode());
        }

    }
}
