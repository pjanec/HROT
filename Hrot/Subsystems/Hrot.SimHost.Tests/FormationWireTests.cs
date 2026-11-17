using System.Collections.Generic;
using System.Linq;
using CarKinem.Formation;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Dds;
using Hrot.Map.Common.Replication.Egress;
using Hrot.Map.Common.Replication.Ingress;
using Hrot.Map.Common.Translators;
using Hrot.NED.Descriptors;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-9007</c> (R-9007 / R-9012; docs/designs/vehicle-tactics/DESIGN_Road_Convoy.md C-8) — a road column crosses from the node running its brain to
    /// every other node (the kinematic owner drives it, the traffic node tells SUMO its gaps): the lead's <see cref="FormationController"/> on change; a
    /// follower's EXPLICIT-leader <see cref="FormationFollower"/> slot with the leader resolved to the receiver's entity, and one "left" sample when it ends; a
    /// commander-led slot is never sent (every node derives it) and never removed by the wire; a non-owner publishes nothing.
    /// 🔴 Red-proofs: the follower egress sending commander-led slots ⇒ the "not sent" assert red; the ingress removing any slot on <c>LeaderId 0</c> ⇒ the
    /// commander-led slot is gone ⇒ red; no "left" sample ⇒ the replica keeps the slot ⇒ red.
    /// </summary>
    [Collection("SimHostDds")]
    public sealed class FormationWireTests
    {
        private sealed class Capture<T> : IDdsWriter<T>
        {
            public List<T> Written { get; } = new();
            public void Write(T sample) => Written.Add(sample);
            public void DisposeInstance(T key) { }
        }

        private const int LeadId = 81, FollowerId = 82, SquadMateId = 83;

        private static (EntityRepository World, NetworkEntityMap Map, Entity Lead, Entity Follower, Entity SquadMate) Node(int primaryOwner, int localNode)
        {
            var w = new EntityRepository();
            w.RegisterComponent<NetworkIdentity>();
            w.RegisterComponent<NetworkAuthority>();
            w.RegisterComponent<FormationController>();
            w.RegisterComponent<FormationFollower>();
            var map = new NetworkEntityMap();
            Entity Make(int id)
            {
                var e = w.CreateEntity();
                w.AddComponent(e, new NetworkIdentity { Value = id });
                w.AddComponent(e, new NetworkAuthority(primaryOwner, localNode));
                map.Register(id, e);
                return e;
            }
            return (w, map, Make(LeadId), Make(FollowerId), Make(SquadMateId));
        }

        private static FormationController Column(float spacing)
            => new() { Type = FormationType.Column, Params = new FormationParams { Spacing = spacing, MaxCatchUpFactor = 1.2f } };

        [Fact]
        public void CE9007_TheBrainNode_PublishesTheLeadsController_OnChangeOnly_ANonOwnerNothing()
        {
            var (w, _, lead, _, _) = Node(primaryOwner: 1, localNode: 1);
            using (w)
            {
                var wire = new Capture<EntityFormationController>();
                var egress = new EntityFormationControllerEgressTranslator(wire);
                w.AddComponent(lead, new FormationController());   // the type's default
                egress.ScanAndPublish(w);
                Assert.Equal(LeadId, Assert.Single(wire.Written).EntityId);
                egress.ScanAndPublish(w);
                Assert.Single(wire.Written);                       // unchanged: nothing sent

                w.SetComponent(lead, Column(15f));                 // the convoy forms
                egress.ScanAndPublish(w);
                Assert.Equal(2, wire.Written.Count);
                Assert.Equal(((byte)FormationType.Column, 15f, 1.2f), (wire.Written[1].Type, wire.Written[1].Spacing, wire.Written[1].MaxCatchUpFactor));
            }

            var (other, _, theirs, _, _) = Node(primaryOwner: 2, localNode: 1);
            using (other)
            {
                var wire = new Capture<EntityFormationController>();
                other.AddComponent(theirs, Column(15f));
                new EntityFormationControllerEgressTranslator(wire).ScanAndPublish(other);
                Assert.Empty(wire.Written);
            }
        }

        [Fact]
        public void CE9007_AnExplicitLeaderSlot_IsSent_ACommanderLedOneNever_AndItsEndSendsOneLeftSample()
        {
            var (w, _, lead, follower, squadMate) = Node(primaryOwner: 1, localNode: 1);
            using (w)
            {
                var wire = new Capture<EntityFormationFollower>();
                var egress = new EntityFormationFollowerEgressTranslator(wire);
                w.AddComponent(follower, new FormationFollower { SlotIndex = 2, Leader = lead, IsInFormation = 1, State = FormationMemberState.Rejoining });
                w.AddComponent(squadMate, new FormationFollower { SlotIndex = 1, IsInFormation = 1 });   // commander-led: derived on every node
                egress.ScanAndPublish(w);
                var s = Assert.Single(wire.Written);
                Assert.Equal((FollowerId, LeadId, (ushort)2, (byte)1), (s.EntityId, s.LeaderId, s.SlotIndex, s.IsInFormation));
                egress.ScanAndPublish(w);
                Assert.Single(wire.Written);                       // unchanged

                w.RemoveComponent<FormationFollower>(follower);    // it left the column
                egress.ScanAndPublish(w);
                Assert.Equal(2, wire.Written.Count);
                Assert.Equal((FollowerId, 0), (wire.Written[1].EntityId, wire.Written[1].LeaderId));
                egress.ScanAndPublish(w);
                Assert.Equal(2, wire.Written.Count);               // the "left" sample once
            }
        }

        [Fact]
        public void CE9007_AReplica_TakesTheColumn_WithItsOwnLeader_AndALeftSampleRemovesOnlyTheConvoysSlot()
        {
            var (w, map, lead, follower, squadMate) = Node(primaryOwner: 2, localNode: 1);
            using (w)
            {
                w.AddComponent(lead, new FormationController());
                w.AddComponent(squadMate, new FormationFollower { SlotIndex = 1, IsInFormation = 1 });   // this node's own commander-led slot
                using (var cmd = new EntityCommandBuffer())
                {
                    new EntityFormationControllerIngressTranslator(null, map).Apply(EntityFormationControllerEgressTranslator.ToSample(LeadId, Column(20f)), cmd, w);
                    new EntityFormationFollowerIngressTranslator(null, map).Apply(
                        EntityFormationFollowerEgressTranslator.ToSample(FollowerId, LeadId, new FormationFollower { SlotIndex = 3, IsInFormation = 1 }), cmd, w);
                    cmd.Playback(w);
                }
                var c = w.GetComponentRO<FormationController>(lead);
                Assert.Equal((FormationType.Column, 20f), (c.Type, c.Params.Spacing));
                var f = w.GetComponentRO<FormationFollower>(follower);
                Assert.Equal((lead, (ushort)3), (f.Leader, f.SlotIndex));   // THIS node's lead

                var ingress = new EntityFormationFollowerIngressTranslator(null, map);
                using (var cmd = new EntityCommandBuffer())
                {
                    ingress.Apply(new EntityFormationFollower { EntityId = FollowerId }, cmd, w);
                    ingress.Apply(new EntityFormationFollower { EntityId = SquadMateId }, cmd, w);
                    cmd.Playback(w);
                }
                Assert.False(w.HasComponent<FormationFollower>(follower));   // it left the column
                Assert.True(w.HasComponent<FormationFollower>(squadMate));   // the commander-led slot is this node's, never the wire's
            }

            var (owner, ownerMap, ownLead, _, _) = Node(primaryOwner: 1, localNode: 1);
            using (owner)
            {
                owner.AddComponent(ownLead, Column(15f));
                using var cmd = new EntityCommandBuffer();
                new EntityFormationControllerIngressTranslator(null, ownerMap).Apply(EntityFormationControllerEgressTranslator.ToSample(LeadId, new FormationController()), cmd, owner);
                cmd.Playback(owner);
                Assert.Equal(15f, owner.GetComponentRO<FormationController>(ownLead).Params.Spacing);   // the owner never takes its own sample back
            }
        }

        [Fact]
        public void CE9007_TheSharedPack_CarriesBothFormationPairs_OnEveryNode()
        {
            using var participant = new DdsParticipant(231u);   // the SimHostDds collection runs serially: 231 is the animal pack rail's, ≤ 232 is Cyclone's domain limit
            var entityMap = new NetworkEntityMap();
            var pack = SharedTranslatorPack.Create(participant, entityMap, localNodeId: 1, new FdpEventBus(),
                new Fdp.Toolkit.Replication.Systems.GhostCreationSystem(entityMap)).ToList();
            Assert.Equal(new[] { GlobalComponentIds.FormationController }, Assert.Single(pack.OfType<EntityFormationControllerEgressTranslator>()).TargetComponentIds);
            Assert.Single(pack.OfType<EntityFormationControllerIngressTranslator>());
            Assert.Equal((long)EDescriptorType.dtFormationFollower, Assert.Single(pack.OfType<EntityFormationFollowerEgressTranslator>()).DescriptorOrdinal);
            Assert.Single(pack.OfType<EntityFormationFollowerIngressTranslator>());
        }
    }
}
