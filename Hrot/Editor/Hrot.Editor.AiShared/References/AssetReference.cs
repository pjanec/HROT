namespace Hrot.Editor.AiShared.References;

/// <summary>A place in an asset that references a sub-element.</summary>
public sealed record AssetReference(
    Guid HostAssetId,
    AssetKind HostKind,
    Guid HostElementId,
    string HostDisplayPath,
    string TargetKey,
    SubElementKind TargetKind);
