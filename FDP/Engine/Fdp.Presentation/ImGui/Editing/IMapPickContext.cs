using Fdp.Toolkit.Behavior.Params;
using Fdp.Toolkit.Replication;

namespace Fdp.Presentation.Editing
{
    /// <summary>
    /// ⭐⭐ THE pick context: brokers async map/entity pick requests between an editing surface — the component editor
    /// and the mission panel — and the application's map pick service (<c>DESIGN_Map_Picking_Unification.md</c> P4;
    /// the mission panel's own <c>IPickInteractionContext</c> was deleted).
    /// Requests are keyed on the stable <c>EditNode.JsonPath</c> so pending picks
    /// survive a <c>RebuildDocument</c> call.
    /// </summary>
    public interface IMapPickContext
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
        /// Returns <see langword="true"/> and sets <paramref name="location"/> (geodetic latitude/longitude) when a
        /// result is available.
        /// </summary>
        bool TryConsumeLocationPick(string jsonPath, out PickableGeoPoint location);
    }
}
