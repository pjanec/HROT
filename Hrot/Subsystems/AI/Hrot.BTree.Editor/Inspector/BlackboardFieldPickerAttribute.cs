using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared.Blackboard;

namespace Hrot.BTree.Editor.Inspector;

/// <summary>
/// Marker attribute for StructEdit fields that should render as a blackboard field
/// picker dropdown constrained to fields compatible with the action method's expression-target type.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class BlackboardFieldPickerAttribute : Attribute
{
    /// <summary>
    /// Display string used in the picker when no variables are compatible with the action's DTO type.
    /// </summary>
    public const string NoCompatibleVariablesDisplay = "(no compatible variables)";

    /// <summary>
    /// Returns the subset of <paramref name="availableVariables"/> whose type is compatible
    /// with the expression-target type of <paramref name="actionFqn"/>.
    /// When <paramref name="actionFqn"/> is null or not found in the exporter, all variable
    /// names are returned (no type filtering).
    /// </summary>
    public static IReadOnlyList<string> GetCompatibleVariables(
        string? actionFqn,
        IReadOnlyList<BlackboardVariableEntry> availableVariables,
        IActionSchemaExporter exporter)
    {
        if (actionFqn is null)
            return availableVariables.Select(v => v.Name).ToList();

        var entry = exporter.Lookup(actionFqn);
        if (entry is null)
            return availableVariables.Select(v => v.Name).ToList();

        return availableVariables
            .Where(v => v.FieldType == entry.DtoType)
            .Select(v => v.Name)
            .ToList();
    }
}
