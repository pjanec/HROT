using System;
using System.Collections.Generic;
using System.Reflection;
using CycloneDDS.Schema;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Hrot.Network.NED.Gizmos;
using Xunit;
using GizmoInteractionBatch = GizmoMap.Network.GizmoInteractionBatch;
using GizmoInteractionEventKind = GizmoMap.Network.GizmoInteractionEventKind;

namespace Hrot.DDS.DataModel.Tests
{
    // ── Test helpers ──────────────────────────────────────────────────────────

    internal sealed class CapturingWriter : IDdsWriter<GizmoInteractionBatch>
    {
        public List<GizmoInteractionBatch> Written = new();
        public void Write(GizmoInteractionBatch sample) => Written.Add(sample);
    }

    internal sealed class SingleItemReader : IDdsReader<GizmoInteractionBatch>
    {
        private readonly Queue<GizmoInteractionBatch> _items;
        public SingleItemReader(params GizmoInteractionBatch[] items)
            => _items = new Queue<GizmoInteractionBatch>(items);
        public bool TryRead(out GizmoInteractionBatch sample)
        {
            if (_items.TryDequeue(out sample)) return true;
            sample = default;
            return false;
        }
    }

    // ── Repo helper ───────────────────────────────────────────────────────────

    internal static class GizmoInteractionTestRepo
    {
        public static EntityRepository Create()
        {
            var repo = new EntityRepository();
            // ⭐ §6.7 — interaction ingress asks "is this anchor id in my WORLD", so the component that
            //   answers that has to be registered.
            repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
            repo.RegisterEvent<GizmoInteractionStartedEvent>();
            repo.RegisterEvent<GizmoDragUpdateEvent>();
            repo.RegisterEvent<GizmoInteractionCommitEvent>();
            repo.RegisterEvent<GizmoInteractionCancelEvent>();
            return repo;
        }
    }

    // ── SC-GZ037 tests ────────────────────────────────────────────────────────

    public class GizmoInteractionTranslatorTests
    {
        // SC-GZ037-1: GizmoInteractionBatch has DdsTopicAttribute with correct name.
        [Fact]
        public void SC_GZ037_1_GizmoInteractionBatch_HasDdsTopicAttribute()
        {
            var attr = (DdsTopicAttribute?)Attribute.GetCustomAttribute(
                typeof(GizmoInteractionBatch), typeof(DdsTopicAttribute));
            Assert.NotNull(attr);
            Assert.Equal("GizmoInteractionBatch", attr!.TopicName);
        }


        // ⭐⭐⭐ S1/S2 (DESIGN_Gizmo_Anchor_Identity.md §6) — THESE TESTS USED TO PIN THE DEFECT.
        //   They asserted `record.PickAnchorId == (uint)entity.Index` and fed `PickStreamId = generation`,
        //   i.e. they encoded the SENDER's process-local ECS handle travelling over DDS — which is exactly
        //   the cross-node mis-targeting the design removes, and it is why the bug stayed green.
        //   ⭐ Rewritten to the contract the record itself documents (GizmoInteractionBatch.cs:21 — "a
        //     blittable breakdown of stable network ID").
        //   ⭐⭐ NetId is deliberately NOT equal to entity.Index, so a test cannot pass by coincidence.
        private const long NetId = 90210L;

        /// <summary>
        /// ⭐⭐ §6.7 — the map is a WORLD SINGLETON, set on the repository, which is how every production
        /// host wires it (<c>CgfSubsystem.cs:637</c>, <c>SimHostApp.cs:546</c>,
        /// <c>EditorSubsystem.cs:1122</c>, and now <c>ReplayBrowserSubsystem</c>).
        /// ⛔ It used to be a constructor argument on the translators; both took
        /// <c>NetworkEntityMap? entityMap = null</c> purely to translate id⇄handle, and both lost it.
        /// ⭐ Registering it here also gives the entity a real <c>NetworkIdentity</c>, without which
        /// §6.7's resolve — and production's own <c>EmitPickBox</c> (constraint C2) — see no anchor.
        /// </summary>
        private static Fdp.Core.Entity NetworkedEntity(EntityRepository repo, long netId = NetId)
        {
            var e = repo.CreateEntity();
            repo.AddComponent(e, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = netId });

            if (!repo.HasSingletonManaged<Fdp.Toolkit.Replication.Services.NetworkEntityMap>())
                repo.SetSingletonManaged(new Fdp.Toolkit.Replication.Services.NetworkEntityMap());
            repo.GetSingletonManaged<Fdp.Toolkit.Replication.Services.NetworkEntityMap>()!
                .Register(netId, e);
            return e;
        }

