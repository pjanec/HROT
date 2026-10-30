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
    /// <para>⭐ CE-1040 — both views are north-up now (docs/DESIGN_Map_North_Up.md), so scale, centre AND orientation are kept.
    /// ⛔ HISTORY: until 2026-10-11 the 2-D map drew north DOWN (a mirror) and the swap flipped north and south.</para>
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

        /// <summary>
        /// ⭐ <c>CE-1446</c> (R-1046, DESIGN_Cluster_Load_Phase §11.9 ③) — the map's 3-D build. A request for 3-D while it is NOT built starts it and leaves the map in 2-D (<see cref="Pending3D"/>) — the camera is not swapped, so the
        /// 2-D map stays usable instead of freezing for the build — and <see cref="Update"/> goes to 3-D when it is done. Null (a bare switch, a rail): 3-D is always immediate.
        /// </summary>
        public Map3DBuild? Build { get; set; }

        /// <summary>⭐ CE-1446 — a 3-D request is waiting for the map's 3-D build (the map is still 2-D). Drawn as a small "building 3-D map…" note.</summary>
        public bool Pending3D { get; private set; }

        /// <summary>⭐ CE-1446 — why the last pending 3-D request was dropped: the build failed (its message); null otherwise.</summary>
        public string? Pending3DFailure { get; private set; }

        private bool _pendingAnimate;
        private Vector3? _pendingCentre;

        /// <summary>⭐ CE-1446 — once per frame (the map's mode layer calls it): completes a pending request into 3-D when the build is done; drops it, with the message, when the build failed.</summary>
        public void Update()
        {
            if (!Pending3D) return;
            if (Build is null || Build.IsBuilt) { Pending3D = false; Set(true, _pendingAnimate, _pendingCentre); return; }
            if (Build.Failure is { } failure) { Pending3D = false; Pending3DFailure = failure; }
        }

        public void Toggle() => Set(!Is3D);

        /// <summary>⭐ <c>CE-1342</c> — the switch, landing centred on <paramref name="centreOn"/> (the selected unit; null = where the map looks).</summary>
        public void Toggle(Vector3? centreOn) => Set(!Is3D, centreOn: centreOn);

        /// <summary>Switch to 3-D (<paramref name="on"/>) or back to 2-D, animated; <paramref name="animate"/> false snaps (tests, restore);
        /// <paramref name="centreOn"/> set = the new view is centred there (⭐ <c>CE-1342</c>).</summary>
        public void Set(bool on, bool animate = true, Vector3? centreOn = null)
        {
            if (!on && Pending3D) Pending3D = false;   // ⭐ CE-1446 — going back to 2-D cancels a waiting 3-D request
            if (_toggling || on == Is3D) return;
            if (on && Build is { IsBuilt: false } build)
            {
                // ⭐ CE-1446 — the 3-D map is not built: start it, stay in 2-D (no camera swap), and complete in Update
                Pending3D = true; Pending3DFailure = null; _pendingAnimate = animate; _pendingCentre = centreOn;
                build.Request3D();
                return;
            }
            Pending3D = false;
            float seconds = animate ? Seconds : 0.0001f;
            if (on)
            {
                var overhead = Camera3D.OverheadMatching(_camera2D);
                if (centreOn is { } c) overhead = overhead with { LookAt = c };
                Camera3D.Pose = overhead;
                _canvas.Camera = Camera3D;
                _toggling = true;
                Camera3D.AnimateTo(overhead with { Pitch = TiltPitch }, seconds, () => _toggling = false);
                if (!animate) Camera3D.Update(1f);
            }
            else
            {
                var p = Camera3D.Pose;
                if (Camera3D.Driver != null) Camera3D.Driver = null;   // a follow lets go: the 2-D map has no driver
                if (centreOn is { } c) p = p with { LookAt = c };
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

        /// <summary>⭐ CE-1033 S5 — the pose the map shows now: the 3-D camera's, or in 2-D the overhead pose matching the 2-D view.</summary>
        public CameraPose CurrentPose => Is3D ? Camera3D.Pose : Camera3D.OverheadMatching(_camera2D);

        /// <summary>True when <paramref name="pose"/> is what the 2-D map shows: straight down, north up (M10: <i>"the 2-D map is simply
        /// that camera at pitch −90°"</i>).</summary>
        public static bool IsTwoDPose(CameraPose pose)
        {
            float dy = MathF.IEEERemainder(pose.Yaw - MapCamera3D.NorthUpYaw, 2f * MathF.PI);
            return MathF.Abs(pose.Pitch - MapCamera3D.OverheadPitch) < 0.01f && MathF.Abs(dy) < 0.01f;
        }

        /// <summary>⭐ CE-1033 S5 — show <paramref name="pose"/> at once, in 3-D or (when it is a 2-D pose) in 2-D: a saved view
        /// restored. Ignored while a switch animates.</summary>
        public void ShowPose(CameraPose pose)
        {
            if (_toggling) return;
            Camera3D.Pose = pose;
            if (IsTwoDPose(pose))
            {
                MatchTwoD();
                _canvas.Camera = _camera2D;
            }
            else
            {
                _canvas.Camera = Camera3D;
            }
        }

        /// <summary>The 2-D camera at the 3-D camera's centre and metres per pixel.</summary>
        private void MatchTwoD()
        {
            float zoom = Math.Clamp(1f / Camera3D.MetresPerPixel, _camera2D.MinZoom, _camera2D.MaxZoom);
            var centre = new Vector2(Camera3D.LookAt.X, Camera3D.LookAt.Y);
            var offset = _camera2D.Offset;
            var d = (Camera3D.ViewCentre - offset) / zoom;   // ⭐ CE-4084 — the visible map's centre, as the 3-D camera projects it
            var target = centre - (MapCamera.NorthUp ? new Vector2(d.X, -d.Y) : d);   // ⭐ CE-1040 — screen-down is world-south
            _camera2D.ApplyCameraView(new MapCameraView
            {
                Target = target, Offset = offset, Zoom = zoom, SmoothTarget = target, SmoothZoom = zoom,
            });
        }
    }
}
