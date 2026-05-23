using System.Text;
using ImGuiNET;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Windows;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Windows;

// Window that displays all event declarations of the loaded HSM asset.
// Supports Find References and Rename for each event.
public sealed class HsmEventsWindow
{
    public const string WindowId = "hsm_events";

    private readonly HsmAsset _asset;
    private readonly IRefactorService _refactorService;
    private readonly FindResultsWindow _findResults;

    // Pending rename state
    private EventDefinition? _pendingRenameEvent;
    private readonly byte[] _renameBuf = new byte[256];
    private bool _openRenameModal;

    public HsmEventsWindow(
        HsmAsset asset,
        IRefactorService refactorService,
        FindResultsWindow findResults)
    {
        _asset = asset;
        _refactorService = refactorService;
        _findResults = findResults;
    }

    public void Render()
    {
        if (_asset.AllEvents.Count == 0)
        {
            ImGui.TextDisabled("No events declared in this asset.");
            return;
        }

        // Draw events table
        if (ImGui.BeginTable("##events", 5,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter |
            ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable |
            ImGuiTableFlags.ScrollY, new System.Numerics.Vector2(0, 0)))
        {
            ImGui.TableSetupColumn("ID",       ImGuiTableColumnFlags.WidthFixed, 40f);
            ImGui.TableSetupColumn("Name",     ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Payload",  ImGuiTableColumnFlags.WidthFixed, 60f);
            ImGui.TableSetupColumn("Indirect", ImGuiTableColumnFlags.WidthFixed, 60f);
            ImGui.TableSetupColumn("Global",   ImGuiTableColumnFlags.WidthFixed, 50f);
            ImGui.TableHeadersRow();

            foreach (var ev in _asset.AllEvents)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text(ev.EventId.ToString());
                ImGui.TableSetColumnIndex(1);
                ImGui.Selectable(ev.Name, false, ImGuiSelectableFlags.SpanAllColumns);
                // Right-click context menu
                var popupId = $"##evctx_{ev.EventId}";
                if (ImGui.BeginPopupContextItem(popupId))
                {
                    if (ImGui.MenuItem("Find References"))
                    {
                        var refs = _refactorService.FindReferences(ev.Name);
                        _findResults.ShowReferences(ev.Name, refs);
                    }
                    if (ImGui.MenuItem("Rename..."))
                    {
                        _pendingRenameEvent = ev;
                        Array.Clear(_renameBuf, 0, _renameBuf.Length);
                        var nameBytes = Encoding.UTF8.GetBytes(ev.Name);
                        Array.Copy(nameBytes, _renameBuf,
                            Math.Min(nameBytes.Length, _renameBuf.Length - 1));
                        _openRenameModal = true;
                    }
                    ImGui.EndPopup();
                }
                ImGui.TableSetColumnIndex(2);
                ImGui.Text(ev.PayloadSize.ToString());
                ImGui.TableSetColumnIndex(3);
                ImGui.Text(ev.IsIndirect ? "yes" : "no");
                ImGui.TableSetColumnIndex(4);
                ImGui.Text(ev.HasGlobalTransition ? "yes" : "no");
            }
            ImGui.EndTable();
        }

        // Rename modal
        if (_openRenameModal)
        {
            ImGui.OpenPopup("Rename Event##hsmev");
            _openRenameModal = false;
        }
        if (_pendingRenameEvent != null)
        {
            var modalOpen = true;
            if (ImGui.BeginPopupModal("Rename Event##hsmev", ref modalOpen,
                ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text($"Rename event: {_pendingRenameEvent.Name}");
                ImGui.Text("New name:");
                ImGui.SameLine();
                ImGui.InputText("##evname", _renameBuf, (uint)_renameBuf.Length);
                if (ImGui.Button("Preview"))
                {
                    var newName = Encoding.UTF8.GetString(_renameBuf).TrimEnd('\0');
                    if (!string.IsNullOrWhiteSpace(newName))
                    {
                        // Machine-scoped rename: only IncludeHsm
                        var opts = new RefactorOptions(
                            IncludeBlueprint: false,
                            IncludeBTree: false,
                            IncludeHsm: true);
                        var preview = _refactorService.PreviewRename(
                            _pendingRenameEvent.Name, newName, opts);
                        _findResults.ShowRenamePreview(preview);
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    _pendingRenameEvent = null;
                    Array.Clear(_renameBuf, 0, _renameBuf.Length);
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
            if (!modalOpen)
            {
                _pendingRenameEvent = null;
                Array.Clear(_renameBuf, 0, _renameBuf.Length);
            }
        }
    }
}
