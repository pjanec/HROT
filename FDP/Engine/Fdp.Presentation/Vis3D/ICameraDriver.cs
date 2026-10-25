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

        /// <summary>
        /// ⭐ <c>CE-1352</c> — just before the frame is drawn: re-place the camera on what it follows as the world is NOW. The map's
        /// <see cref="Update"/> runs before the simulation's step, the bodies are drawn after it, so a camera placed in Update lagged its unit
        /// by however many steps that frame ran (0, 1, 2 …) — the stutter behind a possessed vehicle. Default: nothing.
        /// </summary>
        void BeforeDraw(MapCamera3D camera) { }

        /// <summary>The driver is being replaced or removed (release what it locked, e.g. the cursor).</summary>
        void Detached(MapCamera3D camera) { }

        /// <summary>
        /// ⭐ <c>CE-3289</c> (docs/DESIGN_Map_3D_Mode.md §6ab) — true when the view is a pair of EYES (first person: fog from the lens, the
        /// weather read at the eye); false when it looks AT a point like the free camera does (a film director: the weather read where
        /// it looks, fog starting part way there). Default: eyes.
        /// </summary>
        bool ViewsFromEye => true;
    }
}
