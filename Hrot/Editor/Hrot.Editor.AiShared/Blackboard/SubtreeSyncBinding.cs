namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Describes a single parameter synchronization binding between a sub-tree node
/// and a variable in the master (parent) asset's blackboard.
/// </summary>
/// <param name="FieldName">
/// Name of the field in the sub-tree's blackboard struct that is being bound.
/// </param>
/// <param name="MasterVariableName">
/// Name of the corresponding variable in the master (parent) blackboard.
/// Null when the binding has not yet been mapped to a master variable.
/// </param>
/// <param name="SyncIn">
/// When true, the master variable's value is copied into the sub-tree field on entry.
/// </param>
/// <param name="SyncOut">
/// When true, the sub-tree field's value is copied back to the master variable on exit.
/// </param>
public sealed record SubtreeSyncBinding(
    string FieldName,
    string? MasterVariableName,
    bool SyncIn,
    bool SyncOut);
