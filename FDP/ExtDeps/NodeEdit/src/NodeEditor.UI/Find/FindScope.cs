namespace NodeEditor.UI.Find;

/// <summary>The scope of a find operation.</summary>
public enum FindScope
{
    /// <summary>Search only within the currently visible graph.</summary>
    CurrentGraph,
    /// <summary>Search across all graphs in the current asset.</summary>
    Asset,
    /// <summary>Search across all open tabs.</summary>
    OpenTabs,
    /// <summary>Search the entire project.</summary>
    WholeProject,
}
