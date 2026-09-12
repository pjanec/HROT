using System.Collections.Generic;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Network;
using Hrot.Network.NED.Gizmos;
using Xunit;
using DebugPrimitivesBatch = GizmoMap.Network.DebugPrimitivesBatch;

namespace Hrot.DDS.DataModel.Tests
{
    // ── Test helper ───────────────────────────────────────────────────────────

    internal sealed class QueuedReader : IDdsReader<DebugPrimitivesBatch>
    {
        private readonly Queue<DebugPrimitivesBatch> _items;
        public QueuedReader(params DebugPrimitivesBatch[] items)
            => _items = new Queue<DebugPrimitivesBatch>(items);
        public bool TryRead(out DebugPrimitivesBatch sample)
        {
            if (_items.TryDequeue(out sample)) return true;
            sample = default;
            return false;
        }
    }

    // ── SC-GZ038 tests ────────────────────────────────────────────────────────

    public class GizmoIngressTranslatorTests
    {
        // SC-GZ038-1: Most recent batch replaces buffer contents.
        [Fact]
        public void SC_GZ038_1_PollAndApply_UsesLatestBatch()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 64);

            // Two batches with different primitive counts (DebugPrimitive is 64 bytes each).
            var batch1 = new DebugPrimitivesBatch { NodeId = 1, FrameNumber = 1,
                PrimitivesData = new byte[1 * 64] };
            var batch2 = new DebugPrimitivesBatch { NodeId = 1, FrameNumber = 2,
                PrimitivesData = new byte[3 * 64] };

            var reader = new QueuedReader(batch1, batch2);
            var translator = new DebugPrimitivesIngressTranslator(buffer, reader);
            translator.PollAndApply();

