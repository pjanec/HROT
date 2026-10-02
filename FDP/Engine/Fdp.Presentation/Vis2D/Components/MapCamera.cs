using System.Numerics;
using Raylib_cs;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Input;
using Fdp.Toolkit.Runner;

namespace Fdp.Toolkit.Vis2D.Components
{
    /// <summary>
    /// Wrapper around Raylib Camera2D with mouse control.
    /// Handles pan, zoom-to-cursor, and coordinate conversion.
    /// </summary>
    public class MapCamera : IMapCameraProvider
    {
        public Camera2D InnerCamera; // Public field for direct access if needed, or property
        
        /// <summary>
        /// ⭐⭐⭐ <b>THE SEAM CANNOT BE POISONED.</b> 📄 Added <c>2026-09-20</c> after a measured operator
        /// defect: the editor's 2-D map became completely unclickable and the marquee invisible, because
        /// <c>Raylib.GetScreenToWorld2D</c> — which is <c>(screen − Offset) / Zoom + Target</c> — returned
        /// <c>NaN</c> for every mouse position.
        ///
        /// <para>🔴 <b>Why ONE bad write is permanent.</b> <c>NaN</c> propagates: once it reaches
        /// <c>Target</c> or <c>Zoom</c>, every later screen↔world conversion is <c>NaN</c>, every hit-test
        /// comparison is <c>false</c>, and the terminal falls through to the canvas on EVERY click —
        /// forever, with no error anywhere. 📐 Measured from the product: <c>frame=763 pickable=708</c>
        /// (the pick boxes were all there) at <c>worldPos=(NaN,NaN)</c>.</para>
        ///
        /// <para>⛔ <b>Rejecting is right; repairing silently is not.</b> A dropped write keeps the camera
        /// usable and REPORTS the caller, so the producer can be found. ⭐ Same shape as
        /// <c>EmitPickBox</c>'s §6.8 <c>networkId == 0</c> guard: the rule belongs on the seam that owns
        /// the invariant, so every caller gets it.</para>
        ///
        /// <para>⚠ <c>Zoom</c> additionally rejects <c>&lt;= 0</c>: a zero zoom is a division by zero in
        /// the same conversion, and <c>SetZoom</c> already guarded it — ⛔ but this setter did not, so
        /// the guard was reachable only through one of the two doors.</para>
        /// </summary>
        public float Zoom 
        { 
            get => InnerCamera.Zoom; 
            set
            {
                if (!float.IsFinite(value) || value <= 0f)
                {
                    Fdp.Core.Logging.FdpLog<MapCamera>.Warn(
                        $"[MapCamera] REJECTED a non-finite or non-positive Zoom ({value}). " +
                        "Accepting it would make every screen<->world conversion NaN and silently " +
                        "un-click the whole map. The camera is unchanged; fix the caller.");
                    return;
                }
                InnerCamera.Zoom = value;
            }
        }

        public Vector2 Target
        {
            get => InnerCamera.Target;
            set
            {
                if (!IsFinite(value))
                {
                    Fdp.Core.Logging.FdpLog<MapCamera>.Warn(
                        $"[MapCamera] REJECTED a non-finite Target ({value.X},{value.Y}). " +
                        "Accepting it would make every screen<->world conversion NaN and silently " +
                        "un-click the whole map. The camera is unchanged; fix the caller.");
                    return;
                }
                InnerCamera.Target = value;
            }
        }

        /// <summary>⭐ One predicate, so the three seams cannot disagree about what "usable" means.</summary>
        internal static bool IsFinite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
        
        public Vector2 Offset
        {
            get => InnerCamera.Offset;
            set => InnerCamera.Offset = value;
        }

        // Configuration
        public float ZoomSpeed { get; set; } = 0.1f;
        public float MinZoom { get; set; } = 0.1f;
        public float MaxZoom { get; set; } = 10.0f;
        
        // ── NEW: Smoothing Toggle ─────────────────────────────────────────
        public bool EnableSmoothing { get; set; } = false;

