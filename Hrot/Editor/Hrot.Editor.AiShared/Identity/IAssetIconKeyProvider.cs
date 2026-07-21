namespace Hrot.Editor.AiShared;

/// <summary>
/// Optional seam (punch-list #9): an <see cref="IEditableAsset"/> that supplies its own icon key,
/// finer-grained than the one <see cref="AssetKindIcons.GetIconKey"/> derives from its
/// <see cref="AssetKind"/>. The asset browser / Open-Asset picker prefers this key when present
/// (e.g. a Blueprint distinguishing Action vs Condition vs Function), and falls back to the
/// per-kind default otherwise. Kept in the shared layer alongside the existing blueprint-aware
/// seams (e.g. IComposedBlueprintIdentity) so the panel stays generic and the specifics are injected.
/// </summary>
public interface IAssetIconKeyProvider
{
    /// <summary>The icon key to render for this asset, or <see langword="null"/> to use the kind default.</summary>
    string? IconKey { get; }
}
