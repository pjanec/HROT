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
        /// and since §6.7 that is the ONLY identity — the <c>AnchorIndex</c>+<c>StreamId</c> ECS payload
        /// is deleted. 📄 docs/DESIGN_Gizmo_Anchor_Identity.md §6.7.
        /// </summary>
        private static GizmoPickToken TokenFor(long networkId = 90210L, uint subElementId = 0u)
            => new GizmoPickToken
            {
                AnchorId     = networkId,
                SubElementId = subElementId,
            };

        // SC-GZ025-1: a Started interaction publishes GizmoInteractionStartedEvent exactly once,
        // carrying the anchor's NETWORK ID (§6.7 — the ECS handle is no longer forwarded or rebuilt).
        [Fact]
        public void SC_GZ025_1_Started_PublishesStartedEventOnce_WithTheAnchorId()
        {
            var layer  = MakeLayer(out var bus);

            layer.OnInteraction(TokenFor(), GizmoInteractionEventKind.Started,
                new Vector3(10f, 20f, 0f), actionId: 0, stateFlags: 0);

            bus.SwapBuffers();
            var events = bus.Read<GizmoInteractionStartedEvent>();

            Assert.Equal(1, events.Length);
            Assert.Equal(90210L, events[0].Token.AnchorId);
            Assert.Equal(10f, events[0].WorldPos.X, precision: 3);
            Assert.Equal(20f, events[0].WorldPos.Y, precision: 3);
        }

        // SC-GZ025-2: SubElementId survives the hop — it is how a vertex handle is told from its polygon.
        [Fact]
        public void SC_GZ025_2_Started_CarriesSubElementId()
        {
            var layer  = MakeLayer(out var bus);

            layer.OnInteraction(TokenFor(subElementId: 4u), GizmoInteractionEventKind.Started,
                Vector3.Zero, 0, 0);

            bus.SwapBuffers();
            Assert.Equal(4u, bus.Read<GizmoInteractionStartedEvent>()[0].Token.SubElementId);
        }

        // SC-GZ025-3: a CANVAS interaction publishes an INVALID token. §6.7 — the canvas sentinel -1
        // is carried through as AnchorId, and `IsValid` is false because nothing resolves it to an
        // entity. ⛔ RED-PROOF SHAPE: make ToPickToken force AnchorId to 0 or drop the -1 sentinel
        //    handling and this flips. ⚠ Before §6.7 this rail guarded a DIFFERENT hazard — a rebuilt
        //    Entity(0,0), which is a perfectly valid ECS index — and that hazard no longer exists.
        [Fact]
        public void SC_GZ025_3_CanvasInteraction_PublishesAnInvalidToken()
        {
            var layer = MakeLayer(out var bus);

            layer.OnInteraction(
                new GizmoPickToken { AnchorId = -1L },   // the canvas sentinel
                GizmoInteractionEventKind.Started, Vector3.Zero, 0, 0);

            bus.SwapBuffers();
            var events = bus.Read<GizmoInteractionStartedEvent>();

            Assert.Equal(1, events.Length);
            Assert.Equal(-1L, events[0].Token.AnchorId);
            Assert.False(events[0].Token.IsValid);
        }

        // SC-GZ025-4: each event kind publishes its OWN type, and only that one.
        [Fact]
        public void SC_GZ025_4_EachKind_PublishesItsOwnEventType()
        {
            var layer  = MakeLayer(out var bus);
            var token  = TokenFor();

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
