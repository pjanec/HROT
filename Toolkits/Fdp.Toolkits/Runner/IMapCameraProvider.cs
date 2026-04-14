using Fdp.Toolkit.Vis2D.Components;

namespace Fdp.Toolkit.Runner
{
    /// <summary>
    /// Optional interface for subsystems that own a 2-D map view.
    /// The <see cref="SubsystemOrchestrator"/> queries this interface on each
    /// subsystem at perspective-switch time to copy the outgoing map's camera state to
    /// the incoming one, so operators see the same region without any jump.
    /// Uses <see cref="MapCameraView"/> (a plain-data struct) so that this interface
    /// can live in the engine layer without a dependency on the Raylib rendering layer.
    /// </summary>
    public interface IMapCameraProvider
    {
        /// <summary>
        /// Returns the current camera view snapshot, or <see langword="null"/> when the
        /// visualization has not yet been initialised (e.g. headless mode or before the first frame).
        /// </summary>
        MapCameraView? GetCameraView();

        /// <summary>
        /// Applies a previously captured camera view snapshot.
        /// Used by <see cref="SubsystemOrchestrator"/> to sync cameras on perspective switch.
        /// </summary>
        void ApplyCameraView(MapCameraView view);
    }
}