        public Vis2DInputMap InputMap { get; set; } = Vis2DInputMap.Default;

        // State for dragging
        private Vector2 _lastMousePos;
        private bool _isDragging;


        private Vector2 _targetTarget;
        private float _targetZoom;

        // Damping factors
        public float ZoomDamping { get; set; } = 15.0f;
        public float PanDamping { get; set; } = 20.0f;

        public MapCamera()
        {
            InnerCamera = new Camera2D();
            InnerCamera.Zoom = 1.0f;
            InnerCamera.Rotation = 0.0f;
            InnerCamera.Offset = Vector2.Zero;
            InnerCamera.Target = Vector2.Zero;

            _targetZoom = 1.0f;
            _targetTarget = Vector2.Zero;
        }

        /// <summary>
        /// ⭐⭐⭐ <b>THE LAST LINE OF DEFENCE, and the one that matters — this is the ONLY per-frame writer
        /// of <see cref="InnerCamera"/>.</b> 📄 Added <c>2026-09-20</c> with the property guards; see the
        /// <c>Zoom</c> setter for the measured defect (the whole 2-D map silently un-clickable).
        ///
        /// <para>🔴🔴 <b>TWO HOLES THE PROPERTY GUARDS DO NOT COVER, and both were live:</b>
        /// <list type="number">
        /// <item>⛔⛔ <b>The old clamp could not see <c>NaN</c>.</b> <c>if (_targetZoom &lt; MinZoom)</c> and
        /// <c>if (_targetZoom &gt; MaxZoom)</c> are <b>both false</b> for <c>NaN</c> — every comparison
        /// against <c>NaN</c> is false — so a <c>NaN</c> target sailed through a block whose entire job was
        /// validation.</item>
        /// <item>⛔⛔ <b>These assignments write the FIELD, not the guarded properties</b>, so they bypass
        /// the checks added beside them.</item>
        /// </list></para>
        ///
        /// <para>🔒 <b>And <c>NaN</c> here is SELF-SUSTAINING:</b> <c>Lerp(NaN, x, t)</c> is <c>NaN</c>, so
        /// once it lands in <c>InnerCamera</c> the smoothing re-poisons it every frame forever. ⭐ That is
        /// exactly the observed signature — a valid world position on the first frames, then <c>NaN</c> for
        /// the rest of the session.</para>
        ///
        /// <para>⭐⭐ <b>So this RECOVERS rather than merely refusing.</b> A camera found non-finite is
        /// snapped back to its (validated) targets, which means an already-poisoned session repairs itself
        /// on the next frame instead of staying dead until restart. ⛔ It still WARNS every time it has to,
        /// because a silent repair would hide the producer.</para>
        /// </summary>
        public virtual void Update(float dt)
        {
            // ⚠ dt first: a non-finite or negative dt makes every interpolation below non-finite, and it
            //   arrives from outside this class. Treat it as "no interpolation this frame".
            if (!float.IsFinite(dt) || dt < 0f) dt = 0f;

            // ⭐ Validate the TARGETS with finite-aware logic. ⛔ `!(x >= Min)` is NOT the same as
            //   `x < Min` — it is true for NaN, which is precisely the case the old clamp missed.
            if (!(_targetZoom >= MinZoom) || !(_targetZoom <= MaxZoom))
            {
                if (!float.IsFinite(_targetZoom))
                {
                    Fdp.Core.Logging.FdpLog<MapCamera>.Warn(
                        $"[MapCamera] target zoom was non-finite ({_targetZoom}); reset to 1. " +
                        "Something wrote a NaN/Inf zoom — fix the caller.");
                    _targetZoom = 1.0f;
                }
                else
                {
                    _targetZoom = System.Math.Clamp(_targetZoom, MinZoom, MaxZoom);
                }
            }

            if (!IsFinite(_targetTarget))
            {
                Fdp.Core.Logging.FdpLog<MapCamera>.Warn(
                    $"[MapCamera] target position was non-finite ({_targetTarget.X},{_targetTarget.Y}); " +
                    "reset to origin. Something focused the camera on a NaN position — fix the caller.");
                _targetTarget = Vector2.Zero;
            }

            // ⭐⭐⭐ RECOVERY: if the camera is ALREADY poisoned, snapping it to the validated targets is
            //    the only way out — Lerp would carry the NaN forever.
            if (!float.IsFinite(InnerCamera.Zoom) || InnerCamera.Zoom <= 0f || !IsFinite(InnerCamera.Target))
            {
                Fdp.Core.Logging.FdpLog<MapCamera>.Warn(
                    $"[MapCamera] RECOVERING a non-finite camera (zoom={InnerCamera.Zoom}, " +
                    $"target=({InnerCamera.Target.X},{InnerCamera.Target.Y})). Every screen<->world " +
                    "conversion was NaN, which makes the whole map unclickable.");
                InnerCamera.Zoom   = _targetZoom;
                InnerCamera.Target = _targetTarget;
                return;
            }

            if (EnableSmoothing)
            {
                // Interpolate
                InnerCamera.Zoom = Lerp(InnerCamera.Zoom, _targetZoom, dt * ZoomDamping);
                InnerCamera.Target = Vector2.Lerp(InnerCamera.Target, _targetTarget, dt * PanDamping);
            }
            else
            {
                // Snap directly
                InnerCamera.Zoom = _targetZoom;
                InnerCamera.Target = _targetTarget;
            }
        }