        // SC-GZ037-2: Egress system writes DragUpdate record with correct fields.
        [Fact]
        public void SC_GZ037_2_EgressSystem_Writes_DragUpdate_Correctly()
        {
            using var repo = GizmoInteractionTestRepo.Create();
            var writer = new CapturingWriter();
            var interactionBus = new FdpEventBus();
            interactionBus.Register<GizmoDragUpdateEvent>();
            var sys = new GizmoInteractionEgressTranslator(nodeId: 7, writer: writer, interactionBus: interactionBus);

            interactionBus.Publish(new GizmoDragUpdateEvent
            {
                // ⭐ §6.7 — the token already holds the network id, so the egress needs no map at all.
                Token    = new PickToken { AnchorId = NetId, SubElementId = 3 },
                WorldPos = new System.Numerics.Vector3(1f, 2f, 3f),
            });
            interactionBus.SwapBuffers();
            sys.ScanAndPublish(repo);

            Assert.Single(writer.Written);
            var record = writer.Written[0];
            Assert.Equal(GizmoInteractionEventKind.DragUpdate, record.Kind);
            Assert.Equal(7, record.SourceNodeId);
            Assert.Equal(NetId, record.PickAnchorId);   // S2 — the NETWORK id, not the ECS index
            Assert.Equal(3u, record.PickSubElementId);
            Assert.Equal(1f, record.WorldX, precision: 4);
            Assert.Equal(2f, record.WorldY, precision: 4);
            Assert.Equal(3f, record.WorldZ, precision: 4);
        }

        // SC-GZ037-3: Ingress translates Commit batch to GizmoInteractionCommitEvent.
        [Fact]
        public void SC_GZ037_3_IngressSystem_Translates_Commit()
        {
            using var repo = GizmoInteractionTestRepo.Create();
            var entity = NetworkedEntity(repo);

            var batch = new GizmoInteractionBatch
            {
                Kind                 = GizmoInteractionEventKind.Commit,
                PickAnchorId         = NetId,   // S1 — a NETWORK id, resolved locally
                PickStreamId         = 0u,
                PickSubElementId     = 5,
                WorldX = 10f, WorldY = 20f, WorldZ = 30f,
            };
            var reader = new SingleItemReader(batch);
            var interactionBus = new FdpEventBus();
            interactionBus.Register<GizmoInteractionCommitEvent>();
            interactionBus.Register<GizmoInteractionCancelEvent>();
            var sys = new GizmoInteractionIngressTranslator(reader: reader, interactionBus: interactionBus);
            var cmd = new EntityCommandBuffer();
            sys.PollIngress(cmd, repo);
            interactionBus.SwapBuffers();

            var commits = interactionBus.Read<GizmoInteractionCommitEvent>().ToArray();
            Assert.Single(commits);
            // ⭐ §6.7 — the received NETWORK id is what the token carries; the consumer resolves it.
            Assert.Equal(NetId, commits[0].Token.AnchorId);
            Assert.Equal(5u, commits[0].Token.SubElementId);
            Assert.Equal(10f, commits[0].WorldPos.X, precision: 4);
        }

        // SC-GZ037-4: Dead entity DragUpdate yields CancelEvent.
        [Fact]
        public void SC_GZ037_4_IngressSystem_DeadEntity_DragUpdate_YieldsCancelEvent()
        {
            using var repo = GizmoInteractionTestRepo.Create();
            // ⭐ §6.7 — the map entry survives the destruction, which is exactly what lets this node say
            //   "I KNEW this anchor and it is gone" rather than "not mine". See
            //   NetworkIdResolver.IsKnownDeadAnchor for why the two must not be conflated.
            var entity = NetworkedEntity(repo);
            repo.DestroyEntity(entity);
            repo.Bus.SwapBuffers();

            var batch = new GizmoInteractionBatch
            {
                Kind                 = GizmoInteractionEventKind.DragUpdate,
                PickAnchorId         = NetId,   // S1 — a NETWORK id, resolved locally
                PickStreamId         = 0u,
            };
            var reader = new SingleItemReader(batch);
            var interactionBus = new FdpEventBus();
            interactionBus.Register<GizmoInteractionCancelEvent>();
            interactionBus.Register<GizmoDragUpdateEvent>();
            var sys = new GizmoInteractionIngressTranslator(reader: reader, interactionBus: interactionBus);
            var cmd = new EntityCommandBuffer();
            sys.PollIngress(cmd, repo);
            interactionBus.SwapBuffers();

            var cancels  = interactionBus.Read<GizmoInteractionCancelEvent>().ToArray();
            var dragEvts = interactionBus.Read<GizmoDragUpdateEvent>().ToArray();
            Assert.Single(cancels);
            Assert.Empty(dragEvts);
        }

