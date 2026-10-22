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
    /// button is held</b> (so tools keep their keys; ⭐ CE-1142 Shift faster, Ctrl travel, both cross the map; a held key accelerates),
    /// the <b>middle button pans</b>, the <b>wheel dollies</b> (⭐ CE-1138 a lone click nudges, a fast spin travels).
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

        /// <summary>
        /// ⭐ CE-1072 (docs/DESIGN_Map_3D_Mode.md §6l) — how fast a wheel step glides home (s, the time constant): each click adds to a
        /// TARGET distance the camera eases toward, so the wheel flies instead of jumping. 🔒 User, 2026-10-14: "the scroll button based
        /// camera moving forwards and backwards jumps with each click instead of being a smooth flight". 0 = the old snap.
        /// </summary>
        public float WheelGlideSeconds { get; set; } = 0.12f;

        /// <summary>⭐ CE-1072 — how fast W/A/S/D/Q/E reach and leave their speed (s, the time constant): a fly that starts and stops
        /// over a few frames instead of a step. 0 = instant.</summary>
        public float FlyEaseSeconds { get; set; } = 0.08f;

        /// <summary>⭐ CE-1138 — the share of the distance ONE lone wheel click travels (it was 12 %: a big jump far out).</summary>
        public const float WheelBaseShare = 0.04f;

        /// <summary>⭐ CE-1138 — clicks closer together than this (s) are one STREAK; each click in a streak travels further.</summary>
        public const float WheelStreakSeconds = 0.35f;

        /// <summary>⭐ CE-1138 — how much each further click of a streak adds to the step (×), and the most a step may grow to.</summary>
        public const float WheelStreakGrowth = 0.6f, WheelStreakMax = 8f;

        /// <summary>⭐ CE-1142 — fly speed per metre of the EYE's height above the ground (m/s per m), at least <see cref="FlyMinSpeed"/>.</summary>
        public const float FlySpeedPerHeight = 0.4f, FlyMinSpeed = 0.6f;

        /// <summary>⭐ CE-1142 — a fly STARTS at this share of its speed, reaches it after <see cref="FlyRampSeconds"/>, and keeps
        /// accelerating to <see cref="FlyMaxAcceleration"/>× by <see cref="FlyAccelerateSeconds"/> of holding a key.</summary>
        public const float FlyStartShare = 0.25f, FlyRampSeconds = 1f, FlyMaxAcceleration = 5f, FlyAccelerateSeconds = 5f;

        /// <summary>⭐ CE-1142 — the modifiers, distinct: Shift ×4 (faster), Ctrl ×20 (travel), both ×100 (cross the map).</summary>
        public const float FlyShiftBoost = 4f, FlyCtrlBoost = 20f, FlyBothBoost = 100f;

        /// <summary>The ground height at a point — keeps the look-at point on the relief (R-248). Null ⇒ the look-at Z is kept.</summary>
        public Func<float, float, float>? GroundHeight { get; set; }

        /// <summary>The screen size when no window is open (tests); the window's size otherwise.</summary>
        public Vector2 FallbackViewport { get; set; } = new(1280, 720);

        private float _dt;
        private Vector2 _lastMouse;
        private bool _looking, _panning;
        private float _wheelTravel;                  // ⭐ CE-1072 — metres of eye travel along the view the wheel still owes (+ = in)
        private Vector3 _flyVelocity;               // ⭐ CE-1072 — the eased W/A/S/D velocity (m/s)
        private float _flyHeld;                     // ⭐ CE-1142 — seconds a move key has been held (the acceleration)
        private float _wheelIdle = float.PositiveInfinity;   // ⭐ CE-1138 — seconds since the last wheel click
        private int _wheelStreak;                   // ⭐ CE-1138 — clicks in the current streak (0 = a lone click)
        private float _wheelSign;
        private Vector2? _frameViewport;            // ⭐ CE-1072 — the window size, read once a frame
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
        public Vector3 Up
        {
            get
            {
                var level = new Vector3(-MathF.Sin(Pitch) * MathF.Cos(Yaw), -MathF.Sin(Pitch) * MathF.Sin(Yaw), MathF.Cos(Pitch));
                if (Roll == 0f) return level;
                var right = Vector3.Cross(Forward, level);
                return level * MathF.Cos(Roll) + right * MathF.Sin(Roll);   // ⭐ CE-1095 — banked
            }
        }

        /// <summary>⭐ CE-1095 (Hrot docs/DESIGN_Entity_Control.md §8f P-3) — the view's BANK (radians; + banks it right: its up leans
        /// right, as a cockpit's does when the aircraft rolls right). 0 for every view but a flyer's first person (its driver sets it each
        /// frame and clears it when it lets go). Not part of <see cref="CameraPose"/>: no saved or scenario view is ever banked.</summary>
        public float Roll { get; set; }
        public Vector3 Right => Vector3.Cross(Forward, Up);
        public Vector3 Eye => LookAt - Forward * Distance;

        /// <summary>A fixed screen size instead of the window's (tests; a camera drawing into a sub-viewport).</summary>
        public Vector2? ViewportOverride { get; set; }

        /// <remarks>⭐ CE-1072 — read from the window ONCE a frame (cleared in <see cref="Update"/>): 📐 measured 2026-10-14, asking
        /// the window per call cost 13 % of a 3-D frame, because <see cref="ZoomAt"/> asks it for every primitive drawn.</remarks>
        public Vector2 Viewport => ViewportOverride ?? (_frameViewport ??= WindowOpen()
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

        /// <summary>How long a "centre on" glides (s).</summary>
        public const float FocusSeconds = 0.35f;

        /// <summary>
        /// ⭐ CE-1134 — "centre on" in 3-D: the look-at point glides to the ground point (its height from the terrain), the yaw, pitch
        /// and distance kept, so the operator looks at the entity from where they were looking. ⛔ Not while a driver (first person)
        /// owns the camera. 🔒 User, 2026-10-16: <i>"center the map, either 2D or 3D, on a selected entity"</i>. Before, the base
        /// method set the 2-D target, which the 3-D camera never reads, so centring did nothing in 3-D.
        /// </summary>
        public override void FocusOn(Vector2 position, float zoom = -1f)
        {
            if (_driver != null || !float.IsFinite(position.X) || !float.IsFinite(position.Y)) return;
            float ground = GroundHeight?.Invoke(position.X, position.Y) ?? LookAt.Z;
            if (!float.IsFinite(ground)) ground = LookAt.Z;
            AnimateTo(Pose with { LookAt = new Vector3(position, ground) }, FocusSeconds);
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

        /// <summary>
        /// ⭐ <c>CE-3174</c> (M-9) — when set, <see cref="Update"/> and <see cref="HandleInput"/> are the driver's (a first-person view);
        /// null = the free camera. Setting it ends any animation; replacing it tells the old one it was <see cref="ICameraDriver.Detached"/>.
        /// </summary>
        public ICameraDriver? Driver
        {
            get => _driver;
            set
            {
                if (ReferenceEquals(_driver, value)) return;
                _driver?.Detached(this);
                _driver = value;
                _animSeconds = 0f;
                _animDone = null;
                _looking = _panning = false;
                _wheelTravel = 0f;
                _flyVelocity = Vector3.Zero;
            }
        }
        private ICameraDriver? _driver;

        public override void Update(float dt)
        {
            _cachePixel = new Vector2(float.NaN);   // a new frame: entities may have moved
            _frameViewport = null;                  // ⭐ CE-1072 — and the window may have been resized
            if (!float.IsFinite(dt) || dt < 0f) dt = 0f;
            _dt = dt;
            if (_driver != null)
            {
                _driver.Update(this, dt);
                Sanitise();
                SyncEquivalents();
                return;
            }
            if (float.IsFinite(_wheelIdle)) _wheelIdle += dt;
            if (!IsAnimating) GlideWheel(dt);
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
            if (_driver != null)   // ⭐ CE-3174 — a driver holds the map's input
            {
                _lastMouse = input.MousePosition;
                bool usedByDriver = _driver.HandleInput(this, input);
                if (usedByDriver) { Sanitise(); SyncEquivalents(); }
                return usedByDriver;
            }
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
                // ⭐ CE-1072 — a click adds eye travel along the view; Update glides it out; at the pivot a flat step, so the wheel
                //   keeps flying forward (Unity). ⭐ CE-1138 — a LONE click is a small step (WheelBaseShare of the distance); clicks in
                //   quick succession, the same way, grow it (a streak), so a fast spin travels far and one click nudges. 🔒 User,
                //   2026-10-16: "multiple wheel clicks needed to switch to faster mode for travelling long distances".
                float sign = MathF.Sign(wheel);
                _wheelStreak = _wheelIdle <= WheelStreakSeconds && sign == _wheelSign ? _wheelStreak + 1 : 0;
                _wheelIdle = 0f;
                _wheelSign = sign;
                float grow = WheelStepGrowth(_wheelStreak);
                float heading = Distance - _wheelTravel;
                _wheelTravel += heading > MinDistance * 1.01f
                    ? heading * (1f - MathF.Pow(1f - MathF.Min(0.5f, WheelBaseShare * grow), wheel))
                    : MathF.Max(0.5f, MinDistance) * wheel * grow;
                if (WheelGlideSeconds <= 0f) GlideWheel(float.PositiveInfinity);
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

                    // ⭐ CE-1142 — the speed follows how high the EYE is above the ground (it followed the look-at DISTANCE, which
                    //    after a fly is arbitrary: a metre above a soldier with the pivot 300 m ahead flew at 30 m/s). A press starts
                    //    slow and accelerates while held; the modifiers are distinct (Shift faster, Ctrl travel, both cross the map).
                    //    🔒 User, 2026-10-16: "way to fast for small movemenet at high zoom … slower starting speed with acceleration
                    //    desired … Ctrl+alt+shift modifiers are somewhat weak, similar to each other - i need a way how to travel larger distances".
                    bool shift = input.IsKeyDown(MapKeyboardKey.LeftShift) || input.IsKeyDown(MapKeyboardKey.RightShift);
                    bool ctrl  = input.IsKeyDown(MapKeyboardKey.LeftControl) || input.IsKeyDown(MapKeyboardKey.RightControl);
                    var move = Vector3.Zero;
                    if (input.IsKeyDown((MapKeyboardKey)KeyboardKey.W)) move += Forward;
                    if (input.IsKeyDown((MapKeyboardKey)KeyboardKey.S)) move -= Forward;
                    if (input.IsKeyDown((MapKeyboardKey)KeyboardKey.D)) move += Right;
                    if (input.IsKeyDown((MapKeyboardKey)KeyboardKey.A)) move -= Right;
                    if (input.IsKeyDown((MapKeyboardKey)KeyboardKey.E)) move += Vector3.UnitZ;
                    if (input.IsKeyDown((MapKeyboardKey)KeyboardKey.Q)) move -= Vector3.UnitZ;
                    // ⭐ CE-1072 — eased: the velocity heads for the keys' velocity with a short time constant, so a tap or a release
                    //   is a quick ramp rather than a step (a step at an uneven frame rate is what reads as a jerk).
                    _flyHeld = move != Vector3.Zero ? _flyHeld + _dt : 0f;
                    float speed = FlySpeed(EyeHeightAboveGround(), _flyHeld, shift, ctrl);
                    var wanted = move != Vector3.Zero ? Vector3.Normalize(move) * speed : Vector3.Zero;
                    float k = FlyEaseSeconds <= 0f ? 1f : 1f - MathF.Exp(-_dt / FlyEaseSeconds);
                    _flyVelocity += (wanted - _flyVelocity) * k;
                    if (wanted == Vector3.Zero && _flyVelocity.LengthSquared() < 1e-6f) _flyVelocity = Vector3.Zero;
                    LookAt += _flyVelocity * _dt;
                }
                _looking = true;
                used = true;
            }
            else { _looking = false; _flyVelocity = Vector3.Zero; _flyHeld = 0f; }

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

        /// <summary>⭐ CE-1138 — how much a wheel click of a streak's <paramref name="streak"/>-th further click grows the base step.</summary>
        public static float WheelStepGrowth(int streak) => MathF.Min(WheelStreakMax, 1f + WheelStreakGrowth * streak);

        /// <summary>
        /// ⭐ CE-1142 — the fly speed (m/s): <see cref="FlySpeedPerHeight"/> × the eye's <paramref name="heightAboveGround"/> (at least
        /// <see cref="FlyMinSpeed"/>), ramped from <see cref="FlyStartShare"/> to full over <see cref="FlyRampSeconds"/> of
        /// <paramref name="heldSeconds"/>, then accelerating to <see cref="FlyMaxAcceleration"/>× by <see cref="FlyAccelerateSeconds"/>;
        /// Shift / Ctrl / both boost it.
        /// </summary>
        public static float FlySpeed(float heightAboveGround, float heldSeconds, bool shift, bool ctrl)
        {
            float basis = MathF.Max(FlyMinSpeed, MathF.Max(0f, heightAboveGround) * FlySpeedPerHeight);
            float ramp = heldSeconds < FlyRampSeconds
                ? FlyStartShare + (1f - FlyStartShare) * (heldSeconds / FlyRampSeconds)
                : 1f + (FlyMaxAcceleration - 1f) * MathF.Min(1f, (heldSeconds - FlyRampSeconds) / (FlyAccelerateSeconds - FlyRampSeconds));
            float boost = shift && ctrl ? FlyBothBoost : ctrl ? FlyCtrlBoost : shift ? FlyShiftBoost : 1f;
            return basis * ramp * boost;
        }

        /// <summary>⭐ CE-1142 — how high the eye is above the ground under it (the eye's own height when no ground is known).</summary>
        public float EyeHeightAboveGround()
        {
            var eye = Eye;
            return GroundHeight != null ? eye.Z - GroundHeight(eye.X, eye.Y) : eye.Z;
        }

        /// <summary>
        /// ⭐ CE-1072 — one frame of the wheel's glide: a share of the owed travel moves the eye along the view (the look-at point
        /// stays); travel past <see cref="MinDistance"/> moves the look-at point forward too, so the wheel keeps flying instead of
        /// stopping at the pivot.
        /// </summary>
        private void GlideWheel(float dt)
        {
            if (_wheelTravel == 0f) return;
            float k = WheelGlideSeconds <= 0f || float.IsPositiveInfinity(dt) ? 1f : 1f - MathF.Exp(-dt / WheelGlideSeconds);
            float d = MathF.Abs(_wheelTravel) < 0.002f ? _wheelTravel : _wheelTravel * k;
            _wheelTravel -= d;
            float next = Distance - d;                       // the eye moves along the view; the look-at point stays
            if (next < MinDistance)
            {
                LookAt += Forward * (MinDistance - next);    // past the pivot: the pivot flies forward with the eye
                _followedXY = new Vector2(LookAt.X, LookAt.Y);   // a flight, not a pan: no relief snap
                next = MinDistance;
            }
            else if (next > MaxDistance) { next = MaxDistance; _wheelTravel = 0f; }
            Distance = next;
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
        private PickFilter _cacheFilter;
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
            // ⭐ CE-1126 — keyed on the FILTER (it was "is it a drag"): the crosshair's Aim and a click's All differ by more than that
            if (screenPos == _cachePixel && filter == _cacheFilter && Pose == _cachePose) return _cacheResult;

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
            _cachePixel = screenPos; _cacheFilter = filter; _cachePose = Pose; _cacheResult = best;
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

        /// <summary>
        /// ⭐ CE-1146 (docs/DESIGN_Map_3D_Mode.md §6aa) — the sky, sun and air to draw with; null = <see cref="SceneAtmosphere.Default"/>
        /// (a clear day). A host that knows the weather sets it — ⭐ in first person AND, since 2026-10-19, in the free camera (🔒 user:
        /// <i>"just showing the same weather as in FPS as the first slice"</i>; the 2026-10-16 "first person only" is superseded).
        /// </summary>
        public SceneAtmosphere? Atmosphere { get; set; }

        /// <summary>⭐ CE-1146 — the share of the look-at distance the free camera's fog does not touch: what it looks at stays clear, the world
        /// beyond fades by the visibility. First person (a driver) starts the fog at the eye.</summary>
        public const float FreeFogStartShare = 0.9f;

        /// <summary>⭐ CE-1146 — how far from the eye the fog starts (m): 0 while a driver looks out of a unit, else most of the look-at distance.</summary>
        public float FogStart => Driver is { ViewsFromEye: true } ? 0f : Distance * FreeFogStartShare;   // ⭐ CE-3289 — a director looks AT, like the free camera

        /// <summary>The far clip distance, m.</summary>
        public float FarClip => MathF.Max(2000f, Distance * 60f);

        /// <summary>The near clip's share of the look-at distance. ⛔ <c>R-1014</c> (🔒 user, 2026-10-20: <i>"the near plane seems to be too far
        /// … i need near plane as it was before"</i>): 0.001, as before <c>CE-1196</c> raised it to 0.005 to stop SUMO's roads flickering
        /// from afar — 0.005 clipped near geometry (1 m at 200 m out). The road flicker is fixed some other way (<c>CE-1196</c> reopened),
        /// never by moving the near plane.</summary>
        public const float NearClipShare = 0.001f;

        /// <summary>The near clip distance, m: <see cref="NearClipShare"/> of the look-at distance, never under 5 cm.</summary>
        public float NearClip => MathF.Max(0.05f, Distance * NearClipShare);

        /// <summary>⭐ <c>CE-1196</c> — the smallest depth difference the 24-bit depth buffer tells apart at <paramref name="atMetres"/> from
        /// the eye with the near clip <paramref name="near"/> (m): ≈ d² / (near · 2²⁴) for a far clip much farther than the near.</summary>
        public static float DepthStepAt(float atMetres, float near) => atMetres * atMetres / (near * 16_777_216f);

        /// <summary>⭐ <c>CE-1196</c> (docs/DESIGN_Map_3D_Mode.md §6z CA-4) — how many depth-buffer steps a layer lying ON the ground (SUMO's
        /// roads, the gizmo fills draped on it) is drawn toward the eye: enough to win over the ground it lies on at every distance, without
        /// moving the near plane (<c>R-1014</c>).</summary>
        public const float GroundBiasSteps = 4f;

        /// <summary>⭐ <c>CE-1196</c> — <see cref="GroundBiasSteps"/> in clip depth (NDC −1…1, two units per 2²⁴ steps): a constant there is a
        /// constant number of depth steps at every distance, so its reach in metres grows with the step itself
        /// (<see cref="GroundBiasMetresAt"/>).</summary>
        public static readonly float GroundBiasNdc = GroundBiasSteps * 2f / 16_777_216f;

        /// <summary>⭐ <c>CE-1196</c> — how far toward the eye (m) the ground bias moves something <paramref name="atMetres"/> away, near clip
        /// <paramref name="near"/>.</summary>
        public static float GroundBiasMetresAt(float atMetres, float near) => GroundBiasSteps * DepthStepAt(atMetres, near);

        /// <summary>⭐ CE-1146 — the fog constant for <paramref name="a"/>: its visibility's, never thinner than one that fades fully by
        /// the far clip (so the world ends in haze, not at an edge); without a visibility, the camera-distance haze it always had.</summary>
        public float FogDensityFor(in SceneAtmosphere a)
            => a.VisibilityM > 0f
                ? MathF.Max(SceneAtmosphere.FogFor(a.VisibilityM), SceneAtmosphere.FogFor(FarClip * 0.9f))
                : 1.1f / (Distance * 6f + 600f);

        public override void BeginMode()
        {
            var vp = Viewport;
            var a = Atmosphere ?? SceneAtmosphere.Default;
            Raylib.DrawRectangleGradientV(0, 0, (int)vp.X, (int)vp.Y, a.SkyTop, a.SkyHorizon);
            if (a.SunVisible) DrawSun(a);
            var cam = ToRaylib();
            if (LitShader.IsCreated)
            {
                LitShader.Shared.ApplyFrame(cam.Position, FogDensityFor(a), Atmosphere is { VisibilityM: > 0f } ? FogStart : 0f);
                LitShader.Shared.ApplyAtmosphere(a);
            }
            Rlgl.SetClipPlanes(NearClip, FarClip);
            Raylib.BeginMode3D(cam);
            // ⭐ CE-1146 (§6aa.CL) — the cloud decks, as background: before every layer, without depth, so the world is drawn over them
            if (a.Clouds is { Length: > 0 } clouds) CloudDeckRenderer.Draw(this, clouds);
        }

        /// <summary>⭐ CE-1146 — the sun disc and its glow, on the sky behind the world (drawn before it, so terrain covers a set sun).</summary>
        private void DrawSun(in SceneAtmosphere a)
        {
            if (Vector3.Dot(a.ToSun, Forward) <= 0.05f) return;
            var at = WorldToScreen(Eye + a.ToSun * (FarClip * 0.5f));
            if (!float.IsFinite(at.X) || !float.IsFinite(at.Y)) return;
            Raylib.DrawCircleV(at, 70f, new Color(a.SunDisc.R, a.SunDisc.G, a.SunDisc.B, (byte)40));
            Raylib.DrawCircleV(at, 30f, new Color(a.SunDisc.R, a.SunDisc.G, a.SunDisc.B, (byte)90));
            Raylib.DrawCircleV(at, 13f, a.SunDisc);
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
