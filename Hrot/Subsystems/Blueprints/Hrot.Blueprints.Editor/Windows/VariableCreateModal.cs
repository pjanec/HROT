using Hrot.Blueprints.Editor.Host;
using ImGuiNET;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// Small ImGui modal for creating a blueprint variable: a name text field plus a
/// type dropdown sourced from <see cref="BlueprintTypeSystem.SelectableTypeIds"/>.
/// <para>
/// The modal owns only transient UI state (open flag, name buffer, selected type index).
/// The actual variable-creation work is delegated to a caller-supplied callback so the
/// create path stays headless-testable (see
/// <see cref="Host.BlueprintDocumentFactory.CreateVariable"/>); this class never touches
/// the asset directly. All ImGui calls are gated behind a current-context check so the
/// type is safe to instantiate in headless tests.
/// </para>
/// </summary>
public sealed class VariableCreateModal
{
    private const string PopupId = "Create Variable##bp_create_var";

    private readonly Action<string, string> _onConfirm;

    private bool   _openRequested;
    private string _name = "NewVar";
    private int    _typeIndex;

    /// <param name="onConfirm">
    /// Invoked with <c>(name, typeId)</c> when the user confirms. Wire this to
    /// <see cref="Host.BlueprintDocumentFactory.CreateVariable"/>.
    /// </param>
    public VariableCreateModal(Action<string, string> onConfirm)
        => _onConfirm = onConfirm ?? throw new ArgumentNullException(nameof(onConfirm));

    /// <summary>
    /// Requests the modal to open on the next <see cref="Draw"/> call, resetting the
    /// name/type fields to defaults. Call this from the My Blueprint "+" command.
    /// </summary>
    public void Open()
    {
        _name          = "NewVar";
        _typeIndex     = 0;
        _openRequested = true;
    }

    /// <summary>
    /// Draws the modal if open. No-op when there is no current ImGui context (headless).
    /// Must be called every frame from the owning window's draw routine.
    /// </summary>
    public void Draw()
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero)
            return;

        if (_openRequested)
        {
            ImGui.OpenPopup(PopupId);
            _openRequested = false;
        }

        bool open = true;
        if (!ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextUnformatted("Name");
        ImGui.SetNextItemWidth(220f);
        ImGui.InputText("##bp_var_name", ref _name, 128);

        ImGui.TextUnformatted("Type");
        ImGui.SetNextItemWidth(220f);
        var typeIds = BlueprintTypeSystem.SelectableTypeIds;
        var preview = ShortName(typeIds[Math.Clamp(_typeIndex, 0, typeIds.Count - 1)]);
        if (ImGui.BeginCombo("##bp_var_type", preview))
        {
            for (int i = 0; i < typeIds.Count; i++)
            {
                bool selected = i == _typeIndex;
                if (ImGui.Selectable(ShortName(typeIds[i]), selected))
                    _typeIndex = i;
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.Separator();

        bool canCreate = !string.IsNullOrWhiteSpace(_name);
        if (!canCreate) ImGui.BeginDisabled();
        if (ImGui.Button("Create", new System.Numerics.Vector2(100, 0)))
        {
            _onConfirm(_name, typeIds[Math.Clamp(_typeIndex, 0, typeIds.Count - 1)]);
            ImGui.CloseCurrentPopup();
        }
        if (!canCreate) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new System.Numerics.Vector2(100, 0)))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private static string ShortName(string typeId)
    {
        var dot = typeId.LastIndexOf('.');
        return dot >= 0 && dot < typeId.Length - 1 ? typeId[(dot + 1)..] : typeId;
    }
}
