using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.AiEditor.Persistence;
using Hrot.Editor.AiShared.Blackboard;
using NodeEditor.Core.Interfaces;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>Which AI host a <see cref="BlackboardMyBlueprintModel"/> is describing.</summary>
public enum BlackboardHostKind
{
    BTree,
    Hsm,
}

/// <summary>
/// ⭐⭐⭐ <b><c>C-outline</c> — BTree and HSM get their own My Blueprint outline.</b>
///
/// <para>
/// 📄 <c>DESIGN_Variable_Details_And_Editing.md</c> §1c: ⭐ <b>sections ARE the classification.</b>
/// <c>C-sections</c> (Batch 66) did this for blueprints; this is the same shape for the AI hosts. ⛔
/// The panel itself needed nothing — <c>MyBlueprintPanel</c> lives in <c>NodeEditor.UI</c> and
/// <c>IMyBlueprintModel</c> in <c>NodeEditor.Core</c>, so <i>"nothing about it is
/// blueprint-specific"</i>.
/// </para>
///
/// <para>
/// ⭐⭐ <b>ONE implementation, two section lists — not two models.</b> The hosts differ in <b>which
/// sections exist</b> and in their create-command ids; the machinery that turns a blackboard into
/// items is identical. ⛔ Two classes would be two places to fix the next section rule, which is the
/// duplication §1c's own <c>Role</c>/<c>Scope</c> deletion was about.
/// </para>
///
/// <para>
/// ⭐⭐ <b>ONE section, "Variables"</b> (<c>CE-3193</c>, <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §5c B3a): the
/// reserved <c>Params</c> row first, then every variable. ⛔ HISTORY: three sections — Inputs / Working State / Asset Globals —
/// were the authored <c>Role × Scope</c> product made visible; the author had nothing to decide with them, so the kind went
/// and so did they. Whether a variable is working state is derived from the bindings.
/// </para>
///
/// <para>
/// ⚠ <b>EMPTY rather than ABSENT.</b> <c>SectionLocalVariables</c>' rule applies here too: <i>"a
/// section that appears and disappears reads as a broken feature."</i> ⇒ every section the host
/// supports is always listed; only its item list is empty.
/// </para>
/// </summary>
public sealed class BlackboardMyBlueprintModel : IMyBlueprintModel
{
    public const string SectionInputs       = "bb.inputs";   // ⚠ the id predates the rename to "Variables"; ids are not shown

    private readonly Func<IReadOnlyList<BlackboardVariableEntry>> _variables;
    private readonly IReadOnlyList<MyBlueprintSectionDescriptor>  _sections;

    public BlackboardMyBlueprintModel(
        BlackboardHostKind host,
        Func<IReadOnlyList<BlackboardVariableEntry>> variables)
    {
        _variables = variables ?? throw new ArgumentNullException(nameof(variables));
        Host       = host;
        _sections  = BuildSections(host);
    }

    public BlackboardHostKind Host { get; }

    public IReadOnlyList<MyBlueprintSectionDescriptor> Sections => _sections;

    public event Action? Changed;

    /// <summary>Hosts call this when the asset's blackboard changes.</summary>
    public void RaiseChanged() => Changed?.Invoke();

    /// <summary>
    /// ⭐ The per-host section list. ⚠ The <b>create-command ids are host-qualified</b> so the two
    /// outlines cannot pick up each other's commands when both perspectives are open.
    /// </summary>
    private static IReadOnlyList<MyBlueprintSectionDescriptor> BuildSections(BlackboardHostKind host)
    {
        string prefix = host == BlackboardHostKind.Hsm ? "hsm" : "btree";

        return new[]
        {
            new MyBlueprintSectionDescriptor(
                // ⭐ CE-2135 (B3a) — "Variables": the packed, bindable variables (the reserved Params first, when named).
                //   ⛔ HISTORY: "Inputs" — every row here was the behaviour's JSON params until it named a params DTO.
                Id: SectionInputs, DisplayName: "Variables", SortOrder: 0, IconKey: null,
                CanCreateItems: true, CanHaveCategories: false,
                CreateCommandId: $"{prefix}.blackboard.createInput"),
        };
    }

    /// <summary>
    /// ⭐⭐ <b>The heading a section shows.</b> ⛔ Static because a Details panel must resolve it BEFORE
    /// an asset is selected — the model is null until then, and a routing path that needs a model would
    /// silently produce a raw id like <c>"bb.inputs"</c> as the heading.
    ///
    /// <para>⭐ Reads the same descriptor list <see cref="Sections"/> is built from, so the outline tree
    /// and the Details heading cannot disagree. ⚠ The two hosts' display names are identical; only
    /// their create-command ids are host-qualified, which is why this takes no host.</para>
    /// </summary>
    public static string DisplayNameOf(string sectionId)
        => BuildSections(BlackboardHostKind.BTree)
               .FirstOrDefault(s => s.Id == sectionId)?.DisplayName
           ?? sectionId;

    /// <summary>⭐ Which section a variable belongs to — §1c's table, in one place.</summary>
    public static string SectionOf(BlackboardVariableEntry v)
        => SectionInputs;   // ⭐ CE-3193 — one section

    public IReadOnlyList<MyBlueprintItem> GetItems(string sectionId)
        => _variables()
            .Where(v => SectionOf(v) == sectionId)
            .Select(v => new MyBlueprintItem(
                ItemId:       v.Name,
                SectionId:    sectionId,
                DisplayName:  v.Name,
                CategoryPath: null,
                IconKey:      null,
                BadgeText:    v.TypeDisplayName(),
                AccentColor:  null,
                Children:     null,
                // ⚠ An editor-owned (node-owned) variable is neither renamable nor deletable by the
                //   designer -- §5's row-kind rule, applied to the outline rather than to the table.
                IsRenamable:  !v.IsAutoManaged,
                IsDeletable:  !v.IsAutoManaged,
                IsHostDefined: v.IsAutoManaged,
                Tooltip:      v.Comment))
            .ToList();
}

internal static class BlackboardVariableEntryDisplay
{
    /// <summary>Short type label for the outline badge.</summary>
    public static string TypeDisplayName(this BlackboardVariableEntry v) => v.FieldType.Name;
}