        private float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        public virtual bool HandleInput(IInputProvider input)
        {
            // Gather inputs
            float wheel = input.MouseWheelMove;
            Vector2 mousePos = input.MousePosition;
            bool isPanDown = input.IsMouseButtonDown(InputMap.PanButton);
            bool isCaptured = input.IsMouseCaptured;

            return ProcessInput(wheel, mousePos, isPanDown, isCaptured);
        }

        /// <summary>
        /// Update camera targets based on inputs. Public for testing.
        /// Returns true if input was consumed/handled.
        /// </summary>
        public bool ProcessInput(float wheelMove, Vector2 mousePos, bool isPanDown, bool isInputCaptured)
        {
            if (isInputCaptured)
            {
                _isDragging = false;
                return false;
            }

            bool interacted = false;

            // Zoom
            if (wheelMove != 0)
            {
                interacted = true;
                // Zoom logic:
                // 1. Get world point based on CURRENT state (approximate start point)
                // actually, for stability, we might want to base it on TARGET state if we are already moving?
                // But the user sees the CURRENT state. So they point at a pixel on screen corresponding to world X.
                Vector2 mouseWorldBefore = ScreenToWorld(mousePos);

                // 2. Adjust target zoom
                // Use current target zoom as base to avoid "fighting" the interpolation
                float newZoom = _targetZoom + (wheelMove * ZoomSpeed * _targetZoom);
                
                newZoom = System.Math.Clamp(newZoom, MinZoom, MaxZoom);
                _targetZoom = newZoom;

                // 3. Adjust target target
                // constraint: ScreenToWorld(mousePos) using (TargetZoom, TargetTarget) == mouseWorldBefore
                // mouseWorldBefore = (mousePos - Offset) / TargetZoom + TargetTarget
                // TargetTarget = mouseWorldBefore - (mousePos - Offset) / TargetZoom
                
                _targetTarget = mouseWorldBefore - (mousePos - InnerCamera.Offset) / _targetZoom;
            }

            // Pan
            if (isPanDown)
            {
                interacted = true;
                if (!_isDragging)
                {
                    _isDragging = true;
                    _lastMousePos = mousePos;
                    // Reset targets to current state on drag start to prevent jumps?
                    // _targetTarget = InnerCamera.Target; 
                    // _targetZoom = InnerCamera.Zoom;
                    // Actually, if we are animating somewhere and user grabs, we should probably stop and grab.
                    _targetTarget = InnerCamera.Target;
                    _targetZoom = InnerCamera.Zoom;
                }
                else
                {
                    Vector2 deltaWrapper = mousePos - _lastMousePos;
                    
                    // We move target by -deltaWorld
                    // deltaWorld = deltaScreen / CurrentZoom? Or TargetZoom?
                    // To interact 1:1 with cursor, we must use CurrentZoom.
                    
                    Vector2 deltaWorld = deltaWrapper / InnerCamera.Zoom;
                    
                    // We modify the TargetTarget directly to "pull" it.
                    // If Damping is high, InnerCamera.Target follows closely.
                    // Effectively we are setting the desired position.
                    
                    _targetTarget -= deltaWorld;
                    
                    // Also maintain InnerCamera.Target close to mouse for responsivness if needed?
                    // But Update() will handle it.
                    
                    _lastMousePos = mousePos;
                }
            }
            else
            {
                _isDragging = false;
            }

            return interacted;
        }

