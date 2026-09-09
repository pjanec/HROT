using Hrot.Common;

namespace Hrot.ScenarioEditor.Tools
{
    /// <summary>
    /// ⭐⭐ <b>The tool vocabulary, and its bridge to the legacy <see cref="EditorTool"/> enum.</b>
    /// 📄 <c>docs/UX/UX_Feature_Tool_Model.md</c> §4.
    ///
    /// <para>🔒 <c>Q27</c> ruling D — <i>"an action activates a tool; a tool is not an action"</i>. ⇒ these
    /// ids are the TOOL vocabulary and are deliberately NOT <c>GlobalActionId</c>s.</para>
    ///
    /// <para>⚠⚠ <b><see cref="EditorTool"/> is NOT retired here, and that is deliberate.</b> It is the
    /// wire form of <c>ActivateEditorToolEvent</c>, which both hosts publish and which
    /// <c>PresentationComponentRegistry</c> registers. ⛔ Retiring it is a separate change with its own
    /// blast radius; this slice adds a vocabulary beside it and maps between them in ONE place, so there is
    /// no second mapping to drift. ⭐ Its own header already records that the enum is *"the surviving
    /// vocabulary of a deleted architecture"* — that stays true and is `UXI-07`'s later business.</para>
    /// </summary>
    public static class ScenarioToolIds
    {
        /// <summary>🔒 The NULL modal tool — a real state, not a dead case. <c>Q27</c>, and it supersedes
        /// <c>UXI-02</c>'s proposal to delete the dead <c>Select</c> button.</summary>
        public const string Select  = "scenario.select";

        /// <summary>Entity placement / spawn mode.</summary>
        public const string Spawn   = "scenario.spawn";

        /// <summary>Vertex edit on an <c>EditablePolyline</c>.</summary>
        public const string Edit    = "scenario.edit";

        /// <summary>Waypoint edit on a <c>RoutePlan</c>.</summary>
        public const string Route   = "scenario.route";

        /// <summary>Measurement line.</summary>
        public const string Measure = "scenario.measure";

        /// <summary>Entity rotation.</summary>
        public const string Rotate  = "scenario.rotate";

        // ── UXI-07 step 4a — the modal interactions that used to arm the arbiter DIRECTLY ─────────
        //
        // ⭐⭐ These are TOOLS by Q27's definition — "at most one active tool requiring focus per
        //    subsystem" — even though none of them belongs on the toolbar. 🔒 That is exactly why
        //    ToolDescriptor.ShowOnToolbar defaults to FALSE: the user's ruling, "tools do not
        //    necessarily need to be shown on the toolbar — this must be optional."
        // ⛔ They were NOT tool ids before because they were never routed through an arbiter; §4.8's
        //    inventory is what turned them from "adapter internals" into named modals.

        /// <summary>Zone obstacle placement — <c>IZoneAuthoringController.StartObstaclePlacementMode</c>.</summary>
        public const string PlaceObstacle = "scenario.place.obstacle";

        /// <summary>
        /// Entity placement driven by a REMOTE creation session (IG's <c>MapCommandController</c>).
        /// ⚠ Deliberately distinct from <see cref="Spawn"/>: <c>Spawn</c> is the operator arming the
        /// local spawn adapter, this is a request arriving from ExCon with its own session id and
        /// lifetime. ⭐ Same gizmo, different owner — and the controller must be able to tell them apart
        /// so cancelling one does not silently orphan the other's session.
        /// </summary>
        public const string PlaceRemoteEntity = "scenario.place.remote-entity";

        /// <summary>Area (polygon) authoring — <c>ISpawnController.StartAreaAuthoringMode</c>.</summary>
        public const string PlaceArea = "scenario.place.area";

        /// <summary>Route (waypoint sequence) authoring — <c>ISpawnController.StartRouteAuthoringMode</c>.</summary>
        public const string PlaceRoute = "scenario.place.route";

        // ── UXI-07 step 4b — the PICKERS ─────────────────────────────────────────────────────────
        //
        // ⭐⭐⭐ These are INTERRUPTIONS, not switches: they are pushed with ToolController.PushModal so
        //    the tool underneath is SUSPENDED and resumes when the pick finishes. ⛔ Activating them
        //    would DESTROY a half-drawn route instead of pausing it — §4.8's measured reason 4b could
        //    not be built before PushModal existed.

        /// <summary>Pick one world location — <c>IMapPickService.PickLocationAsync</c>.</summary>
        public const string PickLocation = "scenario.pick.location";

        /// <summary>Pick one entity — <c>IMapPickService.PickEntityAsync</c>.</summary>
        public const string PickEntity = "scenario.pick.entity";

        /// <summary>Box-select entities — <c>IMapPickService.PickAreaEntitiesAsync</c>.</summary>
        public const string PickArea = "scenario.pick.area";

        /// <summary>
        /// Drag a spatial BOUNDS rectangle — <c>ISpatialPickerContext.RequestBoundingBoxPick</c>.
        /// ⚠ Deliberately distinct from <see cref="PickArea"/>: same gesture, different meaning and a
        /// different result. <c>PickArea</c> returns the ENTITIES inside the box; this returns the BOX,
        /// as a search filter. ⛔ Collapsing them would make one of the two lie about what it yields.
        /// </summary>
        public const string PickBounds = "scenario.pick.bounds";

        /// <summary>
        /// The ONE mapping from the legacy enum to the tool vocabulary. ⛔ If a second one appears, that is
        /// the duplication this issue exists to remove.
        /// </summary>
        public static string ForEditorTool(EditorTool tool) => tool switch
        {
            EditorTool.Select  => Select,
            EditorTool.Spawn   => Spawn,
            EditorTool.Edit    => Edit,
            EditorTool.Route   => Route,
            EditorTool.Measure => Measure,
            EditorTool.Rotate  => Rotate,
            _                  => tool.ToString(),
        };
    }
}
