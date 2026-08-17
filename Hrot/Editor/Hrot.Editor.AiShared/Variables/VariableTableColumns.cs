using System;
using System.Collections.Generic;

namespace Hrot.Editor.AiShared.Variables;

/// <summary>The three columns that may ever exist. ⛔ There is no fourth.</summary>
public enum VariableColumn
{
    Name,
    Value,
    Type,
}

/// <summary>
/// ⭐⭐ <b>The column set — <c>Name</c> and <c>Value</c> mandatory, <c>Type</c> the ONE toggle (§9).</b>
///
/// <para>
/// ⚠ <b>Today's control has SEVEN</b>: <c>Name · Type · Bytes · Value · Role · Scope · remove</c>.
/// ⛔ <b>Bytes, Role and Scope go</b> — everything dropped lives in the dialog. And <c>Role</c>/
/// <c>Scope</c> are not merely moved: §1c deletes them as a concept, because ⭐ <b>the SECTION is the
/// classification</b>.
/// </para>
///
/// <para>
/// ⛔⛔ <b>Deliberately NOT a column-visibility framework.</b> §1's own words: <i>"seven columns is what
/// we are escaping; a configurable system is how it grows back. One named toggle cannot drift."</i>
/// ⇒ this type is a <b>struct with one bool</b>, not a set of columns a caller can add to. ⭐ That is
/// why the rail — <i>"any other column fails it"</i> — is enforced by the TYPE rather than by a test:
/// there is no expression that names a fourth column.
/// </para>
///
/// <para>
/// ⭐ <b>Defaults differ by surface, and the reason is in the user's own words:</b> Watch hides
/// <c>Type</c> — <i>"not even the data type is important for monitoring"</i> — while Details shows it,
/// because Details is authoring, where you pick types.
/// </para>
/// </summary>
public readonly record struct VariableTableColumns(bool ShowType)
{
    /// <summary>Details — authoring ⇒ <c>Type</c> shown.</summary>
    public static VariableTableColumns Details => new(ShowType: true);

    /// <summary>Watch — monitoring ⇒ <c>Type</c> hidden.</summary>
    public static VariableTableColumns Watch => new(ShowType: false);

    /// <summary>The ordered visible set. ⭐ <c>Name</c> first and <c>Value</c> last-but-one is the
    /// reading order the design assumes; <c>Type</c> sits between them when shown.</summary>
    public IReadOnlyList<VariableColumn> Visible => ShowType
        ? new[] { VariableColumn.Name, VariableColumn.Type, VariableColumn.Value }
        : new[] { VariableColumn.Name, VariableColumn.Value };

    /// <summary>⭐ The rail in executable form: whatever the toggle says, <c>Name</c> and <c>Value</c>
    /// are present and the set is a subset of the three.</summary>
    public bool IsValid
    {
        get
        {
            var visible = Visible;
            bool name = false, value = false;
            foreach (var c in visible)
            {
                if (c == VariableColumn.Name)  name  = true;
                if (c == VariableColumn.Value) value = true;
                if (c != VariableColumn.Name && c != VariableColumn.Value && c != VariableColumn.Type)
                    return false;
            }
            return name && value;
        }
    }
}
