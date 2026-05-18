using Fdp.Toolkit.ReplayBrowser.Search;

namespace Fdp.Presentation.Editing
{
    /// <summary>
    /// Brokers async spatial area pick requests between the search panel
    /// and the map's bounding-box gizmo. Keys are the node's stable JsonPath.
    /// </summary>
    public interface ISpatialPickerContext
    {
        /// <summary>Returns true when a pick is currently active for <paramref name="jsonPath"/>.</summary>
        bool IsPickPendingFor(string jsonPath);

        /// <summary>Initiates a bounding-box pick for the field at <paramref name="jsonPath"/>.</summary>
        void RequestBoundingBoxPick(string jsonPath);

        /// <summary>
        /// Attempts to consume a completed bounding-box pick.
        /// Returns <see langword="true"/> and sets <paramref name="box"/> when a result is available.
        /// </summary>
        bool TryConsumeBoundingBoxPick(string jsonPath, out BoundingBox2D box);
    }
}
