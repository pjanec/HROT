using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using Hrot.AiEditor.Persistence;
using Hrot.Editor.AiShared.Comparison;
using Hrot.Editor.AiShared.Refactor;
using Hrot.Editor.AiShared.Windows;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// ⭐ CE-4254 (<c>docs/DESIGN_Field_Declaration_Editor.md</c> §2) — which affordances a declaration host's Variables table draws. ⭐ DATA on the source, not a type hierarchy and not a
/// host-name test: the host draws a column or button only when its feature is on, so a source cannot inherit a blackboard column it never asked for.
/// ⭐ CE-3401 (D11) — no byte column, no memory budget: the blackboard's only extra over a parameters table is the Live column.
/// </summary>
/// <param name="LiveValue">The <i>Live</i> column: the blackboard's LIVE runtime value (<see cref="Fdp.Presentation.Editing.IFieldLiveValues"/>).</param>
/// <param name="UsedByColumn">A read-only <i>used by N</i> column (<c>CountNodesReferencingVariable</c>).</param>
/// <param name="RemoveUnused">The <i>[ Remove unused ]</i> bulk delete.</param>
public sealed record DeclarationFeatures(bool LiveValue, bool UsedByColumn, bool RemoveUnused)
{
    /// <summary>Today's blackboard / blueprint-variable table: the Live column and the bulk delete.</summary>
    public static readonly DeclarationFeatures Blackboard = new(LiveValue: true, UsedByColumn: false, RemoveUnused: true);
    /// <summary>A scenario / macro's declared PARAMETERS: a used-by count; no live value, no bulk delete.</summary>
    public static readonly DeclarationFeatures Parameters = new(LiveValue: false, UsedByColumn: true, RemoveUnused: false);
}

public interface IVariablesSchemaSource
{
    bool IsReadOnly { get; }

    /// <summary>⭐ CE-4254 — what this source's table draws. ⛔ NO DEFAULT BODY (the seam's rule): every implementer answers, so a new source cannot compile without deciding.</summary>
    DeclarationFeatures Features { get; }
    IReadOnlyList<VariableViewModel> Variables { get; }
    string? GetRefactorKey(string variableName);
    void AddVariable(BlackboardVariableEntry entry);
    void RemoveVariable(string name);
    void RemoveVariables(IReadOnlyList<string> names);
    void RenameVariable(string oldName, string newName);
    void MoveVariable(int sourceIndex, int destIndex);
    int CountNodesReferencingVariable(string name);
    
