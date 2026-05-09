using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Hrot.Network.NED.Gizmos;
using Xunit;
using DebugPrimitivesBatch = GizmoMap.Network.DebugPrimitivesBatch;
using GizmoInteractionBatch = GizmoMap.Network.GizmoInteractionBatch;
using GizmoInteractionEventKind = GizmoMap.Network.GizmoInteractionEventKind;

namespace Hrot.DDS.DataModel.Tests
{
    // ── Test helpers ─────────────────────────────────────────────────────────

    internal sealed class SingleBatchReader : IDdsReader<GizmoInteractionBatch>
    {
        private bool _consumed;
        private readonly GizmoInteractionBatch _batch;
        public SingleBatchReader(GizmoInteractionBatch batch) => _batch = batch;
        public bool TryRead(out GizmoInteractionBatch sample)
        {
            if (!_consumed) { _consumed = true; sample = _batch; return true; }
            sample = default; return false;
        }
    }

    internal sealed class PrimitiveBatchReader : IDdsReader<DebugPrimitivesBatch>
    {
        private readonly Queue<DebugPrimitivesBatch> _items;
        public PrimitiveBatchReader(params DebugPrimitivesBatch[] items)
            => _items = new Queue<DebugPrimitivesBatch>(items);
        public bool TryRead(out DebugPrimitivesBatch sample)
        {
            if (_items.TryDequeue(out sample)) return true;
            sample = default; return false;
        }
    }

    // ── SC-GZ045 Composition-root wiring tests ────────────────────────────────

    public class CompositionRootWiringTests
    {
        // SC-GZ045-3: GizmoInteractionEgressSystem with null writer executes without throwing.
        [Fact]
        public void SC_GZ045_3_EgressSystem_WithNullWriter_DoesNotThrow()
        {
            using var repo = new EntityRepository();
            var sys = new GizmoInteractionEgressTranslator(nodeId: 1, writer: null);

            // Publish an event so there is something to drain.
            var token = new PickToken { Target = Entity.Null, SubElementId = 0 };
            repo.Bus.Publish(new GizmoInteractionStartedEvent { Token = token });
            repo.Bus.SwapBuffers();

            // Must not throw — null writer silently drops the event.
            var ex = Record.Exception(() => sys.ScanAndPublish(repo));
            Assert.Null(ex);
        }

        // SC-GZ045-3b: GizmoInteractionIngressSystem with null reader executes without throwing.
        [Fact]
        public void SC_GZ045_3b_IngressSystem_WithNullReader_DoesNotThrow()
        {
            using var repo = new EntityRepository();
            var sys = new GizmoInteractionIngressTranslator(reader: null);

            var ex = Record.Exception(() =>
            {
                var cmd = new EntityCommandBuffer();
                sys.PollIngress(cmd, repo);
            });
            Assert.Null(ex);
        }

