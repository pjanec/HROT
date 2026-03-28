using System.Reflection;
using Bagira.BDC.SSTD.Orchestration;
using CycloneDDS.Schema;
using Xunit;

namespace Bagira.DDS.DataModel.Tests
{
    public class OrchestrationSchemaTests
    {
        [Fact]
        public void AllTopicStructsHaveDdsTopicAttribute()
        {
            // Reflect over ALL partial struct types in Bagira.BDC.SSTD.Orchestration
            // so that any newly added topic structs are automatically covered.
            // Code-gen suffixes produced by CycloneDDS for internal plumbing types.
            // Topic structs declared by hand never have these suffixes.
            static bool IsCodeGenType(Type t) =>
                t.Name.EndsWith("_Native", StringComparison.Ordinal) ||
                t.Name.EndsWith("View", StringComparison.Ordinal) ||
                t.Name.EndsWith("KeyHolder", StringComparison.Ordinal);

            var orchestrationAssembly = typeof(SystemStateTopic).Assembly;
            var topicStructs = orchestrationAssembly.GetTypes()
                .Where(t => t.IsValueType && !t.IsEnum && t.IsPublic
                    && !IsCodeGenType(t)
                    && t.Namespace == "Bagira.BDC.SSTD.Orchestration")
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
                Assert.Equal("bdc-sst-orchestration", idlAttr!.FileName);
            }
        }

        [Fact]
        public void DsmStateEnumHasExpectedValues()
        {
            Assert.Equal(0, (int)DSMState.Standby);
            Assert.Equal(31, (int)DSMState.RunningLive);
            Assert.Equal(99, (int)DSMState.Degraded);
        }

        [Fact]
        public void NodeHeartbeatHasDdsKeyOnNodeId()
        {
            var f = typeof(NodeHeartbeat).GetField(nameof(NodeHeartbeat.NodeId));
            Assert.NotNull(f);
            Assert.NotNull(f!.GetCustomAttribute<DdsKeyAttribute>());
        }

        [Fact]
        public void SystemStateTopicQosIsDurableTransientLocal()
        {
            var qos = typeof(SystemStateTopic).GetCustomAttribute<DdsQosAttribute>();
            Assert.NotNull(qos);
            Assert.Equal(DdsDurability.TransientLocal, qos!.Durability);
            Assert.Equal(1, qos.HistoryDepth);
        }
    }
}
