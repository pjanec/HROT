using System;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Represents a single variable in an editor-managed blackboard.
/// </summary>
/// <param name="Name">The variable identifier (C# field name).</param>
/// <param name="FieldType">The CLR type of the variable.</param>
/// <param name="Comment">
/// Optional doc comment. If non-null, emitted as a summary block above
/// the field declaration. If null, no doc comment is emitted.
/// </param>
/// <param name="IsAutoManaged">
/// True when this variable was auto-created by the "Promote to new variable" feature.
/// Auto-managed variables are owned by the editor and may be removed when the binding
/// that created them is cleared. Defaults to false for hand-authored variables.
/// </param>
public record BlackboardVariableEntry(string Name, Type FieldType, string? Comment, bool IsAutoManaged = false);
