namespace Hrot.Editor.AiShared.Debug;

/// <summary>
/// Subsystem-provided pane rendered inside the RuntimeInspectorWindow content area.
/// One implementation per subsystem (BTree, HSM, Blueprint); selected at runtime
/// by matching TargetKind to the active asset's kind.
/// </summary>
public interface IRuntimeInspectorPane
{
    /// <summary>The asset kind this pane handles.</summary>
    AssetKind TargetKind { get; }

    /// <summary>
    /// Draw the pane's ImGui content. Called every frame while the matching asset is active.
    /// Do NOT call ImGui.Begin/End here; the window already did that.
    /// </summary>
    void Draw();
}