    /// <summary>
    /// ⭐⭐⭐ <b>Batch 98 (<c>98a</c>) — WHERE AN INITIAL-VALUE EDIT LANDS, in the vocabulary all three
    /// hosts already share.</b>
    ///
    /// <para>🔴🔴 <b>The defect.</b> 📐 Measured: <c>VariableEditCommit.CommitInitialValue</c> resolved
    /// its write target through <c>PerspectiveWorkspaceRegistrar.DeclarationOwnerOf</c>, which
    /// type-tests <c>store.ActiveAsset is IBlackboardManagedAsset</c> — ⛔ <b>and
    /// <c>BlueprintAsset</c> is not one.</b> ⇒ in <b>PLANNING</b>, the ordinary authoring state, the
    /// target is the initial value, the owner was <b>always <c>null</c> on Blueprint</b>, and
    /// <b>OK refused on every Blueprint variable, every time.</b></para>
    ///
    /// <para>⭐⭐ <b>Why HERE and not on <c>IBlackboardManagedAsset</c>.</b> 📌 <c>95a</c> and
    /// <c>R-108</c> both keep the two vocabularies apart on purpose: <c>IBlackboardManagedAsset</c> is
    /// the <b>AI blackboard's</b> interface, and a <c>BlueprintAsset</c> speaks
    /// <c>VariableDecl</c>/<c>ParameterDecl</c> with a persisted <c>Guid Id</c>. ⛔ Widening it to
    /// swallow blueprints is explicitly forbidden by this batch's handoff. ⭐ <b>This interface is
    /// already the one thing all three hosts implement</b> — it carries <c>RenameVariable</c>,
    /// <c>RemoveVariable</c> and <c>MoveVariable</c> for exactly the same reason.</para>
    ///
    /// <para>⛔⛔ <b>NO DEFAULT BODY, deliberately.</b> 📌 <c>U-5</c>/<c>BP-230</c>, stated in this very
    /// file: <i>"a default body is the interface volunteering to lie on an implementer's behalf."</i>
    /// ⚠ That is precisely how <c>UpdateVariableRole</c> shipped as <c>{ }</c> and how a blueprint's
    /// Role combo landed in an empty body for two batches. ⭐ Every implementer answers, and a new one
    /// <b>cannot compile</b> without deciding.</para>
    ///
    /// <para>⭐ <c>null</c> clears the authored default — byte-stable, exactly as
    /// <c>IBlackboardManagedAsset.UpdateVariableDefaultValueJson</c> defines it. ⚠ An unknown name is
    /// a <b>no-op</b>, not a throw: the row may have been deleted under an open dialog.</para>
    /// </summary>
    void UpdateVariableDefaultValueJson(string name, string? defaultValueJson);

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 99 (<c>99a</c>) — where the PROPERTIES form's OK lands.</b>
    ///
    /// <para>📌 <c>R-108</c>/<c>R-109</c>: <i>"'Properties…' must open the DECLARATION, not the
    /// value"</i>, and it is a <b>CUSTOM</b> form. ⇒ it needs somewhere to put the declaration's
    /// members, and <see cref="UpdateVariableDefaultValueJson"/> reaches exactly one of them.</para>
    ///
    /// <para>⭐ <b>ONE method, not five setters.</b> The properties are committed together by one OK,
    /// so five calls would be five chances to fire the dirty callback five times — ⛔ and a host that
    /// implemented four of them would look finished.</para>
    ///
    /// <para>⚠ <c>Variables.VariablePropertyValues</c> carries <c>null</c> for <i>"this kind does not
    /// have it"</i>, and an implementer <b>must leave those members alone</b> — ⛔ coercing <c>null</c>
    /// to <c>""</c> erases a comment the form never showed.</para>
    ///
    /// <para>⛔⛔ <b><c>Name</c> and <c>Type</c> are NOT part of this, and that is <c>R-109</c>.</b>
    /// A rename is an OPERATION and goes through <c>VariableRenameCommit</c> *(the refactor service —
    /// 📌 <c>M-15</c>)*; a retype is a MIGRATION *(<c>StructureHash</c> moves — <c>R-24</c>)*.</para>
    ///
    /// <para>⛔ <b>NO DEFAULT BODY</b> — 📌 <c>U-5</c>/<c>BP-230</c>, stated above.</para>
    /// </summary>
    void UpdateVariableProperties(string name, Variables.VariablePropertyValues values);

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 99 (<c>99a</c>) — reads the declaration's KIND and its non-value members.</b>
    ///
    /// <para>⚠ <b>Why <see cref="Variables"/>' view model is not enough.</b> 📐 <c>VariableViewModel</c>
    /// carries <c>DefaultValueJson</c> and <b>not</b> Tooltip, Category, IsEditable or IsExposedOnSpawn
    /// — and those are exactly the members <c>VariablePropertySchema.For(BlueprintVariable)</c> says the
    /// form must show. ⛔ Widening the view model would put four members on the projection <b>every
    /// table row builds</b>, for a form that opens on one row at a time.</para>
    ///
    /// <para>⭐ <b>The KIND rides along</b> because this source is the only thing that knows which
    /// carrier it read — 📌 <c>95a</c>: the asset is exactly what cannot be type-tested.</para>
    ///
    /// <para>⛔ <b>NO DEFAULT BODY.</b> ⚠ <c>null</c> for an unknown name is correct and expected.</para>
    /// </summary>
    Variables.DeclarationPropertySnapshot? ReadVariableProperties(string name);

    // Aliasing
    IReadOnlyList<UnboundRequirementViewModel> UnboundRequirements { get; }
    void AddAlias(string name, BlackboardAliasBinding binding);
    void RemoveAlias(string name, Guid requirementAssetId, Guid requirementElementId);
    IReadOnlyDictionary<Guid, int>? GetParallelRegionMap();
}

public sealed class BTreeHsmSchemaSource : IVariablesSchemaSource, IRevisionedSource
{
    private readonly IBlackboardManagedAsset _asset;
    private BlackboardWindowViewModel _vm;
    private bool _isReadOnly;
    private VariableViewModel[] _seen = Array.Empty<VariableViewModel>();
    private int _seenCount = -1;
    private int _revision;

    public BTreeHsmSchemaSource(IBlackboardManagedAsset asset, BlackboardWindowViewModel vm, bool isReadOnly)
    {
        _asset = asset;
        _vm = vm;
        _isReadOnly = isReadOnly;
    }

    /// <summary>
    /// ⭐ CE-3401 — a long-lived source: the window builds a NEW view model every frame, so the host hands each one in here. <see cref="Revision"/> moves only when a row (or the read-only state) really differs from
    /// the last one — what the Define table compares, so a steady frame rebuilds nothing. ⚠ Allocates only when something changed.
    /// </summary>
    public void Update(BlackboardWindowViewModel vm, bool isReadOnly)
    {
        _vm = vm;
        var rows = vm.Variables;
        bool same = _seenCount == rows.Count && _isReadOnly == isReadOnly;
        for (int i = 0; same && i < rows.Count; i++) same = RowSame(_seen[i], rows[i]);
        _isReadOnly = isReadOnly;
        if (same) return;
        _revision++;
        if (_seen.Length < rows.Count) _seen = new VariableViewModel[Math.Max(rows.Count, _seen.Length * 2)];
        for (int i = 0; i < rows.Count; i++) _seen[i] = rows[i];
        _seenCount = rows.Count;
    }

    /// <summary>Moves when a row's name, type, default, comment, flags, aliases or params fields differ from the last <see cref="Update"/>.</summary>
    public int Revision => _revision;

