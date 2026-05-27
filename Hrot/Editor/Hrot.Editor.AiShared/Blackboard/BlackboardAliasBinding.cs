using System;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// Records one aliasing entry: a sub-tree requirement that has been bound to a defined variable.
/// Stored on the variable in the asset model; used by the Variables panel to render the "aliased by" badge.
/// </summary>
public record BlackboardAliasBinding(
    Guid   RequiringAssetId,
    Guid   RequiringElementId,
    string RequiringAssetName,
    string RequiredByPath,
    Type   DtoType);
