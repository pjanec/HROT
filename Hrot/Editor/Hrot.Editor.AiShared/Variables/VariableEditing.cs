using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using StructEdit.Core;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐ <b>The two menu items ARE the two <see cref="EditScope"/>s (§3).</b> ⭐ The USER picks the act;
/// run state only decides availability.
/// </summary>
public enum VariableEditAction
{
    /// <summary>⭐ <i>"Edit value…"</i> · double-click the VALUE cell ⇒ <see cref="EditScope.ForField"/>.</summary>
    EditValue,
    /// <summary>⭐ <i>"Properties…"</i> · double-click the NAME cell ⇒ <see cref="EditScope.WholeComponent"/>.</summary>
    Properties,
}

/// <summary>What the run state and the row kind together permit.</summary>
public enum VariableEditAvailability
{
    /// <summary>⛔ no dialog at all.</summary>
    Denied,
    /// <summary>⚠ opens, but nothing can be committed.</summary>
    ReadOnly,
    Editable,
}

/// <summary>The three carriers §2 measured. ⛔ There is no fourth; adding one means adding a carrier.</summary>
public enum VariableDeclarationKind
{
    /// <summary><c>VariableDecl</c> — nine members.</summary>
    BlueprintVariable,
    /// <summary><c>ParameterDecl</c> — six members.</summary>
    BlueprintParameter,
    /// <summary><c>BlackboardVariableEntry</c> — seven members.</summary>
    BlackboardEntry,
}

/// <summary>An editable property of a declaration. ⛔ <c>Role</c>/<c>Scope</c> is deliberately absent.</summary>
public enum VariableProperty
{
    Name,
    Type,
    DefaultValue,
    Tooltip,
    Comment,
    Category,
    IsEditable,
    IsExposedOnSpawn,
}

/// <summary>
/// ⭐⭐ <b>The editable set, per declaration kind — MEASURED off the carriers, not taken from a spec
/// (§2).</b>
///
/// <para>
/// ⛔ <b>Fields with no storage are not here.</b> <c>D7</c>'s <b>Replication</b> (Replicated,
/// RepCondition, RepNotify) and <b>Range</b> (Min/Max) have <b>no member on any carrier</b> — building
/// them would produce controls with nowhere to save. ⇒ §9's rail asserts each set against the
/// carrier's REFLECTED members, so a property with no backing member fails.
/// </para>
///
/// <para>
/// ⛔⛔ <b><c>Role</c>/<c>Scope</c> is NOT A PROPERTY AT ALL</b> (§1c, user ruling <c>2026-08-16</c>) —
/// ⭐ <b>the SECTION is the classification.</b> Not in the dialog, not a column, not editable on any
/// host. <c>BlackboardVariableEntry</c> does carry the two members, which is exactly why their absence
/// here has to be deliberate and asserted rather than incidental.
/// </para>
///
/// <para>
/// ⚠ <b><c>IsExposedOnSpawn</c> is KEPT although nothing reads it at spawn.</b> Per the
/// <c>.dev/</c> rule, unreferenced ≠ unintentional: it is persisted and it has a backing member, so it
/// is storable and belongs. 📐 <b>The gap is FILED, not closed</b> — see the Batch 68 report.
/// </para>
/// </summary>
public static class VariablePropertySchema
{
    private static readonly VariableProperty[] BlueprintVariableSet =
    {
        VariableProperty.Name, VariableProperty.Type, VariableProperty.DefaultValue,
        VariableProperty.Tooltip, VariableProperty.Comment, VariableProperty.Category,
        VariableProperty.IsEditable, VariableProperty.IsExposedOnSpawn,
    };

    private static readonly VariableProperty[] BlueprintParameterSet =
    {
        VariableProperty.Name, VariableProperty.Type, VariableProperty.DefaultValue,
        VariableProperty.Tooltip, VariableProperty.Comment,
    };

    private static readonly VariableProperty[] BlackboardEntrySet =
    {
        VariableProperty.Name, VariableProperty.Type, VariableProperty.DefaultValue,
        VariableProperty.Comment,
    };

    public static IReadOnlyList<VariableProperty> For(VariableDeclarationKind kind) => kind switch
    {
        VariableDeclarationKind.BlueprintVariable  => BlueprintVariableSet,
        VariableDeclarationKind.BlueprintParameter => BlueprintParameterSet,
        VariableDeclarationKind.BlackboardEntry    => BlackboardEntrySet,
        _ => Array.Empty<VariableProperty>(),
    };

