using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using Hrot.Network.NED.SimHost;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Systems;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-3246</c> (docs/designs/brain-split/BS-1-DESIGN.md §6.4a) — a hit reaches the node that owns the victim's <c>Health</c> from
    /// ANY node, and is applied exactly once. Two nodes, each with the real egress, ingress and <see cref="HealthApplicationSystem"/>; the
    /// wire is the egress's written samples handed to BOTH ingresses (DDS delivers a node's own sample back to it).
    /// </summary>
    public sealed class HitDamageAcrossNodesTests : IDisposable
    {
        private sealed class Wire : IDdsWriter<EntityHitDamage>
        {
            public List<EntityHitDamage> Written { get; } = new();
            public void Write(EntityHitDamage sample) => Written.Add(sample);
            public void DisposeInstance(EntityHitDamage key) { }
        }

        private sealed class MdWire : IDdsWriter<EntityManDowned>
        {
            public List<EntityManDowned> Written { get; } = new();
            public void Write(EntityManDowned sample) => Written.Add(sample);
            public void DisposeInstance(EntityManDowned key) { }
        }

        private sealed class Node : IDisposable
        {
            public readonly EntityRepository World = new();
            public readonly NetworkEntityMap Map = new();
            public readonly Wire Out = new();
            public readonly DamageAssessedEgressTranslator Egress;
            public readonly EntityHitDamageIngressTranslator Ingress;
            public readonly HealthApplicationSystem Apply = new();
            public readonly MdWire MdOut = new();                      // ⭐ CE-3364 D5
            public readonly ManDownedEgressTranslator MdEgress;
            public readonly ManDownedIngressTranslator MdIngress;
            public Entity Victim;

            public Node(int nodeId, int healthOwner)
            {
                World.RegisterComponent<Health>();
                World.RegisterComponent<NetworkAuthority>();
                World.RegisterEvent<DamageAssessedEvent>();
                World.RegisterEvent<ManDownedEvent>();
                MdEgress = new ManDownedEgressTranslator(MdOut, Map, nodeId);
                MdIngress = new ManDownedIngressTranslator(participant: null, Map, nodeId);
                Egress = new DamageAssessedEgressTranslator(Out, Map, nodeId);
                Ingress = new EntityHitDamageIngressTranslator(participant: null, Map, nodeId);
                Victim = World.CreateEntity();
                World.AddComponent(Victim, new Health { Current = 100f, Max = 100f });
                World.AddComponent(Victim, new NetworkAuthority(primaryOwnerId: healthOwner, localNodeId: nodeId));
                World.SetAuthority<Health>(Victim, nodeId == healthOwner);
                Map.Register(50, Victim);
            }

            /// <summary>One frame: this frame's events become readable, the egress sends, the owner applies.</summary>
            public void Frame()
            {
                World.Bus.SwapBuffers();
                Egress.ScanAndPublish(World);
                MdEgress.ScanAndPublish(World);
                Apply.Execute(World, 0.1f);
            }

            public void ReceiveDowned(IEnumerable<EntityManDowned> samples)
            {
                var view = (ISimulationView)World;
                var cmd = (EntityCommandBuffer)view.GetCommandBuffer();
                foreach (var s in samples) MdIngress.ProcessSample(in s, cmd);
                cmd.Playback(World);
            }

            public void Receive(IEnumerable<EntityHitDamage> samples)
            {
                var view = (ISimulationView)World;
                var cmd = (EntityCommandBuffer)view.GetCommandBuffer();
                foreach (var s in samples) Ingress.ProcessSample(in s, cmd, view);
                cmd.Playback(World);
            }

            public float Hp => World.GetComponentRO<Health>(Victim).Current;
            public void Dispose() => World.Dispose();
        }

        private readonly Node _a = new(nodeId: 1, healthOwner: 2);   // the striker's brain — does NOT own the victim's Health
        private readonly Node _b = new(nodeId: 2, healthOwner: 2);   // the victim's Health owner

        public void Dispose() { _a.Dispose(); _b.Dispose(); }

        private void Deliver()
        {
            var sent = new List<EntityHitDamage>(_a.Out.Written);
            sent.AddRange(_b.Out.Written);
            _a.Out.Written.Clear(); _b.Out.Written.Clear();
            _a.Receive(sent);   // DDS hands every node its own sample too
            _b.Receive(sent);
        }

        /// <summary>
        /// A blow struck on node A (not the owner) lands once on node B (the owner); A never applies it, and A's own sample coming back
        /// off the wire is dropped. ✅ Red-proofs: the egress sending only where <c>AppliesHere</c> (the old Muscle habit inverted) ⇒ A
        /// sends nothing ⇒ B stays at 100 ⇒ red; the ingress keeping its own sample ⇒ A's bus carries the hit twice ⇒ red.
        /// </summary>
        [Fact]
        public void CE3246_AHitRaisedOffTheOwner_LandsOnceOnTheOwner()
        {
            _a.World.Bus.Publish(new DamageAssessedEvent { HitEntity = _a.Victim, TotalDamage = 30f });
            _a.Frame();
            Assert.Single(_a.Out.Written);
            Assert.Equal(1L, _a.Out.Written[0].SourceNodeId);
            Assert.Equal(100f, _a.Hp);   // A is not the owner

            Deliver();
            _a.Frame(); _b.Frame();
            Assert.Equal(70f, _b.Hp);
            Assert.Empty(_a.Out.Written);   // a remote hit is never re-sent
            Assert.Empty(_b.Out.Written);
            Assert.Equal(0, _a.World.Bus.Read<DamageAssessedEvent>().Length);   // its own sample came back and was dropped

            for (int i = 0; i < 5; i++) { Deliver(); _a.Frame(); _b.Frame(); }
            Assert.Equal(70f, _b.Hp);   // once
        }

        /// <summary>
        /// A hit raised ON the owner is applied there once and never sent. ✅ Red-proof: the egress sending every local hit ⇒ B writes a
        /// sample ⇒ red (and, with the ingress keeping its own sample, B would apply it twice).
        /// </summary>
        [Fact]
        public void CE3246_AHitRaisedOnTheOwner_IsAppliedOnce_AndNeverSent()
        {
            _b.World.Bus.Publish(new DamageAssessedEvent { HitEntity = _b.Victim, TotalDamage = 20f });
            _b.Frame();
            Assert.Equal(80f, _b.Hp);
            Assert.Empty(_b.Out.Written);
            for (int i = 0; i < 5; i++) { Deliver(); _a.Frame(); _b.Frame(); }
            Assert.Equal(80f, _b.Hp);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4155</c> (SR-82; docs/DESIGN_Scenario_Rules.md §10.10) — a hit by a RETHROWN round keeps BOTH throwers across the hit-damage
        /// wire: raised on node A with <c>Source</c> = the last thrower and <c>OriginalSource</c> = the first, it arrives on the owner B naming B's
        /// entities for both; a hit with no original arrives with Null (= the same). ✅ Red-proofs: the egress not writing
        /// <c>OriginalSourceEntityId</c> ⇒ Null on B ⇒ red; the ingress not reading it ⇒ red.
        /// </summary>
        [Fact]
        public void CE4155_ARethrownHits_BothThrowers_CrossTheWire()
        {
            Entity Thrower(Node n, int netId) { var e = n.World.CreateEntity(); n.Map.Register(netId, e); return e; }
            Thrower(_a, 60); Thrower(_a, 61);
            var lastB = Thrower(_b, 60); var firstB = Thrower(_b, 61);
            _a.World.Bus.Publish(new DamageAssessedEvent
            {
                HitEntity = _a.Victim, TotalDamage = 30f,
                Source = _a.Map.TryGetEntity(60, out var last) ? last : Entity.Null,
                OriginalSource = _a.Map.TryGetEntity(61, out var first) ? first : Entity.Null,
            });
            _a.World.Bus.Publish(new DamageAssessedEvent { HitEntity = _a.Victim, TotalDamage = 5f, Source = last });   // a first throw
            _a.Frame();
            Assert.Equal(new[] { 61, 0 }, new[] { _a.Out.Written[0].OriginalSourceEntityId, _a.Out.Written[1].OriginalSourceEntityId });

            Deliver();
            _b.Frame();
            var arrived = _b.World.Bus.Read<DamageAssessedEvent>().ToArray();
            Assert.Equal(new[] { (lastB, firstB), (lastB, Entity.Null) }, arrived.Select(h => (h.Source, h.OriginalSource)).ToArray());
        }

        /// <summary>
        /// ⭐⭐ <c>CE-3364</c> D5 — the downing reaches every node ONCE. A blow raised on A (not the Health owner) downs the man on B (the owner); B raises
        /// the <c>ManDownedEvent</c> locally and sends it; A raises it ONCE (<c>IsRemote</c>, the victim / source resolved to A's entities); B's own sample coming
        /// back is dropped (<c>SourceNodeId</c> — DDS hands a writer its own samples), and neither node ever re-sends what arrived. ✅ Red-proofs: the ingress keeping its
        /// own sample ⇒ B raises it twice ⇒ red; the egress not skipping <c>IsRemote</c> ⇒ A re-sends what arrived ⇒ red.
        /// </summary>
        [Fact]
        public void CE3364_ADowningOnTheOwner_ReachesTheOtherNodeOnce_AndNeverEchoes()
        {
            Entity Man(Node n, int netId) { var e = n.World.CreateEntity(); n.Map.Register(netId, e); return e; }
            var shooterA = Man(_a, 60); var shooterB = Man(_b, 60);
            _a.World.Bus.Publish(new DamageAssessedEvent { HitEntity = _a.Victim, TotalDamage = 150f, Source = shooterA });
            _a.Frame(); Deliver();
            _a.Frame(); _b.Frame();                          // B applies the hit: Health 0 ⇒ a ManDownedEvent raised on B
            Assert.Equal(0f, _b.Hp);
            _a.Frame(); _b.Frame();                          // B's egress sends it
            var sent = Assert.Single(_b.MdOut.Written);
            Assert.Equal((50, 60, (byte)InjuryState.Dead, 2), (sent.VictimEntityId, sent.SourceEntityId, sent.State, sent.SourceNodeId));
            Assert.Empty(_a.MdOut.Written);                  // A saw no downing of its own

            var wire = new List<EntityManDowned>(_b.MdOut.Written); _b.MdOut.Written.Clear();
            _a.ReceiveDowned(wire); _b.ReceiveDowned(wire);   // DDS delivers it to B too
            _a.World.Bus.SwapBuffers(); _b.World.Bus.SwapBuffers();
            var onA = _a.World.Bus.Read<ManDownedEvent>().ToArray();
            var arrived = Assert.Single(onA);
            Assert.Equal((_a.Victim, shooterA, true, InjuryState.Dead), (arrived.Victim, arrived.Source, arrived.IsRemote, arrived.State));
            Assert.Equal(0, _b.World.Bus.Read<ManDownedEvent>().Length);   // B dropped its own sample

            for (int i = 0; i < 4; i++) { _a.Frame(); _b.Frame(); }
            Assert.Empty(_a.MdOut.Written);                  // what arrived is never sent again
            Assert.Empty(_b.MdOut.Written);
        }
    }
}
