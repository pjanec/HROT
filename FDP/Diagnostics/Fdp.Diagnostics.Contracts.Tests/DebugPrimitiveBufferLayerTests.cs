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

        /// <summary>⭐ CE-1160 — 🔒 User, 2026-10-16: <i>"If i click Entity OFF, i am no longer able to open menu by right clicking the
        /// entity, green selection stop showing … I need the entity selection and right click work always independently of what i pick in
        /// the debug layers menu."</i> The entity pick box is an invisible layer-0 box; refusing it with the Entities layer left the
        /// terminal's hit-test nothing to find.</summary>
        [Fact]
        public void CE1160_APickTarget_IsNeverRefusedByLayer_NorEvicted_ButAVisibleBoxOnAHiddenLayerStillIs()
        {
            var b = new DebugPrimitiveBuffer(capacity: 4);
            b.SetActiveLayers(new LayerMask256());            // every layer hidden — Entities (0) included
            var pickBox = DebugPrimitive.MakeBox2D(new Vector2(10f, 20f), new Vector2(2f, 2f), new Rgba32(0, 0, 0, 0),
                sizeMode: SizeMode.WorldMeters, target: PipelineTarget.Map2D, layer: 0, anchorId: 1000);   // as EntityPresentationGizmoShared.EmitPickBox
            var segment = DebugPrimitive.MakePickSegment(Vector2.Zero, new Vector2(5f, 0f), networkId: 1001);
            var visible = DebugPrimitive.MakeBox2D(new Vector2(10f, 20f), new Vector2(2f, 2f), new Rgba32(255, 0, 0, 255),
                layer: 0, anchorId: 1000);                    // a drawn symbol box: still hidden by its layer
            b.EmitRaw(pickBox);
            b.EmitRaw(segment);
            b.EmitRaw(visible);

            var frame = b.GetFrame().ToArray();
            Assert.Equal(2, frame.Length);
            Assert.All(frame, p => Assert.True(DebugPrimitiveBuffer.IsPickTarget(in p)));
            Assert.Equal(1, b.SuppressedCount);

            b.SetActiveLayers(OnlyLayerZero());
            Line(b, Visible); Line(b, Visible);               // full (4)
            b.EmitRaw(DebugPrimitive.MakeLayerControlMask(OnlyLayerZero()));
            b.DrawMainMenuBinding("[{\"label\":\"View\"}]");
            frame = b.GetFrame().ToArray();
            Assert.Equal(2, frame.Count(p => DebugPrimitiveBuffer.IsPickTarget(in p)));   // the plain lines gave way, not the pick targets
        }

        /// <summary>⭐ CC-16 — an anchor arriving at a full buffer used to be DROPPED (it was not "protected"); it evicts a plain primitive now,
        /// like the mask and the bindings, so an entity never loses its anchor (and with it every EntityLocal primitive) to overflow.</summary>
        [Fact]
        public void CC16_AnAnchorArrivingAtAFullBuffer_EvictsAPlainPrimitive_InsteadOfBeingDropped()
        {
            var b = new DebugPrimitiveBuffer(capacity: 8);
            for (int i = 0; i < 8; i++) Line(b, Visible);     // full
            b.EmitRaw(DebugPrimitive.MakeSpatialAnchor(77, 1f, 2f, 3f, 0f));

            var frame = b.GetFrame().ToArray();
            Assert.Equal(8, frame.Length);
            Assert.Contains(frame, p => p.Shape == DebugPrimitiveShape.SpatialAnchor && p.NetworkId == 77);
            Assert.Equal(7, frame.Count(p => p.Shape == DebugPrimitiveShape.Line));
            Assert.Equal(1, b.DroppedCount);                  // the evicted plain line
        }

        /// <summary>⭐ CC-16 — <c>MoveDefaultLayer</c> moved a pick target off layer 0 (it was not in its skip list), so a tool's pick box left the
        /// layer the hit-test relies on.</summary>
        [Fact]
        public void CC16_MoveDefaultLayer_LeavesAPickTargetOnLayerZero_ButMovesAPlainPrimitive()
        {
            var b = new DebugPrimitiveBuffer(capacity: 8);
            b.EmitRaw(DebugPrimitive.MakeBox2D(new Vector2(1f, 1f), new Vector2(2f, 2f), new Rgba32(0, 0, 0, 0), layer: 0, anchorId: 5));   // pick target
            Line(b, Visible);
            b.MoveDefaultLayer(0, 9);

            var frame = b.GetFrame().ToArray();
            Assert.Equal(0, frame.Single(p => DebugPrimitiveBuffer.IsPickTarget(in p)).DebugLayer);
            Assert.Equal(9, frame.Single(p => p.Shape == DebugPrimitiveShape.Line).DebugLayer);
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

        /// <summary>⭐ CE-1470 — a frame that overflows GROWS the buffer at its reset, so the next frame of the same load drops nothing; the drop is reported
        /// (<c>LastFrameDropped</c>, the frame-stats peak) instead of being silent. 📐 Windows, scale-1 + 1 000 men: all 16 384 slots full, the rest dropped by
        /// draw order. ✅ Red-proof: <c>GrowAfterOverflow</c> not called ⇒ the second frame still drops.</summary>
        [Fact]
        public void CE1470_AnOverflowingFrame_GrowsTheBuffer_SoTheNextFrameKeepsEverything()
        {
            var b = new DebugPrimitiveBuffer(capacity: 8);
            void Frame() { for (int i = 0; i < 20; i++) b.DrawLine(Vector3.Zero, Vector3.One, new Rgba32(255, 255, 255)); }
            Frame();
            Assert.Equal(12, b.DroppedCount);
            b.EndFrame(0.016f);
            Assert.Equal(12, b.LastFrameDropped);
            Assert.True(b.Capacity >= 20, $"grown to {b.Capacity}");
            Assert.True(DebugPrimitiveBuffer.PeakDropped >= 12);
            Frame();
            Assert.Equal(0, b.DroppedCount);
            Assert.Equal(20, b.GetFrame().Length);
            b.Clear();
            Assert.Equal(0, b.LastFrameDropped);
            Assert.True(b.Capacity <= DebugPrimitiveBuffer.MaxCapacity);
        }
        // ── ⭐⭐ Simulated devices S1d-1 (docs/designs/devices/DESIGN_Simulated_Devices.md §3.6 D-41 / R4-5 / R5-7 / R6-5 / R6-9, R7-13) — HUD scopes ──

        private const long Device = 1001;

        [Fact]
        public void S1d1_EndScope_StampsTheRunsCount_AtTheScopesActualSlot()
        {
            var b = new DebugPrimitiveBuffer(capacity: 16);
            Line(b, Visible);
            int slot = b.BeginScope(Device);
            Line(b, Visible); Line(b, Visible); Line(b, Visible);
            Assert.Equal(3, b.EndScope());
            Line(b, Visible);
            var f = b.GetFrame();
            Assert.Equal(1, slot);
            Assert.Equal(DebugPrimitiveShape.HudScope, f[slot].Shape);
            Assert.Equal(Device, f[slot].HudScopeSource);
            Assert.Equal(3, f[slot].HudScopeCount);
            Assert.Equal(6, f.Length);
        }

        [Fact]
        public void S1d1_AnEmptyScope_IsDropped()
        {
            var b = new DebugPrimitiveBuffer(capacity: 16);
            Line(b, Visible);
            b.BeginScope(Device);
            Assert.Equal(0, b.EndScope());
            Assert.Equal(1, b.GetFrame().Length);              // the cursor rolled back: no marker left
            Assert.Equal(1, b.EmptyScopesDropped);
            Assert.DoesNotContain(DebugPrimitiveShape.HudScope, b.GetFrame().ToArray().Select(p => p.Shape));
        }

        [Fact]
        public void S1d1_AScope_IsNeverRefusedByLayer()
        {
            var b = new DebugPrimitiveBuffer(capacity: 16);
            b.SetActiveLayers(new LayerMask256());           // every layer hidden
            int slot = b.BeginScope(Device);
            Assert.Equal(0, slot);
            Assert.Equal(DebugPrimitiveShape.HudScope, b.GetFrame()[0].Shape);
        }

        [Fact]
        public void S1d1_AFullBuffer_TheScopeSurvivesEviction_CountsWhatWasStored_AndDroppedCountCountsTheRest()
        {
            var b = new DebugPrimitiveBuffer(capacity: 8);
            for (int i = 0; i < 5; i++) Line(b, Visible);
            int slot = b.BeginScope(Device);                  // slot 5
            for (int i = 0; i < 4; i++) Line(b, Visible);     // two fit (6, 7), two dropped
            b.DrawMainMenuBinding("[{\"label\":\"View\"}]"); // protected: evicts the run's LAST plain line (7) — a meta shape inside the run
            Assert.Equal(2, b.EndScope());
            var f = b.GetFrame();
            Assert.Equal(5, slot);
            Assert.Equal(DebugPrimitiveShape.HudScope, f[slot].Shape);
            Assert.Equal(2, f[slot].HudScopeCount);           // the part that fit stays scoped (⛔ 0 would release it to every reader)
            Assert.Equal(DebugPrimitiveShape.MainMenuBinding, f[7].Shape);
            Assert.Equal(3, b.DroppedCount);                  // two lines that did not fit + the evicted one
            for (int i = 0; i < 20; i++) b.EmitRaw(DebugPrimitive.MakeLayerControlMask(new LayerMask256()));   // more protected ones: the scope is never evicted
            Assert.Equal(DebugPrimitiveShape.HudScope, b.GetFrame()[slot].Shape);
        }

        [Fact]
        public void S1d1_AScopeOpenedOnAFullBuffer_IsNotStored_AndCounted()
        {
            var b = new DebugPrimitiveBuffer(capacity: 4);
            for (int i = 0; i < 4; i++) Line(b, Visible);
            Assert.Equal(-1, b.BeginScope(Device));
            Line(b, Visible);
            Assert.Equal(0, b.EndScope());
            Assert.Equal(2, b.DroppedCount);                  // the scope and its line
            Assert.DoesNotContain(DebugPrimitiveShape.HudScope, b.GetFrame().ToArray().Select(p => p.Shape));
        }

        [Fact]
        public void S1d1_AReAppendedHeldPicture_IsReStampedForThisFramesSlots()
        {
            var b = new DebugPrimitiveBuffer(capacity: 16);
            b.BeginScope(Device); Line(b, Visible); Line(b, Visible); b.EndScope();
            Line(b, Visible);
            var held = b.GetFrame().ToArray();                // [scope 2, L, L, L]
            b.Clear();
            Line(b, Visible); Line(b, Visible);
            b.AppendHeld(held);
            var f = b.GetFrame();
            Assert.Equal(6, f.Length);
            Assert.Equal(DebugPrimitiveShape.HudScope, f[2].Shape);
            Assert.Equal(2, f[2].HudScopeCount);

            var small = new DebugPrimitiveBuffer(capacity: 3);  // the held picture no longer fits: the scope owns only what was stored
            small.AppendHeld(held);
            Assert.Equal(2, small.GetFrame()[0].HudScopeCount);
        }

        [Fact]
        public void S1d1_R7_13_AnAppendFromAnotherThreadInsideARun_IsCounted()
        {
            var b = new DebugPrimitiveBuffer(capacity: 64);
            b.BeginScope(Device);
            Line(b, Visible);
            Assert.Equal(0, b.ForeignAppendsInScope);
            var t = new System.Threading.Thread(() => Line(b, Visible));
            t.Start(); t.Join();
            b.EndScope();
            Assert.Equal(1, b.ForeignAppendsInScope);
            var after = new System.Threading.Thread(() => Line(b, Visible));   // no scope open: not foreign
            after.Start(); after.Join();
            Assert.Equal(1, b.ForeignAppendsInScope);
        }

        [Fact]
        public void S1d1_BeginAndEndScope_AllocateNothingSteady()
        {
            var b = new DebugPrimitiveBuffer(capacity: 256);
            for (int k = 0; k < 50; k++) { b.Clear(); b.BeginScope(Device); Line(b, Visible); b.EndScope(); b.BeginScope(Device); b.EndScope(); }
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 500; k++) { b.Clear(); b.BeginScope(Device); Line(b, Visible); Line(b, Visible); b.EndScope(); b.BeginScope(Device); b.EndScope(); }
            Assert.Equal(0, System.GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