    /// <summary>
    /// The carrier member that backs <paramref name="property"/>, or <c>null</c> when the kind does not
    /// have it. ⭐ Used by §9's rail to prove every offered property is storable.
    /// </summary>
    public static string? BackingMember(VariableDeclarationKind kind, VariableProperty property)
        => (kind, property) switch
        {
            (_, VariableProperty.Name)         => "Name",
            (VariableDeclarationKind.BlackboardEntry, VariableProperty.Type) => "FieldType",
            (_, VariableProperty.Type)         => "Type",
            (_, VariableProperty.DefaultValue) => "DefaultValueJson",
            (VariableDeclarationKind.BlackboardEntry, VariableProperty.Comment) => "Comment",
            (_, VariableProperty.Comment)      => "Comment",
            (VariableDeclarationKind.BlackboardEntry, _) => null,
            (_, VariableProperty.Tooltip)      => "Tooltip",
            (VariableDeclarationKind.BlueprintVariable, VariableProperty.Category)         => "Category",
            (VariableDeclarationKind.BlueprintVariable, VariableProperty.IsEditable)       => "IsEditable",
            (VariableDeclarationKind.BlueprintVariable, VariableProperty.IsExposedOnSpawn) => "IsExposedOnSpawn",
            _ => null,
        };
}

/// <summary>
/// ⭐ <b>§5's matrix, as a function.</b> ⭐⭐ <b>Run state decides WRITABILITY, not which dialog</b> —
/// the user already chose the act.
/// </summary>
public static class VariableEditPolicy
{
    public static VariableEditAvailability Resolve(
        VariableEditAction action, VariableRunState runState, VariableRow row)
    {
        // ⛔ Editability = run state ∧ ROW KIND. 🔒 passthrough and node-owned rows never get a
        //   writable dialog, in either mode; a stale row's asset or entity is gone entirely.
        if (row.IsStale) return VariableEditAvailability.Denied;

        // ⛔ Replay: no dialog. There is nothing to edit and nothing to stage.
        if (runState == VariableRunState.Replay) return VariableEditAvailability.Denied;

        if (!row.CanEverBeWritten) return VariableEditAvailability.ReadOnly;

        return (action, runState) switch
        {
            // planning: the value dialog edits the INITIAL value ⇒ JSON; properties fully editable.
            (_, VariableRunState.Planning) => VariableEditAvailability.Editable,

            // running/paused: the value dialog edits the LIVE value ⇒ staged...
            (VariableEditAction.EditValue, _) => VariableEditAvailability.Editable,

            // ...but ⛔ you cannot retype a variable mid-run.
            _ => VariableEditAvailability.ReadOnly,
        };
    }
}

/// <summary>
/// ⭐⭐⭐ <b>ONE dialog implementation, two scopes (§3, §9).</b>
///
/// <para>
/// ⛔ <b>The two actions differ ONLY by the <see cref="EditScope"/> argument</b> — same
/// <c>IEditSession</c> lifecycle, same OK/Cancel, same validation. §9's rail is a reflection test:
/// exactly one call site constructs a variable edit session, and this routes to it
/// (<see cref="DefaultValueAuthoring.OpenSession"/>) rather than calling
/// <c>IComponentEditService.Open</c> again.
/// </para>
///
/// <para>
/// 🔴 <b>That rail failed before Batch 68:</b> <c>InspectorWindow</c> inlined its own copy of
/// <c>Hydrate</c> and opened its own session, so a variable default-value dialog had two
/// implementations. Routed, not rebuilt.
/// </para>
/// </summary>
public sealed class VariableEditLauncher
{
    private readonly IComponentEditService _editService;

    public VariableEditLauncher(IComponentEditService editService)
        => _editService = editService ?? throw new ArgumentNullException(nameof(editService));

    /// <summary>⭐ The scope for an action — the ONLY thing that differs between the two menu items.</summary>
    public static EditScope ScopeFor(VariableEditAction action, string variablePath)
        => action == VariableEditAction.Properties
            ? EditScope.WholeComponent
            : EditScope.ForField(EditPath.Parse(variablePath));

    /// <summary>
    /// Opens the dialog for <paramref name="row"/>, or returns <c>null</c> when §5 denies it.
    /// ⚠ <see cref="VariableEditAvailability.ReadOnly"/> still OPENS — the design says properties are
    /// read-only mid-run, not absent; refusing to open would hide the values a designer wants to read.
    /// </summary>
    public IEditSession? Open(
        VariableRow row, VariableEditAction action, VariableRunState runState,
        BlackboardVariableEntry entry)
    {
        var availability = VariableEditPolicy.Resolve(action, runState, row);
        if (availability == VariableEditAvailability.Denied) return null;

        return DefaultValueAuthoring.OpenSession(
            _editService, entry, ScopeFor(action, row.Origin.VariablePath));
    }
}
