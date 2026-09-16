using Fdp.Presentation.Tests.Vis2D;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Layers;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Gizmos
{
    public class DebugGizmoLayerGizmoTests
    {
        // Builds a RenderContext that shows the layer at bit index.
        private static RenderContext MakeCtx(int layerBitIndex, float zoom = 1f)
        {
            return new RenderContext
            {
                Zoom              = zoom,
                VisibleLayersMask = 0xFFFF_FFFFu, // All layers visible
                // 91d: production ALWAYS supplies a provider (MapCanvas:119).
                Resources         = HeadlessResourceProvider.Instance,
            };
        }

        // Returns a Line primitive anchored to a non-null entity (Token.IsValid = true)
        // placed at worldPos so that HandleInput at the same point hits it.

        // SC-GZ013-1: Draw with injected CapturingRenderer2D raises no exception.
        [Fact]
        public void SC_GZ013_1_Draw_WithInjectedRenderer_NoException()
        {
            var buffer   = new DebugPrimitiveBuffer(16);
            var bus      = new FdpEventBus();
            var renderer = new CapturingRenderer2D();
            var layer    = new DebugGizmoLayer(31, buffer, bus, renderer.AsLayerRenderer());

            var prim = RenderTestHelpers.MakeLine();
            buffer.DrawLine(Vector3.Zero, Vector3.One, Rgba32.Green);

            var ctx = MakeCtx(layerBitIndex: 31);

            // Just verifying no exception is thrown.
            layer.Draw(ctx);

            // Renderer received the primitive (at least one was in the buffer).
            Assert.True(renderer.Dispatched.Count >= 1);

            bus.Dispose();
        }

        // 🔴🔴 SC-GZ013-2 RETIRED 2026-09-10 (R4, docs/DESIGN_Gizmo_Renderer_Seam.md §6).
        //   It drove `layer.HandleInput(...)` and asserted it returned true and published a Started
        //   event. THREE reasons it could never do that, none of which the rail could see because this
        //   class SIGSEGV'd before running (CE-259aa):
        //     ① `HandleInput` returns FALSE BY DESIGN — the layer declines IMapLayer input because the
        //        inner terminal polls the hardware (see the note on the method);
        //     ② the primitive it built was a LINE, and the live hit-test serves Box2D and Sphere only
        //        (CE-259ac);
        //     ③ its own comment admitted the fixture was improvised: "use a Subclass trick: directly
        //        append via a thin helper below".
        //   ⭐ Both halves it wanted are now railed properly, and they actually run:
        //     the publication → DebugGizmoLayerActivationTests.SC-GZ025-1..4 (via OnInteraction);
        //     the hit geometry → DebugGizmoLayerHitTests.SC-GZ026-1..4 (via PickTopmostEntityAnchor);
        //     the design decision that HandleInput declines → SC-GZ025-5.
        //   🔒 R-131 says analyse, fix, or JUSTIFY the removal. This is the justification.

        // 🔴 SC-GZ013-3 RETIRED 2026-09-10, for the same reason as SC-GZ013-2 and one more.
        //   ⛔ It asserted `layer.HandleInput(...)` returns FALSE for a miss — which is TRUE, but
        //     VACUOUSLY: the method returns false for a HIT as well, by design. ⇒ the rail could not
        //     distinguish "correctly missed" from "never looks at anything", and it passed either way.
        //   ⭐ What it meant to assert is now a real rail: DebugGizmoLayerHitTests.SC-GZ026-2/3b (a miss
        //     is a miss, PAIRED with a hit that hits, on the live hit-test), and SC-GZ025-5 which pins
        //     that HandleInput declines on purpose.
        //   ⚠ It also used `AppendTo` and `MakePickableLine`, both of which built an EntityLocal LINE
        //     with an ECS handle in the anchor-key slot — the CE-259z confusion, and a shape the live
        //     hit-test never served anyway (CE-259ac).

        // (MakePickableLine went with it — it authored the primitive shape described above.)

        // SC-GZ013-4: VisibleLayersMask with layer bit clear => Draw skips rendering.
        [Fact]
        public void SC_GZ013_4_LayerBitClear_DrawSkipsRendering()
        {
            var buffer   = new DebugPrimitiveBuffer(16);
            var bus      = new FdpEventBus();
            var renderer = new CapturingRenderer2D();
            var layer    = new DebugGizmoLayer(5, buffer, bus, renderer.AsLayerRenderer()); // Bit 5

            buffer.DrawLine(Vector3.Zero, Vector3.One, Rgba32.Green);

                        var ctx = new RenderContext
            {
                Zoom              = 1f,
                VisibleLayersMask = 0u, // All bits off => layer 5 bit also off
                Resources         = HeadlessResourceProvider.Instance,
            };

            layer.Draw(ctx);

            Assert.Equal(0, renderer.Dispatched.Count);

            bus.Dispose();
        }

        // 🔴🔴 `AppendTo` DELETED 2026-09-10 (CE-259z). ⛔ Its premise was FALSE, and it said so at
        //   length: "DebugPrimitiveBuffer does not expose a public generic append, so we use
        //   IDebugDrawBuilder.DrawEntityLocal which stores AnchorIndex/Generation." ⭐⭐ The buffer has
        //   HAD a public `AppendRaw(in DebugPrimitive)` and `EmitRaw(in DebugPrimitive)` the whole time.
        //   ⇒ eight lines of commented-out deliberation ("Option: ... Since we cannot do that cleanly")
        //     talking itself into routing a raw primitive through the wrong helper.
        //   ⛔ And it built `new Entity(prim.AnchorIndex, prim.AnchorGeneration)` — reading offset 8/12 as
        //     an ECS handle, which for an EntityLocal primitive is the SpatialAnchor cache key. Exactly
        //     the confusion CE-259z fixes at the source.
        //   ⭐ Its only caller was SC-GZ013-2, retired above. Use `buffer.AppendRaw(in prim)`.

    }
}
