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
        /// <summary>⭐ U-25 / S4 — the entity-list preset that narrows the list to the scenario's named <b>Points</b> (TKB type 8816): a host's list opener reads <c>tkb:&lt;type&gt;</c> as "only entities of that TKB type".</summary>
        public const string NamedPointsPreset = "tkb:8816";

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
        /// Returns <see langword="true"/> and sets <paramref name="location"/> (local metres, S0 item 5 / SR-135) when a
        /// result is available.
        /// </summary>
        bool TryConsumeLocationPick(string jsonPath, out Fdp.Toolkit.Replication.PointLink location);

        /// <summary>
        /// ⭐ <c>CE-1059</c> — <see langword="true"/> when this context can also offer the operator a searchable LIST of entities
        /// (the drawer then shows <c>List…</c> beside <c>Pick Entity</c>). The chosen entity arrives through the same
        /// <see cref="TryConsumeEntityPick"/>. 📄 <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §13.12 O3.
        /// </summary>
        bool CanListEntities => false;

        /// <summary>⭐ <c>CE-1059</c> — opens the entity list for the field at <paramref name="jsonPath"/>, narrowed by
        /// <paramref name="filterPresets"/> as a map pick is. A no-op where <see cref="CanListEntities"/> is false.</summary>
        void RequestEntityListPick(string jsonPath, string[]? filterPresets) { }

        /// <summary>
        /// ⭐ <c>CE-4206</c> R1 (DESIGN_Map_Picking_Unification §6.2, F2) — the LIVE reference scope of the world this surface edits: the names, GUIDs and keys the
        /// reference leaves (<c>EntityLink</c>, <c>PointLink</c>) show and write. <see langword="null"/> (the default) for a host with no repository
        /// (ExCon over DDS): the leaves then draw the plain forms, honestly.
        /// </summary>
        Fdp.Toolkit.Scenario.LiveRefScope? References => null;

        /// <summary>⭐ <c>CE-4206</c> R2 (F6) — starts editing the AREA SHAPE at <paramref name="jsonPath"/> on the map, from <paramref name="start"/> (circle / rectangle; anything else
        /// places a new circle). A no-op where the host cannot.</summary>
        void RequestAreaEdit(string jsonPath, Fdp.Toolkit.Replication.AreaLink start) { }

        /// <summary>⭐ U-10 / M2 (DESIGN_Show_On_Map §2) — the map's preview service: a row whose type has a place (<see cref="MapPreviews.Has"/>) draws the ⌖,
        /// hover previews, click pins. <see langword="null"/> (the default) for a host with no map: no ⌖.</summary>
        MapPreview? Preview => null;

        /// <summary>⭐ <c>CE-4206</c> R3 (F8) — this context can centre the map on a value (the leaves then draw <i>Show on map</i>).</summary>
        bool CanShowOnMap => false;

        /// <summary>⭐ <c>CE-4206</c> R3 (F8) — centres the map on the entity <paramref name="entity"/> (the map menu's Center View move). A no-op where <see cref="CanShowOnMap"/> is false.</summary>
        void ShowOnMap(Fdp.Toolkit.Replication.EntityRef entity) { }

        /// <summary>⭐ <c>CE-4206</c> R3 (F8) — centres the map on the point <paramref name="point"/> (local metres).</summary>
        void ShowOnMap(Fdp.Core.WorldPoint point) { }

        /// <summary>⭐ <c>CE-4206</c> R2 — a finished area edit for <paramref name="jsonPath"/>, once. <see langword="false"/> while none is ready (or it was cancelled).</summary>
        bool TryConsumeAreaEdit(string jsonPath, out Fdp.Toolkit.Replication.AreaLink area) { area = default; return false; }

        /// <summary>⭐ N-5 (DESIGN_Struct_Editor §5) — where the host's selection stands: the entity picker's distance column measures from it. False ⇒ no distances.</summary>
        bool TryGetSelectionPosition(out Fdp.Core.WorldPoint position) { position = default; return false; }
    }
}
