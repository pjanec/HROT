using System.Linq;

namespace Hrot.Editor.AiShared.Comparison;

/// <summary>
/// Decoration data for a single blackboard variable field, computed from the active comparison session.
/// See design section 6.7.
/// </summary>
public sealed record FieldDecoration(
    bool IsAdded,
    bool IsRemoved,
    bool IsRetyped,
    bool IsRenamed,
    string? OldName,
    string? NewType);

/// <summary>
/// Computes comparison decorations for blackboard variable fields by inspecting the active session.
/// Extracted as a static helper so it can be tested without ImGui.
/// See design section 6.7.
/// </summary>
public static class BlackboardComparisonDecorator
{
    private static readonly FieldDecoration NoneDecoration =
        new FieldDecoration(false, false, false, false, null, null);

    /// <summary>
    /// Returns the decoration info for <paramref name="fieldName"/> from the active session.
    /// Returns a no-decoration record when <paramref name="session"/> is null.
    /// </summary>
    public static FieldDecoration GetDecoration(string fieldName, ComparisonSessionState? session)
    {
        if (session == null) return NoneDecoration;

        bool isAdded   = false;
        bool isRemoved = false;
        bool isRetyped = false;
        bool isRenamed = false;
        string? oldName  = null;
        string? newType  = null;

        foreach (var change in session.Response.Changes)
        {
            // Match on ElementId equality (primary key) or ElementDescription containing the field name.
            bool matchesById   = string.Equals(change.ElementId, fieldName, StringComparison.OrdinalIgnoreCase);
            bool matchesByDesc = change.ElementDescription.Contains(fieldName, StringComparison.OrdinalIgnoreCase);
            bool matches       = matchesById || matchesByDesc;

            if (!matches) continue;

            switch (change.Kind.ToLowerInvariant())
            {
                case "variable_added":
                    isAdded = true;
                    break;

                case "variable_removed":
                    isRemoved = true;
                    break;

                case "variable_retyped":
                    isRetyped = true;
                    newType   = change.NewValue;
                    break;

                case "variable_renamed":
                    isRenamed = true;
                    oldName   = change.OldValue;
                    break;
            }
        }

        return new FieldDecoration(isAdded, isRemoved, isRetyped, isRenamed, oldName, newType);
    }
}
