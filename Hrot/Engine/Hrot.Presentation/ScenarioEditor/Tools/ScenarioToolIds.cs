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
