using System;
using System.Collections.Generic;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Validates blackboard variable names for the Add Variable popup.
/// Returns null when valid; a human-readable error string when invalid.
/// </summary>
public static class BlackboardNameValidator
{
    // C# keywords that are not valid as identifier names.
    /// <summary>
    /// Returns null when <paramref name="name"/> is valid; otherwise returns a
    /// human-readable error message describing why the name is invalid.
    /// </summary>
    /// <param name="name">Candidate variable name.</param>
    /// <param name="existingVars">Optional existing variable list to check for duplicates.</param>
    public static string? Validate(string? name, IReadOnlyList<BlackboardVariableEntry>? existingVars = null)
    {
        if (string.IsNullOrEmpty(name))
            return "Name must not be empty.";

        if (!char.IsLetter(name[0]) && name[0] != '_')
            return "Name must start with a letter or underscore.";

        foreach (char c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                return "Name must contain only letters, digits, or underscores.";
        }

        // ⭐ CE-2039 — the complete reserved list (this one lacked lock, goto, throw, try, …) plus contextual "var".
        if (global::Hrot.AiEditor.Persistence.Emit.Identifiers.IsReservedKeyword(name) || name == "var")
            return $"'{name}' is a C# keyword and cannot be used as a variable name.";

        if (existingVars != null)
        {
            foreach (var v in existingVars)
            {
                if (v.Name == name)
                    return $"A variable named '{name}' already exists.";
            }
        }

        return null;
    }
}
