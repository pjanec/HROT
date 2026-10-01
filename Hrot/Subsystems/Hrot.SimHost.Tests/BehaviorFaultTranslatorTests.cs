using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using Hrot.Network.Translators;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Replication.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-484</c> — a behaviour fault reaches the operator: <see cref="BehaviorFaultEgressTranslator"/> (the node that
    /// raised it) → <see cref="BehaviorFaultReport"/> → <see cref="BehaviorFaultIngressTranslator"/> (every node) → a row of
    /// <see cref="BehaviorFaultLog"/>. 📄 <c>docs/blueprints/DESIGN_Behaviour_Fault_And_Teardown.md</c> §4c, acceptance ③.
    /// </summary>
    public sealed class BehaviorFaultTranslatorTests : IDisposable
    {
        private sealed class CapturingWriter<T> : IDdsWriter<T>
        {
            public List<T> Written { get; } = new();
            public void Write(T sample) => Written.Add(sample);
            public void DisposeInstance(T key) { }
        }

        private readonly EntityRepository _world = new();
        private readonly NetworkEntityMap _entityMap = new();

        public BehaviorFaultTranslatorTests() => _world.RegisterManagedEvent<BehaviorFaultNotification>();

        public void Dispose() => _world.Dispose();

        private Entity Spawn(long netId)
        {
            var e = _world.CreateEntity();
            _entityMap.Register(netId, e);
            return e;
        }

        private void Publish(Entity e, int hash = 0x1234, uint run = 2, BehaviorFaultCode code = BehaviorFaultCode.MissingInput)
        {
            _world.Bus.PublishManaged(new BehaviorFaultNotification
            {
                Entity = e, BehaviorHash = hash, InstanceId = run, Code = code, Message = "no area", SimTime = 12.5,
            });
            _world.Bus.SwapBuffers();
        }

        [Fact]
        public void Egress_WritesOneReport_WithNetIdNameAndOrigin()
        {
            var registry = new BehaviorRegistry();
            registry.Register("PlatoonHillAttack", new BehaviorDefinition { Name = "PlatoonHillAttack", BrainTier = BehaviorConstants.BrainTierBTree });
            Assert.True(registry.TryGetId("PlatoonHillAttack", out int hash));
            var writer = new CapturingWriter<BehaviorFaultReport>();
            var egress = new BehaviorFaultEgressTranslator(writer, _entityMap, registry, localNodeId: 3);
            var e = Spawn(1006);

            Publish(e, hash);
            egress.ScanAndPublish(_world);

            var r = Assert.Single(writer.Written);
            Assert.Equal(1006, r.EntityId);
            Assert.Equal(3, r.OriginNodeId);
            Assert.Equal("PlatoonHillAttack", r.BehaviorName);
            Assert.Equal(hash, r.BehaviorHash);
            Assert.Equal(2u, r.InstanceId);
            Assert.Equal((int)BehaviorFaultCode.MissingInput, r.Code);
            Assert.Equal("no area", r.Message);
            Assert.Equal(1, egress.SentSampleCount);
        }

        [Fact]
        public void Egress_EntityWithoutNetworkId_IsNotSent()
        {
            var writer = new CapturingWriter<BehaviorFaultReport>();
            var egress = new BehaviorFaultEgressTranslator(writer, _entityMap, null, localNodeId: 3);

            Publish(_world.CreateEntity());
            egress.ScanAndPublish(_world);

            Assert.Empty(writer.Written);
        }

        [Fact]
        public void Ingress_AddsOneErrorRow_DropsDuplicates_AndItsOwnSamples()
        {
            var log = new BehaviorFaultLog();
            var ingress = new BehaviorFaultIngressTranslator(participant: null, localNodeId: 1, log);
            var report = new BehaviorFaultReport
            {
                EntityId = 1006, OriginNodeId = 2, BehaviorName = "PlatoonHillAttack", BehaviorHash = 7, InstanceId = 2,
                Code = (int)BehaviorFaultCode.NoAnswerTimeout, Message = "no EQS answer in 5 s",
            };

            Assert.True(ingress.ProcessSample(in report));
            Assert.False(ingress.ProcessSample(in report));                      // the same fault again (another node)
            var own = report with { OriginNodeId = 1, InstanceId = 3 };
            Assert.False(ingress.ProcessSample(in own));                         // our own sample — Raise reported it

            var row = Assert.Single(log.GetMessages());
            Assert.Equal(LogSeverity.Error, row.Severity);
            Assert.Contains("entity 1006", row.Message);
            Assert.Contains("PlatoonHillAttack", row.Message);
            Assert.Contains("NoAnswerTimeout", row.Message);
            Assert.Contains("no EQS answer in 5 s", row.Message);
        }

        [Fact]
        public void Ingress_NextRunOfTheSameBehaviour_IsANewRow()
        {
            var log = new BehaviorFaultLog();
            var ingress = new BehaviorFaultIngressTranslator(participant: null, localNodeId: 1, log);
            var first = new BehaviorFaultReport { EntityId = 5, OriginNodeId = 2, BehaviorHash = 7, InstanceId = 2, Message = "x" };

            Assert.True(ingress.ProcessSample(in first));
            var next = first with { InstanceId = 3 };
            Assert.True(ingress.ProcessSample(in next));
            Assert.Equal(2, log.GetMessages().Count);
        }
    }
}
