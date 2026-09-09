using System.Text.Json.Nodes;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Selection;
using Hrot.Utility.Editor.Model;

namespace Hrot.Utility.Editor.Windows;

/// <summary>⭐⭐⭐ U-obs-5 — the whole of what <see cref="UtilityDecisionWindow"/> shows, this frame.
/// ⭐ <b>Not static chrome</b> despite the card-table UI itself being a stub — <see cref="HasActiveAsset"/>/
/// <see cref="ActiveAssetName"/> reflect real selection state, which IS what the window currently
/// renders.</summary>
public sealed record UtilityDecisionWindowViewModel(
    string PanelId, string PanelKind, bool HasActiveAsset, string? ActiveAssetName) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

// ManagedWindow host for the Utility AI card-table editor.
// Card-table UI rendered in later batches; this batch wires selection and asset activation.
public sealed class UtilityDecisionWindow : ManagedWindow
{
    /// <summary>⭐ <c>U-obs-5</c> — THE KIND. Single host: stays a local literal.</summary>
    internal const string Kind = "utility-decision-editor";

    private readonly EditorSelectionStore _store;
    private UtilityDecisionAsset?         _activeAsset;

    public UtilityDecisionAsset? ActiveAsset => _activeAsset;

    public UtilityDecisionWindow(EditorSelectionStore store)
        : base("utility_decision_editor", "Utility Decision Editor", "Authoring",
               WindowScope.PerspectiveBound)
    {
        _store = store;
        _store.OnSelectionChanged += OnSelectionChanged;

        // ⭐⭐⭐ U-obs-5 — DECLARED AT CONSTRUCTION, ALWAYS, ungated on CaptureEnabled.
        PanelSnapshot.DeclareInstrumented(Id);
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

    /// <summary>⭐⭐⭐ BUILD — a pure projection of the active asset. No ImGui.</summary>
    private UtilityDecisionWindowViewModel BuildAndPublish()
    {
        var vm = new UtilityDecisionWindowViewModel(Id, Kind, _activeAsset != null, _activeAsset?.DisplayName);
        if (PanelSnapshot.CaptureEnabled) PanelSnapshot.Register(vm);
        return vm;
    }

    /// <summary>⭐ Test hook — the BUILD + CAPTURE portion, callable with no live ImGui context.</summary>
    internal UtilityDecisionWindowViewModel SimulateDrawClientArea() => BuildAndPublish();

    protected override void DrawClientArea()
    {
        BuildAndPublish();

        if (_activeAsset is null)
        {
            ImGuiNET.ImGui.TextDisabled("No utility decision open. Use the Asset Browser.");
            return;
        }

        ImGuiNET.ImGui.Text($"Decision: {_activeAsset.DisplayName}");
        ImGuiNET.ImGui.TextDisabled("Card-table UI coming in a later batch.");
    }
}
