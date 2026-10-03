using System.Numerics;
using Fdp.Toolkit.Replication;

namespace Fdp.Presentation.Editing
{
    /// <summary>
    /// Brokers async map/entity pick requests between the component editor window
    /// and the application's spatial and entity-selection services.
    /// Requests are keyed on the stable <c>EditNode.JsonPath</c> so pending picks
    /// survive a <c>RebuildDocument</c> call.
    /// </summary>
    public interface IComponentPickerContext
    {
        /// <summary>Returns <see langword="true"/> if a pick is currently in flight for the given path.</summary>
        bool IsPickPendingFor(string jsonPath);

        /// <summary>Initiates an entity pick for the field at <paramref name="jsonPath"/>.</summary>
        void RequestEntityPick(string jsonPath, string[]? filterPresets);

        /// <summary>Initiates a world-location pick for the field at <paramref name="jsonPath"/>.</summary>
        void RequestLocationPick(string jsonPath);

        /// <summary>
        /// Attempts to consume a completed entity pick.
        /// Returns <see langword="true"/> and sets <paramref name="picked"/> — the picked entity's NETWORK id, the form an
        /// authored field stores (<c>DESIGN_Entity_Reference.md</c> D1) — when a result is available.
        /// </summary>
        bool TryConsumeEntityPick(string jsonPath, out EntityRef picked);

        /// <summary>
        /// Attempts to consume a completed location pick.
        /// Returns <see langword="true"/> and sets <paramref name="location"/> when a result is available.
        /// </summary>
        bool TryConsumeLocationPick(string jsonPath, out Vector3 location);
    }
}