        // SC-GZ045-3c: DebugPrimitivesIngressTranslator with null reader executes without throwing.
        [Fact]
        public void SC_GZ045_3c_IngressTranslator_WithNullReader_DoesNotThrow()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 64);
            var translator = new DebugPrimitivesIngressTranslator(buffer, reader: null);
            var ex = Record.Exception(() => translator.PollAndApply());
            Assert.Null(ex);
        }

        // SC-GZ045-4: After PollAndApply with a reader supplying one DebugPrimitivesBatch,
        //             the gizmo buffer is populated.
        [Fact]
        public void SC_GZ045_4_PollAndApply_PopulatesBuffer()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 64);

            var prim   = new DebugPrimitive
            {
                Shape        = DebugPrimitiveShape.Sphere,
                TargetView   = PipelineTarget.Map2D,
                SphereCenter = new Vector3(1, 2, 3),
                SphereRadius = 5f,
            };
            // DebugPrimitive is 64 bytes; serialize as raw bytes for the DDS byte-array transport.
            var primArray = new DebugPrimitive[] { prim };
            var batch  = new DebugPrimitivesBatch
            {
                NodeId         = 1,
                FrameNumber    = 1,
                PrimitivesData = System.Runtime.InteropServices.MemoryMarshal.AsBytes(primArray.AsSpan()).ToArray(),
            };

            var reader     = new PrimitiveBatchReader(batch);
            var translator = new DebugPrimitivesIngressTranslator(buffer, reader: reader, filterNodeId: null);
            translator.PollAndApply();

            Assert.True(buffer.GetFrame().Length > 0, "Buffer should have primitives after PollAndApply.");
        }
    }

    // ── SC-GZ047 Space propagation tests ─────────────────────────────────────

    public class SpacePropagationTests
    {
        // SC-GZ047-1: GizmoDragUpdateEvent has a CoordinateSpace Space field.
        [Fact]
        public void SC_GZ047_1_GizmoDragUpdateEvent_HasSpaceField()
        {
            var evt = new GizmoDragUpdateEvent { Space = CoordinateSpace.Screen };
            Assert.Equal(CoordinateSpace.Screen, evt.Space);
        }

        // SC-GZ047-2: GizmoInteractionCommitEvent has a CoordinateSpace Space field.
        [Fact]
        public void SC_GZ047_2_GizmoInteractionCommitEvent_HasSpaceField()
        {
            var evt = new GizmoInteractionCommitEvent { Space = CoordinateSpace.Screen };
            Assert.Equal(CoordinateSpace.Screen, evt.Space);
        }

        // SC-GZ047-3: GizmoInteractionBatch has a byte Space field encoding CoordinateSpace.
        [Fact]
        public void SC_GZ047_3_GizmoInteractionBatch_HasSpaceField()
        {
            var batch = new GizmoInteractionBatch { Space = (byte)CoordinateSpace.Screen };
            Assert.Equal((byte)CoordinateSpace.Screen, batch.Space);
        }

        // SC-GZ047-4: EgressSystem propagates Space from DragUpdateEvent into the written batch.
        [Fact]
        public void SC_GZ047_4_EgressSystem_PropagatesSpace_InDragUpdate()
        {
            using var repo = new EntityRepository();
            var captured  = new List<GizmoInteractionBatch>();
            var writer    = new CapturingGizmoWriter(captured);
            var sys       = new GizmoInteractionEgressTranslator(nodeId: 1, writer: writer);

            var token = new PickToken { Target = Entity.Null, SubElementId = 0 };
            repo.Bus.Publish(new GizmoDragUpdateEvent
            {
                Token    = token,
                WorldPos = Vector3.Zero,
                Space    = CoordinateSpace.Screen,
            });
            repo.Bus.SwapBuffers();

            sys.ScanAndPublish(repo);

            Assert.Equal(1, captured.Count);
            Assert.Equal((byte)CoordinateSpace.Screen, captured[0].Space);
        }

        // SC-GZ047-5: IngressSystem restores Space in the published DragUpdateEvent.
        [Fact]
        public void SC_GZ047_5_IngressSystem_RestoresSpace_InDragUpdate()
        {
            using var repo = new EntityRepository();
            var entity = repo.CreateEntity();
            var token  = new PickToken { Target = entity, SubElementId = 0 };

            var batch = new GizmoInteractionBatch
            {
                Kind                 = GizmoInteractionEventKind.DragUpdate,
                PickAnchorId         = (uint)entity.Index,
                PickStreamId         = entity.Generation,
                Space                = (byte)CoordinateSpace.Screen,
            };

            var reader = new SingleBatchReader(batch);
            var sys    = new GizmoInteractionIngressTranslator(reader: reader);
            var cmd = new EntityCommandBuffer();
            sys.PollIngress(cmd, repo);
            cmd.Playback(repo);
            repo.Bus.SwapBuffers();

            var events = repo.Bus.Read<GizmoDragUpdateEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(CoordinateSpace.Screen, events[0].Space);
        }
    }

    internal sealed class CapturingGizmoWriter : IDdsWriter<GizmoInteractionBatch>
    {
        private readonly List<GizmoInteractionBatch> _captured;
        public CapturingGizmoWriter(List<GizmoInteractionBatch> captured) => _captured = captured;
        public void Write(GizmoInteractionBatch sample) => _captured.Add(sample);
    }
}
