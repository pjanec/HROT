using System;
using System.Collections.Generic;
using Hrot.NED.Messages;
using Hrot.Map.Common.Dds;
using Hrot.Network.NED.SimHost;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
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

        private sealed class Node : IDisposable
        {
            public readonly EntityRepository World = new();
            public readonly NetworkEntityMap Map = new();
            public readonly Wire Out = new();
            public readonly DamageAssessedEgressTranslator Egress;
            public readonly EntityHitDamageIngressTranslator Ingress;
            public readonly HealthApplicationSystem Apply = new();
            public Entity Victim;

            public Node(int nodeId, int healthOwner)
            {
                World.RegisterComponent<Health>();
                World.RegisterComponent<NetworkAuthority>();
                World.RegisterEvent<DamageAssessedEvent>();
                Egress = new DamageAssessedEgressTranslator(Out, Map, nodeId);
                Ingress = new EntityHitDamageIngressTranslator(participant: null, Map, nodeId);
                Victim = World.CreateEntity();
                World.AddComponent(Victim, new Health { Current = 100f, Max = 100f });
                World.AddComponent(Victim, new NetworkAuthority(primaryOwnerId: healthOwner, localNodeId: nodeId));
                World.SetAuthority<Health>(Victim, nodeId == healthOwner);
                Map.Register(50L, Victim);
            }

            /// <summary>One frame: this frame's events become readable, the egress sends, the owner applies.</summary>
            public void Frame()
            {
                World.Bus.SwapBuffers();
                Egress.ScanAndPublish(World);
                Apply.Execute(World, 0.1f);
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
    }
}
