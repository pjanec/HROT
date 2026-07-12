using System;
using Hrot.AiEditor.Persistence;

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
/// <param name="DefaultValueJson">
/// JSON-encoded default value for this variable, authored via the StructEdit surface (B-3).
/// <c>null</c> when no default has been authored (byte-stable: omitted from persisted JSON when null).
/// The value is applied once at behaviour assignment via the generated <c>ParseParamsDelegate</c>
/// before any runtime overrides. Only meaningful for Category-2 (editor-managed) variables.
/// </param>
/// <param name="Role">
/// Authoring role of this variable: Input (default) or State.
/// Persisted; omitted from JSON when default (Input) for back-compat.
/// </param>
/// <param name="Scope">
/// Working-state scope (only meaningful when Role == State): Node (default), Behavior, or Entity.
/// Persisted; omitted from JSON when default (Node) for back-compat.
/// </param>
public record BlackboardVariableEntry(
    string               Name,
    Type                 FieldType,
    string?              Comment,
    bool                 IsAutoManaged    = false,
    string?              DefaultValueJson = null,
    BlackboardVariableRole Role           = BlackboardVariableRole.Input,
    WorkingStateScope    Scope            = WorkingStateScope.Node);
