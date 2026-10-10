using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using Raylib_cs;

namespace Fdp.Toolkit.Vis3D
{
    /// <summary>A pose of the 3-D map camera: the point looked at (HROT, metres), how far away, and the view direction.</summary>
    /// <param name="Yaw">Heading of the view direction, radians from east (+X) counter-clockwise; +π/2 looks north.</param>
    /// <param name="Pitch">Elevation of the view direction, radians; negative looks down, −π/2 straight down.</param>
    public readonly record struct CameraPose(Vector3 LookAt, float Distance, float Yaw, float Pitch);

    /// <summary>
    /// ⭐ CE-1033 S1 — the map's 3-D camera (<c>docs/DESIGN_Map_3D_Mode.md</c> §3.2, M2, M8, M12). A <see cref="MapCamera"/>, so the
    /// canvas, its input chain and every layer keep working: <see cref="ScreenToWorld"/> answers the ground point under the
    /// cursor (S1: the plane at the look-at height — the mesh pick is S2's <c>Picker3D</c>), and the 2-D <see cref="MapCamera.Zoom"/>
    /// and <see cref="MapCamera.Target"/> are kept at their equivalents (pixels per metre at the look-at point, its XY).
    ///
    /// <para>Free camera, Unity scene view (M8): <b>right-drag looks</b> around the eye, <b>W/A/S/D/Q/E move only while the right
    /// button is held</b> (so tools keep their keys; Shift is faster), the <b>middle button pans</b>, the <b>wheel dollies</b>.
    /// A right CLICK stays the context menu — the canvas tells drag from click.</para>
    ///
    /// <para>⭐ <b>North is UP in 3-D, and in 2-D too</b> since CE-1040 (docs/DESIGN_Map_North_Up.md) — the swap keeps the
    /// orientation. ⛔ HISTORY: the 2-D map used to draw north DOWN, a mirror of the view from above.</para>
    /// </summary>
    public sealed class MapCamera3D : MapCamera
    {
        public const float OverheadPitch = -1.5690f;          // a hair off straight down, so the up vector stays defined
        public const float NorthUpYaw = MathF.PI / 2f;
        private const float MinPitch = -1.5690f, MaxPitch = 1.2f;

        public Vector3 LookAt { get; set; }
        public float Distance { get; set; } = 300f;
        public float Yaw { get; set; } = NorthUpYaw;
        public float Pitch { get; set; } = OverheadPitch;
        public float FovyDegrees { get; set; } = 45f;
        public float MinDistance { get; set; } = 2f;
        public float MaxDistance { get; set; } = 30000f;

        /// <summary>Radians of look per pixel of right-drag.</summary>
        public float LookSpeed { get; set; } = 0.004f;

        /// <summary>The ground height at a point — keeps the look-at point on the relief (R-248). Null ⇒ the look-at Z is kept.</summary>
        public Func<float, float, float>? GroundHeight { get; set; }

        /// <summary>The screen size when no window is open (tests); the window's size otherwise.</summary>
        public Vector2 FallbackViewport { get; set; } = new(1280, 720);

        private float _dt;
        private Vector2 _lastMouse;
        private bool _looking, _panning;
        private Vector2 _followedXY = new(float.NaN, float.NaN);   // the look-at XY the ground height was last matched at

        // animation
        private CameraPose _from, _to;
        private float _animT, _animSeconds;
        private Action? _animDone;
        public bool IsAnimating => _animSeconds > 0f;

        public override bool Is3D => true;

        public CameraPose Pose
        {
            get => new(LookAt, Distance, Yaw, Pitch);
            set { LookAt = value.LookAt; Distance = value.Distance; Yaw = value.Yaw; Pitch = value.Pitch; SyncEquivalents(); }
        }

        // ── the view basis (HROT, Z up) ──

