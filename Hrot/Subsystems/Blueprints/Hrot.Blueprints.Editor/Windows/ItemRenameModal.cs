using ImGuiNET;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// BP-12b — a one-field rename prompt for My Blueprint items.
///
/// <para>
/// Deliberately generic: it knows nothing about what is being renamed. The validity rules differ per
/// item kind (a custom event must be a C# identifier, a variable need not be) and live in
/// <c>BlueprintDocumentFactory.RenameItem</c>, which is the authoritative guard and simply changes
/// nothing when a name is refused. The modal's only job is to collect a string.
/// </para>
///
/// <para>
/// Like the other modals here it owns transient UI state only, and gates every ImGui call behind a
/// current-context check so it is safe to construct headlessly.
/// </para>
/// </summary>
public sealed class ItemRenameModal
{
    private const string PopupId = "Rename##bp_rename_item";

    private bool             _openRequested;
    private string           _name = "";
    private bool             _focusPending;
    private System.Action<string>? _onConfirm;

    /// <summary>
    /// Requests the prompt on the next <see cref="Draw"/>, seeded with <paramref name="currentName"/>.
    /// <paramref name="onConfirm"/> receives the entered text.
    /// </summary>
    public void Open(string currentName, System.Action<string> onConfirm)
    {
        _name          = currentName ?? "";
        _onConfirm     = onConfirm;
        _openRequested = true;
        _focusPending  = true;
    }

    /// <summary>Draws the prompt if open. No-op headlessly, or when nothing has asked for it.</summary>
    public void Draw()
    {
        if (ImGui.GetCurrentContext() == System.IntPtr.Zero) return;

        if (_openRequested)
        {
            ImGui.OpenPopup(PopupId);
            _openRequested = false;
        }

        bool open = true;
        if (!ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        if (_focusPending)
        {
            ImGui.SetKeyboardFocusHere();
            _focusPending = false;
        }

        ImGui.SetNextItemWidth(240f);
        bool entered = ImGui.InputText("##bp_rename_name", ref _name, 128,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);

        bool canRename = !string.IsNullOrWhiteSpace(_name);
        if (!canRename)
            ImGui.TextColored(new System.Numerics.Vector4(0.95f, 0.55f, 0.20f, 1f),
                "Name cannot be empty.");

        ImGui.Separator();

        if (!canRename) ImGui.BeginDisabled();
        if (ImGui.Button("Rename", new System.Numerics.Vector2(100, 0)) || (entered && canRename))
        {
            _onConfirm?.Invoke(_name.Trim());
            _onConfirm = null;
            ImGui.CloseCurrentPopup();
        }
        if (!canRename) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new System.Numerics.Vector2(100, 0)))
        {
            _onConfirm = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }
}
