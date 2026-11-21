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
/// <remarks>⛔ CE-3193 — <c>Role</c>/<c>Scope</c> are gone: whether a variable is working state is derived from the bindings
/// (<see cref="IStatefulScopeAsset.WorkingStateVariableNames"/>), never authored.</remarks>
public record BlackboardVariableEntry(
    string               Name,
    Type                 FieldType,
    string?              Comment,
    bool                 IsAutoManaged    = false,
    string?              DefaultValueJson = null,
    bool                 IsReservedParams = false);
