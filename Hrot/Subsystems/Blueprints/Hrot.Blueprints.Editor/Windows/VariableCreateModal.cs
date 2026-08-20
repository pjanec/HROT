using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
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
    /// <summary>
    /// ⭐⭐ <b>Per-INSTANCE, and that is the whole point.</b> ⛔ This was a <c>const</c>, and
    /// <see cref="BlueprintMyBlueprintWindow"/> builds <b>two</b> of these — asset variables and
    /// <c>BP-57</c>'s graph locals. Two instances sharing one ImGui popup id is not two dialogs that
    /// look alike; it is <b>one window that both of them append into</b>, because
    /// <c>BeginPopupModal</c> with an already-open id appends rather than opening a second window.
    ///
    /// <para>
    /// ⚠ <b>What that looked like on screen:</b> pressing <c>+</c> on <b>Local Variables</b> drew every
    /// field twice — the asset-variable modal's set first, the locals set below it — and the
    /// <b>first</b> Create button belonged to the <i>asset</i> modal. ⇒ the locals <c>+</c> created a
    /// <b>global</b> variable. ⛔ Not a cosmetic duplicate: the gesture did the wrong thing silently,
    /// which is the exact failure mode <c>Q26-B2</c> exists to rule out.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b><see cref="FunctionCreateModal"/> already solved this, in <c>BP-77</c>, when it was
    /// parameterised by <c>noun</c> for Macro</b> — same class, two instances, per-instance id. This
    /// is that fix applied to the sibling that was duplicated without it. ⇒ the rule for this window
    /// is now general: <b>a modal class instantiated more than once takes a noun</b>.
    /// </para>
    /// </summary>
    private readonly string _popupId;

    /// <summary>Headless seam — <c>ModalPopupIdTests</c> asserts the ids are pairwise distinct.</summary>
    internal string PopupId => _popupId;

    /// <summary>FC-2/LV-4: UI ceiling for a fixed list's capacity (display/typo guard only).</summary>
    internal const int MaxCapacity = 256;

    /// <summary>Confirm payload: (name, typeId, capacity, initialLength); capacity 0 = scalar.</summary>
    public delegate void ConfirmHandler(string name, string typeId, int capacity, int initialLength);

    private readonly ConfirmHandler  _onConfirm;
    private readonly BlueprintAsset? _asset;
    private readonly string          _title;
    private readonly string          _defaultName;

    private bool   _openRequested;
    private string _name;
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
    /// <param name="noun">
    /// ⭐ What is being created: <c>"Variable"</c> (the default, so every existing caller is
    /// unchanged) or <c>"Local Variable"</c>. Drives the window <b>title</b>, the <b>popup id</b> and
    /// the <b>default name</b> — exactly the three things <see cref="FunctionCreateModal"/>'s
    /// <c>BP-77</c> noun drives, for exactly the same reason.
    /// ⚠ <b>The title is not decoration here:</b> the two dialogs are otherwise identical, so it is
    /// the only thing telling the designer which list they are about to write to.
    /// </param>
    public VariableCreateModal(
        ConfirmHandler onConfirm, BlueprintAsset? asset = null, string noun = "Variable")
    {
        _onConfirm   = onConfirm ?? throw new ArgumentNullException(nameof(onConfirm));
        _asset       = asset;
        _title       = "Create " + noun;
        // ⭐ "NewVariable" / "NewLocalVariable" — the spelling CanvasRenderer's promote-to-variable
        // already uses, rather than a third one. (It replaces the old "NewVar", which nothing pins.)
        _defaultName = "New" + noun.Replace(" ", string.Empty);
        _popupId     = $"{_title}##bp_create_{_defaultName.ToLowerInvariant()}";
        _name        = _defaultName;
    }

    /// <summary>
    /// Requests the modal to open on the next <see cref="Draw"/> call, resetting the
    /// name/type/container fields to defaults. Call this from the My Blueprint "+" command.
    /// </summary>
    public void Open()
    {
        _name          = _defaultName;
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
            ImGui.OpenPopup(_popupId);
            _openRequested = false;
        }

        bool open = true;
        if (!ImGui.BeginPopupModal(_popupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
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
    ///
    /// <para>
    /// ⭐⭐ <b><c>S5</c> retired the hand-written switch that used to live here.</b> It listed twelve
    /// type ids — <i>"mirrors the selectable-type set"</i>, in its own words — so widening that set
    /// silently produced a budget of <b>0 bytes</b> for the eight primitives it had never heard of, and
    /// the budget line simply vanished. 🔴 <b>Third mirror of one table</b>, and the same failure the
    /// <c>U-8</c> struct case already demonstrated once.
    /// </para>
    ///
    /// <para>
    /// ⭐ The compiler's registry already holds an exact <c>SizeBytes</c> for every offerable
    /// primitive — it is the number the state layout is computed from — so ask it, and fall back to
    /// reflection only for a discovered struct the registry does not carry. ⇒ the budget line can no
    /// longer disagree with the bytes the variable will actually occupy.
    /// </para>
    /// </summary>
    internal static int ElementByteSize(string typeId)
    {
        if (string.IsNullOrEmpty(typeId)) return 0;

        if (StaticTypeRegistry.Instance.TryResolve(
                new BlueprintTypeRef { TypeId = typeId }, out var ir)
            && ir.IsUnmanaged && ir.SizeBytes > 0)
            return ir.SizeBytes;

        // U-8: a discovered [BlackboardDtoStruct] is selectable, so the list budget must be able to
        // size one. ⭐ The type was FOUND by reflection, so its size is available the same way — this
        // is not a second source of truth, it is the same one asked a second question.
        return StructByteSize(typeId);
    }

    /// <summary>
    /// U-8 — the marshalled size of a discovered <c>[BlackboardDtoStruct]</c>, or 0 when the id names
    /// no such struct.
    ///
    /// <para>
    /// ⚠ <b>Found by widening <c>SelectableTypeIds</c>, not predicted.</b> That list feeds the
    /// <b>list-element</b> picker as well as the scalar one, and the budget line needs a byte size per
    /// element — so adding structs to it silently produced a budget of *"≈ 4 bytes"* for every struct
    /// list. A repo test (<c>Modal_BudgetHelper_KnowsEverySelectableUnmanagedElementSize</c>) caught it
    /// on the same run.
    /// </para>
    /// </summary>
    internal static int StructByteSize(string typeId)
    {
        if (string.IsNullOrEmpty(typeId)) return 0;
        foreach (var t in Hrot.Editor.AiShared.Blackboard.BlackboardTypeChoiceBuilder
                     .DiscoverBlackboardDtoStructTypes())
        {
            if (!string.Equals(t.FullName, typeId, StringComparison.Ordinal)) continue;
            try { return System.Runtime.InteropServices.Marshal.SizeOf(t); }
            catch { return 0; }   // non-blittable ⇒ not a valid list element; 0 hides the budget line
        }
        return 0;
    }
}
