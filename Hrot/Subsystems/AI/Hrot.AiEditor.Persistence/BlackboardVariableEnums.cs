namespace Hrot.AiEditor.Persistence;

/// <summary>
/// Authoring role of a blackboard variable (S3-1).
/// Default is Input (value 0) so omit-when-default serialization works correctly.
/// </summary>
public enum BlackboardVariableRole
{
    /// <summary>A parameter / input value. Default.</summary>
    Input  = 0,
    /// <summary>Mutable working state.</summary>
    State  = 1,
}

/// <summary>
/// Scope of a State-role blackboard variable (S3-1).
/// Determines how the slot key is computed and how the slot is provisioned.
/// Default is Node (value 0) so omit-when-default serialization works correctly.
/// Only meaningful when <see cref="BlackboardVariableRole"/> is <see cref="BlackboardVariableRole.State"/>.
/// </summary>
public enum WorkingStateScope
{
    /// <summary>Per-node local slot. Default.</summary>
    Node     = 0,
    /// <summary>Shared across all nodes within one behavior assignment on an entity.</summary>
    Behavior = 1,
    /// <summary>Shared across all behaviors on an entity.</summary>
    Entity   = 2,
}
