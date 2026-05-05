using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Layers;
using Raylib_cs;
using Xunit;

namespace Fdp.Toolkit.Vis2D.Tests.Gizmos
{
    public class DebugGizmoLayerGizmoTests
    {
        // Builds a RenderContext that shows the layer at bit index.
        private static RenderContext MakeCtx(int layerBitIndex, float zoom = 1f)
        {
            var cam = new Camera2D { Zoom = zoom };
            return new RenderContext
            {
                Camera            = cam,
                VisibleLayersMask = 0xFFFF_FFFFu, // All layers visible
            };
        }

        // Returns a Line primitive anchored to a non-null entity (Token.IsValid = true)
        // placed at worldPos so that HandleInput at the same point hits it.
        private static DebugPrimitive MakePickableLine(Vector2 worldPos)
        {
            // Entity(0, 1): Index=0 >= 0 and Generation=1 != 0 => not null => IsValid.
            var p = DebugPrimitive.MakeLine(
                new Vector3(worldPos.X, worldPos.Y, 0f),
                new Vector3(worldPos.X + 1f, worldPos.Y, 0f),
                Rgba32.Red);
            p.TargetView       = PipelineTarget.Map2D;
            p.DebugLayer       = 0;
            p.AnchorIndex      = 0;    // Index 0 is >= 0 => not IsNull
            p.AnchorGeneration = 1;    // Generation 1 != 0 => not IsNull
            return p;
        }

        // SC-GZ013-1: Draw with injected CapturingRenderer2D raises no exception.
        [Fact]
        public void SC_GZ013_1_Draw_WithInjectedRenderer_NoException()
        {
            var buffer   = new DebugPrimitiveBuffer(16);
            var bus      = new FdpEventBus();
            var renderer = new CapturingRenderer2D();
            var layer    = new DebugGizmoLayer(31, buffer, bus, renderer);

            var prim = RenderTestHelpers.MakeLine();
            buffer.DrawLine(Vector3.Zero, Vector3.One, Rgba32.Green);

            var ctx = MakeCtx(layerBitIndex: 31);

            // Just verifying no exception is thrown.
            layer.Draw(ctx);

            // Renderer received the primitive (at least one was in the buffer).
            Assert.True(renderer.Dispatched.Count >= 1);

            bus.Dispose();
        }

        // SC-GZ013-2: HandleInput within hit radius of pickable primitive => returns true
        // and publishes GizmoInteractionStartedEvent.
        [Fact]
        public void SC_GZ013_2_HandleInput_HitPrimitive_ReturnsTrueAndPublishesEvent()
        {
            var buffer   = new DebugPrimitiveBuffer(16);
            var bus      = new FdpEventBus();
            var renderer = new CapturingRenderer2D();
            var layer    = new DebugGizmoLayer(31, buffer, bus, renderer);

            // Pickable line at (10, 10).
            var worldPos = new Vector2(10f, 10f);
            var prim = MakePickableLine(worldPos);
            // Manually append — DebugPrimitiveBuffer has no generic AppendRaw; use DrawEntityLocal
            // to get a properly anchored primitive in the buffer. We use a direct line with
            // AnchorIndex/Generation set; use DrawLine for the buffer but override via
            // a second buffer push after reflection is not ideal, so we use a Subclass trick:
            // directly append via a thin helper below.
            AppendTo(buffer, prim);

            bool result = layer.HandleInput(worldPos, MouseButton.Left, isPressed: true);
            Assert.True(result);

            bus.SwapBuffers();
            var events = bus.Read<GizmoInteractionStartedEvent>();
            Assert.Equal(1, events.Length);
            Assert.Equal(new Vector3(worldPos.X, worldPos.Y, 0f), events[0].WorldPos);

            bus.Dispose();
        }

        // SC-GZ013-3: HandleInput far from any pickable primitive => returns false.
        [Fact]
        public void SC_GZ013_3_HandleInput_NoHit_ReturnsFalse()
        {
            var buffer   = new DebugPrimitiveBuffer(16);
            var bus      = new FdpEventBus();
            var renderer = new CapturingRenderer2D();
            var layer    = new DebugGizmoLayer(31, buffer, bus, renderer);

            var prim = MakePickableLine(new Vector2(100f, 100f));
            AppendTo(buffer, prim);

            // Click at (0, 0) — well outside 5-unit hit radius of (100, 100).
            bool result = layer.HandleInput(new Vector2(0f, 0f), MouseButton.Left, isPressed: true);
            Assert.False(result);

            bus.Dispose();
        }

        // SC-GZ013-4: VisibleLayersMask with layer bit clear => Draw skips rendering.
        [Fact]
        public void SC_GZ013_4_LayerBitClear_DrawSkipsRendering()
        {
            var buffer   = new DebugPrimitiveBuffer(16);
            var bus      = new FdpEventBus();
            var renderer = new CapturingRenderer2D();
            var layer    = new DebugGizmoLayer(5, buffer, bus, renderer); // Bit 5

            buffer.DrawLine(Vector3.Zero, Vector3.One, Rgba32.Green);

            var cam = new Camera2D { Zoom = 1f };
            var ctx = new RenderContext
            {
                Camera            = cam,
                VisibleLayersMask = 0u, // All bits off => layer 5 bit also off
            };

            layer.Draw(ctx);

            Assert.Equal(0, renderer.Dispatched.Count);

            bus.Dispose();
        }

        // ---- Helper to append a DebugPrimitive directly to a buffer ---------
        // DebugPrimitiveBuffer does not expose a public generic append, so we use
        // IDebugDrawBuilder.DrawEntityLocal which stores AnchorIndex/Generation.
        // For a more direct test, we construct a EntityLocal line primitive with the
        // same world position but skip EntityLocal resolution (no view => skipped).
        // Instead we use a non-EntityLocal line with manually set anchor fields.
        // Since the struct is public and all fields are public, we write directly
        // and use the DrawLine + a secondary buffer push via a tiny test buffer wrapper.
        private static void AppendTo(DebugPrimitiveBuffer buffer, DebugPrimitive prim)
        {
            // Use a test-only subclass of DebugPrimitiveBuffer? No — use the internal
            // Clear-then-write approach: we have a fresh buffer, so we can rely on the
            // internal array. Since we cannot do that cleanly, use an Entity anchor
            // and DrawEntityLocal which sets AnchorIndex/Generation.
            //
            // We append via DrawEntityLocal then patch the resulting primitive's LineStart
            // to match our desired world position. Since DebugPrimitiveBuffer is sealed
            // and GetFrame returns a readonly span, we instead push two separate helpers:
            //
            // Option: use a fresh buffer and construct the primitive via the reflection-free
            // approach of calling DrawEntityLocal with a valid entity.
            //
            // For testing hit-testing without ISimulationView, we push the primitive with
            // EntityLocal space but WITHOUT a view in the layer. The hit-test loop only
            // checks prim.Token.IsValid (which uses AnchorIndex/Generation), and skips
            // the rendering-time EntityLocal resolution.

            buffer.DrawEntityLocal(
                new Entity(prim.AnchorIndex, prim.AnchorGeneration),
                prim.LineStart,
                prim.LineEnd,
                prim.Color,
                layer: prim.DebugLayer);
        }
    }
}
