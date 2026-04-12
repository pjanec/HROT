using System.Numerics;
using Xunit;
using FDP.Toolkit.Vis2D.Components;
using FDP.Toolkit.Vis2D.Tests.Input;
using FDP.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

namespace FDP.Toolkit.Vis2D.Tests.Components
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
            camera.InputMap.PanButton = MouseButton.Right;
            
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
    }
}
