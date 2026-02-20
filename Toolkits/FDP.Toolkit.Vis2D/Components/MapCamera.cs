using System.Numerics;
using Raylib_cs;
using FDP.Toolkit.Vis2D.Abstractions;
using FDP.Toolkit.Vis2D.Input;

namespace FDP.Toolkit.Vis2D.Components
{
    /// <summary>
    /// Wrapper around Raylib Camera2D with mouse control.
    /// Handles pan, zoom-to-cursor, and coordinate conversion.
    /// </summary>
    public class MapCamera
    {
        public Camera2D InnerCamera; // Public field for direct access if needed, or property
        
        public float Zoom 
        { 
            get => InnerCamera.Zoom; 
            set => InnerCamera.Zoom = value; 
        }

        public Vector2 Target
        {
            get => InnerCamera.Target;
            set => InnerCamera.Target = value;
        }
        
        public Vector2 Offset
        {
            get => InnerCamera.Offset;
            set => InnerCamera.Offset = value;
        }

        // Configuration
        public float ZoomSpeed { get; set; } = 0.1f;
        public float MinZoom { get; set; } = 0.1f;
        public float MaxZoom { get; set; } = 10.0f;
        
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

        public virtual void Update(float dt)
        {
            // Validating targets
            if (_targetZoom < MinZoom) _targetZoom = MinZoom;
            if (_targetZoom > MaxZoom) _targetZoom = MaxZoom;

            // Interpolate Zoom
            InnerCamera.Zoom = Lerp(InnerCamera.Zoom, _targetZoom, dt * ZoomDamping);

            // Interpolate Target
            InnerCamera.Target = Vector2.Lerp(InnerCamera.Target, _targetTarget, dt * PanDamping);
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
            _targetTarget = position;
            if (zoom > 0) _targetZoom = zoom;
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
    }
}
