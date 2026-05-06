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

        // SC-GZ037-2: Egress system writes DragUpdate record with correct fields.
        [Fact]
        public void SC_GZ037_2_EgressSystem_Writes_DragUpdate_Correctly()
        {
            using var repo = GizmoInteractionTestRepo.Create();
            var entity = repo.CreateEntity();
            var writer = new CapturingWriter();
            var sys = new GizmoInteractionEgressSystem(nodeId: 7, writer: writer);

            repo.Bus.Publish(new GizmoDragUpdateEvent
            {
                Token    = new PickToken { Target = entity, SubElementId = 3 },
                WorldPos = new System.Numerics.Vector3(1f, 2f, 3f),
            });
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f);

            Assert.Single(writer.Written);
            var record = writer.Written[0];
            Assert.Equal(GizmoInteractionEventKind.DragUpdate, record.Kind);
            Assert.Equal(7, record.SourceNodeId);
            Assert.Equal(entity.Index, record.PickEntityIndex);
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
            var entity = repo.CreateEntity();

            var batch = new GizmoInteractionBatch
            {
                Kind                 = GizmoInteractionEventKind.Commit,
                PickEntityIndex      = entity.Index,
                PickEntityGeneration = entity.Generation,
                PickSubElementId     = 5,
                WorldX = 10f, WorldY = 20f, WorldZ = 30f,
            };
            var reader = new SingleItemReader(batch);
            var sys = new GizmoInteractionIngressSystem(reader: reader);
            sys.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            var commits = repo.Bus.Read<GizmoInteractionCommitEvent>().ToArray();
            Assert.Single(commits);
            Assert.Equal(entity, commits[0].Token.Target);
            Assert.Equal(5u, commits[0].Token.SubElementId);
            Assert.Equal(10f, commits[0].WorldPos.X, precision: 4);
        }

        // SC-GZ037-4: Dead entity DragUpdate yields CancelEvent.
        [Fact]
        public void SC_GZ037_4_IngressSystem_DeadEntity_DragUpdate_YieldsCancelEvent()
        {
            using var repo = GizmoInteractionTestRepo.Create();
            var entity = repo.CreateEntity();
            var index  = entity.Index;
            var gen    = entity.Generation;
            repo.DestroyEntity(entity);
            repo.Bus.SwapBuffers();

            var batch = new GizmoInteractionBatch
            {
                Kind                 = GizmoInteractionEventKind.DragUpdate,
                PickEntityIndex      = index,
                PickEntityGeneration = gen,
            };
            var reader = new SingleItemReader(batch);
            var sys = new GizmoInteractionIngressSystem(reader: reader);
            sys.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            var cancels  = repo.Bus.Read<GizmoInteractionCancelEvent>().ToArray();
            var dragEvts = repo.Bus.Read<GizmoDragUpdateEvent>().ToArray();
            Assert.Single(cancels);
            Assert.Empty(dragEvts);
        }

        // SC-GZ037-5: Cancel always forwarded, even for dead entity.
        [Fact]
        public void SC_GZ037_5_IngressSystem_Cancel_AlwaysForwarded()
        {
            using var repo = GizmoInteractionTestRepo.Create();
            var entity = repo.CreateEntity();
            var index  = entity.Index;
            var gen    = entity.Generation;
            repo.DestroyEntity(entity);
            repo.Bus.SwapBuffers();

            var batch = new GizmoInteractionBatch
            {
                Kind                 = GizmoInteractionEventKind.Cancel,
                PickEntityIndex      = index,
                PickEntityGeneration = gen,
            };
            var reader = new SingleItemReader(batch);
            var sys = new GizmoInteractionIngressSystem(reader: reader);
            sys.Execute(repo, 0f);
            repo.Bus.SwapBuffers();

            var cancels = repo.Bus.Read<GizmoInteractionCancelEvent>().ToArray();
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
                PickEntityIndex      = 100,
                PickEntityGeneration = 2,
                PickSubElementId     = 7,
                WorldX = 1.5f, WorldY = 2.5f, WorldZ = 3.5f,
            };

            Assert.Equal(3, batch.SourceNodeId);
            Assert.Equal(42u, batch.SequenceNumber);
            Assert.Equal(GizmoInteractionEventKind.DragUpdate, batch.Kind);
            Assert.Equal(100, batch.PickEntityIndex);
            Assert.Equal(2, batch.PickEntityGeneration);
            Assert.Equal(7, batch.PickSubElementId);
            Assert.Equal(1.5f, batch.WorldX);
            Assert.Equal(2.5f, batch.WorldY);
            Assert.Equal(3.5f, batch.WorldZ);
        }

        // SC-GZ037-7: Null writer — egress returns without exception.
        [Fact]
        public void SC_GZ037_7_EgressSystem_NullWriter_NoOp()
        {
            using var repo = new EntityRepository();
            var sys = new GizmoInteractionEgressSystem(nodeId: 1, writer: null);
            repo.Bus.SwapBuffers();
            sys.Execute(repo, 0f); // must not throw
        }

        // SC-GZ037-8: Null reader — ingress returns without exception.
        [Fact]
        public void SC_GZ037_8_IngressSystem_NullReader_NoOp()
        {
            using var repo = new EntityRepository();
            var sys = new GizmoInteractionIngressSystem(reader: null);
            sys.Execute(repo, 0f); // must not throw
        }
    }
}
