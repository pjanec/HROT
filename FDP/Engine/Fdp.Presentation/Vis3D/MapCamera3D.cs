using System;
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
    /// <para>⚠ <b>North is UP in 3-D, and the 2-D map draws it DOWN</b> (measured: world Y goes screen-down in
    /// <see cref="MapCamera"/>, and the 2-D renderer draws world XY straight, <c>DebugPrimitiveRenderer2D.cs:273</c>) — the 2-D map
    /// is a mirror of the view from above. A camera above the ground cannot show a mirror, so the switch keeps east on the right and
    /// flips north–south (see <see cref="MapViewSwitch"/>).</para>
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
                LookAt = LookAt with { Z = GroundHeight(LookAt.X, LookAt.Y) };
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

                    float speed = MathF.Max(5f, Distance) * (input.IsKeyDown(MapKeyboardKey.LeftShift) ? 3f : 1f) * _dt;
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

        /// <summary>The ground point under a screen pixel — S1: the plane at the look-at height, capped at the horizon.</summary>
        public override Vector2 ScreenToWorld(Vector2 screenPos)
        {
            var dir = RayDirection(screenPos);
            var eye = Eye;
            float maxRange = MathF.Max(500f, Distance * 30f);
            if (dir.Z < -1e-4f)
            {
                float t = (LookAt.Z - eye.Z) / dir.Z;
                if (t > 0f && t < maxRange) { var p = eye + dir * t; return new Vector2(p.X, p.Y); }
            }
            var flat = Flat(dir);
            if (flat == Vector2.Zero) flat = Flat(Forward);
            return new Vector2(eye.X, eye.Y) + flat * maxRange;
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
