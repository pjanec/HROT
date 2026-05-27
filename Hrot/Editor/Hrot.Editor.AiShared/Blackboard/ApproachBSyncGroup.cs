using System;
using System.Collections.Generic;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Describes one subtree node that needs an Approach B orchestrator.
/// Produced by <see cref="IBTreeSyncableAsset.GetApproachBSyncGroups"/>.
/// </summary>
/// <param name="NodeVisualId">Visual ID of the Subtree node in the master BTree.</param>
/// <param name="SubtreeName">
/// Identifier-safe name of the sub-tree asset (e.g. "Shoot_BT").
/// Used as the orchestrator method name suffix.
/// </param>
/// <param name="SubtreeDtoTypeName">
/// Short type name of the sub-tree's blackboard struct (e.g. "FireAtTargetParams").
/// </param>
/// <param name="SubtreeDtoTypeNs">
/// Namespace of the sub-tree's blackboard struct, or null when in the same namespace.
/// </param>
/// <param name="Bindings">
/// All sync bindings for this node (including SyncIn=false/SyncOut=false entries).
/// The emitter filters to active sync operations.
/// </param>
public sealed record ApproachBSyncGroup(
    Guid NodeVisualId,
    string SubtreeName,
    string SubtreeDtoTypeName,
    string? SubtreeDtoTypeNs,
    IReadOnlyList<SubtreeSyncBinding> Bindings);
