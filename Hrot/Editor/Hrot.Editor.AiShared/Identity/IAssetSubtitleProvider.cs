namespace Hrot.Editor.AiShared.Identity;

/// <summary>
/// BP-85 — an asset that can describe itself in one short word for the canvas breadcrumb
/// (e.g. a Blueprint's dispatch: "Instance", "Library", "AiPrimitive").
///
/// <para>
/// Optional and kind-agnostic, exactly like <see cref="IAssetIconKeyProvider"/>: the shared canvas
/// asks for it and simply omits the segment when an asset does not implement it, so BTree/HSM
/// assets need no changes.
/// </para>
/// </summary>
public interface IAssetSubtitleProvider
{
    /// <summary>
    /// A short qualifier for this asset, or <see langword="null"/> to show none.
    /// Keep it to a single word — it renders inline next to the asset name.
    /// </summary>
    string? Subtitle { get; }
}
