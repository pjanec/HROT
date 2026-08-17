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
/// ⭐⭐ <b>Sections are the <c>Role × Scope</c> product made visible</b>, exactly as §1c tabulates it:
/// <b>Inputs</b> = <c>Role.Input</c> · <b>Working State</b> = <c>Role.State</c> with <c>Node</c> scope ·
/// <b>Asset Globals</b> = <c>Role.State</c> shared wider (<c>Behavior</c>/<c>Entity</c>). ⛔ <b>And
/// still no <c>Role</c>/<c>Scope</c> control on any host</b> — the section IS the control.
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
    public const string SectionInputs       = "bb.inputs";
    public const string SectionWorkingState = "bb.workingState";
    public const string SectionAssetGlobals = "bb.assetGlobals";

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
                Id: SectionInputs, DisplayName: "Inputs", SortOrder: 0, IconKey: null,
                CanCreateItems: true, CanHaveCategories: false,
                CreateCommandId: $"{prefix}.blackboard.createInput"),

            new MyBlueprintSectionDescriptor(
                Id: SectionWorkingState, DisplayName: "Working State", SortOrder: 1, IconKey: null,
                CanCreateItems: true, CanHaveCategories: false,
                CreateCommandId: $"{prefix}.blackboard.createWorkingState"),

            new MyBlueprintSectionDescriptor(
                Id: SectionAssetGlobals, DisplayName: "Asset Globals", SortOrder: 2, IconKey: null,
                CanCreateItems: true, CanHaveCategories: false,
                CreateCommandId: $"{prefix}.blackboard.createAssetGlobal"),
        };
    }

    /// <summary>⭐ Which section a variable belongs to — §1c's table, in one place.</summary>
    public static string SectionOf(BlackboardVariableEntry v)
        => v.Role != BlackboardVariableRole.State ? SectionInputs
         : v.Scope == WorkingStateScope.Node      ? SectionWorkingState
         :                                          SectionAssetGlobals;

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
