using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;
using ImGuiNET;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// Small ImGui modal for creating a blueprint variable: a name text field plus a
/// type dropdown sourced from <see cref="BlueprintTypeSystem.SelectableTypeIds"/>, and
/// (FC-2/LV-4) a Container dropdown — "Single" (scalar) or "List (fixed)" with Capacity /
/// Initial Length fields and a live state-bytes budget line.
/// <para>
/// The modal owns only transient UI state (open flag, name buffer, selected type index,
/// container fields). The actual variable-creation work is delegated to a caller-supplied
/// callback so the create path stays headless-testable (see
/// <see cref="Host.BlueprintDocumentFactory.CreateVariable"/>); this class never touches
/// the asset directly. All ImGui calls are gated behind a current-context check so the
/// type is safe to instantiate in headless tests.
/// </para>
/// </summary>
public sealed class VariableCreateModal
{
    private const string PopupId = "Create Variable##bp_create_var";

    /// <summary>FC-2/LV-4: UI ceiling for a fixed list's capacity (display/typo guard only).</summary>
    internal const int MaxCapacity = 256;

    /// <summary>Confirm payload: (name, typeId, capacity, initialLength); capacity 0 = scalar.</summary>
    public delegate void ConfirmHandler(string name, string typeId, int capacity, int initialLength);

    private readonly ConfirmHandler  _onConfirm;
    private readonly BlueprintAsset? _asset;

    private bool   _openRequested;
    private string _name = "NewVar";
    private int    _typeIndex;
    private bool   _isList;
    private int    _capacity      = 4;
    private int    _initialLength = 0;

    /// <param name="onConfirm">
    /// Invoked with <c>(name, typeId, capacity, initialLength)</c> when the user confirms
    /// (<c>capacity == 0</c> for a scalar). Wire this to
    /// <see cref="Host.BlueprintDocumentFactory.CreateVariable"/>.
    /// </param>
    /// <param name="asset">
    /// The owning asset, used to validate the entered name against existing variables
    /// (case-insensitive). When a collision is detected the modal shows an inline warning
    /// and disables Confirm rather than auto-renaming. May be <see langword="null"/> in
    /// tests, in which case duplicate-checking is skipped.
    /// </param>
    public VariableCreateModal(ConfirmHandler onConfirm, BlueprintAsset? asset = null)
    {
        _onConfirm = onConfirm ?? throw new ArgumentNullException(nameof(onConfirm));
        _asset     = asset;
    }

    /// <summary>
    /// Requests the modal to open on the next <see cref="Draw"/> call, resetting the
    /// name/type/container fields to defaults. Call this from the My Blueprint "+" command.
    /// </summary>
    public void Open()
    {
        _name          = "NewVar";
        _typeIndex     = 0;
        _isList        = false;
        _capacity      = 4;
        _initialLength = 0;
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
        var typeId  = typeIds[Math.Clamp(_typeIndex, 0, typeIds.Count - 1)];
        if (ImGui.BeginCombo("##bp_var_type", ShortName(typeId)))
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

        // FC-2/LV-4: Container — Single (scalar) or List (fixed-capacity). The discriminator
        // written to the asset is BlueprintTypeRef.Capacity (never IsArray — F7).
        ImGui.TextUnformatted("Container");
        ImGui.SetNextItemWidth(220f);
        if (ImGui.BeginCombo("##bp_var_container", _isList ? "List (fixed)" : "Single"))
        {
            if (ImGui.Selectable("Single", !_isList)) _isList = false;
            if (ImGui.Selectable("List (fixed)", _isList)) _isList = true;
            ImGui.EndCombo();
        }

        bool listElementManaged = false;
        if (_isList)
        {
            ImGui.TextUnformatted("Capacity");
            ImGui.SetNextItemWidth(220f);
            ImGui.InputInt("##bp_var_capacity", ref _capacity);
            _capacity = Math.Clamp(_capacity, 1, MaxCapacity);

            ImGui.TextUnformatted("Initial Length");
            ImGui.SetNextItemWidth(220f);
            ImGui.InputInt("##bp_var_initlen", ref _initialLength);
            _initialLength = Math.Clamp(_initialLength, 0, _capacity);

            // Budget line: what this list costs in the instance's state bytes
            // (4-byte Count header + Capacity × element size; alignment padding may add a little).
            int elemSize = ElementByteSize(typeId);
            if (elemSize > 0)
                ImGui.TextDisabled($"≈ {4 + _capacity * elemSize} bytes of state ({_capacity} × {elemSize} B + Count)");

            listElementManaged = typeId == BlueprintTypeSystem.String;
            if (listElementManaged)
                ImGui.TextColored(new System.Numerics.Vector4(0.95f, 0.55f, 0.20f, 1f),
                    "A fixed list needs a blittable element type — String is not supported.");
        }

        // Inline validation: blank name, or a name that collides (case-insensitively) with an
        // existing variable. On collision we warn and disable Confirm — never auto-rename.
        bool isBlank     = string.IsNullOrWhiteSpace(_name);
        bool isDuplicate = !isBlank
            && _asset != null
            && BlueprintDocumentFactory.IsDuplicateVariableName(_asset, _name);

        if (isBlank)
            ImGui.TextColored(new System.Numerics.Vector4(0.95f, 0.55f, 0.20f, 1f),
                "Name cannot be empty.");
        else if (isDuplicate)
            ImGui.TextColored(new System.Numerics.Vector4(0.95f, 0.55f, 0.20f, 1f),
                $"A variable named '{_name.Trim()}' already exists.");

        ImGui.Separator();

        bool canCreate = !isBlank && !isDuplicate && !listElementManaged;
        if (!canCreate) ImGui.BeginDisabled();
        if (ImGui.Button("Create", new System.Numerics.Vector2(100, 0)))
        {
            _onConfirm(_name, typeId, _isList ? _capacity : 0, _isList ? _initialLength : 0);
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

    /// <summary>
    /// FC-2/LV-4: element payload size for the budget line; 0 = unknown (line omitted).
    /// Mirrors the selectable-type set, not a general sizeof.
    /// </summary>
    internal static int ElementByteSize(string typeId) => typeId switch
    {
        BlueprintTypeSystem.Bool    => 1,
        BlueprintTypeSystem.Byte    => 1,
        BlueprintTypeSystem.Int32   => 4,
        BlueprintTypeSystem.UInt32  => 4,
        BlueprintTypeSystem.Single  => 4,
        BlueprintTypeSystem.Float64 => 8,
        BlueprintTypeSystem.Vector2 => 8,
        BlueprintTypeSystem.Vector3 => 12,
        BlueprintTypeSystem.Entity  => 8,
        BlueprintTypeSystem.FixedString32 => 32,
        BlueprintTypeSystem.FixedString64 => 64,
        BlueprintTypeSystem.FixedString128 => 128,
        _ => 0,
    };
}