        public Vector3 Forward => new(MathF.Cos(Pitch) * MathF.Cos(Yaw), MathF.Cos(Pitch) * MathF.Sin(Yaw), MathF.Sin(Pitch));
        public Vector3 Up => new(-MathF.Sin(Pitch) * MathF.Cos(Yaw), -MathF.Sin(Pitch) * MathF.Sin(Yaw), MathF.Cos(Pitch));
        public Vector3 Right => Vector3.Cross(Forward, Up);
        public Vector3 Eye => LookAt - Forward * Distance;

        /// <summary>A fixed screen size instead of the window's (tests; a camera drawing into a sub-viewport).</summary>
        public Vector2? ViewportOverride { get; set; }

        public Vector2 Viewport => ViewportOverride ?? (WindowOpen()
            ? new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight())
            : FallbackViewport);

        /// <summary>False in a test process with no window — or no native Raylib at all (the 2-D camera's math is window-free too).</summary>
        private static bool WindowOpen()
        {
            try { return Raylib.IsWindowReady(); }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }

        private float TanHalfFov => MathF.Tan(FovyDegrees * MathF.PI / 360f);

        /// <summary>Metres on the ground per screen pixel, at the look-at point.</summary>
        public float MetresPerPixel => 2f * Distance * TanHalfFov / MathF.Max(1f, Viewport.Y);

        /// <summary>The Raylib camera for this pose.</summary>
        public Camera3D ToRaylib() => new()
        {
            Position = HrotToRaylib.Position(Eye),
            Target = HrotToRaylib.Position(LookAt),
            Up = HrotToRaylib.Position(Up),
            FovY = FovyDegrees,
            Projection = CameraProjection.Perspective,
        };

        /// <summary>The pose that shows what a 2-D camera shows: overhead, the same centre, the same metres per pixel.</summary>
        public CameraPose OverheadMatching(MapCamera camera2D)
        {
            var vp = Viewport;
            var centre = camera2D.ScreenToWorld(vp / 2f);
            float ground = GroundHeight?.Invoke(centre.X, centre.Y) ?? 0f;
            float distance = vp.Y / camera2D.Zoom / 2f / TanHalfFov;
            return new CameraPose(new Vector3(centre, ground), Math.Clamp(distance, MinDistance, MaxDistance), NorthUpYaw, OverheadPitch);
        }

        /// <summary>Animate to <paramref name="target"/> over <paramref name="seconds"/> (eased), then call <paramref name="done"/>.</summary>
        public void AnimateTo(CameraPose target, float seconds, Action? done = null)
        {
            _from = Pose;
            _to = target;
            _animT = 0f;
            _animSeconds = MathF.Max(0.0001f, seconds);
            _animDone = done;
        }

        public override void Update(float dt)
        {
            _cachePixel = new Vector2(float.NaN);   // a new frame: entities may have moved
            if (!float.IsFinite(dt) || dt < 0f) dt = 0f;
            _dt = dt;
            if (IsAnimating)
            {
                _animT += dt / _animSeconds;
                float t = MathF.Min(1f, _animT);
                float s = t * t * (3f - 2f * t);
                LookAt = Vector3.Lerp(_from.LookAt, _to.LookAt, s);
                Distance = _from.Distance + (_to.Distance - _from.Distance) * s;
                Yaw = _from.Yaw + WrapPi(_to.Yaw - _from.Yaw) * s;
                Pitch = _from.Pitch + (_to.Pitch - _from.Pitch) * s;
                if (t >= 1f)
                {
                    _animSeconds = 0f;
                    var done = _animDone;
                    _animDone = null;
                    done?.Invoke();
                }
            }
            else if (GroundHeight != null)
            {
                // ⭐⭐⭐ CE-1044 — FOLLOW THE RELIEF ONLY WHEN THE LOOK-AT POINT MOVED SIDEWAYS (a pan, an animation), never to
                //    re-pin its height every frame. 🔒 User, 2026-10-10: "in unity the forward key (w) always flies to where the
                //    camera is currently looking … when i press right button and move mouse up/down the camera is not just
                //    changing the pitch but travel up/down." 🔴 It re-pinned LookAt.Z to the ground EVERY frame, and the eye is
                //    LookAt − Forward·Distance: a pitch change moved the look-at point's height, the pin undid it by moving the
                //    EYE; W's vertical component was cancelled the same way. ⇒ free-look and fly-through were impossible.
                var xy = new Vector2(LookAt.X, LookAt.Y);
                if (_looking) _followedXY = xy;                                   // flying: the eye is the anchor, never the ground
                else if (xy != _followedXY)
                {
                    LookAt = LookAt with { Z = GroundHeight(LookAt.X, LookAt.Y) };
                    _followedXY = xy;
                }
            }
            Sanitise();
            SyncEquivalents();
        }

