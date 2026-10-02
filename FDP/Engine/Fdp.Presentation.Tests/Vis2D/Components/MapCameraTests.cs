using System.Numerics;
using Xunit;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis2D.Tests.Input;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Fdp.Toolkit.Vis2D.Tests.Components
{
    // Mock class to avoid native Raylib calls
    public class TestableMapCamera : MapCamera
    {
        public override Vector2 ScreenToWorld(Vector2 screenPos)
        {
            // Simple logic: World = Target + (Screen - Offset) / Zoom
            return InnerCamera.Target + (screenPos - InnerCamera.Offset) * (1.0f / InnerCamera.Zoom);
        }

        public override Vector2 WorldToScreen(Vector2 worldPos)
        {
            // Simple logic: Screen = Offset + (World - Target) * Zoom
            return InnerCamera.Offset + (worldPos - InnerCamera.Target) * InnerCamera.Zoom;
        }

        public override void BeginMode() { /* No-op */ }
        public override void EndMode() { /* No-op */ }
    }

    public class MapCameraTests
    {
        [Fact]
        public void MapCamera_ZoomIn_IncreasesZoom()
        {
            // Arrange
            var camera = new TestableMapCamera();
            camera.Zoom = 1.0f;
            float initialZoom = camera.Zoom;
            var input = new MockInputProvider { MouseWheelMove = 1.0f };

            // Act - simulate wheel move up (positive)
            // Zoom speed is 0.1f by default.
            camera.HandleInput(input);
            camera.Update(0.016f);

            // Assert
            Assert.True(camera.Zoom > initialZoom);
        }

        [Fact]
        public void MapCamera_ZoomClamp_EnforcesLimits()
        {
            var camera = new TestableMapCamera();
            camera.MinZoom = 0.5f;
            camera.MaxZoom = 2.0f;
            camera.Zoom = 2.0f;
            // Hack: set internal target too to avoid interpolation from 1.0 to 2.0
            camera.FocusOn(camera.Target, 2.0f);
            camera.Update(1.0f); // Stabilize
            
            var input = new MockInputProvider { MouseWheelMove = 1.0f };

            // Act - try to zoom in more
            camera.HandleInput(input);
            camera.Update(0.016f);

            // Assert
            Assert.Equal(2.0f, camera.Zoom);

            // Act - try to zoom out below min
            camera.Zoom = 0.5f;
            camera.FocusOn(camera.Target, 0.5f); // Set target logic
            
            input.MouseWheelMove = -1.0f;
            camera.HandleInput(input);
            camera.Update(0.016f);
            
            // If target is clamped, interpolation will head to clamped value.
            // If we started at 0.5 and try to lower target, target clamps to 0.5.
            // Interpolation stays at 0.5.
            Assert.Equal(0.5f, camera.Zoom);
        }

        [Fact]
        public void MapCamera_ScreenToWorld_RoundTrip()
        {
            var camera = new TestableMapCamera();
            camera.Zoom = 2.0f;
            camera.Target = new Vector2(50, 50);
            camera.Offset = new Vector2(400, 300); // Center of screen usually

            Vector2 screenPoint = new Vector2(400, 300); // Should map to target directly if offset is center
            Vector2 worldPoint = camera.ScreenToWorld(screenPoint);

            Assert.Equal(new Vector2(50, 50), worldPoint);

            Vector2 screenPointBack = camera.WorldToScreen(worldPoint);
            Assert.Equal(screenPoint, screenPointBack);
        }

        [Fact]
        public void MapCamera_Pan_MovesTarget()
        {
            var camera = new TestableMapCamera();
            camera.Zoom = 1.0f;
            camera.Target = Vector2.Zero;
            camera.InputMap.PanButton = MapMouseButton.Right;
            
            var input = new MockInputProvider();

            // 1. Press Right Button
            input.IsRightDown = true;
            input.MousePosition = new Vector2(100, 100);
            camera.HandleInput(input);
            camera.Update(0.016f);
            
            // 2. Move Mouse
            input.MousePosition = new Vector2(90, 90); // Moved (-10, -10)
            camera.HandleInput(input);
            camera.Update(0.016f);
            
             // 3. Move Mouse Again to ensure drag continues
            input.MousePosition = new Vector2(80, 80);
            camera.HandleInput(input);
            camera.Update(1.0f); // Fast forward interpolation

            // Expect Target to move. Original logic: 
            // Delta = (-10, -10). DeltaWorld = (-10, -10). Target -= (-10, -10) = (+10, +10).
            // We moved twice? 
            // First time: Start Drag. _lastMouse = 100.
            // Second time: 90. Delta = -10. Target moves +10.
            // Third time: 80. Delta = -10. Target moves +10.
            // Total +20.
            // Let's just check it moved "some amount" in positive direction.
            Assert.True(camera.Target.X > 0);
            Assert.True(camera.Target.Y > 0);
        }

        // ══ A NON-FINITE CAMERA UN-CLICKS THE WHOLE MAP ═══════════════════════════
        // 🔴 MEASURED IN THE PRODUCT 2026-09-20, from an operator report: the editor's 2-D map was
        //    completely unclickable and the marquee invisible. The diagnostic showed
        //    `frame=763 pickable=708` -- every pick box present -- at `worldPos=(NaN,NaN)`.
        //    GetScreenToWorld2D is (screen − Offset) / Zoom + Target, so ONE non-finite field makes every
        //    conversion NaN, every hit-test comparison false, and every click fall through to the canvas.
        // ⛔ It is PERMANENT without these guards, because Lerp(NaN, x, t) is NaN: the smoothing
        //    re-poisons the camera every frame for the rest of the session.

        /// <summary>
        /// ⭐⭐⭐ <b>The bug the old "validation" could not see.</b> <c>if (zoom &lt; Min)</c> and
        /// <c>if (zoom &gt; Max)</c> are <b>both false for <c>NaN</c></b> — every comparison against
        /// <c>NaN</c> is false — so a <c>NaN</c> sailed straight through the block whose only job was to
        /// validate it. ⛔ Red-proof: restore the two plain comparisons and this reddens.
        /// </summary>
        [Fact]
        public void ANonFiniteTargetZoom_IsCaughtByUpdate_NotWavedThroughTheClamp()
        {
            var camera = new TestableMapCamera();
            // ⛔ Poison the private target directly: the public seams all guard, which is the point —
            //   this rail is about the CLAMP inside Update, the one place a NaN could still arrive.
            typeof(MapCamera)
                .GetField("_targetZoom", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(camera, float.NaN);

            camera.Update(0.016f);

            Assert.True(float.IsFinite(camera.Zoom), "a NaN zoom makes every screen<->world conversion NaN");
            Assert.True(camera.Zoom > 0f);
        }

        /// <summary>
        /// ⭐⭐⭐ <b>RECOVERY, not just refusal.</b> A camera already poisoned must heal on the next frame
        /// — otherwise the session stays dead until restart, which is exactly what the operator saw.
        ///
        /// <para>⚠⚠ <b><c>EnableSmoothing = true</c> IS LOAD-BEARING HERE, and the first version of this
        /// rail omitted it and was VACUOUS — the red-proof caught it.</b> 📐 With smoothing OFF (the
        /// default) <c>Update</c> SNAPS <c>InnerCamera</c> from the validated targets every frame, so a
        /// poisoned camera already heals and the rail passed with the recovery branch deleted. ⛔ It is
        /// only the <b>interpolating</b> path that carries <c>NaN</c> forward, because
        /// <c>Lerp(NaN, x, t)</c> is <c>NaN</c> — that is the case this rail must exercise.</para>
        ///
        /// <para>⭐ The same measurement re-frames the production bug: with smoothing off, the camera can
        /// only be non-finite if <c>_targetZoom</c>/<c>_targetTarget</c> are — i.e. the poison entered
        /// through <c>FocusOn</c> or the NaN-blind clamp, and the snap faithfully copied it every
        /// frame.</para>
        /// </summary>
        [Fact]
        public void AnAlreadyPoisonedCamera_RecoversOnTheNextUpdate()
        {
            var camera = new TestableMapCamera { EnableSmoothing = true };
            camera.InnerCamera.Zoom   = float.NaN;                  // ⛔ the FIELD, as the smoothing writes it
            camera.InnerCamera.Target = new Vector2(float.NaN, float.NaN);

            camera.Update(0.016f);

            Assert.True(float.IsFinite(camera.Zoom));
            Assert.True(float.IsFinite(camera.Target.X) && float.IsFinite(camera.Target.Y));

            // ⭐ And the thing that actually matters: a screen position converts to a real world point.
            var world = camera.ScreenToWorld(new Vector2(100f, 100f));
            Assert.True(float.IsFinite(world.X) && float.IsFinite(world.Y),
                "if this is NaN the map is silently unclickable — 708 pick boxes and not one reachable");
        }

        /// <summary>⚠ The three public write seams reject poison and leave the camera usable.</summary>
        [Fact]
        public void TheWriteSeams_RejectNonFiniteValues()
        {
            var camera = new TestableMapCamera();
            camera.Update(0.016f);
            float goodZoom = camera.Zoom;

            camera.Zoom   = float.NaN;
            camera.Target = new Vector2(float.NaN, 5f);
            camera.FocusOn(new Vector2(1f, float.PositiveInfinity));
            camera.Update(0.016f);

            Assert.Equal(goodZoom, camera.Zoom);
            Assert.True(float.IsFinite(camera.Target.X) && float.IsFinite(camera.Target.Y));
        }

        /// <summary>⚠ A non-finite <c>dt</c> arrives from outside and would poison both interpolations.</summary>
        [Fact]
        public void ANonFiniteDeltaTime_DoesNotPoisonTheCamera()
        {
            var camera = new TestableMapCamera();
            camera.FocusOn(new Vector2(10f, 20f));

            camera.Update(float.NaN);

            Assert.True(float.IsFinite(camera.Zoom));
            Assert.True(float.IsFinite(camera.Target.X) && float.IsFinite(camera.Target.Y));
        }
    }
}
