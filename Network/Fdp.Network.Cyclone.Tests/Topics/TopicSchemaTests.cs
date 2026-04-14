using System;
using System.Linq;
using System.Reflection;
using CycloneDDS.Schema;
using ModuleHost.Network.Cyclone.Topics;
using Xunit;

namespace ModuleHost.Network.Cyclone.Tests.Topics
{
    public class TopicSchemaTests
    {
        [Fact]
        public void NetworkAppId_Equality_Works()
        {
            var id1 = new NetworkAppId { AppDomainId = 1, AppInstanceId = 100 };
            var id2 = new NetworkAppId { AppDomainId = 1, AppInstanceId = 100 };
            var id3 = new NetworkAppId { AppDomainId = 1, AppInstanceId = 101 };

            // Test IEquatable<NetworkAppId>
            Assert.True(id1.Equals(id2));
            Assert.False(id1.Equals(id3));

            // Test operator overloads
            Assert.True(id1 == id2);
            Assert.False(id1 == id3);
            Assert.False(id1 != id2);
            Assert.True(id1 != id3);

            // Test GetHashCode consistency
            Assert.Equal(id1.GetHashCode(), id2.GetHashCode());
        }

        [Fact]
        public void NetworkAppId_HasDdsStructAttribute()
        {
            var type = typeof(NetworkAppId);
            var attr = type.GetCustomAttribute<DdsStructAttribute>();
            
            Assert.NotNull(attr);
        }

        [Fact]
        public void NetworkAppId_FieldsHaveCorrectDdsIds()
        {
            var type = typeof(NetworkAppId);
            var domainField = type.GetField("AppDomainId");
            var instanceField = type.GetField("AppInstanceId");

            Assert.NotNull(domainField);
            Assert.NotNull(instanceField);

            var domainAttr = domainField.GetCustomAttribute<DdsIdAttribute>();
            var instanceAttr = instanceField.GetCustomAttribute<DdsIdAttribute>();

            Assert.NotNull(domainAttr);
            Assert.NotNull(instanceAttr);
            Assert.Equal(0, domainAttr.Id);
            Assert.Equal(1, instanceAttr.Id);
        }

        [Fact]
        public void EntityMasterTopic_HasDdsTopicAttribute()
        {
            var type = typeof(EntityMasterTopic);
            var attr = type.GetCustomAttribute<DdsTopicAttribute>();
            
            Assert.NotNull(attr);
            Assert.Equal("SST_EntityMaster", attr.TopicName);
        }

        [Fact]
        public void EntityMasterTopic_HasCorrectQosSettings()
        {
            var type = typeof(EntityMasterTopic);
            var attr = type.GetCustomAttribute<DdsQosAttribute>();
            
            Assert.NotNull(attr);
            Assert.Equal(DdsReliability.Reliable, attr.Reliability);
            Assert.Equal(DdsDurability.TransientLocal, attr.Durability);
            Assert.Equal(DdsHistoryKind.KeepLast, attr.HistoryKind);
            Assert.Equal(100, attr.HistoryDepth);
        }

        [Fact]
        public void EntityMasterTopic_EntityIdIsKey()
        {
            var type = typeof(EntityMasterTopic);
            var field = type.GetField("EntityId");
            
            Assert.NotNull(field);
            
            var keyAttr = field.GetCustomAttribute<DdsKeyAttribute>();
            Assert.NotNull(keyAttr);
        }

        [Fact]
        public void EntityMasterTopic_FieldsHaveSequentialDdsIds()
        {
            var type = typeof(EntityMasterTopic);
            var fields = new[]
            {
                ("EntityId", 0),
                ("OwnerId", 1),
                ("DisTypeValue", 2),
                ("Flags", 3)
            };

            foreach (var (fieldName, expectedId) in fields)
            {
                var field = type.GetField(fieldName);
                Assert.NotNull(field);
                
                var attr = field.GetCustomAttribute<DdsIdAttribute>();
                Assert.NotNull(attr);
                Assert.Equal(expectedId, attr.Id);
            }
        }

        [Fact]
        public void EntityStateTopic_HasDdsTopicAttribute()
        {
            var type = typeof(EntityStateTopic);
            var attr = type.GetCustomAttribute<DdsTopicAttribute>();
            
            Assert.NotNull(attr);
            Assert.Equal("SST_EntityState", attr.TopicName);
        }

        [Fact]
        public void EntityStateTopic_UsesBestEffortQos()
        {
            var type = typeof(EntityStateTopic);
            var attr = type.GetCustomAttribute<DdsQosAttribute>();
            
            Assert.NotNull(attr);
            Assert.Equal(DdsReliability.BestEffort, attr.Reliability);
            Assert.Equal(DdsDurability.Volatile, attr.Durability);
        }

        [Fact]
        public void NetworkAffiliation_HasExpectedValues()
        {
            Assert.Equal(0, (int)NetworkAffiliation.Neutral);
            Assert.Equal(1, (int)NetworkAffiliation.Friend_Blue);
            Assert.Equal(2, (int)NetworkAffiliation.Hostile_Red);
            Assert.Equal(3, (int)NetworkAffiliation.Unknown);
        }

        [Fact]
        public void NetworkLifecycleState_HasExpectedValues()
        {
            Assert.Equal(0, (int)NetworkLifecycleState.Ghost);
            Assert.Equal(1, (int)NetworkLifecycleState.Constructing);
            Assert.Equal(2, (int)NetworkLifecycleState.Active);
            Assert.Equal(3, (int)NetworkLifecycleState.TearDown);
        }
    }
}
