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
            Type[] topics =
            [
                typeof(SystemStateTopic),
                typeof(SysOpRequest),
                typeof(SysOpStatus),
                typeof(NodeOpCommand),
                typeof(NodeOpStatus),
                typeof(NodeHeartbeat),
                typeof(OrchestratorContextTopic),
            ];
            foreach (var t in topics)
            {
                Assert.NotNull(t.GetCustomAttribute<DdsTopicAttribute>());
                var idl = t.GetCustomAttribute<DdsIdlFileAttribute>();
                Assert.NotNull(idl);
                Assert.Equal("bdc-sst-orchestration", idl!.FileName);
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
