using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Refactor;
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
    private readonly IRefactorService _refactorService;
    private readonly FindResultsWindow _findResults;

    private string? _pendingRenameKey;
    private readonly byte[] _renameBuf = new byte[512];
    private bool _openRenameModal;

    public InspectorWindow(
        EditorSelectionStore store,
        IRefactorService refactorService,
        FindResultsWindow findResults)
        : base("ai_inspector", "Inspector", "Authoring", WindowScope.PerspectiveBound)
    {
        _store = store;
        _refactorService = refactorService;
        _findResults = findResults;
    }

    protected override void DrawClientArea()
    {
        if (_store.ActiveAsset is null)
        {
            ImGuiNET.ImGui.TextDisabled("Select an asset to begin.");
            return;
        }

        var asset = _store.ActiveAsset;

        ImGuiNET.ImGui.Selectable(asset.Name);
        if (ImGuiNET.ImGui.BeginPopupContextItem("##insp_ctx"))
        {
            if (ImGuiNET.ImGui.MenuItem("Find References"))
            {
                var refs = _refactorService.FindReferences(asset.Name);
                _findResults.ShowReferences(asset.Name, refs);
            }
            if (ImGuiNET.ImGui.MenuItem("Rename..."))
            {
                _pendingRenameKey = asset.Name;
                _openRenameModal = true;
                Array.Clear(_renameBuf, 0, _renameBuf.Length);
            }
            if (ImGuiNET.ImGui.MenuItem("Go to Definition"))
            {
                // placeholder -- navigation wired in a later phase
            }
            ImGuiNET.ImGui.EndPopup();
        }

        if (_openRenameModal)
        {
            ImGuiNET.ImGui.OpenPopup("Rename##insp");
            _openRenameModal = false;
        }

        if (_pendingRenameKey != null)
        {
            var renameOpen = true;
            if (ImGuiNET.ImGui.BeginPopupModal("Rename##insp", ref renameOpen,
                ImGuiNET.ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGuiNET.ImGui.Text($"Rename: {_pendingRenameKey}");
                ImGuiNET.ImGui.Text("New name:");
                ImGuiNET.ImGui.SameLine();
                ImGuiNET.ImGui.InputText("##rname_insp", _renameBuf, (uint)_renameBuf.Length);
                if (ImGuiNET.ImGui.Button("OK"))
                {
                    var newKey = System.Text.Encoding.UTF8.GetString(_renameBuf).TrimEnd('\0');
                    if (!string.IsNullOrWhiteSpace(newKey))
                    {
                        var preview = _refactorService.PreviewRename(
                            _pendingRenameKey, newKey, new RefactorOptions());
                        _findResults.ShowRenamePreview(preview);
                    }
                    _pendingRenameKey = null;
                    Array.Clear(_renameBuf, 0, _renameBuf.Length);
                    ImGuiNET.ImGui.CloseCurrentPopup();
                }
                ImGuiNET.ImGui.SameLine();
                if (ImGuiNET.ImGui.Button("Cancel"))
                {
                    _pendingRenameKey = null;
                    Array.Clear(_renameBuf, 0, _renameBuf.Length);
                    ImGuiNET.ImGui.CloseCurrentPopup();
                }
                ImGuiNET.ImGui.EndPopup();
            }
            if (!renameOpen)
            {
                _pendingRenameKey = null;
                Array.Clear(_renameBuf, 0, _renameBuf.Length);
            }
        }
    }
}
