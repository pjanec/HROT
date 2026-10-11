using Fdp.Toolkit.Vis2D.Abstractions;

namespace Fdp.Toolkit.Vis3D
{
    /// <summary>
    /// ⭐ <c>CE-3174</c> (M-9; <c>docs/DESIGN_Entity_Control.md</c> §8d) — something that drives <see cref="MapCamera3D"/> instead of the
    /// operator's free camera: a first-person view on a possessed unit, later a final engine (C-1). While set
    /// (<see cref="MapCamera3D.Driver"/>), the camera's own update and input are replaced by the driver's — and because the canvas
    /// gives the camera the input before any layer (<c>MapCanvas.HandleInput</c>), a driver that consumes it holds the map's focus:
    /// no click, drag or context menu reaches a layer.
    /// </summary>
    public interface ICameraDriver
    {
        /// <summary>Places the camera for this frame (pose, distance).</summary>
        void Update(MapCamera3D camera, float dt);

        /// <summary>Reads the operator's input. Return true to consume it (the usual answer).</summary>
        bool HandleInput(MapCamera3D camera, IInputProvider input);

        /// <summary>The driver is being replaced or removed (release what it locked, e.g. the cursor).</summary>
        void Detached(MapCamera3D camera) { }
    }
}
