using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ModuleHost.Network.Cyclone.Services;
using ModuleHost.Network.Cyclone.Topics;
using Xunit;

namespace ModuleHost.Network.Cyclone.Tests.Services
{
    public class NodeIdMapperTests
    {
        [Fact]
        public void LocalNode_AlwaysHasId1()
        {
            // Arrange & Act
            var mapper = new NodeIdMapper(localDomain: 10, localInstance: 100);
            var localId = new NetworkAppId { AppDomainId = 10, AppInstanceId = 100 };
            
            // Assert
            int internalId = mapper.GetOrRegisterInternalId(localId);
            Assert.Equal(1, internalId);
            
            // Verify bidirectional mapping
            var retrievedExternal = mapper.GetExternalId(1);
            Assert.Equal(localId, retrievedExternal);
        }

        [Fact]
        public void NewExternalId_GetsUniqueInternalId()
        {
            // Arrange
            var mapper = new NodeIdMapper(localDomain: 10, localInstance: 100);
            var external1 = new NetworkAppId { AppDomainId = 20, AppInstanceId = 200 };
            var external2 = new NetworkAppId { AppDomainId = 30, AppInstanceId = 300 };
            var external3 = new NetworkAppId { AppDomainId = 40, AppInstanceId = 400 };

            // Act
            int id1 = mapper.GetOrRegisterInternalId(external1);
            int id2 = mapper.GetOrRegisterInternalId(external2);
            int id3 = mapper.GetOrRegisterInternalId(external3);

            // Assert
            Assert.NotEqual(id1, id2);
            Assert.NotEqual(id1, id3);
            Assert.NotEqual(id2, id3);
            Assert.True(id1 > 1); // Should not use ID 1 (reserved for local)
            Assert.True(id2 > 1);
            Assert.True(id3 > 1);
        }

        [Fact]
        public void Bidirectional_Mapping_Consistent()
        {
            // Arrange
            var mapper = new NodeIdMapper(localDomain: 10, localInstance: 100);
            var externalIds = new[]
            {
                new NetworkAppId { AppDomainId = 20, AppInstanceId = 200 },
                new NetworkAppId { AppDomainId = 30, AppInstanceId = 300 },
                new NetworkAppId { AppDomainId = 40, AppInstanceId = 400 }
            };

            // Act & Assert
            foreach (var externalId in externalIds)
            {
                int internalId = mapper.GetOrRegisterInternalId(externalId);
                var roundTrip = mapper.GetExternalId(internalId);
                
                Assert.Equal(externalId, roundTrip);
            }
        }

        [Fact]
        public void GetOrRegisterInternalId_ReturnsExistingId_WhenCalledTwice()
        {
            // Arrange
            var mapper = new NodeIdMapper(localDomain: 10, localInstance: 100);
            var externalId = new NetworkAppId { AppDomainId = 20, AppInstanceId = 200 };

            // Act
            int firstCall = mapper.GetOrRegisterInternalId(externalId);
            int secondCall = mapper.GetOrRegisterInternalId(externalId);

            // Assert
            Assert.Equal(firstCall, secondCall);
        }

        [Fact]
        public void GetExternalId_ThrowsException_ForUnregisteredId()
        {
            // Arrange
            var mapper = new NodeIdMapper(localDomain: 10, localInstance: 100);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => mapper.GetExternalId(999));
        }

        [Fact]
        public void ConcurrentAccess_ThreadSafe()
        {
            // Arrange
            var mapper = new NodeIdMapper(localDomain: 10, localInstance: 100);
            var externalIds = Enumerable.Range(0, 100)
                .Select(i => new NetworkAppId { AppDomainId = i, AppInstanceId = i * 10 })
                .ToList();

            var internalIds = new int[externalIds.Count];

            // Act - Register IDs concurrently
            Parallel.For(0, externalIds.Count, i =>
            {
                internalIds[i] = mapper.GetOrRegisterInternalId(externalIds[i]);
            });

            // Assert - All IDs should be unique
            var uniqueIds = new HashSet<int>(internalIds);
            Assert.Equal(externalIds.Count, uniqueIds.Count);

            // Assert - Bidirectional consistency
            Parallel.ForEach(externalIds, externalId =>
            {
                int internalId = mapper.GetOrRegisterInternalId(externalId);
                var roundTrip = mapper.GetExternalId(internalId);
                Assert.Equal(externalId, roundTrip);
            });
        }

        [Fact]
        public void HasInternalId_ReturnsTrueForRegisteredIds()
        {
            // Arrange
            var mapper = new NodeIdMapper(localDomain: 10, localInstance: 100);
            var externalId = new NetworkAppId { AppDomainId = 20, AppInstanceId = 200 };

            // Act
            int internalId = mapper.GetOrRegisterInternalId(externalId);

            // Assert
            Assert.True(mapper.HasInternalId(internalId));
            Assert.True(mapper.HasInternalId(1)); // Local node
            Assert.False(mapper.HasInternalId(999)); // Unregistered
        }
    }
}
