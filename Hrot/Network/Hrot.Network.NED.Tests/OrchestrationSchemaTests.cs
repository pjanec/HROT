using System.Reflection;
using Hrot.NED.Descriptors.Orchestration;
using CycloneDDS.Schema;
using Xunit;

namespace Hrot.DDS.DataModel.Tests
{
    public class OrchestrationSchemaTests
    {
        [Fact]
        public void AllTopicStructsHaveDdsTopicAttribute()
        {
            // Reflect over ALL partial struct types in Hrot.NED.Descriptors.Orchestration
            // so that any newly added topic structs are automatically covered.
            // Code-gen suffixes produced by CycloneDDS for internal plumbing types.
            // Topic structs declared by hand never have these suffixes.
            static bool IsCodeGenType(Type t) =>
                t.Name.EndsWith("_Native", StringComparison.Ordinal) ||
                t.Name.EndsWith("View", StringComparison.Ordinal) ||
                t.Name.EndsWith("KeyHolder", StringComparison.Ordinal);

            var orchestrationAssembly = typeof(ClusterStateTopic).Assembly;
            var topicStructs = orchestrationAssembly.GetTypes()
                .Where(t => t.IsValueType && !t.IsEnum && t.IsPublic
                    && !IsCodeGenType(t)
                    && t.Namespace == "Hrot.NED.Descriptors.Orchestration")
                .ToList();

            Assert.NotEmpty(topicStructs);

            foreach (var t in topicStructs)
            {
                var topicAttr = t.GetCustomAttribute<DdsTopicAttribute>();
                Assert.True(topicAttr != null,
                    $"Type {t.Name} is missing [DdsTopic] attribute.");

                var idlAttr = t.GetCustomAttribute<DdsIdlFileAttribute>();
                Assert.True(idlAttr != null,
                    $"Type {t.Name} is missing [DdsIdlFile] attribute.");
                Assert.Equal("hrot-orchestration", idlAttr!.FileName);
            }
        }

        [Fact]
        public void DsmStateEnumHasExpectedValues()
        {
            Assert.Equal(0, (int)ClusterState.Idle);
            Assert.Equal(31, (int)ClusterState.OperatingLive);
            Assert.Equal(99, (int)ClusterState.Degraded);
        }

        [Fact]
        public void SysOpType_StepTime_Is14()
        {
            Assert.Equal(14, (int)ClusterOpType.StepTime);
        }

        [Fact]
        public void SysOpType_SetTimeScale_Is15()
        {
            Assert.Equal(15, (int)ClusterOpType.SetTimeScale);
        }

        [Fact]
        public void NodeHeartbeatHasDdsKeyOnNodeId()
        {
            var f = typeof(NodeHeartbeat).GetField(nameof(NodeHeartbeat.NodeId));
            Assert.NotNull(f);
            Assert.NotNull(f!.GetCustomAttribute<DdsKeyAttribute>());
        }

        [Fact]
        public void ClusterStateTopicQosIsDurableTransientLocal()
        {
            var qos = typeof(ClusterStateTopic).GetCustomAttribute<DdsQosAttribute>();
            Assert.NotNull(qos);
            Assert.Equal(DdsDurability.TransientLocal, qos!.Durability);
            Assert.Equal(1, qos.HistoryDepth);
        }

        /// <summary>
        /// CGF1-S0506: AssetInventoryTopic must be codegen-registered in the orchestration IDL assembly.
        /// </summary>
        [Fact]
        public void AssetInventoryTopic_IsRegisteredInIdl()
        {
            var types = typeof(AssetInventoryTopic).Assembly.GetTypes()
                .Where(t => t.Namespace == "Hrot.NED.Descriptors.Orchestration")
                .Select(t => t.Name)
                .ToArray();
            Assert.Contains("AssetInventoryTopic", types);
        }

        /// <summary>
        /// CGF1-S0506: AssetInventoryTopic must be TransientLocal with KeepLast/depth=1.
        /// </summary>
        [Fact]
        public void AssetInventoryTopicQos_IsTransientLocalKeepLast1()
        {
            var qos = typeof(AssetInventoryTopic).GetCustomAttribute<DdsQosAttribute>();
            Assert.NotNull(qos);
            Assert.Equal(DdsDurability.TransientLocal, qos!.Durability);
            Assert.Equal(DdsHistoryKind.KeepLast, qos.HistoryKind);
            Assert.Equal(1, qos.HistoryDepth);
        }
    }
}
