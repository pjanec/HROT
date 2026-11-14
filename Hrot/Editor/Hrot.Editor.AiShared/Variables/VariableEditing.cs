using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Inspector;
using StructEdit.Core;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>
/// ⭐⭐ <b>The two menu items.</b> ⭐ The USER picks the act; run state only decides availability.
///
/// <para>⚠⚠ <b>Batch 96 (<c>96b</c>) — the old sentence here was <i>"the two menu items ARE the two
/// <see cref="EditScope"/>s (§3)"</i>, and 📐 it is FALSE for a whole-variable edit:</b> both open the
/// whole value document, because the session's root IS the value. ⛔ Kept as a note rather than
/// deleted — the design said it, and what it should have distinguished *(value vs DECLARATION)* is a
/// gap this batch filed rather than built.</para>
/// </summary>
public enum VariableEditAction
{
    /// <summary>⭐ <i>"Edit value…"</i> · double-click the VALUE cell.
    /// ⚠ <b>Batch 96 (<c>96b</c>):</b> this used to say <i>"⇒ <c>EditScope.ForField</c>"</i>. 📐 Measured
    /// false — the session is opened over the variable's VALUE, so a whole-variable edit IS the whole
    /// document. See <see cref="VariableEditLauncher.ScopeFor"/>.</summary>
    EditValue,
    /// <summary>⭐ <i>"Properties…"</i> · double-click the NAME cell ⇒ <see cref="EditScope.WholeComponent"/>.
    /// ⚠ It opens the VALUE document, not the DECLARATION — a gap filed by Batch 96, not built.</summary>
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

    /// <summary>
    /// ⭐ The scope the dialog opens with.
    ///
    /// <para>
    /// ⛔⛔⛔ <b>Batch 96 (<c>96b</c>) — A VARIABLE'S NAME IS NOT A PATH INSIDE ITS OWN VALUE.</b>
    /// 📐 This used to be <c>EditScope.ForField(EditPath.Parse(ToJsonPath(variablePath)))</c>, i.e.
    /// <c>"$.Count"</c> for a variable named <c>Count</c>. ⚠ But
    /// <see cref="DefaultValueAuthoring.OpenSession"/> opens the session over <b>THE VARIABLE'S
    /// VALUE</b> — <c>Open(instance, varEntry.FieldType, scope)</c> — so <b>the document root IS the
    /// value, at <c>$</c></b>. ⇒ ⛔ <c>"$.Count"</c> asked for a field named <c>Count</c> INSIDE the
    /// <c>int</c>; there is none, <c>FilterNode</c> matched nothing, and <c>ApplyScope</c> fell through
    /// to an <b>EMPTY <c>SelectionRoot</c></b>.
    /// </para>
    ///
    /// <para>
    /// ⇒ ⭐⭐⭐ <b>wrong for EVERY variable on EVERY host, scalar or DTO</b> — for a DTO it meant
    /// <i>"a field called <c>Count</c> inside the DTO"</i>, which is a different thing from
    /// <i>"the variable <c>Count</c>"</i>. ⭐ <b>"Edit value…" of a whole variable IS the whole
    /// document.</b>
    /// </para>
    ///
    /// <para>
    /// ⚠⚠ <b>Batch 75 fixed the SPACE and not the PREMISE.</b> Its note *(kept below, because it is
    /// still true about <see cref="ToJsonPath"/>)* diagnosed <c>"Name"</c> vs <c>"$.Name"</c> and
    /// rooted the path — ⛔ **the rooted path was just as empty**, and the rail that covered it asserted
    /// the scope's shape rather than the resulting DOCUMENT. 📌 That is why it stayed green for four
    /// batches.
    /// </para>
    ///
    /// <para>
    /// ⭐⭐ <b>The <see cref="EditScope.ForField"/> arm is KEPT, not deleted</b> — 📌 the handoff is
    /// explicit: <i>"what <c>ForField</c> is FOR is a real sub-path"</i>, a field INSIDE a DTO variable.
    /// ⚠ <b>That gesture does not exist yet</b>, so no production caller passes
    /// <paramref name="fieldSubPath"/> today — ⛔ and the fix is to stop feeding it the variable name,
    /// not to remove the capability.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>Consequence, stated rather than hidden:</b> for a whole-variable edit both actions now
    /// return <see cref="EditScope.WholeComponent"/>, so <b>they no longer differ by scope</b>. ⛔ The
    /// design's <i>"two menu items = the two <c>EditScope</c>s"</i> was written before this was
    /// measured; what actually distinguishes <i>"Properties…"</i> is that it should edit the
    /// DECLARATION, and it does not — see the report's finding on that, which is a capability question
    /// and not this method's to answer.
    /// </para>
    /// </summary>
    /// <param name="fieldSubPath">
    /// ⭐ A path to a field <b>INSIDE</b> the variable's value, in variable or JSON-path space.
    /// ⛔ <b>NEVER the variable's own name</b> — that is the defect above. <c>null</c> or empty means
    /// <i>"the whole value"</i>, which is what every production caller means today.
    /// </param>
    public static EditScope ScopeFor(VariableEditAction action, string? fieldSubPath = null)
        => action == VariableEditAction.Properties || string.IsNullOrEmpty(fieldSubPath)
            ? EditScope.WholeComponent
            : EditScope.ForField(EditPath.Parse(ToJsonPath(fieldSubPath)));

    /// <summary>⭐ Variable space → StructEdit's JSON-path space. Already-rooted paths pass through, so
    /// a caller that knows the document shape is not second-guessed.
    /// ⚠ <b>Batch 96:</b> its one remaining caller is <see cref="ScopeFor"/>'s sub-path arm — 📐 the
    /// passthrough exists so a caller that already speaks JSON-path space is not rooted twice, and
    /// nothing else in the repo calls it.</summary>
    internal static string ToJsonPath(string variablePath)
        => variablePath.StartsWith("$", StringComparison.Ordinal) ? variablePath : "$." + variablePath;

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

        // ⭐⭐⭐ Batch 96 (96b) — NO sub-path. 🔴 This used to pass row.Origin.VariablePath, which named
        //    the VARIABLE and was then read as a field path INSIDE the variable's own value ⇒ the
        //    document filtered to nothing and the dialog drew an empty body. See ScopeFor.
        // ⭐⭐⭐ THE DIALOG OPENS OVER WHAT THE ROW IS SHOWING — 🔴 it used to always open over the
        //    DECLARATION's default *(user, 2026-08-20: the row read "312", the dialog opened at "0")*.
        // ⭐ The arm is chosen by the same function the COMMIT uses, so "which value am I editing?" and
        //   "where will OK put it?" can never disagree — ⛔ two matrices is how they would.
        // ⚠ Only the OBJECT arm is read: it is the decoded value the table itself renders. A row with
        //   none, or one the run has not written, seeds null and OpenSession falls back to the
        //   declaration — ⛔ never a guess.
        var seed = VariableEditCommit.TargetFor(runState) == VariableEditCommit.Target.LiveBlackboard
            ? row.ReadValueObject?.Invoke()
            : null;

        return DefaultValueAuthoring.OpenSession(
            _editService, entry, ScopeFor(action), seed);
    }
}