        public override bool HandleInput(IInputProvider input)
        {
            var mouse = input.MousePosition;
            if (input.IsMouseCaptured || IsAnimating)
            {
                _looking = _panning = false;
                _lastMouse = mouse;
                return false;
            }

            bool used = false;
            float wheel = input.MouseWheelMove;
            if (wheel != 0f)
            {
                Distance = Math.Clamp(Distance * MathF.Pow(0.88f, wheel), MinDistance, MaxDistance);
                used = true;
            }

            var delta = mouse - _lastMouse;
            if (input.IsMouseButtonDown(MapMouseButton.Right))
            {
                if (_looking)
                {
                    // Look around the EYE (Unity): the eye stays, the look-at point swings.
                    var eye = Eye;
                    Yaw -= delta.X * LookSpeed;
                    Pitch = Math.Clamp(Pitch - delta.Y * LookSpeed, MinPitch, MaxPitch);
                    LookAt = eye + Forward * Distance;

                    // ⭐ CE-1044 — Unity-like speeds. 🔒 User, 2026-10-10: plain WASD "around 10 times slower allowing for finer flight";
                    //    Shift / Ctrl faster. Base = a tenth of the distance per second (min 0.5 m/s); Shift or Ctrl = 10× that (the
                    //    old speed); both = 30×.
                    bool shift = input.IsKeyDown(MapKeyboardKey.LeftShift) || input.IsKeyDown(MapKeyboardKey.RightShift);
                    bool ctrl  = input.IsKeyDown(MapKeyboardKey.LeftControl) || input.IsKeyDown(MapKeyboardKey.RightControl);
                    float boost = shift && ctrl ? 30f : (shift || ctrl) ? 10f : 1f;
                    float speed = MathF.Max(0.5f, Distance * 0.1f) * boost * _dt;
                    var move = Vector3.Zero;
                    if (input.IsKeyDown((MapKeyboardKey)KeyboardKey.W)) move += Forward;
                    if (input.IsKeyDown((MapKeyboardKey)KeyboardKey.S)) move -= Forward;
                    if (input.IsKeyDown((MapKeyboardKey)KeyboardKey.D)) move += Right;
                    if (input.IsKeyDown((MapKeyboardKey)KeyboardKey.A)) move -= Right;
                    if (input.IsKeyDown((MapKeyboardKey)KeyboardKey.E)) move += Vector3.UnitZ;
                    if (input.IsKeyDown((MapKeyboardKey)KeyboardKey.Q)) move -= Vector3.UnitZ;
                    if (move != Vector3.Zero) LookAt += Vector3.Normalize(move) * speed;
                }
                _looking = true;
                used = true;
            }
            else _looking = false;

            if (input.IsMouseButtonDown(MapMouseButton.Middle))
            {
                if (_panning)
                {
                    var d = ScreenDeltaToWorld(delta);
                    LookAt -= new Vector3(d, 0f);
                }
                _panning = true;
                used = true;
            }
            else _panning = false;

            _lastMouse = mouse;
            if (used) { Sanitise(); SyncEquivalents(); }
            return used;
        }

