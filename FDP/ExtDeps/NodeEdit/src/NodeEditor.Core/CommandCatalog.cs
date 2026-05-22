namespace NodeEditor.Core;

/// <summary>
/// Canonical command-ID strings. Keep in sync with spec D.0 and the
/// editor's command publication code.
/// </summary>
public static class CommandCatalog
{
    // File / Asset
    public const string Save             = "editor.save";
    public const string SaveAll          = "editor.save-all";
    public const string Reload           = "editor.reload";
    public const string Compile          = "editor.compile";
    public const string QuickReload      = "editor.quick-reload";

    // Edit
    public const string Undo             = "editor.undo";
    public const string Redo             = "editor.redo";
    public const string Cut              = "editor.cut";
    public const string Copy             = "editor.copy";
    public const string Paste            = "editor.paste";
    public const string Duplicate        = "editor.duplicate";
    public const string SelectAll        = "editor.select-all";
    public const string SelectNone       = "editor.select-none";
    public const string InvertSelection  = "editor.invert-selection";
    public const string DeleteSelection  = "editor.delete-selection";

    // View
    public const string FrameAll         = "editor.frame-all";
    public const string FrameSelection   = "editor.frame-selection";
    public const string ZoomIn           = "editor.zoom-in";
    public const string ZoomOut          = "editor.zoom-out";
    public const string ZoomReset        = "editor.zoom-reset";
    public const string ToggleGrid       = "editor.toggle-grid";
    public const string ToggleMinimap    = "editor.toggle-minimap";

    // Navigation
    public const string NextTab          = "editor.next-tab";
    public const string PrevTab          = "editor.prev-tab";
    public const string CloseTab         = "editor.close-tab";
    public const string GoToGraph        = "editor.go-to-graph";
    public const string NextError        = "editor.next-error";
    public const string PrevError        = "editor.prev-error";
    public const string NextBookmark     = "editor.next-bookmark";
    public const string PrevBookmark     = "editor.prev-bookmark";

    // Add/Create
    public const string AddNode             = "editor.add-node";
    public const string AddComment          = "editor.add-comment";
    public const string AddReroute          = "editor.add-reroute";
    public const string CreateFunction      = "editor.create-function";
    public const string CreateCustomEvent   = "editor.create-custom-event";
    public const string CreateVariable      = "editor.create-variable";
    public const string CreateMacro         = "editor.create-macro";

    // Refactor
    public const string CollapseToFunction  = "editor.collapse-to-function";
    public const string CollapseToMacro     = "editor.collapse-to-macro";
    public const string CollapseToComment   = "editor.collapse-to-comment";
    public const string ExpandNode          = "editor.expand-node";
    public const string PromoteToVariable   = "editor.promote-to-variable";
    public const string Rename              = "editor.rename";

    // Find
    public const string FindInGraph         = "editor.find-in-graph";
    public const string FindInAsset         = "editor.find-in-asset";
    public const string FindInProject       = "editor.find-in-project";
    public const string GoToDefinition      = "editor.go-to-definition";
    public const string FindReferences      = "editor.find-references";
    public const string FindNext            = "editor.find-next";
    public const string FindPrev            = "editor.find-prev";

    // Debug
    public const string ToggleBreakpoint    = "editor.toggle-breakpoint";
    public const string ToggleWatch         = "editor.toggle-watch";
    public const string DebugContinue       = "editor.continue";
    public const string DebugStepOver       = "editor.step-over";
    public const string DebugStepInto       = "editor.step-into";
    public const string DebugStepOut        = "editor.step-out";
    public const string ClearAllBreakpoints = "editor.clear-all-breakpoints";

    // Alignment
    public const string AlignLeft          = "editor.align-left";
    public const string AlignRight         = "editor.align-right";
    public const string AlignTop           = "editor.align-top";
    public const string AlignBottom        = "editor.align-bottom";
    public const string AlignCenterH       = "editor.align-center-h";
    public const string AlignCenterV       = "editor.align-center-v";
    public const string DistributeH        = "editor.distribute-h";
    public const string DistributeV        = "editor.distribute-v";
    public const string StraightenConn     = "editor.straighten-connection";
}
