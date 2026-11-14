using System.Collections.Generic;
using System.Linq;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Dds;
using Hrot.NED.Messages;
using Hrot.Network.NED.SimHost;
using Hrot.Map.Common.Translators;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-3364</c> D5 (📄 docs/DESIGN_Man_Downed_Event.md §3) — <c>dtManDowned</c> (175), the <c>EntityHitDamage</c> event shape: a <see cref="ManDownedEvent"/> raised on THIS node is
    /// written once; one that ARRIVED is raised again with <c>IsRemote</c> and never sent again; a node drops its own sample.
    /// </summary>
    [Collection("SimHostDds")]
    public sealed class ManDownedWireTests
    {
        private sealed class Wire : IDdsWriter<EntityManDowned>
        {
            public List<EntityManDowned> Written { get; } = new();
            public void Write(EntityManDowned sample) => Written.Add(sample);
            public void DisposeInstance(EntityManDowned key) { }
        }

        private static EntityRepository Node() { var w = new EntityRepository(); w.RegisterEvent<ManDownedEvent>(); return w; }

        private static ManDownedEvent[] Raise(ManDownedIngressTranslator ingress, EntityRepository w, in EntityManDowned m, out bool raised)
        {
            var cmd = (EntityCommandBuffer)((ISimulationView)w).GetCommandBuffer();
            raised = ingress.ProcessSample(in m, cmd);
            cmd.Playback(w); w.Bus.SwapBuffers();
            return w.Bus.Read<ManDownedEvent>().ToArray();
        }

        /// <summary>Every field survives sender → egress → ingress → the other node's event, the entities resolved to ITS ids, the body part as id + 1 on the wire, the downing marked remote.
        /// ✅ Red-proof: any field dropped in the egress or the ingress ⇒ red.</summary>
        [Fact]
        public void EveryField_RoundTrips_WithTheEntitiesResolved_AndMarkedRemote()
        {
            using var sender = Node(); using var receiver = Node();
            var sMap = new NetworkEntityMap(); var rMap = new NetworkEntityMap();
            Entity Pair(int net, out Entity r) { var s = sender.CreateEntity(); sMap.Register(net, s); r = receiver.CreateEntity(); rMap.Register(net, r); return s; }
            var victim = Pair(10, out var rv); var src = Pair(11, out var rs); var orig = Pair(12, out var ro);
            sender.Bus.Publish(new ManDownedEvent { Victim = victim, Source = src, OriginalSource = orig, State = InjuryState.Incapacitated, HasBodyPart = 1, BodyPart = 0, VictimSide = ForceId.Hostile });
            sender.Bus.SwapBuffers();
            var wire = new Wire();
            new ManDownedEgressTranslator(wire, sMap, localNodeId: 1).ScanAndPublish(sender);
            var m = Assert.Single(wire.Written);
            Assert.Equal((10, 11, 12, (byte)InjuryState.Incapacitated, (byte)1, (byte)ForceId.Hostile, 1), (m.VictimEntityId, m.SourceEntityId, m.OriginalSourceEntityId, m.State, m.BodyPart, m.VictimSide, m.SourceNodeId));

            var got = Assert.Single(Raise(new ManDownedIngressTranslator(null, rMap, localNodeId: 2), receiver, in m, out var raised));
            Assert.True(raised);
            Assert.Equal((rv, rs, ro, InjuryState.Incapacitated, (byte)1, (byte)0, ForceId.Hostile, true), (got.Victim, got.Source, got.OriginalSource, got.State, got.HasBodyPart, got.BodyPart, got.VictimSide, got.IsRemote));
        }

        /// <summary>A downing with no part reads 0 on the wire and no part back; an unknown source / original read Null; an unmapped victim is not sent. ✅ Red-proof: the part always written as id + 1 ⇒ the no-part case reads part 0 ⇒ red.</summary>
        [Fact]
        public void NoPart_UnknownSource_AndAnUnmappedVictim()
        {
            using var sender = Node(); using var receiver = Node();
            var sMap = new NetworkEntityMap(); var rMap = new NetworkEntityMap();
            var v = sender.CreateEntity(); sMap.Register(10, v); var rv = receiver.CreateEntity(); rMap.Register(10, rv);
            var stranger = sender.CreateEntity();                                  // never mapped
            sender.Bus.Publish(new ManDownedEvent { Victim = v, Source = stranger, State = InjuryState.Dead });
            sender.Bus.Publish(new ManDownedEvent { Victim = stranger, State = InjuryState.Dead });   // an unmapped victim
            sender.Bus.SwapBuffers();
            var wire = new Wire();
            new ManDownedEgressTranslator(wire, sMap, 1).ScanAndPublish(sender);
            var m = Assert.Single(wire.Written);
            Assert.Equal(((byte)0, 0, 0), (m.BodyPart, m.SourceEntityId, m.OriginalSourceEntityId));
            var got = Assert.Single(Raise(new ManDownedIngressTranslator(null, rMap, 2), receiver, in m, out _));
            Assert.Equal((rv, Entity.Null, Entity.Null, (byte)0), (got.Victim, got.Source, got.OriginalSource, got.HasBodyPart));
        }

        /// <summary>⭐ No double counting. The node that applied the Health NEVER raises its own sample again (DDS hands a writer its own samples), a node that is not that one raises it ONCE,
        /// and what ARRIVED (<c>IsRemote</c>) is never sent again; a victim this node does not know raises nothing. ✅ Red-proofs: the own-node skip removed ⇒ the sender raises it
        /// twice ⇒ red; the egress's <c>IsRemote</c> skip removed ⇒ a relay re-sends ⇒ red.</summary>
        [Fact]
        public void ANodeDropsItsOwnSample_AnArrivedDowningIsNeverResent_AnUnknownVictimIsDropped()
        {
            using var sender = Node(); var sMap = new NetworkEntityMap();
            var v = sender.CreateEntity(); sMap.Register(10, v);
            sender.Bus.Publish(new ManDownedEvent { Victim = v, State = InjuryState.Dead });
            sender.Bus.SwapBuffers();
            var wire = new Wire();
            new ManDownedEgressTranslator(wire, sMap, 1).ScanAndPublish(sender);
            var m = wire.Written.Single();
            Assert.Empty(Raise(new ManDownedIngressTranslator(null, sMap, localNodeId: 1), sender, in m, out var raised));   // its own
            Assert.False(raised);

            using var relay = Node(); var rMap = new NetworkEntityMap(); rMap.Register(10, relay.CreateEntity());
            Assert.Single(Raise(new ManDownedIngressTranslator(null, rMap, 2), relay, in m, out _));
            var again = new Wire();
            new ManDownedEgressTranslator(again, rMap, 2).ScanAndPublish(relay);   // the swap that made it readable happened in Raise; the event is IsRemote
            Assert.Empty(again.Written);

            using var blind = Node();
            Assert.Empty(Raise(new ManDownedIngressTranslator(null, new NetworkEntityMap(), 3), blind, in m, out raised));
            Assert.False(raised);
        }

        /// <summary>The shared pack carries the pair on every node. ✅ Red-proof: either registration removed ⇒ red.</summary>
        [Fact]
        public void TheSharedPack_CarriesThePair_OnEveryNode()
        {
            using var participant = new DdsParticipant(191u);
            var entityMap = new NetworkEntityMap();
            var pack = SharedTranslatorPack.Create(participant, entityMap, localNodeId: 1, new FdpEventBus(),
                new Fdp.Toolkit.Replication.Systems.GhostCreationSystem(entityMap)).ToList();
            var egress = Assert.Single(pack.OfType<ManDownedEgressTranslator>());
            Assert.Single(pack.OfType<ManDownedIngressTranslator>());
            Assert.Equal((long)Hrot.NED.Descriptors.EDescriptorType.dtManDowned, egress.DescriptorOrdinal);
            Assert.Equal(175, (int)Hrot.NED.Descriptors.EDescriptorType.dtManDowned);
        }
    }
}
