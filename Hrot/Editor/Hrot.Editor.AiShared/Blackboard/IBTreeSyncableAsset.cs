using System;
using System.Collections.Generic;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Implemented by assets that expose BTree-specific subtree sync capabilities.
/// Allows <see cref="Hrot.Editor.AiShared.Windows.InspectorWindow"/> to render the
/// PARAMETER SYNCHRONIZATION section without a direct reference to BTree.Editor.
/// </summary>
public interface IBTreeSyncableAsset
{
    /// <summary>
    /// Returns subtree info for the node identified by <paramref name="nodeVisualId"/>,
    /// or null when the node does not exist or is not a Subtree node.
    /// </summary>
    SubtreeNodeInfo? GetSubtreeNodeInfo(Guid nodeVisualId);

    /// <summary>
    /// Returns the sync bindings currently recorded for the Subtree node identified by
    /// <paramref name="nodeVisualId"/>.  Returns an empty list when none exist.
    /// </summary>
    IReadOnlyList<SubtreeSyncBinding> GetSyncBindings(Guid nodeVisualId);

    /// <summary>
    /// Upserts a sync binding for the node identified by <paramref name="nodeVisualId"/>.
    /// An existing binding with the same <see cref="SubtreeSyncBinding.FieldName"/> is replaced.
    /// Fires Changed.
    /// </summary>
    void SetSyncBinding(Guid nodeVisualId, SubtreeSyncBinding binding);

    /// <summary>
    /// Removes all sync bindings for the node identified by <paramref name="nodeVisualId"/>.
    /// No-op when no bindings exist. Fires Changed only when bindings were actually removed.
    /// </summary>
    void ClearSyncBindings(Guid nodeVisualId);

    /// <summary>
    /// Returns all blackboard variables in this asset whose display type name equals
    /// <paramref name="typeName"/> (exact match, case-sensitive).
    /// The display name is derived via <c>BlackboardTypeHelper.GetDisplayName(FieldType)</c>,
    /// so "int" matches <see cref="int"/> variables but "Int32" does not.
    /// </summary>
    IReadOnlyList<BlackboardVariableEntry> GetVariablesOfType(string typeName);

    /// <summary>
    /// Records sub-tree identity metadata for a Subtree node.
    /// Called by the Inspector whenever it renders the sync table for a resolved node.
    /// This metadata is used by the orchestrator emitter to generate Approach B actions.
    /// </summary>
    void RecordSubtreeNodeMeta(
        Guid nodeVisualId,
        string subTreeName,
        string subDtoTypeName,
        string? subDtoTypeNs);

    /// <summary>
    /// Returns Approach B sync groups: subtree nodes that have at least one binding
    /// where <see cref="SubtreeSyncBinding.SyncIn"/> or <see cref="SubtreeSyncBinding.SyncOut"/>
    /// is true, and whose sub-tree identity has been recorded via
    /// <see cref="RecordSubtreeNodeMeta"/>.
    /// </summary>
    IReadOnlyList<ApproachBSyncGroup> GetApproachBSyncGroups();

    /// <summary>
    /// Returns auto-allocated blackboard variable entries for Approach B subtree nodes
    /// that are not covered by an Approach A alias.
    /// Display-only -- not passed to the bin-packer until type resolution is available.
    /// </summary>
    IReadOnlyList<BlackboardVariableEntry> GetAutoAllocatedVariables();
}
