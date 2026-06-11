using Hrot.Editor.AiShared.Identity;

namespace Hrot.Editor.AiShared.Recipes;

/// <summary>
/// Per-kind factory that creates a new in-memory asset from a recipe or from the
/// hardcoded "Empty" sentinel. File I/O is performed by later phases (dialogs);
/// <see cref="CreateNew"/> mints identity and in-memory content only.
/// </summary>
public interface INewAssetService
{
    /// <summary>
    /// The <see cref="AssetKind"/> this service creates.
    /// </summary>
    AssetKind Kind { get; }

    /// <summary>
    /// Creates a new in-memory asset with a freshly minted <see cref="IEditableAsset.AssetId"/>.
    /// </summary>
    /// <param name="recipe">
    /// The recipe asset to clone from, or <see langword="null"/> to use the in-code "Empty" recipe.
    /// Implementations may also treat a synthetic "Empty" entry as the empty case.
    /// </param>
    /// <param name="name">The display name for the new asset.</param>
    /// <param name="relPath">
    /// The target subfolder relative to the kind's asset root. Used to set
    /// <see cref="IEditableAsset.SourceFilePath"/> on the returned asset; no file I/O
    /// is performed by this method.
    /// </param>
    /// <returns>
    /// A new <see cref="IEditableAsset"/> with fresh identity. The caller is responsible
    /// for saving and registering it.
    /// </returns>
    IEditableAsset CreateNew(IEditableAsset? recipe, string name, string relPath);

    /// <summary>
    /// Returns the recipes this kind offers: the in-code "Empty" entry plus any
    /// discovered recipe assets from disk.
    /// </summary>
    IReadOnlyList<IEditableAsset> AvailableRecipes();
}
