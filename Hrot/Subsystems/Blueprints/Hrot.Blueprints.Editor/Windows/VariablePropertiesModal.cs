using System;
using System.Collections.Generic;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor.Host;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Variables;
using ImGuiNET;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// ⭐⭐⭐ <b>Batch 99 (<c>99a</c>) — <i>"Properties…"</i>, as a CUSTOM form.</b>
///
/// <para>📌 <b><c>R-108</c></b>: <i>"'Properties…' must open the DECLARATION, not the value — and the
/// two menu items are TWO OBJECTS, not two SCOPES."</i> · 📌 <b><c>R-109</c></b>: ⛔ <b>and it cannot be
/// a StructEdit document</b>, because two of its fields are <b>OPERATIONS</b>:</para>
/// <list type="table">
///   <item><term><b><c>Name</c></b></term><description>a <b>RENAME</b> ⇒ <see cref="VariableRenameCommit"/>,
///   which runs the <b>refactor service</b>. ⚠ 📌 <c>M-15</c>: BTree/HSM store the NAME STRING in the
///   binding, so skipping that <b>dangles it</b> — <c>BTREE0002</c>, a whole-asset skip.</description></item>
///   <item><term><b><c>Type</c></b></term><description>a <b>RETYPE MIGRATION</b> — <c>DefaultValueJson</c>
///   may not convert, offsets move, <b><c>StructureHash</c> moves</b> *(<c>R-24</c>)*. ⛔ <b>Shipped
///   DISABLED with its reason</b> this batch — 📌 the handoff: <i>"do not hold the dialog"</i>, and
///   <i>"do NOT silently write the new type and leave <c>DefaultValueJson</c> unconvertible."</i></description></item>
/// </list>
///
/// <para>⭐⭐⭐ <b>Ruling 9, at the right level: CREATE and EDIT-PROPERTIES draw the SAME FORM.</b> Both
/// this and <see cref="VariableCreateModal"/> render through
/// <see cref="VariablePropertyFields.Draw"/> — ⛔ <b>not two dialogs that each know how to draw a type
/// combo</b>. ⭐ The offerable set is <c>BlueprintTypeSystem.SelectableTypeIds</c>, the ONE list
/// <c>S5</c> left *(Batch 65)*.</para>
///
/// <para>⭐ <b>Read-only is DIALOG-LEVEL</b>, from <c>VariableEditPolicy</c> through the caller —
/// ⛔ no per-field flag anywhere *(📌 <c>R-109</c>)*, and ⛔ not a second matrix *(ruling 9)*.</para>
/// </summary>
public sealed class VariablePropertiesModal
{
    /// <summary>⭐ Per-INSTANCE — 📌 <c>VariableCreateModal</c>'s own lesson: two instances sharing one
    /// popup id is ONE window both append into, and its first button belongs to the other one.</summary>
    private const string PopupId = "Variable Properties##bp_variable_properties";

    /// <summary>Headless seam — <c>ModalPopupIdTests</c> asserts the ids are pairwise distinct.</summary>
    internal static string PopupIdForTest => PopupId;

    /// <summary>
    /// ⭐⭐ The reason <c>Type</c> is disabled this batch, shown to the designer verbatim.
    /// ⛔ Not a TODO comment: 📌 the visual guide's <c>F3</c> — <i>"every refusal GREYED WITH A TOOLTIP,
    /// not a click that dead-ends"</i>.
    /// </summary>
    internal const string RetypeUnavailable =
        "Changing a variable's type is a migration (the default value may not convert and the "
      + "blackboard layout moves) — not yet supported.";

