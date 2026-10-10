using System;
using System.Numerics;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Components;

namespace Fdp.Toolkit.Vis3D
{
    /// <summary>
    /// ⭐ CE-1033 S1 — the map's 2-D ↔ 3-D switch (<c>docs/DESIGN_Map_3D_Mode.md</c> M12, §3.3 third sequence): an animated camera
    /// move pivoting at the OVERHEAD pose, so neither swap jumps in scale or centre. To 3-D: snap the 3-D camera overhead over the
    /// 2-D view's centre at the 2-D metres-per-pixel, swap, tilt. Back: return overhead, then hand the 2-D camera the same centre
    /// and scale, swap.
    ///
    /// <para>⚠ One thing does flip at the swap: the 2-D map draws north DOWN (a mirror of the view from above — see
    /// <see cref="MapCamera3D"/>), the 3-D view draws it UP. Scale, centre and east-is-right are kept.</para>
    /// </summary>
    public sealed class MapViewSwitch
    {
        private readonly MapCanvas _canvas;
        private readonly MapCamera _camera2D;
        private bool _toggling;

        public MapCamera3D Camera3D { get; }

        /// <summary>The tilt the switch settles at (radians, negative = looking down).</summary>
        public float TiltPitch { get; set; } = -35f * MathF.PI / 180f;
        public float Seconds { get; set; } = 0.6f;

        public MapViewSwitch(MapCanvas canvas, MapCamera camera2D, MapCamera3D? camera3D = null)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _camera2D = camera2D ?? throw new ArgumentNullException(nameof(camera2D));
            Camera3D = camera3D ?? new MapCamera3D();
        }

        /// <summary>True while the map shows (or is moving into) 3-D.</summary>
        public bool Is3D => ReferenceEquals(_canvas.Camera, Camera3D);

        /// <summary>True while an animated switch runs (a second request is ignored until it ends).</summary>
        public bool IsSwitching => _toggling;

        public void Toggle() => Set(!Is3D);

        /// <summary>Switch to 3-D (<paramref name="on"/>) or back to 2-D, animated; <paramref name="animate"/> false snaps (tests, restore).</summary>
        public void Set(bool on, bool animate = true)
        {
            if (_toggling || on == Is3D) return;
            float seconds = animate ? Seconds : 0.0001f;
            if (on)
            {
                var overhead = Camera3D.OverheadMatching(_camera2D);
                Camera3D.Pose = overhead;
                _canvas.Camera = Camera3D;
                _toggling = true;
                Camera3D.AnimateTo(overhead with { Pitch = TiltPitch }, seconds, () => _toggling = false);
                if (!animate) Camera3D.Update(1f);
            }
            else
            {
                var p = Camera3D.Pose;
                _toggling = true;
                Camera3D.AnimateTo(p with { Yaw = MapCamera3D.NorthUpYaw, Pitch = MapCamera3D.OverheadPitch }, seconds, () =>
                {
                    MatchTwoD();
                    _canvas.Camera = _camera2D;
                    _toggling = false;
                });
                if (!animate) Camera3D.Update(1f);
            }
        }

        /// <summary>The 2-D camera at the 3-D camera's centre and metres per pixel.</summary>
        private void MatchTwoD()
        {
            var vp = Camera3D.Viewport;
            float zoom = Math.Clamp(1f / Camera3D.MetresPerPixel, _camera2D.MinZoom, _camera2D.MaxZoom);
            var centre = new Vector2(Camera3D.LookAt.X, Camera3D.LookAt.Y);
            var offset = _camera2D.Offset;
            var target = centre - (vp / 2f - offset) / zoom;
            _camera2D.ApplyCameraView(new MapCameraView
            {
                Target = target, Offset = offset, Zoom = zoom, SmoothTarget = target, SmoothZoom = zoom,
            });
        }
    }
}
