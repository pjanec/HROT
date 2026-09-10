// SC-B28: exclusive InputCaptureBinding handling.
//
// 🔴🔴 RE-HOMED 2026-09-10 (R4, docs/DESIGN_Gizmo_Renderer_Seam.md §6).
//   ⛔ SC-B28-4 and SC-B28-5 asserted `layer.TestHook_IsCaptureActive`, which was a HARD-CODED
//     `=> false`. ⇒ SC-B28-4's `Assert.True` could NEVER pass and SC-B28-5's `Assert.False` was
//     VACUOUSLY GREEN — it would have stayed green with the whole capture mechanism ripped out.
//     SC-B28-6 drove `layer.HandleHover(...)`, which is an EMPTY BODY (the layer declines IMapLayer
//     input; the inner terminal polls the hardware instead).
//   🔒 The honest position, stated rather than papered over with a constant: the live capture state is
//     `GizmoMap.Presentation.DebugGizmoLayer._activeTool` — private, one assembly down, behind a
//     `HandleInput` that polls Raylib. ⛔ It is NOT railable headlessly at this layer, and a hook
//     returning a constant so a rail can claim otherwise is worse than an admitted gap (R-142 ③).
//   ⭐⭐ What IS railable, and now is: the two halves the capture path is actually built from —
//     ① the BINDING primitive the backend emits, whose fields the terminal scans, and
//     ② the layer's OnInteraction publication, which is how a captured interaction reaches FDP.
//   ⭐ The terminal's own scan-and-filter behaviour is railed where it belongs and CAN run:
//     GizmoLayerEntityHitTestTests (11/11, via PickTopmostEntityAnchorUnderCapture) and
//     GizmoMap.Presentation.Tests (41/41).
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Layers;
using GizmoMap.Network;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Layers
{
    public class DebugGizmoLayerCaptureTests
    {
        // SC-B28-4: an exclusive InputCaptureBinding carries its anchor in StructNetworkId (offset 24)
        // and its flags in ConditionMask — the two fields the terminal's capture scan reads.
        // 📄 DESIGN_Gizmo_Anchor_Identity.md §2 ⑫b: the binding's anchor arrives in a DIFFERENT slot
        //    from a hit-testable primitive's BoxAnchorId, which is why the old filter compared halves.
        [Fact]
        public void SC_B28_4_ExclusiveBinding_CarriesAnchorAndFlags()
        {
            var prim = DebugPrimitive.MakeInputCaptureBinding(
                networkId: 90210L, subElementId: 2, exclusive: true, wantsRawInput: true);

            Assert.Equal(DebugPrimitiveShape.InputCaptureBinding, prim.Shape);
            Assert.Equal(90210L, prim.StructNetworkId);
            Assert.Equal((ushort)2, prim.SubElementId);
            Assert.Equal(1u, prim.ConditionMask & 1u);   // exclusive
            Assert.Equal(2u, prim.ConditionMask & 2u);   // wantsRawInput
        }

        // SC-B28-5: a NON-exclusive binding leaves the exclusive bit clear, so the terminal's filter is
        // never armed. ⭐ The pair 4/5 is what pins the bit; either alone passes for a constant.
        [Fact]
        public void SC_B28_5_NonExclusiveBinding_LeavesTheExclusiveBitClear()
        {
            var prim = DebugPrimitive.MakeInputCaptureBinding(
                networkId: 90210L, subElementId: 0, exclusive: false, wantsRawInput: false);

            Assert.Equal(0u, prim.ConditionMask & 1u);
            Assert.Equal(0u, prim.ConditionMask & 2u);
        }

        // SC-B28-6: a captured interaction reaching the layer publishes a GizmoDragUpdateEvent with the
        // world position — the assertion the old rail wanted, through the route that actually carries it.
        [Fact]
        public void SC_B28_6_CapturedDrag_PublishesDragUpdateEventWithWorldPos()
        {
            var bus    = new FdpEventBus();
            var layer  = new DebugGizmoLayer(31, new DebugPrimitiveBuffer(16), bus);
            var anchor = new Entity(4, 1);

            layer.OnInteraction(
                new GizmoPickToken
                {
                    AnchorId    = 90210L,
                    AnchorIndex = anchor.Index,
                    StreamId    = (uint)anchor.Generation,
                },
                GizmoInteractionEventKind.DragUpdate,
                new Vector3(10f, 20f, 0f), actionId: 0, stateFlags: 0);

            bus.SwapBuffers();
            var events = bus.Read<GizmoDragUpdateEvent>();

            Assert.Equal(1, events.Length);
            Assert.Equal(10f, events[0].WorldPos.X, precision: 3);
            Assert.Equal(20f, events[0].WorldPos.Y, precision: 3);
            Assert.Equal(anchor, events[0].Token.Target);
        }
    }
}