    private static bool RowSame(VariableViewModel a, VariableViewModel b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Name != b.Name || a.TypeName != b.TypeName || a.DefaultValueJson != b.DefaultValueJson || a.Comment != b.Comment
            || a.IsUnused != b.IsUnused || a.IsAutoManaged != b.IsAutoManaged || a.IsReadOnly != b.IsReadOnly || a.FieldType != b.FieldType) return false;
        var x = a.AliasedBy; var y = b.AliasedBy;
        if (x.Count != y.Count) return false;
        for (int i = 0; i < x.Count; i++) if (x[i].AssetId != y[i].AssetId || x[i].ElementId != y[i].ElementId || x[i].AssetName != y[i].AssetName) return false;
        var f = a.ParamsFields; var g = b.ParamsFields;
        if ((f?.Count ?? 0) != (g?.Count ?? 0)) return false;
        for (int i = 0; i < (f?.Count ?? 0); i++)
            if (f![i].Name != g![i].Name || f[i].TypeName != g[i].TypeName || f[i].ValueJson != g[i].ValueJson || f[i].IsOverridden != g[i].IsOverridden) return false;
        return true;
    }

    public bool IsReadOnly => _isReadOnly;
    public DeclarationFeatures Features => DeclarationFeatures.Blackboard;
    public IReadOnlyList<VariableViewModel> Variables => _vm.Variables;
    public string? GetRefactorKey(string variableName) => null;
    // ⭐ CE-1277 — a read-only table (a code-built graph, a blackboard that failed to load) REFUSES the edit itself, not only its button.
    public void AddVariable(BlackboardVariableEntry entry) { if (!_isReadOnly) _asset.AddVariable(entry); }
    public void RemoveVariable(string name) { if (!_isReadOnly) _asset.RemoveVariable(name); }
    public void RemoveVariables(IReadOnlyList<string> names) { if (!_isReadOnly) _asset.RemoveVariables(names); }
    public void RenameVariable(string oldName, string newName) { if (!_isReadOnly) _asset.RenameVariable(oldName, newName); }
    public void MoveVariable(int sourceIndex, int destIndex) { if (!_isReadOnly) _asset.MoveVariable(sourceIndex, destIndex); }
    public int CountNodesReferencingVariable(string name) => _asset.CountNodesReferencingVariable(name);
    

    /// <summary>
    /// ⭐ 99a — an AI blackboard entry carries only DefaultValue and Comment of the properties set
    /// *(📌 <c>VariablePropertySchema.For(BlackboardEntry)</c>: four properties, not eight)*, and the
    /// asset already owns both writes. ⚠ <c>null</c> members are LEFT ALONE, per the contract.
    /// </summary>
    public void UpdateVariableProperties(
        string name, Hrot.Editor.AiShared.Variables.VariablePropertyValues values)
    {
        if (values is null) return;
        if (values.DefaultValueJson is not null)
            _asset.UpdateVariableDefaultValueJson(name, values.DefaultValueJson);
        if (values.Comment is not null) _asset.UpdateVariableComment(name, values.Comment);
    }

    /// <summary>
    /// ⭐ 99a — the entry IS the carrier here, so this reads it directly.
    /// ⚠ Tooltip/Category/IsEditable/IsExposedOnSpawn stay <c>null</c>: the entry has <b>no member for
    /// any of them</b>, and the schema agrees. ⛔ Inventing them would draw controls with nowhere to save.
    /// </summary>
    public Hrot.Editor.AiShared.Variables.DeclarationPropertySnapshot? ReadVariableProperties(string name)
    {
        foreach (var v in _asset.BlackboardVariables)
            if (string.Equals(v.Name, name, StringComparison.Ordinal))
                return new Hrot.Editor.AiShared.Variables.DeclarationPropertySnapshot(
                    Hrot.Editor.AiShared.Variables.VariableDeclarationKind.BlackboardEntry,
                    new Hrot.Editor.AiShared.Variables.VariablePropertyValues(
                        DefaultValueJson: v.DefaultValueJson ?? "",
                        Comment:          v.Comment ?? ""),
                    TypeId: v.FieldType?.FullName ?? "");
        return null;
    }

    // ⭐ 98a — the asset already owns this exact call (and its dirty marking); this source forwards.
    //   ⛔ Nothing is re-implemented here: BTree/HSM's initial-value write was never the broken half.
    public void UpdateVariableDefaultValueJson(string name, string? defaultValueJson)
        => _asset.UpdateVariableDefaultValueJson(name, defaultValueJson);

    public IReadOnlyList<UnboundRequirementViewModel> UnboundRequirements => _vm.UnboundRequirements;
    public void AddAlias(string name, BlackboardAliasBinding binding) => _asset.AddAlias(name, binding);
    public void RemoveAlias(string name, Guid reqAssetId, Guid reqElemId) => _asset.RemoveAlias(name, reqAssetId, reqElemId);
    public IReadOnlyDictionary<Guid, int>? GetParallelRegionMap() => _asset.GetParallelRegionMap();
}
