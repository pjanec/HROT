using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Selection;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Inspector window -- shows properties for the currently-selected sub-element.
/// StructEdit-driven dispatch by asset type; subsystems supply facet structs.
/// This is a shell; per-subsystem inspector panels are added in later phases.
/// </summary>
public sealed class InspectorWindow : ManagedWindow
{
    private readonly EditorSelectionStore _store;

    public InspectorWindow(EditorSelectionStore store)
        : base("ai_inspector", "Inspector", "Authoring", WindowScope.PerspectiveBound)
    {
        _store = store;
    }

    protected override void DrawClientArea()
    {
        if (_store.ActiveAsset is null)
        {
            ImGuiNET.ImGui.TextDisabled("Select an asset to begin.");
            return;
        }

        ImGuiNET.ImGui.Text(_store.ActiveAsset.Name);
    }
}