        /// <summary>A drag of <paramref name="screenDelta"/> pixels as metres on the ground at the look-at point.</summary>
        public override Vector2 ScreenDeltaToWorld(Vector2 screenDelta)
        {
            float mpp = MetresPerPixel;
            var right = Flat(Right);
            var ahead = Flat(Forward);
            if (ahead == Vector2.Zero) ahead = Flat(Up);   // straight down: screen-up is the up vector
            return (right * screenDelta.X - ahead * screenDelta.Y) * mpp;
        }

        /// <summary>⭐ CE-1033 S2 — what a pick can hit: the terrain mesh, the entity boxes (added by the shared attach). Nearest wins.</summary>
        public List<IPicker3D> Pickers { get; } = new();

        /// <summary>The last pick (point with height, kind, entity id) — what the five former <c>z = 0</c> sites read (M19).</summary>
        public PickResult LastPick { get; private set; } = PickResult.None;

        // One pick per (pixel, filter, camera pose) per frame: the canvas, the gizmo layer and the hover all ask for the same pixel.
        private Vector2 _cachePixel = new(float.NaN);
        private bool _cacheForDrag;
        private CameraPose _cachePose;
        private PickResult _cacheResult;

        /// <summary>The ground point under a screen pixel: an entity's own position when its box is hit (so its 2-D pick box
        /// contains it), else the terrain, else the plane at the look-at height, capped at the horizon (§3.8).</summary>
        public override Vector2 ScreenToWorld(Vector2 screenPos)
        {
            var p = ScreenToWorld3D(screenPos);
            return new Vector2(p.X, p.Y);
        }

        public override Vector3 ScreenToWorld3D(Vector2 screenPos, bool forDrag = false)
        {
            var pick = Pick(screenPos, forDrag ? PickFilter.Terrain : PickFilter.All);
            if (!forDrag) LastPick = pick;
            return pick.Point;
        }

        /// <summary>The pick under a screen pixel with <paramref name="filter"/> (P2: a drag passes <see cref="PickFilter.Terrain"/>).</summary>
        public PickResult Pick(Vector2 screenPos, PickFilter filter)
        {
            bool forDrag = filter == PickFilter.Terrain;
            if (screenPos == _cachePixel && forDrag == _cacheForDrag && Pose == _cachePose) return _cacheResult;

            var dir = RayDirection(screenPos);
            var eye = Eye;
            float maxRange = MathF.Max(500f, Distance * 30f);
            var best = PickResult.None;
            foreach (var picker in Pickers)
            {
                if ((picker.Kind & filter) == 0) continue;
                if (picker.TryPick(eye, dir, maxRange, out var hit) && hit.Distance < best.Distance) best = hit;
            }
            if (best.Kind == PickKind.None)
            {
                if (dir.Z < -1e-4f)
                {
                    float t = (LookAt.Z - eye.Z) / dir.Z;
                    if (t > 0f && t < maxRange) best = new PickResult(eye + dir * t, PickKind.Ground, t);
                }
                if (best.Kind == PickKind.None)
                {
                    var flat = Flat(dir);
                    if (flat == Vector2.Zero) flat = Flat(Forward);
                    var far = new Vector2(eye.X, eye.Y) + flat * maxRange;
                    best = new PickResult(new Vector3(far, LookAt.Z), PickKind.None, maxRange);
                }
            }
            _cachePixel = screenPos; _cacheForDrag = forDrag; _cachePose = Pose; _cacheResult = best;
            return best;
        }

        /// <summary>Pixels per metre at a world point: the screen height over the view's height at that point's distance.</summary>
        public override float ZoomAt(Vector3 worldPoint)
        {
            float z = MathF.Max(0.1f, Vector3.Dot(worldPoint - Eye, Forward));
            return Viewport.Y / (2f * z * TanHalfFov);
        }