            // Buffer should contain 3 primitives from batch2, not 1 from batch1.
            Assert.Equal(3, buffer.GetFrame().Length);
        }

        // SC-GZ038-3: Null reader — no-op.
        [Fact]
        public void SC_GZ038_3_NullReader_NoOp()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 64);
            var translator = new DebugPrimitivesIngressTranslator(buffer, reader: null);
            translator.PollAndApply(); // must not throw; buffer unchanged
            Assert.Equal(0, buffer.GetFrame().Length);
        }

        // SC-GZ038-4: Filter by NodeId skips other nodes.
        [Fact]
        public void SC_GZ038_4_FilterNodeId_SkipsOtherNodes()
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 64);

            var fromNode5 = new DebugPrimitivesBatch { NodeId = 5, FrameNumber = 1,
                PrimitivesData = new byte[2 * 64] };
            var fromNode9 = new DebugPrimitivesBatch { NodeId = 9, FrameNumber = 2,
                PrimitivesData = new byte[4 * 64] };

            var reader = new QueuedReader(fromNode5, fromNode9);
            var translator = new DebugPrimitivesIngressTranslator(buffer, reader, filterNodeId: 9);
            translator.PollAndApply();

            // Only node 9's batch (4 primitives) should be applied.
            Assert.Equal(4, buffer.GetFrame().Length);
        }

        // =====================================================================
        // CE-259af — a received primitive must not carry the SENDER's ECS handle
        // 🔒 Prompted by the question "why is there still a field for the ECS index and generation?"
        //    The answer: offsets 8/12 are an IN-PROCESS payload the local adapter turns straight back
        //    into `new Entity(AnchorIndex, StreamId)`. For a primitive that arrived over DDS those are
        //    ANOTHER PROCESS's handle ⇒ a wrong local entity, selected silently. Defect D2 relocated
        //    from the wire to the receiver's own boundary.
        // 📄 docs/DESIGN_Gizmo_Anchor_Identity.md §6.6
        // =====================================================================

        private static DebugPrimitivesBatch BatchOf(params DebugPrimitive[] prims)
        {
            var bytes = new byte[prims.Length * 64];
            System.MemoryExtensions.AsSpan(prims).CopyTo(
                System.Runtime.InteropServices.MemoryMarshal.Cast<byte, DebugPrimitive>(bytes.AsSpan()));
            return new DebugPrimitivesBatch { NodeId = 1, FrameNumber = 1, PrimitivesData = bytes };
        }

        private static DebugPrimitiveBuffer Ingest(params DebugPrimitive[] prims)
        {
            var buffer = new DebugPrimitiveBuffer(capacity: 64);
            new DebugPrimitivesIngressTranslator(buffer, new QueuedReader(BatchOf(prims))).PollAndApply();
            return buffer;
        }

        // CE-259af-1: an interactive pick box arrives with its ECS payload STRIPPED, so the local
        // adapter yields an INVALID token and the interaction is forwarded to the owning node instead
        // of resolving to a locally-plausible wrong entity.
        // ⛔ RED-PROOF SHAPE: delete the strip in PollAndApply and AnchorGeneration comes back as 7.
        [Fact]
        public void CE259af_1_ReceivedPickBox_LosesTheSenderEcsHandle()
        {
            // ⭐ §6.7 — no factory stamps an ECS handle any more, so this rail writes offsets 8/12 by
            //   hand to simulate a peer running OLDER code. That is what the strip now guards.
            var pick = DebugPrimitive.MakeBox2D(
                new System.Numerics.Vector2(10f, 20f), new System.Numerics.Vector2(8f, 8f),
                new Rgba32(0, 0, 0, 0),
                anchorId: 90210L);
            pick.AnchorIndex      = 5;
            pick.AnchorGeneration = 7;

            var prim = Ingest(pick).GetFrame()[0];

            Assert.Equal(0, prim.AnchorIndex);
            Assert.Equal((ushort)0, prim.AnchorGeneration);
            // ⭐⭐ The IDENTITY survives untouched — hit-testing, the exclusive-capture filter and the
            //   context-menu lookup all still work on a received primitive.
            Assert.Equal(90210L, prim.BoxAnchorId);
        }

        // CE-259af-2: ⛔⛔ an EntityLocal primitive KEEPS offset 8 — there it is the SpatialAnchor cache
        // KEY (a network id), not an ECS index. Stripping it would stop remote EntityLocal geometry
        // resolving at all, which is the whole purpose of the dumb-terminal two-pass renderer.
        // ⭐ This is the rail that makes a blanket zeroing impossible to ship.
        [Fact]
        public void CE259af_2_ReceivedEntityLocalPrimitive_KeepsItsAnchorCacheKey()
        {
            var p = default(DebugPrimitive);
            p.Shape       = DebugPrimitiveShape.Line;
            p.Space       = CoordinateSpace.EntityLocal;
            p.TargetView  = PipelineTarget.Map2D;
            p.AnchorIndex = 4242;                     // the network id used as the anchor key
            p.LineStart   = new System.Numerics.Vector3(1f, 0f, 0f);
            p.LineEnd     = new System.Numerics.Vector3(2f, 0f, 0f);

            var prim = Ingest(p).GetFrame()[0];

            Assert.Equal(4242, prim.AnchorIndex);
        }

        // CE-259af-3: ⛔⛔ a Text primitive keeps BOTH overlays — StringHash at offset 8 and
        // LineOffsetPx at offset 12 — or remote text loses its content and its line stacking.
        [Fact]
        public void CE259af_3_ReceivedText_KeepsStringHashAndLineOffset()
        {
            var p = DebugPrimitive.MakeText(
                1f, 2f, new FixedString32("hi"), new Rgba32(255, 255, 255, 255),
                lineOffsetPx: -30f);
            p.StringHash = 0xDEADBEEF;   // interned-text mode
            p.TargetView = PipelineTarget.Map2D;

            var prim = Ingest(p).GetFrame()[0];

            Assert.Equal(0xDEADBEEFu, prim.StringHash);
            Assert.Equal((short)-30,  prim.LineOffsetPx);
        }
    }
}
