using System;
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
        // ⭐ CE-4084 (docs/DESIGN_Map_3D_Mode.md §6af) — the 2-D camera is centred on the VISIBLE map, not the window
        private static readonly Vector2 Window = new(1600, 900);
        private static readonly Vector2 DockedCentre = new(1000, 380);

        /// <summary>CE-4084 SC4a — moving the anchor keeps EVERY world point where it is drawn: the picture does not jump when a panel docks.</summary>
        [Fact]
        public void CE4084_AnchorAt_KeepsEveryWorldPointAtItsScreenPosition()
        {
            var cam = new MapCamera { ViewportOverride = Window };
            cam.Zoom = 2f;
            cam.Target = new Vector2(100, 50);
            cam.Offset = new Vector2(200, 100);
            var points = new[] { new Vector2(100, 50), new Vector2(-30, 400), new Vector2(250, -80) };
            var drawn = Array.ConvertAll(points, cam.WorldToScreen);

            cam.AnchorAt(DockedCentre);

            Assert.Equal(DockedCentre, cam.Offset);
            for (int i = 0; i < points.Length; i++)
                Assert.InRange(Vector2.Distance(cam.WorldToScreen(points[i]), drawn[i]), 0f, 0.01f);
        }

        /// <summary>CE-4084 SC4b — a frame's Update anchors at the visible centre (the glide target moves too), so a later Centre View lands there.</summary>
        [Fact]
        public void CE4084_AfterUpdate_FocusOnLandsAtTheVisibleCentre_WithAndWithoutSmoothing()
        {
            foreach (bool smooth in new[] { false, true })
            {
                var cam = new MapCamera { ViewportOverride = Window, ViewCentreSource = _ => DockedCentre, EnableSmoothing = smooth };
                var t0 = new Vector2(12, 34);
                cam.ApplyCameraView(new MapCameraView { Target = t0, Zoom = 1.5f, SmoothTarget = t0, SmoothZoom = 1.5f });
                var before = cam.WorldToScreen(new Vector2(500, 500));
                cam.Update(0.016f);
                Assert.Equal(DockedCentre, cam.Offset);
                Assert.InRange(Vector2.Distance(cam.WorldToScreen(new Vector2(500, 500)), before), 0f, 0.05f);   // the picture did not jump

                var p = new Vector2(-200, 300);
                cam.FocusOn(p);
                for (int i = 0; i < 400; i++) cam.Update(0.016f);
                Assert.InRange(Vector2.Distance(cam.WorldToScreen(p), DockedCentre), 0f, 0.05f);
            }
        }

        /// <summary>
        /// ⭐ CE-1441 (audit #18) — a host does NOT seed <c>Offset = ViewCentre</c>: the camera places a never-placed Offset at the visible centre on its
        /// FIRST update, with <c>Target</c> untouched — so the world point the host set as Target is the one drawn at the centre (IG / CGF / SimHost relied on
        /// their own seed for exactly this). A host that placed Offset itself, or applied a full view, is left alone.
        /// 🔴 Red-proof: delete the <c>InnerCamera.Offset = ViewCentre</c> line in <c>MapCamera.Update</c> ⇒ <c>AnchorAt</c> moves Target instead and the first assertion fails.
        /// </summary>
        [Fact]
        public void CE1441_TheFirstUpdate_CentresANeverPlacedOffset_WithoutMovingTheTarget()
        {
            var cam = new MapCamera { ViewportOverride = Window, ViewCentreSource = _ => DockedCentre };
            var t = new Vector2(120, -80);
            cam.FocusOn(t);                                 // what a host sets, never touching Offset
            cam.Zoom = 2f;

            cam.Update(0.016f);

            Assert.Equal(DockedCentre, cam.Offset);
            Assert.Equal(t, cam.InnerCamera.Target);                                          // not shifted to keep a (0,0)-anchored picture
            Assert.InRange(Vector2.Distance(cam.WorldToScreen(t), DockedCentre), 0f, 0.01f);   // Target IS at the centre

            // a host that placed Offset itself is not re-seeded (the picture is kept; AnchorAt then moves Target as before)
            var placed = new MapCamera { ViewportOverride = Window, ViewCentreSource = _ => DockedCentre, Offset = Vector2.Zero };
            placed.FocusOn(t);
            placed.Zoom = 2f;
            placed.Update(0.016f);
            Assert.NotEqual(t, placed.InnerCamera.Target);
        }

        /// <summary>CE-4084 — with no dockspace the centre is the window's middle (the default source reads no area).</summary>
        [Fact]
        public void CE4084_WithoutAVisibleArea_TheCentreIsTheWindowsMiddle()
        {
            var saved = Fdp.Presentation.WindowManager.OpenMapArea.Current;
            try
            {
                Fdp.Presentation.WindowManager.OpenMapArea.Current = null;
                var cam = new MapCamera { ViewportOverride = Window };
                Assert.Equal(Window / 2f, cam.ViewCentre);
            }
            finally { Fdp.Presentation.WindowManager.OpenMapArea.Current = saved; }
        }

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
            // Let's just check it moved "some amount" in the right direction.
            Assert.True(camera.Target.X > 0);
            // ⭐ CE-1040 — the 2-D map is north-up: dragging the map UP-left (mouse −Y) shows more of the SOUTH, so the target
            //   moves south (−Y). Under the old mirror it moved +Y. 📄 docs/DESIGN_Map_North_Up.md.
            Assert.True(camera.Target.Y < 0);
        }

        /// <summary>One three-frame drag (down at 100,100 → 90,90 → 80,80) with the given button held; returns the camera's target after.</summary>
        private static Vector2 DragWith(Action<MockInputProvider> hold)
        {
            var camera = new TestableMapCamera { Zoom = 1.0f, Target = Vector2.Zero, ViewCentreSource = _ => Vector2.Zero };   // ⭐ CE-4084 — these cameras sit at Offset (0,0): pin the anchor there so "did not pan" still means Target unchanged
            var input = new MockInputProvider();
            hold(input);
            foreach (var p in new[] { new Vector2(100, 100), new Vector2(90, 90), new Vector2(80, 80) })
            {
                input.MousePosition = p;
                camera.HandleInput(input);
            }
            camera.Update(1.0f);
            return camera.Target;
        }

        /// <summary>
        /// ⭐ <c>CE-1287</c> — the MIDDLE button drags the 2-D map, as it does the 3-D one (<c>MapCamera3D.HandleInput</c>: MMB pans).
        /// 🔒 User, 2026-10-23: <i>"2d map need to support also middle button to drag map to be consistent with the 3d map"</i>.
        /// The drag must move the map exactly as the right button's does. ⛔ Red-proof: drop <c>SecondaryPanButton</c> from
        /// <c>MapCamera.IsPanDown</c> and the target stays at the origin.
        /// </summary>
        [Fact]
        public void MapCamera_MiddleButtonDrag_PansTheMap_LikeTheRightButton()
        {
            var middle = DragWith(i => i.IsMiddleDown = true);
            var right  = DragWith(i => i.IsRightDown = true);

            Assert.NotEqual(Vector2.Zero, middle);
            Assert.Equal(right, middle);
        }

        /// <summary>A button that is not a pan button (the left one selects) moves nothing — the second pan button did not turn every
        /// button into a pan.</summary>
        [Fact]
        public void MapCamera_LeftButtonDrag_DoesNotPan()
        {
            Assert.Equal(Vector2.Zero, DragWith(i => i.IsLeftDown = true));
        }

        /// <summary>The pan buttons are configuration: with the middle one reassigned away, a middle drag pans nothing.</summary>
        [Fact]
        public void MapCamera_SecondaryPanButton_IsConfigurable()
        {
            var camera = new TestableMapCamera { Zoom = 1.0f, Target = Vector2.Zero, ViewCentreSource = _ => Vector2.Zero };   // ⭐ CE-4084 — these cameras sit at Offset (0,0): pin the anchor there so "did not pan" still means Target unchanged
            camera.InputMap.SecondaryPanButton = MapMouseButton.Left;
            var input = new MockInputProvider { IsMiddleDown = true };
            foreach (var p in new[] { new Vector2(100, 100), new Vector2(60, 60) })
            {
                input.MousePosition = p;
                camera.HandleInput(input);
            }
            camera.Update(1.0f);
            Assert.Equal(Vector2.Zero, camera.Target);
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