        // SC-GZ037-5: Cancel always forwarded, even for dead entity.
        [Fact]
        public void SC_GZ037_5_IngressSystem_Cancel_AlwaysForwarded()
        {
            using var repo = GizmoInteractionTestRepo.Create();
            var entity = NetworkedEntity(repo);
            repo.DestroyEntity(entity);
            repo.Bus.SwapBuffers();

            var batch = new GizmoInteractionBatch
            {
                Kind                 = GizmoInteractionEventKind.Cancel,
                PickAnchorId         = NetId,   // S1 — a NETWORK id, resolved locally
                PickStreamId         = 0u,
            };
            var reader = new SingleItemReader(batch);
            var interactionBus = new FdpEventBus();
            interactionBus.Register<GizmoInteractionCancelEvent>();
            var sys = new GizmoInteractionIngressTranslator(reader: reader, interactionBus: interactionBus);
            var cmd = new EntityCommandBuffer();
            sys.PollIngress(cmd, repo);
            interactionBus.SwapBuffers();

            var cancels = interactionBus.Read<GizmoInteractionCancelEvent>().ToArray();
            Assert.Single(cancels);
        }

        // SC-GZ037-6: Round-trip test — field preservation.
        [Fact]
        public void SC_GZ037_6_GizmoInteractionBatch_FieldsPreserved()
        {
            var batch = new GizmoInteractionBatch
            {
                SourceNodeId         = 3,
                SequenceNumber       = 42,
                Kind                 = GizmoInteractionEventKind.DragUpdate,
                PickAnchorId         = 100,
                PickStreamId         = 2,
                PickSubElementId     = 7,
                WorldX = 1.5f, WorldY = 2.5f, WorldZ = 3.5f,
            };

            Assert.Equal(3, batch.SourceNodeId);
            Assert.Equal(42u, batch.SequenceNumber);
            Assert.Equal(GizmoInteractionEventKind.DragUpdate, batch.Kind);
            Assert.Equal(100u, batch.PickAnchorId);
            Assert.Equal((ushort)2, batch.PickStreamId);
            Assert.Equal(7u, batch.PickSubElementId);
            Assert.Equal(1.5f, batch.WorldX);
            Assert.Equal(2.5f, batch.WorldY);
            Assert.Equal(3.5f, batch.WorldZ);
        }

        // SC-GZ037-7: Null writer — egress returns without exception.
        [Fact]
        public void SC_GZ037_7_EgressSystem_NullWriter_NoOp()
        {
            using var repo = new EntityRepository();
            var sys = new GizmoInteractionEgressTranslator(nodeId: 1, writer: null, new FdpEventBus());
            repo.Bus.SwapBuffers();
            sys.ScanAndPublish(repo); // must not throw
        }

        // SC-GZ037-8: Null reader — ingress returns without exception.
        [Fact]
        public void SC_GZ037_8_IngressSystem_NullReader_NoOp()
        {
            using var repo = new EntityRepository();
            var sys = new GizmoInteractionIngressTranslator(reader: null, new FdpEventBus());
            var cmd = new EntityCommandBuffer();
            sys.PollIngress(cmd, repo); // must not throw
        }

