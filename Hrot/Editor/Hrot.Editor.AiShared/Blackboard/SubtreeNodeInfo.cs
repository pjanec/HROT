using System;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Immutable descriptor for a Subtree node resolved from a behavior tree asset.
/// Returned by <see cref="IBTreeSyncableAsset.GetSubtreeNodeInfo"/>.
/// </summary>
/// <param name="IsResolved">
/// True when the sub-tree asset referenced by this node was located in the catalog.
/// </param>
/// <param name="SubtreeAssetId">
/// The GUID of the referenced sub-tree asset.
/// May be <see cref="Guid.Empty"/> when the reference has not yet been set.
/// </param>
public sealed record SubtreeNodeInfo(bool IsResolved, Guid SubtreeAssetId);