    /// <summary>
    /// ⭐⭐ The reason <c>Name</c> is disabled when no schema reached this form.
    ///
    /// <para>📐 <b>Measured, not assumed:</b> the host that owns this form —
    /// <c>BlueprintDetailsWindow</c> — holds <b>neither an <c>IVariablesSchemaSource</c> nor an
    /// <c>IRefactorService</c></b>; it is constructed with a selection store and a drawer registry, and
    /// the schema lives in the row SOURCE that the outline builds. ⇒ ⛔ a rename here could only skip
    /// the refactor service, and 📌 <c>M-15</c> makes that a <b>dangling binding</b> on BTree/HSM.</para>
    ///
    /// <para>⭐ <c>VariableRenameCommit</c> IS built and railed — ⛔ it is the wiring that is missing,
    /// and a host that DOES supply a schema renames through it with no further work.</para>
    /// </summary>
    internal const string RenameUnavailableHere =
        "Renaming from here is not wired yet — a rename must run the refactor service, and this "
      + "panel has no schema to rename through. Use the variables list.";

    /// <summary>
    /// ⭐⭐ <b>OPTIONAL, and the honest shape.</b> ⛔ A host with no refactor service must NOT be handed
    /// a no-op one: 📌 <c>M-15</c> — a rename that skips the refactor service <b>dangles the binding</b>
    /// on BTree/HSM, so a silent no-op would be exactly the corruption this design refuses.
    /// ⇒ ⭐ no service ⇒ <c>Name</c> is drawn DISABLED with its reason.
    /// </summary>
    private readonly IRefactorService? _refactorService;

    private bool                    _openRequested;
    private VariableRow?            _row;
    private IVariablesSchemaSource? _schema;
    private Guid                    _assetId;
    private VariableDeclarationKind _kind = VariableDeclarationKind.BlackboardEntry;
    private bool                    _editable = true;
    private string                  _originalName = "";
    private VariablePropertyState   _state = new();

    /// <summary>⭐ What the last OK did. <c>null</c> before any. ⭐ A rail surface.</summary>
    public VariableRenameCommit.Outcome? LastRenameOutcome { get; private set; }

    public VariablePropertiesModal(IRefactorService? refactorService = null)
        => _refactorService = refactorService;

    /// <summary>
    /// ⭐⭐ Whether <c>Name</c> can be committed at all — <b>a schema AND a refactor service</b>.
    /// ⛔ Either one missing means the only available rename would skip the refactor service, which
    /// 📌 <c>M-15</c> makes a dangling binding. ⭐ The form then greys it with its reason.
    /// </summary>
    internal bool CanRename => _schema is not null && _refactorService is not null;

    /// <summary>
    /// ⭐⭐ <b>The FORWARDING probe, per dependency.</b> 📌 The silent-default ruling asks for
    /// <i>"a forwarding rail PER DEPENDENCY, asserted on the CONSTRUCTED OBJECT — not on the
    /// registrar's source."</i>
    ///
    /// <para>⛔ <b><see cref="CanRename"/> cannot serve as that probe</b>, and the reason is the whole
    /// point: it is <c>false</c> when EITHER half is missing, so a host that was handed no refactor
    /// service is indistinguishable from one that has the service and no schema. ⚠ That is precisely
    /// the ambiguity a defaulted dependency hides behind.</para>
    /// </summary>
    internal bool HasRefactorService => _refactorService is not null;

    /// <summary>
    /// ⭐⭐ Opens on <paramref name="row"/>'s declaration, seeded from what it holds NOW.
    ///
    /// <para>⛔ Returns <c>false</c> when the row cannot say what it is — ⭐ the form then does not open
    /// at all, rather than opening over invented values.</para>
    /// </summary>
    /// <param name="editable">
    /// ⭐⭐ <b>DIALOG-LEVEL read-only</b>, from <c>VariableEditGesture.Decide</c> / <c>VariableEditPolicy</c>
    /// — 📌 planning ⇒ editable · running/paused ⇒ read-only *("you cannot retype a variable mid-run")* ·
    /// replay ⇒ read-only. ⛔ The modal does NOT re-decide it.
    /// </param>
    public bool Open(VariableRow row, IVariablesSchemaSource? schema, Guid assetId, bool editable)
    {
        if (row.ReadProperties?.Invoke() is not { } snapshot) return false;

        _row          = row;
        _schema       = schema;
        _assetId      = assetId;
        _kind         = snapshot.Kind;
        _editable     = editable;
        _originalName = row.ShortName;

        var v = snapshot.Values;
        _state = new VariablePropertyState
        {
            Name             = row.ShortName,
            TypeId           = snapshot.TypeId,
            DefaultValueJson = v.DefaultValueJson ?? "",
            Tooltip          = v.Tooltip  ?? "",
            Comment          = v.Comment  ?? "",
            Category         = v.Category ?? "",
            IsEditable       = v.IsEditable       ?? false,
            IsExposedOnSpawn = v.IsExposedOnSpawn ?? false,
        };

        LastRenameOutcome = null;
        _openRequested    = true;
        return true;
    }