        /// <summary>The screen pixel of a ground point (at the look-at height); far off screen when behind the camera.</summary>
        public override Vector2 WorldToScreen(Vector2 worldPos) => WorldToScreen(new Vector3(worldPos, LookAt.Z));

        public Vector2 WorldToScreen(Vector3 world)
        {
            var vp = Viewport;
            var v = world - Eye;
            float z = Vector3.Dot(v, Forward);
            if (z <= 1e-4f) return new Vector2(-1e6f, -1e6f);
            float tan = TanHalfFov, aspect = vp.X / MathF.Max(1f, vp.Y);
            float nx = Vector3.Dot(v, Right) / (z * tan * aspect);
            float ny = Vector3.Dot(v, Up) / (z * tan);
            return new Vector2((nx + 1f) * 0.5f * vp.X, (1f - ny) * 0.5f * vp.Y);
        }

        /// <summary>The direction (HROT, unit) of the ray through a screen pixel.</summary>
        public Vector3 RayDirection(Vector2 screenPos)
        {
            var vp = Viewport;
            float nx = 2f * screenPos.X / MathF.Max(1f, vp.X) - 1f;
            float ny = 1f - 2f * screenPos.Y / MathF.Max(1f, vp.Y);
            float tan = TanHalfFov, aspect = vp.X / MathF.Max(1f, vp.Y);
            return Vector3.Normalize(Forward + Right * (nx * tan * aspect) + Up * (ny * tan));
        }

        public override void BeginMode()
        {
            var vp = Viewport;
            Raylib.DrawRectangleGradientV(0, 0, (int)vp.X, (int)vp.Y, LitShader.SkyTop, LitShader.SkyHorizon);
            var cam = ToRaylib();
            if (LitShader.IsCreated)
                LitShader.Shared.ApplyFrame(cam.Position, 1.1f / (Distance * 6f + 600f));
            Rlgl.SetClipPlanes(MathF.Max(0.05f, Distance * 0.001f), MathF.Max(2000f, Distance * 60f));
            Raylib.BeginMode3D(cam);
        }

        public override void EndMode()
        {
            Raylib.EndMode3D();
            Rlgl.SetClipPlanes(0.01, 1000.0);   // Raylib's defaults, for whatever draws 3-D after the map
        }

        private void SyncEquivalents()
        {
            float mpp = MetresPerPixel;
            if (float.IsFinite(mpp) && mpp > 0f) InnerCamera.Zoom = 1f / mpp;
            if (float.IsFinite(LookAt.X) && float.IsFinite(LookAt.Y)) InnerCamera.Target = new Vector2(LookAt.X, LookAt.Y);
        }

        /// <summary>The camera seam must not be poisoned (see <see cref="MapCamera.Zoom"/>): a non-finite pose snaps back.</summary>
        private void Sanitise()
        {
            if (!float.IsFinite(Distance) || Distance <= 0f) Distance = 300f;
            Distance = Math.Clamp(Distance, MinDistance, MaxDistance);
            if (!float.IsFinite(Yaw)) Yaw = NorthUpYaw;
            if (!float.IsFinite(Pitch)) Pitch = OverheadPitch;
            Pitch = Math.Clamp(Pitch, MinPitch, MaxPitch);
            if (!float.IsFinite(LookAt.X) || !float.IsFinite(LookAt.Y) || !float.IsFinite(LookAt.Z))
            {
                Fdp.Core.Logging.FdpLog<MapCamera3D>.Warn("[MapCamera3D] RECOVERING a non-finite look-at point; reset to the origin.");
                LookAt = Vector3.Zero;
            }
        }

        private static Vector2 Flat(Vector3 v)
        {
            var f = new Vector2(v.X, v.Y);
            float len = f.Length();
            return len < 1e-5f ? Vector2.Zero : f / len;
        }

        private static float WrapPi(float a)
        {
            while (a > MathF.PI) a -= 2f * MathF.PI;
            while (a < -MathF.PI) a += 2f * MathF.PI;
            return a;
        }
    }
}
