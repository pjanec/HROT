using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ModuleHost.Network.Cyclone.Services;
using Xunit;

namespace ModuleHost.Network.Cyclone.Tests.Services
{
    public class TypeIdMapperTests
    {
        [Fact]
        public void GetCoreTypeId_NewDISType_ReturnsUniqueId()
        {
            // Arrange
            var mapper = new TypeIdMapper();
            ulong disType1 = 0x0101010100000001; // Example DIS entity type
            ulong disType2 = 0x0101010100000002;
            ulong disType3 = 0x0101010100000003;

            // Act
            int id1 = mapper.GetCoreTypeId(disType1);
            int id2 = mapper.GetCoreTypeId(disType2);
            int id3 = mapper.GetCoreTypeId(disType3);

            // Assert
            Assert.NotEqual(id1, id2);
            Assert.NotEqual(id1, id3);
            Assert.NotEqual(id2, id3);
            Assert.True(id1 > 0, "TypeId should be positive");
            Assert.True(id2 > 0, "TypeId should be positive");
            Assert.True(id3 > 0, "TypeId should be positive");
        }

        [Fact]
        public void GetCoreTypeId_SameDISType_ReturnsSameId()
        {
            // Arrange
            var mapper = new TypeIdMapper();
            ulong disType = 0x0101010100000001;

            // Act
            int firstCall = mapper.GetCoreTypeId(disType);
            int secondCall = mapper.GetCoreTypeId(disType);
            int thirdCall = mapper.GetCoreTypeId(disType);

            // Assert
            Assert.Equal(firstCall, secondCall);
            Assert.Equal(firstCall, thirdCall);
        }

        [Fact]
        public void BidirectionalMapping_Consistent()
        {
            // Arrange
            var mapper = new TypeIdMapper();
            var disTypes = new ulong[]
            {
                0x0101010100000001, // Tank
                0x0101010100000002, // Jeep
                0x0101010100000003, // Helicopter
                0x0101010100000004  // Ship
            };

            // Act & Assert
            foreach (var disType in disTypes)
            {
                int coreTypeId = mapper.GetCoreTypeId(disType);
                ulong roundTrip = mapper.GetDISType(coreTypeId);
                
                Assert.Equal(disType, roundTrip);
            }
        }

        [Fact]
        public void GetDISType_UnregisteredTypeId_ThrowsException()
        {
            // Arrange
            var mapper = new TypeIdMapper();

            // Act & Assert
            Assert.Throws<ArgumentException>(() => mapper.GetDISType(999));
        }

        [Fact]
        public void HasCoreTypeId_RegisteredType_ReturnsTrue()
        {
            // Arrange
            var mapper = new TypeIdMapper();
            ulong disType = 0x0101010100000001;
            int coreTypeId = mapper.GetCoreTypeId(disType);

            // Act
            bool hasType = mapper.HasCoreTypeId(coreTypeId);

            // Assert
            Assert.True(hasType);
        }

        [Fact]
        public void HasCoreTypeId_UnregisteredType_ReturnsFalse()
        {
            // Arrange
            var mapper = new TypeIdMapper();

            // Act
            bool hasType = mapper.HasCoreTypeId(999);

            // Assert
            Assert.False(hasType);
        }

        [Fact]
        public void ConcurrentAccess_ThreadSafe()
        {
            // Arrange
            var mapper = new TypeIdMapper();
            var disTypes = Enumerable.Range(0, 100).Select(i => (ulong)(0x0101010100000000 + i)).ToArray();
            var results = new int[disTypes.Length];

            // Act - Multiple threads accessing mapper concurrently
            Parallel.For(0, disTypes.Length, i =>
            {
                results[i] = mapper.GetCoreTypeId(disTypes[i]);
            });

            // Assert - All IDs should be unique
            var uniqueIds = new HashSet<int>(results);
            Assert.Equal(disTypes.Length, uniqueIds.Count);

            // Assert - Repeated calls should return same IDs
            Parallel.For(0, disTypes.Length, i =>
            {
                int secondCall = mapper.GetCoreTypeId(disTypes[i]);
                Assert.Equal(results[i], secondCall);
            });
        }

        [Fact]
        public void TypeIdSequence_StartsAt1_IncrementsSequentially()
        {
            // Arrange
            var mapper = new TypeIdMapper();

            // Act
            int id1 = mapper.GetCoreTypeId(0x0101010100000001);
            int id2 = mapper.GetCoreTypeId(0x0101010100000002);
            int id3 = mapper.GetCoreTypeId(0x0101010100000003);

            // Assert
            Assert.Equal(1, id1);
            Assert.Equal(2, id2);
            Assert.Equal(3, id3);
        }

        [Fact]
        public void MixedOperations_MaintainConsistency()
        {
            // Arrange
            var mapper = new TypeIdMapper();
            var mappings = new Dictionary<ulong, int>();

            // Act - Build up mappings
            for (int i = 0; i < 50; i++)
            {
                ulong disType = (ulong)(0x0101010100000000 + i);
                int coreId = mapper.GetCoreTypeId(disType);
                mappings[disType] = coreId;
            }

            // Assert - Verify all forward mappings
            foreach (var kvp in mappings)
            {
                int retrievedCoreId = mapper.GetCoreTypeId(kvp.Key);
                Assert.Equal(kvp.Value, retrievedCoreId);
            }

            // Assert - Verify all reverse mappings
            foreach (var kvp in mappings)
            {
                ulong retrievedDisType = mapper.GetDISType(kvp.Value);
                Assert.Equal(kvp.Key, retrievedDisType);
            }
        }
    }
}
