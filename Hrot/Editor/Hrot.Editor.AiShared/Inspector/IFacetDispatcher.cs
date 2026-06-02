using Hrot.Editor.AiShared.Selection;

namespace Hrot.Editor.AiShared.Inspector;

/// <summary>
/// Seam that per-subsystem facet dispatchers implement.
/// InspectorWindow calls <see cref="GetFacet"/> to get a boxed facet struct for a
/// sub-selection, then <see cref="ApplyFacet"/> to write an edited facet back to the
/// asset and mark it dirty.
/// This interface lives in AiShared and is implemented by the subsystem editor assemblies
/// (BTree.Editor, Hsm.Editor) — dependency direction: subsystem → AiShared, not the reverse.
/// </summary>
public interface IFacetDispatcher
{
    /// <summary>
    /// Returns a boxed copy of the facet struct for <paramref name="subSelection"/>,
    /// or <c>null</c> if the sub-selection is not handled by this dispatcher.
    /// </summary>
    object? GetFacet(IAssetSubSelection subSelection);

    /// <summary>
    /// Applies the edited <paramref name="facet"/> back to the asset model and marks it dirty.
    /// Called only when <see cref="GetFacet"/> previously returned a non-null value for the
    /// same <paramref name="subSelection"/>.
    /// </summary>
    void ApplyFacet(IAssetSubSelection subSelection, object facet);
}
