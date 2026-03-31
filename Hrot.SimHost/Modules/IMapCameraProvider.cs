using FDP.Toolkit.Vis2D.Components;

namespace Hrot.SimHost.Modules
{
    /// <summary>
    /// Exposes a presentation module's <see cref="MapCamera"/> so that
    /// <c>PerspectiveCoordinatorSystem</c> can snap cameras across active/inactive tiers
    /// on a perspective switch.
    /// </summary>
    public interface IMapCameraProvider
    {
        /// <summary>Returns the <see cref="MapCamera"/> owned by this presentation module.</summary>
        MapCamera GetCamera();
    }
}