        public void FocusOn(Vector2 position, float zoom = -1f)
        {
            // ⭐⭐⭐ THE THIRD DOOR, and the one most likely to be handed a bad value: callers focus on an
            //    ENTITY POSITION, and a component can hold NaN. ⛔ `_targetTarget` is copied into
            //    InnerCamera.Target every frame by Update(), so poisoning it here is exactly as permanent
            //    as writing Target directly. 📄 See the Target setter for the measured defect.
            if (!IsFinite(position))
            {
                Fdp.Core.Logging.FdpLog<MapCamera>.Warn(
                    $"[MapCamera] REJECTED FocusOn a non-finite position ({position.X},{position.Y}) — " +
                    "this is the write that un-clicks the map. The camera is unchanged; fix the caller.");
                return;
            }

            _targetTarget = position;
            if (zoom > 0 && float.IsFinite(zoom)) _targetZoom = zoom;
        }

        /// <summary>
        /// Instantly copies all camera state (current and target) from <paramref name="source"/>
        /// so that this camera shows exactly the same view without any animation.
        /// Use when two map views must be aligned at the moment of a perspective switch.
        /// </summary>
        public void SnapTo(MapCamera source)
        {
            InnerCamera.Zoom   = source.InnerCamera.Zoom;
            InnerCamera.Target = source.InnerCamera.Target;
            InnerCamera.Offset = source.InnerCamera.Offset;
            _targetZoom   = source._targetZoom;
            _targetTarget = source._targetTarget;
            _isDragging   = false;
        }


        public virtual void BeginMode()
        {
            Raylib.BeginMode2D(InnerCamera);
        }

        public virtual void EndMode()
        {
            Raylib.EndMode2D();
        }

        public virtual Vector2 ScreenToWorld(Vector2 screenPos)
        {
            // Calculate manually to support unit testing (Raylib context might not be available) and ensure consistency
            // Formula matches Raylib's GetScreenToWorld2D for Rotation=0
            // World = (Screen - Offset) / Zoom + Target
            return (screenPos - InnerCamera.Offset) / InnerCamera.Zoom + InnerCamera.Target;
        }

        public virtual Vector2 WorldToScreen(Vector2 worldPos)
        {
            // Calculate manually to support unit testing
            // Screen = (World - Target) * Zoom + Offset
            return (worldPos - InnerCamera.Target) * InnerCamera.Zoom + InnerCamera.Offset;
        }

        // ── IMapCameraProvider implementation ─────────────────────────────────

        /// <inheritdoc/>
        public MapCameraView? GetCameraView() => new MapCameraView
        {
            Target       = InnerCamera.Target,
            Offset       = InnerCamera.Offset,
            Zoom         = InnerCamera.Zoom,
            SmoothTarget = _targetTarget,
            SmoothZoom   = _targetZoom,
        };

        /// <inheritdoc/>
        public void ApplyCameraView(MapCameraView view)
        {
            InnerCamera.Zoom   = view.Zoom;
            InnerCamera.Target = view.Target;
            InnerCamera.Offset = view.Offset;
            _targetZoom   = view.SmoothZoom;
            _targetTarget = view.SmoothTarget;
            _isDragging   = false;
        }
    }
}
