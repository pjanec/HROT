using System.Collections.Generic;
using System.Linq;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Map.Common.Dds;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.Network.Translators;
using Hrot.Map.Common.Translators;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ rules S2 ⑧ (📄 docs/DESIGN_Scenario_Rules.md §00.9 S2-f, SR-121) — <c>dtPlayerMessage</c> (173), the <c>SquadReport</c> EVENT shape: a message raised on one node is written ONCE
    /// by its egress, raised again on every OTHER node (<c>OriginNodeId</c> skips a node's own), every field intact; one that ARRIVED is never sent again.
    /// </summary>
    [Collection("SimHostDds")]
    public sealed class PlayerMessageWireTests
    {
        private sealed class CapturingWriter : IDdsWriter<PlayerMessage>
        {
            public List<PlayerMessage> Written { get; } = new();
            public void Write(PlayerMessage sample) => Written.Add(sample);
            public void DisposeInstance(PlayerMessage key) { }
        }

        private static PlayerMessageEvent Sent() => new()
        {
            MessageId = 7, ToKind = "Unit", ToValue = 412, Text = "Blue-2, hold at the bridge", Seconds = 9.5f, Priority = 3, SentAt = 123.25, Source = 80,
        };

        private static EntityRepository Node() { var w = new EntityRepository(); w.Bus.RegisterManaged<PlayerMessageEvent>(); return w; }

        /// <summary>Every field survives sender → egress → ingress → the other node's event; the origin is the sender's node. ✅ Red-proof: any field dropped in <c>ToWire</c> or the ingress ⇒ red.</summary>
        [Fact]
        public void EveryField_RoundTrips_AndTheOriginIsTheSendersNode()
        {
            using var sender = Node(); using var receiver = Node();
            sender.Bus.PublishManaged(Sent()); sender.Bus.SwapBuffers();
            var wire = new CapturingWriter();
            new PlayerMessageEgressTranslator(wire, localNodeId: 1).ScanAndPublish(sender);
            var m = Assert.Single(wire.Written);
            Assert.Equal(1, m.OriginNodeId);

            var ingress = new PlayerMessageIngressTranslator(participant: null, localNodeId: 2);
            Assert.True(ingress.ProcessSample(in m, receiver));
            receiver.Bus.SwapBuffers();
            var got = Assert.Single(((Fdp.ModuleHost.Abstractions.ISimulationView)receiver).ReadManagedEvents<PlayerMessageEvent>());
            Assert.Equal((7, 1, "Unit", 412, "Blue-2, hold at the bridge"), (got.MessageId, got.OriginNodeId, got.ToKind, got.ToValue, got.Text));
            Assert.Equal((9.5f, (byte)3, 123.25, 80), (got.Seconds, got.Priority, got.SentAt, got.Source));
        }

        /// <summary>A node skips its OWN sample, and a message that ARRIVED (OriginNodeId ≠ 0) is not sent again by the egress. ✅ Red-proof: the own-node skip removed ⇒ the sender raises it twice ⇒ red.</summary>
        [Fact]
        public void ANodeSkipsItsOwnSample_AndAnArrivedMessageIsNeverResent()
        {
            using var sender = Node();
            sender.Bus.PublishManaged(Sent()); sender.Bus.SwapBuffers();
            var wire = new CapturingWriter();
            new PlayerMessageEgressTranslator(wire, 1).ScanAndPublish(sender);
            var m = wire.Written.Single();
            Assert.False(new PlayerMessageIngressTranslator(null, localNodeId: 1).ProcessSample(in m, sender));   // its own

            using var relay = Node();
            Assert.True(new PlayerMessageIngressTranslator(null, 2).ProcessSample(in m, relay)); relay.Bus.SwapBuffers();
            var again = new CapturingWriter();
            new PlayerMessageEgressTranslator(again, 2).ScanAndPublish(relay);
            Assert.Empty(again.Written);                                          // raised with OriginNodeId 1: the egress leaves it
        }

        /// <summary>The shared pack carries the pair once. ✅ Red-proof: either registration removed ⇒ red.</summary>
        [Fact]
        public void TheSharedPack_CarriesThePair_OnEveryNode()
        {
            using var participant = new DdsParticipant(190u);
            var entityMap = new NetworkEntityMap();
            var pack = SharedTranslatorPack.Create(participant, entityMap, localNodeId: 1, new FdpEventBus(),
                new Fdp.Toolkit.Replication.Systems.GhostCreationSystem(entityMap)).ToList();
            var egress = Assert.Single(pack.OfType<PlayerMessageEgressTranslator>());
            Assert.Single(pack.OfType<PlayerMessageIngressTranslator>());
            Assert.Equal((long)EDescriptorType.dtPlayerMessage, egress.DescriptorOrdinal);
            Assert.Equal(173, (int)EDescriptorType.dtPlayerMessage);
        }
    }
}
