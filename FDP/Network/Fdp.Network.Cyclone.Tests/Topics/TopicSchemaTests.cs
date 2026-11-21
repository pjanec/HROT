using System;
using System.Linq;
using System.Reflection;
using CycloneDDS.Schema;
using Fdp.Network.Cyclone.Topics;
using Xunit;

namespace Fdp.Network.Cyclone.Tests.Topics
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

        /// <summary>
        /// CE-4079 ⑤ — an appendable type matches its fields by POSITION, so the order the fields are declared in IS the wire layout; no
        /// <c>[DdsId]</c> is written (it only repeated 0, 1, 2… in this very order). New fields are appended, never inserted.
        /// </summary>
        [Fact]
        public void NetworkAppId_FieldsAreDeclaredInWireOrder_WithNoExplicitIds()
        {
            var fields = typeof(NetworkAppId).GetFields(BindingFlags.Public | BindingFlags.Instance);

            Assert.Equal(new[] { "AppDomainId", "AppInstanceId" }, fields.Select(f => f.Name).ToArray());
            Assert.All(fields, f => Assert.Null(f.GetCustomAttribute<DdsIdAttribute>()));
        }

        [Fact]
        public void EntityMasterTopic_HasDdsTopicAttribute()
        {
            var type = typeof(EntityMasterTopic);
            var attr = type.GetCustomAttribute<DdsTopicAttribute>();
            
            Assert.NotNull(attr);
            Assert.Null(attr.TopicName);   // AQ107 A — never written: derived from the type
            Assert.Equal("Fdp_Network_Cyclone_Topics_EntityMasterTopic", DdsTopicName.Of<EntityMasterTopic>());
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

        /// <summary>CE-4079 ⑤ — the wire layout of the library's demo master is its declaration order (an appendable type; no explicit ids).</summary>
        [Fact]
        public void EntityMasterTopic_FieldsAreDeclaredInWireOrder_WithNoExplicitIds()
        {
            var fields = typeof(EntityMasterTopic).GetFields(BindingFlags.Public | BindingFlags.Instance);

            Assert.Equal(new[] { "EntityId", "OwnerId", "DisTypeValue", "Flags", "TkbTypeValue" }, fields.Select(f => f.Name).ToArray());
            Assert.All(fields, f => Assert.Null(f.GetCustomAttribute<DdsIdAttribute>()));
        }

        [Fact]
        public void EntityStateTopic_HasDdsTopicAttribute()
        {
            var type = typeof(EntityStateTopic);
            var attr = type.GetCustomAttribute<DdsTopicAttribute>();
            
            Assert.NotNull(attr);
            Assert.Null(attr.TopicName);   // AQ107 A — never written: derived from the type
            Assert.Equal("Fdp_Network_Cyclone_Topics_EntityStateTopic", DdsTopicName.Of<EntityStateTopic>());
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
