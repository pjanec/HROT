namespace Hrot.ScenarioEditor
{
    /// <summary>
    /// Describes the high-level operational state of the scenario editor session.
    /// Used by <c>IScenarioStateProvider</c> to let the UI adapters (e.g.
    /// <c>EditorPreviewAdapter</c>) determine the current editing phase without
    /// coupling to internal orchestration state machines.
    /// </summary>
    public enum ScenarioEditorState
    {
        /// <summary>No scenario is loaded; editor is at rest.</summary>
        Idle,

        /// <summary>A scenario is being loaded for editing; authoring operations are suspended.</summary>
        LoadingEdit,

        /// <summary>A scenario is loaded and authoring operations are active.</summary>
        OperatingEdit,

        /// <summary>The editor is entering preview (dry-run) mode; ECS snapshot in progress.</summary>
        LoadingPreview,

        /// <summary>Preview (dry-run) mode is active; authoring operations are suspended.</summary>
        OperatingPreview,

        /// <summary>The scenario is being saved to disk; authoring operations are suspended.</summary>
        SavingEdit,
    }
}