        // SC-GZ066-3: EgressTranslator.WriteRecord produces batch with PickGizmoTypeId
        // equal to the source PickToken.GizmoTypeId.
        [Fact]
        public void SC_GZ066_3_EgressTranslator_WriteRecord_PreservesGizmoTypeId()
        {
            using var repo = GizmoInteractionTestRepo.Create();
            var writer = new CapturingWriter();
            var interactionBus = new FdpEventBus();
            interactionBus.Register<GizmoInteractionStartedEvent>();
            var sys = new GizmoInteractionEgressTranslator(nodeId: 1, writer: writer, interactionBus: interactionBus);

            interactionBus.Publish(new GizmoInteractionStartedEvent
            {
                Token = new Fdp.Toolkit.Diagnostics.Gizmos.PickToken
                {
                    AnchorId    = NetId,
                    GizmoTypeId = 0xAB01u,
                },
                WorldPos = System.Numerics.Vector3.Zero,
            });
            interactionBus.SwapBuffers();
            sys.ScanAndPublish(repo);

            Assert.Single(writer.Written);
            Assert.Equal(0xAB01u, writer.Written[0].PickGizmoTypeId);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>S1+S2 — THE RAIL THE CROSS-NODE DEFECT NEEDED, and the one the old tests could not
        /// express.</b>
        ///
        /// <para>🔴 Before: the egress put the SENDER's <c>Entity.Index</c>/<c>Generation</c> on the wire
        /// and the ingress rebuilt a local handle from them. Indices are allocated per process in spawn
        /// order, so on a receiver with a DIFFERENT layout that handle names a different or dead entity —
        /// silently, because the <c>IsAlive</c> guard dropped it.</para>
        ///
        /// <para>⭐ This drives BOTH ends with <b>deliberately mismatched index layouts</b>: the sender's
        /// entity is the 1st created, the receiver's is the 4th, so their indices CANNOT coincide. The hop
        /// must still land on the receiver's own entity, which is only possible via the network id.</para>
        /// </summary>
        [Fact]
        public void AHopBetweenNodesWithDifferentIndexLayoutsResolvesTheSameEntity()
        {
            // ── sender: its entity is the FIRST created ──
            using var senderRepo = GizmoInteractionTestRepo.Create();
            var senderEntity = NetworkedEntity(senderRepo);

            // ── receiver: burn three entities first, so the SAME network id maps to a DIFFERENT index ──
            using var recvRepo = GizmoInteractionTestRepo.Create();
            recvRepo.CreateEntity(); recvRepo.CreateEntity(); recvRepo.CreateEntity();
            var recvEntity = NetworkedEntity(recvRepo);

            Assert.NotEqual(senderEntity.Index, recvEntity.Index);   // the premise of the rail

            var writer  = new CapturingWriter();
            var sendBus = new FdpEventBus();
            sendBus.Register<GizmoDragUpdateEvent>();
            var egress = new GizmoInteractionEgressTranslator(
                nodeId: 7, writer: writer, interactionBus: sendBus);

            sendBus.Publish(new GizmoDragUpdateEvent
            {
                Token    = new PickToken { AnchorId = NetId, SubElementId = 3 },
                WorldPos = new System.Numerics.Vector3(1f, 2f, 3f),
            });
            sendBus.SwapBuffers();
            egress.ScanAndPublish(senderRepo);

            var onTheWire = Assert.Single(writer.Written);
            Assert.Equal(NetId, onTheWire.PickAnchorId);   // S2 — a network id crossed, not a handle

            // ── the same record arrives on the receiver ──
            var recvBus = new FdpEventBus();
            recvBus.Register<GizmoDragUpdateEvent>();
            recvBus.Register<GizmoInteractionCancelEvent>();
            var ingress = new GizmoInteractionIngressTranslator(
                reader: new SingleItemReader(onTheWire), interactionBus: recvBus);

            ingress.PollIngress(new EntityCommandBuffer(), recvRepo);
            recvBus.SwapBuffers();

            var drags = recvBus.Read<GizmoDragUpdateEvent>().ToArray();
            var drag  = Assert.Single(drags);

            // ⭐⭐⭐ §6.7 — the token carries the ID, and the RECEIVER'S OWN ENTITY is what it resolves
            //   to here. ⛔ The rail used to assert `drag.Token.Target == recvEntity`; asserting the
            //   RESOLVE keeps exactly that claim while the token stays ECS-free — and it still fails if
            //   a sender handle ever crosses, because senderEntity.Index != recvEntity.Index (asserted
            //   above) so a leaked handle would resolve to the wrong entity or to nothing.
            Assert.Equal(NetId, drag.Token.AnchorId);
            Assert.Equal(
                recvEntity,
                Fdp.Toolkit.Replication.Services.NetworkIdResolver.ResolveNetworkId(
                    recvRepo, drag.Token.AnchorId));
        }

        /// <summary>
        /// ⭐ <b>An unknown network id yields NO event.</b> Dropping is correct: the old handle-rebuild
        /// fabricated a wrong-or-dead entity and let it through, and DDS is broadcast — every node sees
        /// every interaction, while <c>DataDrivenGizmoSystem.Recipient</c> routes to the FOCUS HOLDER
        /// first (<c>R-144</c>), so a forwarded foreign drag would feed one operator's gesture into
        /// another operator's active tool.
        ///
        /// <para>⭐⭐ §6.7 kept this and changed only the QUESTION asked — from *"is this id in my
        /// NetworkEntityMap"* (which made a MAPLESS node drop everything, in silence) to *"is this id in
        /// my WORLD"*, which every ECS node can answer.</para>
        /// </summary>
        [Fact]
        public void ANetworkIdThisNodeDoesNotKnowYieldsNoEvent()
        {
            using var repo = GizmoInteractionTestRepo.Create();
            var entity = NetworkedEntity(repo);

            var batch = new GizmoInteractionBatch
            {
                Kind             = GizmoInteractionEventKind.Commit,
                PickAnchorId     = 777777L,      // never registered
                PickSubElementId = 5,
            };
            var bus = new FdpEventBus();
            bus.Register<GizmoInteractionCommitEvent>();
            bus.Register<GizmoInteractionCancelEvent>();

            var sys = new GizmoInteractionIngressTranslator(
                reader: new SingleItemReader(batch), interactionBus: bus);
            sys.PollIngress(new EntityCommandBuffer(), repo);
            bus.SwapBuffers();

            Assert.Empty(bus.Read<GizmoInteractionCommitEvent>().ToArray());
            Assert.Empty(bus.Read<GizmoInteractionCancelEvent>().ToArray());
        }
    }
}
