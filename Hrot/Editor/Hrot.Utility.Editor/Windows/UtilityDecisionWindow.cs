using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Selection;
using Hrot.Utility.Editor.Model;

namespace Hrot.Utility.Editor.Windows;

// ManagedWindow host for the Utility AI card-table editor.
// Card-table UI rendered in later batches; this batch wires selection and asset activation.
public sealed class UtilityDecisionWindow : ManagedWindow
{
    private readonly EditorSelectionStore _store;
    private UtilityDecisionAsset?         _activeAsset;

    public UtilityDecisionAsset? ActiveAsset => _activeAsset;

    public UtilityDecisionWindow(EditorSelectionStore store)
        : base("utility_decision_editor", "Utility Decision Editor", "Authoring",
               WindowScope.PerspectiveBound)
    {
        _store = store;
        _store.OnSelectionChanged += OnSelectionChanged;
    }

    // Opens the given asset and brings the window to front.
    public void OpenAsset(UtilityDecisionAsset asset)
    {
        _activeAsset = asset;
        IsOpen = true;
    }

    private void OnSelectionChanged()
    {
        if (_store.ActiveAsset is UtilityDecisionAsset utilAsset)
            _activeAsset = utilAsset;
    }

    protected override void DrawClientArea()
    {
        if (_activeAsset is null)
        {
            ImGuiNET.ImGui.TextDisabled("No utility decision open. Use the Asset Browser.");
            return;
        }

        ImGuiNET.ImGui.Text($"Decision: {_activeAsset.DisplayName}");
        ImGuiNET.ImGui.TextDisabled("Card-table UI coming in a later batch.");
    }
}
