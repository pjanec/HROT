// SC-GZ025: DebugGizmoLayer turns a terminal interaction into a typed FDP event.
//
// 🔴🔴 RE-HOMED 2026-09-10 (R4, docs/DESIGN_Gizmo_Renderer_Seam.md §6).
//   ⛔ These rails used to drive `layer.HandleInput(worldPos, button, isPressed)` and assert it
//     consumed the click, pushed a tool and published an event. THREE separate things were wrong:
//       ① HandleInput returns FALSE BY DESIGN — the gizmo layer declines IMapLayer input because its
//         input arrives through the inner terminal (see its own note; MapCanvas.cs:229-252 offers it
//         to every layer and GridMapLayer.cs:94 declines identically);
//       ② `layer.TestHook_IsInteractionActive` was a HARD-CODED `=> false`, so `Assert.True` on it
//         could never pass and `Assert.False` was vacuous;
//       ③ and none of it was ever noticed, because this class SIGSEGV'd before running — its renderer
//         double subclassed the wrapper, so Raylib drew underneath it (CE-259aa, defect E1).
//   ⭐⭐ What IS live, and what these now rail: `DebugGizmoLayer.OnInteraction` — the token→PickToken
//     conversion plus the typed publication. That is the layer's entire job on the FDP side, it was
//     COMPLETELY UNCOVERED, and it happens to cover the S3 payload path of
//     docs/DESIGN_Gizmo_Anchor_Identity.md as well.
//   ⛔ Do NOT re-add a second input route to make an old rail pass — ruling 9.
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
    public class DebugGizmoLayerActivationTests
    {
        private static DebugGizmoLayer MakeLayer(out FdpEventBus bus)
        {
            bus = new FdpEventBus();
            return new DebugGizmoLayer(31, new DebugPrimitiveBuffer(16), bus);
        }

        /// <summary>
        /// A terminal token as <c>MakePickToken</c> builds one: <c>AnchorId</c> is the network identity,
        /// <c>AnchorIndex</c>+<c>StreamId</c> the in-process ECS payload.
        /// 📄 docs/DESIGN_Gizmo_Anchor_Identity.md §5.1.
        /// </summary>
        private static GizmoPickToken TokenFor(Entity anchor, long networkId = 90210L, uint subElementId = 0u)
            => new GizmoPickToken
            {
                AnchorId     = networkId,
                SubElementId = subElementId,
                AnchorIndex  = anchor.Index,
                StreamId     = (uint)anchor.Generation,
            };

        // SC-GZ025-1: a Started interaction publishes GizmoInteractionStartedEvent exactly once, with
        // the local Entity rebuilt from the token's PAYLOAD (no map lookup — ReplayBrowser has none).
        [Fact]
        public void SC_GZ025_1_Started_PublishesStartedEventOnce_WithTheLocalEntity()
        {
            var layer  = MakeLayer(out var bus);
            var anchor = new Entity(7, 3);

            layer.OnInteraction(TokenFor(anchor), GizmoInteractionEventKind.Started,
                new Vector3(10f, 20f, 0f), actionId: 0, stateFlags: 0);

            bus.SwapBuffers();
            var events = bus.Read<GizmoInteractionStartedEvent>();

            Assert.Equal(1, events.Length);
            Assert.Equal(anchor, events[0].Token.Target);
            Assert.Equal(10f, events[0].WorldPos.X, precision: 3);
            Assert.Equal(20f, events[0].WorldPos.Y, precision: 3);
        }

        // SC-GZ025-2: SubElementId survives the hop — it is how a vertex handle is told from its polygon.
        [Fact]
        public void SC_GZ025_2_Started_CarriesSubElementId()
        {
            var layer  = MakeLayer(out var bus);
            var anchor = new Entity(7, 3);

            layer.OnInteraction(TokenFor(anchor, subElementId: 4u), GizmoInteractionEventKind.Started,
                Vector3.Zero, 0, 0);

            bus.SwapBuffers();
            Assert.Equal(4u, bus.Read<GizmoInteractionStartedEvent>()[0].Token.SubElementId);
        }

        // SC-GZ025-3: a CANVAS interaction (no entity anchor ⇒ StreamId 0) publishes with an INVALID
        // token rather than fabricating Entity 0 — which is a perfectly valid ECS index.
        // ⛔ RED-PROOF SHAPE: drop the `StreamId == 0` guard in ToPickToken and Target becomes
        //    Entity(0, 0) and IsValid flips.
        [Fact]
        public void SC_GZ025_3_CanvasInteraction_PublishesAnInvalidToken_NotEntityZero()
        {
            var layer = MakeLayer(out var bus);

            layer.OnInteraction(
                new GizmoPickToken { AnchorId = -1L },   // the canvas sentinel; no ECS payload
                GizmoInteractionEventKind.Started, Vector3.Zero, 0, 0);

            bus.SwapBuffers();
            var events = bus.Read<GizmoInteractionStartedEvent>();

            Assert.Equal(1, events.Length);
            Assert.False(events[0].Token.IsValid);
        }

        // SC-GZ025-4: each event kind publishes its OWN type, and only that one.
        [Fact]
        public void SC_GZ025_4_EachKind_PublishesItsOwnEventType()
        {
            var layer  = MakeLayer(out var bus);
            var anchor = new Entity(7, 3);
            var token  = TokenFor(anchor);

            layer.OnInteraction(token, GizmoInteractionEventKind.DragUpdate, Vector3.Zero, 0, 0);
            layer.OnInteraction(token, GizmoInteractionEventKind.Commit,     Vector3.Zero, 0, 0);
            layer.OnInteraction(token, GizmoInteractionEventKind.Cancel,     Vector3.Zero, 0, 0);

            bus.SwapBuffers();
            Assert.Equal(1, bus.Read<GizmoDragUpdateEvent>().Length);
            Assert.Equal(1, bus.Read<GizmoInteractionCommitEvent>().Length);
            Assert.Equal(1, bus.Read<GizmoInteractionCancelEvent>().Length);
            Assert.Equal(0, bus.Read<GizmoInteractionStartedEvent>().Length);
        }

        // SC-GZ025-5: 🔒 THE DESIGN DECISION, pinned. The gizmo layer does NOT consume IMapLayer input —
        // MapCanvas offers it and this layer declines, because the inner terminal polls the hardware.
        // ⛔ A future session that "fixes" HandleInput to return true reddens this and must read
        //    DESIGN_Gizmo_Renderer_Seam.md §6 R4 first. That is the point of the rail.
        [Fact]
        public void SC_GZ025_5_TheLayerDeclinesIMapLayerInput_ByDesign()
        {
            var layer = MakeLayer(out _);

            Assert.False(layer.HandleInput(Vector2.Zero, MapMouseButton.Left, isPressed: true));
            Assert.False(layer.HandleInput(Vector2.Zero, MapMouseButton.Right, isPressed: false));
            Assert.False(layer.HandleDrag(Vector2.Zero, Vector2.One));
            Assert.False(layer.HandleKeyInput(MapKeyboardKey.Escape));
        }
    }
}