    /// <summary>⭐ True while the form has a row. ⭐ A headless rail surface.</summary>
    public bool IsOpen => _row is not null;

    /// <summary>⭐ The state the form is editing. ⭐ Exposed so a headless rail can drive it — 📌
    /// <c>R-21</c>/<c>R-62</c>: the DRAW cannot be exercised, so the COMMIT is driven directly.</summary>
    internal VariablePropertyState State => _state;

    /// <summary>Draws the modal if open. No-op with no ImGui context (headless).</summary>
    public void Draw()
    {
        if (ImGui.GetCurrentContext() == IntPtr.Zero) return;
        if (_row is null) return;

        if (_openRequested) { ImGui.OpenPopup(PopupId); _openRequested = false; }

        bool open = true;
        if (!ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize)) return;

        VariablePropertyFields.Draw(
            _kind, _state, "bp_props",
            BlueprintTypeSystem.SelectableTypeIds,
            ShortName,
            enabled: _editable,
            // ⛔ ALWAYS disabled this batch — see RetypeUnavailable. ⚠ Shown even in an editable form,
            //    because the reason is about the OPERATION, not about the run state.
            typeDisabledReason: RetypeUnavailable,
            // ⭐ Enabled exactly when a schema reached this form, because that is what makes the
            //   refactor-service route available. ⛔ Never a Name box that silently does not commit.
            nameDisabledReason: CanRename ? null : RenameUnavailableHere);

        ImGui.Separator();

        if (!_editable) ImGui.BeginDisabled();
        if (ImGui.Button("OK", new System.Numerics.Vector2(100, 0)))
        {
            Commit();
            ImGui.CloseCurrentPopup();
        }
        if (!_editable) ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(_editable ? "Cancel" : "Close", new System.Numerics.Vector2(100, 0)))
        {
            _row = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The commit — TWO paths, because two of the fields are operations.</b>
    ///
    /// <para>⭐ <b>Properties first, rename second.</b> ⚠ The property write is keyed by NAME, so
    /// renaming first would key it by a name the source no longer knows. ⛔ Not a style choice.</para>
    ///
    /// <para>⛔ <b><c>Type</c> is never written</b> — 📌 <c>R-109</c>. The form shows it disabled with
    /// its reason; this method does not read <c>_state.TypeId</c> at all, so a future edit that enables
    /// the control cannot silently start writing it.</para>
    /// </summary>
    internal void Commit()
    {
        if (_row is not { } row) return;

        if (row.WriteProperties is { } write)
            write(new VariablePropertyValues(
                DefaultValueJson: _state.DefaultValueJson,
                Tooltip:          _state.Tooltip,
                Comment:          _state.Comment,
                Category:         _state.Category,
                IsEditable:       _state.IsEditable,
                IsExposedOnSpawn: _state.IsExposedOnSpawn));

        // ⭐⭐ THE RENAME, through the ONE implementation — 📌 R-109 and ruling 9.
        // ⛔ Never `schema.RenameVariable` directly: that skips the refactor service, and on BTree/HSM
        //   that is the difference between a rename and a dangling binding (M-15).
        if (CanRename)
            LastRenameOutcome = VariableRenameCommit.Rename(
                _schema!, _refactorService!, _assetId, _originalName, _state.Name);

        _row = null;
    }

    private static string ShortName(string typeId)
    {
        if (string.IsNullOrEmpty(typeId)) return "(none)";
        var dot = typeId.LastIndexOf('.');
        return dot >= 0 && dot < typeId.Length - 1 ? typeId[(dot + 1)..] : typeId;
    }
}
