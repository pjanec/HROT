using System.Linq;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Xunit;

namespace Fdp.Diagnostics.Contracts.Tests
{
    /// <summary>
    /// ⭐⭐ CE-1071 (docs/designs/gizmos-1/DESIGN.md §11.11) — the frame buffer applies the layer mask AT EMIT and never drops the meta
    /// primitives a frame cannot lose. 📐 Measured in the live editor: 4 096 primitives and 400-700 dropped a frame, the hidden
    /// Navmesh / Cover / SpatialGrid layers filling 3 500 slots, and the drops falling on the layer mask (so the 3-D view drew every
    /// hidden layer), the View menu (its 2-D / 3-D and RTS entries vanished) and the RTS tool's input capture (no click reached it).
    /// </summary>
    public class DebugPrimitiveBufferLayerTests
    {
        private const byte Visible = 0, Hidden = 10;

        private static LayerMask256 OnlyLayerZero()
        {
            var m = new LayerMask256();
            m.SetBit(Visible);
            return m;
        }

        private static void Line(DebugPrimitiveBuffer b, byte layer)
            => b.DrawLine(Vector3.Zero, Vector3.UnitX, new Rgba32(255, 0, 0, 255), layer: layer);

        [Fact]
        public void CE1071_AHiddenLayer_IsRefusedAtEmit_ItTakesNoSlot_AndIsCountedApart()
        {
            var b = new DebugPrimitiveBuffer(capacity: 16);
            Line(b, Hidden);
            Assert.Single(b.GetFrame().ToArray());           // no mask known yet: everything is stored, as before

            b.Clear();
            b.SetActiveLayers(OnlyLayerZero());
            Assert.True(b.IsLayerActive(Visible));
            Assert.False(b.IsLayerActive(Hidden));
            for (int i = 0; i < 40; i++) Line(b, Hidden);    // far more than the capacity
            Line(b, Visible);

            var frame = b.GetFrame().ToArray();
            Assert.Single(frame);                             // only the visible line took a slot
            Assert.Equal(Visible, frame[0].DebugLayer);
            Assert.Equal(40, b.SuppressedCount);
            Assert.Equal(0, b.DroppedCount);                  // refused is not dropped
        }

        [Fact]
        public void CE1071_TheMetaPrimitives_AreNeverRefusedByLayer()
        {
            var b = new DebugPrimitiveBuffer(capacity: 16);
            var none = new LayerMask256();                    // every layer hidden
            b.SetActiveLayers(none);
            b.EmitRaw(DebugPrimitive.MakeLayerControlMask(none));
            b.EmitRaw(DebugPrimitive.MakeInputCaptureBinding(networkId: 7, subElementId: 0, exclusive: true, wantsRawInput: true));
            b.DrawMainMenuBinding("[{\"label\":\"View\"}]");
            b.DrawContextMenuBinding(5, "[]");

            var shapes = b.GetFrame().ToArray().Select(p => p.Shape).ToArray();
            Assert.Contains(DebugPrimitiveShape.LayerControlMask, shapes);
            Assert.Contains(DebugPrimitiveShape.InputCaptureBinding, shapes);
            Assert.Contains(DebugPrimitiveShape.MainMenuBinding, shapes);
            Assert.Contains(DebugPrimitiveShape.ContextMenuBinding, shapes);
        }

        [Fact]
        public void CE1071_AFullBuffer_StillTakesTheMask_TheMenu_AndATool_sInputCapture_EvictingPlainPrimitives()
        {
            var b = new DebugPrimitiveBuffer(capacity: 8);
            for (int i = 0; i < 8; i++) Line(b, Visible);     // full
            Line(b, Visible);                                 // a plain one is dropped, as before
            Assert.Equal(1, b.DroppedCount);

            b.EmitRaw(DebugPrimitive.MakeLayerControlMask(OnlyLayerZero()));
            b.DrawMainMenuBinding("[{\"label\":\"View\"}]");
            b.EmitRaw(DebugPrimitive.MakeInputCaptureBinding(networkId: 7, subElementId: 0, exclusive: true, wantsRawInput: true));

            var frame = b.GetFrame().ToArray();
            Assert.Equal(8, frame.Length);
            Assert.Contains(frame, p => p.Shape == DebugPrimitiveShape.LayerControlMask);
            Assert.Contains(frame, p => p.Shape == DebugPrimitiveShape.MainMenuBinding);
            Assert.Contains(frame, p => p.Shape == DebugPrimitiveShape.InputCaptureBinding);
            Assert.Equal(5, frame.Count(p => p.Shape == DebugPrimitiveShape.Line));   // three plain lines gave way
            Assert.Equal(4, b.DroppedCount);
        }

        [Fact]
        public void CE1071_EndFrame_ResetsTheCounts_ButKeepsTheMask()
        {
            var b = new DebugPrimitiveBuffer(capacity: 4);
            b.SetActiveLayers(OnlyLayerZero());
            Line(b, Hidden);
            Assert.Equal(1, b.SuppressedCount);
            b.EndFrame(0.016f);
            Assert.Equal(0, b.SuppressedCount);
            Line(b, Hidden);                                  // the mask holds until the layer control hands over a new one
            Assert.Empty(b.GetFrame().ToArray());
        }
    }
}
