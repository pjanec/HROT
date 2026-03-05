using FDP.Toolkit.Vis2D.Components;

namespace Bagira.Runner.Abstractions
{
    /// <summary>
    /// Optional interface for subsystems that own a 2-D map view.
    /// The <see cref="Services.SubsystemOrchestrator"/> queries this interface on each
    /// subsystem at perspective-switch time to copy the outgoing map's camera state to
    /// the incoming one, so operators see the same region without any jump.
    /// </summary>
    public interface IMapCameraProvider
    {
        /// <summary>
        /// Returns the map camera, or <see langword="null"/> when the visualization has
        /// not yet been initialised (e.g. headless mode or before the first frame).
        /// </summary>
        MapCamera? GetMapCamera();
    }
}
