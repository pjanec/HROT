using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using ImGuiNET;
using Fdp.Diagnostics.Contracts.Panels;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Windows;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Windows;

/// <summary>⭐ One event row, projected for the dump — already flat, no delegates/System.Type.</summary>
public sealed record HsmEventRowViewModel(int EventId, string Name, int PayloadSize, bool IsIndirect, bool HasGlobalTransition);

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 follow-up — the whole of what <see cref="HsmEventsWindow"/> shows, this frame.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example.
///
/// <para>⭐ <b>Converted to the <c>Hrot.Editor.AiShared/Shell/*DetailsView</c> family</b> — see
/// <see cref="HsmEventsDetailsView"/>, which wraps this class and supplies the composed
/// <c>{idScope}/{ViewId}</c> address the family uses (<see cref="BuildViewModel(string,string)"/>).
/// <c>BuildViewModel()</c> (no args) keeps <c>WindowId</c> for both roles, for the STANDALONE
/// (not-yet-hosted) shape callers used before this conversion.</para>
///
/// <para>⛔⛔ <b>Registration into the live HSM perspective is NOT wired — reported, not done.</b>
/// 📐 Measured: the only composition root that assembles the HSM <c>PerspectiveWorkspaceRegistrar</c>
/// and could add <c>HsmEventsDetailsViewDescriptor</c> to its <c>DetailsViews</c> catalogue is
/// <c>EditorSubsystem.cs</c> — explicitly on the STOP-AND-REPORT list for this batch. See the batch
/// report for the full finding.</para>
/// </summary>
public sealed record HsmEventsWindowViewModel(
    string PanelId, string PanelKind, IReadOnlyList<HsmEventRowViewModel> Events) : IPanelViewModel
{
    /// <inheritdoc/>
    public JsonNode Dump() => PanelDump.Of(this);
}

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

    // ⭐ HSM-009 — pending CREATE state, same shape as the rename modal above.
    private readonly byte[] _createBuf = new byte[256];
    private bool _openCreateModal;
    private bool _createModalIsOpen;

    /// <summary>⚠ A delete requested from a row's context menu, applied AFTER the row loop — the menu is drawn
    /// while iterating <c>AllEvents</c>, which is the very list the delete mutates.</summary>
    private ushort? _pendingDeleteId;

    /// <summary>⭐ Why the last create/rename was refused, shown in the modal. Empty when there is nothing to say.</summary>
    private string _message = string.Empty;

    public HsmEventsWindow(
        HsmAsset asset,
        IRefactorService refactorService,
        FindResultsWindow findResults)
    {
        _asset = asset;
        _refactorService = refactorService;
        _findResults = findResults;
    }

    /// <summary>⭐⭐⭐ BUILD — a pure projection of the asset's event declarations. No ImGui. ⚠ The
    /// STANDALONE shape — <c>WindowId</c> for both address and kind. <see cref="HsmEventsDetailsView"/>
    /// uses the <see cref="BuildViewModel(string,string)"/> overload instead, composing the address
    /// from its hosting window's <c>idScope</c>.</summary>
    public HsmEventsWindowViewModel BuildViewModel() => BuildViewModel(WindowId, WindowId);

    /// <summary>⭐⭐⭐ BUILD — a pure projection of the asset's event declarations, under a
    /// caller-supplied identity. No ImGui.</summary>
    public HsmEventsWindowViewModel BuildViewModel(string panelId, string panelKind) => new(
        panelId, panelKind,
        _asset.AllEvents.Select(ev => new HsmEventRowViewModel(
            ev.EventId, ev.Name, ev.PayloadSize, ev.IsIndirect, ev.HasGlobalTransition)).ToList());

    // ═══ HSM-009 — the authoring operations, headless and testable ═══════════════════════════════════════
    //
    // ⭐ The ImGui below calls THESE; the rails call them too, so what a rail proves is what the button does.
    //   📄 HSM_Editor_NodeEditor_Host_Design.md §9.1.

    /// <summary>⭐ Declares an event. Returns false and sets <see cref="Message"/> when refused.</summary>
    public bool TryCreateEvent(string? name)
    {
        if (_asset.CreateEvent(name) is null)
        {
            _message = string.IsNullOrWhiteSpace(name)
                ? "Enter a name."
                : $"'{name.Trim()}' is already declared in this machine.";
            return false;
        }
        _message = string.Empty;
        return true;
    }

    /// <summary>
    /// ⭐ Renames an event and reports whether it took.
    /// ⚠ The declaration IS the rename — a transition stores the event's id, not its name — so unlike a variable
    ///   rename this needs no refactor pass. ⛔ That is why this does not route through <c>RefactorService</c>.
    /// </summary>
    public bool TryRenameEvent(ushort eventId, string? newName)
    {
        if (!_asset.RenameEvent(eventId, newName))
        {
            _message = $"Cannot rename to '{newName?.Trim()}' — blank, unchanged, already taken, or an engine-raised name.";
            return false;
        }
        _message = string.Empty;
        return true;
    }

    /// <summary>⭐ Deletes an event. Transitions that still reference it are reported by the validator
    /// (<c>EventReferenceDangling</c>) rather than silently rewritten — see <c>HsmAsset.RemoveEvent</c>.</summary>
    public bool TryDeleteEvent(ushort eventId) => _asset.RemoveEvent(eventId);

    /// <summary>⭐ How many transitions and global transitions would dangle if <paramref name="eventId"/> went away.
    /// Shown on the delete item so the author is told BEFORE, not by a diagnostic after.</summary>
    public int CountReferencingTransitions(ushort eventId)
        => _asset.AllTransitions.Count(t => t.EventId == eventId)
         + _asset.AllGlobalTransitions.Count(g => g.EventId == eventId);

    /// <summary>The last refusal reason, or empty.</summary>
    public string Message => _message;

    public void Render()
    {
        // ⭐ HSM-009 — "+ Add Event" is drawn FIRST and unconditionally: an empty machine is exactly the case
        //   where an author needs it, and the old early-return made it unreachable there.
        if (ImGui.Button("+ Add Event"))
        {
            Array.Clear(_createBuf, 0, _createBuf.Length);
            _message = string.Empty;
            _openCreateModal = true;
        }

        DrawCreateModal();

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
                        // ⭐ HSM-009 — the MACHINE-SCOPED key, which is what HsmReferenceContributor publishes
                        //   ("{AssetId:D}::{EventName}", §9.2). ⛔ This used to pass the bare name and therefore
                        //   always found nothing, because no contributor ever registers an unqualified event name.
                        var key  = $"{_asset.AssetId:D}::{ev.Name}";
                        var refs = _refactorService.FindReferences(key);
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
                        _message = string.Empty;
                    }

                    // ⭐ HSM-009 — delete, with the cost stated up front.
                    // ⚠ DEFERRED to after the loop: AllEvents is the list being iterated, so deleting here
                    //   would throw on the next MoveNext.
                    int referencing = CountReferencingTransitions(ev.EventId);
                    var deleteLabel = referencing == 0
                        ? "Delete"
                        : $"Delete ({referencing} transition(s) will dangle)";
                    if (ImGui.MenuItem(deleteLabel))
                        _pendingDeleteId = ev.EventId;

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

        // ⭐ HSM-009 — the deferred delete, now that the row loop has finished with the list.
        if (_pendingDeleteId is { } doomed)
        {
            TryDeleteEvent(doomed);
            _pendingDeleteId = null;
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

                // ⭐⭐ HSM-009 — RENAME NOW APPLIES. ⛔ This modal's only button used to be "Preview", which
                //   showed a RefactorService preview in the Find Results window and then did nothing: there was
                //   no Apply, so an event could never actually be renamed. ⭐ And the preview was empty anyway —
                //   a transition stores the event's ID, so there is nothing in any file for a text-level rename
                //   to rewrite. ⇒ the declaration edit IS the rename (HsmAsset.RenameEvent).
                if (ImGui.Button("Rename"))
                {
                    var newName = Fdp.Presentation.Utils.ImGuiBufferText.Decode(_renameBuf);
                    if (TryRenameEvent(_pendingRenameEvent.EventId, newName))
                    {
                        _pendingRenameEvent = null;
                        Array.Clear(_renameBuf, 0, _renameBuf.Length);
                        ImGui.CloseCurrentPopup();
                        ImGui.EndPopup();
                        return;
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    _pendingRenameEvent = null;
                    Array.Clear(_renameBuf, 0, _renameBuf.Length);
                    ImGui.CloseCurrentPopup();
                }
                if (_message.Length > 0) ImGui.TextDisabled(_message);
                ImGui.EndPopup();
            }
            if (!modalOpen)
            {
                _pendingRenameEvent = null;
                Array.Clear(_renameBuf, 0, _renameBuf.Length);
            }
        }
    }

    /// <summary>⭐ HSM-009 — the "+ Add Event" modal. Same shape as the rename one above.</summary>
    private void DrawCreateModal()
    {
        if (_openCreateModal)
        {
            ImGui.OpenPopup("New Event##hsmev");
            _openCreateModal   = false;
            _createModalIsOpen = true;
        }
        if (!_createModalIsOpen) return;

        var modalOpen = true;
        if (ImGui.BeginPopupModal("New Event##hsmev", ref modalOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Event name:");
            ImGui.SameLine();
            ImGui.InputText("##newevname", _createBuf, (uint)_createBuf.Length);

            if (ImGui.Button("Create"))
            {
                var name = Fdp.Presentation.Utils.ImGuiBufferText.Decode(_createBuf);
                if (TryCreateEvent(name))
                {
                    Array.Clear(_createBuf, 0, _createBuf.Length);
                    _createModalIsOpen = false;
                    ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                    return;
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                Array.Clear(_createBuf, 0, _createBuf.Length);
                _createModalIsOpen = false;
                ImGui.CloseCurrentPopup();
            }
            if (_message.Length > 0) ImGui.TextDisabled(_message);
            ImGui.EndPopup();
        }
        if (!modalOpen)
        {
            Array.Clear(_createBuf, 0, _createBuf.Length);
            _createModalIsOpen = false;
        }
    }
}
