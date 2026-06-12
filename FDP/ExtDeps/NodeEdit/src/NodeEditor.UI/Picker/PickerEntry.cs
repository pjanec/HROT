using System.Numerics;

namespace NodeEditor.UI.Picker;

/// <summary>
/// One row/cell in a picker window. Generic enough to support text, icon,
/// category breadcrumb, thumbnail, and an opaque caller payload.
/// </summary>
/// <param name="Id">Stable identity for favorites/recent persistence.</param>
/// <param name="Name">Primary label used for fuzzy search ranking.</param>
/// <param name="Description">Long description shown in the detail pane.</param>
/// <param name="Category">Category path "A/B/C" for Wide/Tree layouts.</param>
/// <param name="Keywords">Additional search terms beyond the display name.</param>
/// <param name="IconTextureId">Optional icon texture handle for Grid thumbnails or inline icons.</param>
/// <param name="Tag">Opaque caller payload returned via <see cref="PickerResult"/>.</param>
/// <param name="IconKey">
/// Optional <see cref="Core.Interfaces.IIconProvider"/> key resolved to an
/// <see cref="Core.Interfaces.IconHandle"/> (atlas cell) for inline row icons in flat/tree layouts.
/// Distinct from <see cref="IconTextureId"/> (whole-texture Grid thumbnails).
/// </param>
public sealed record PickerEntry(
    string Id,
    string Name,
    string? Description,
    string? Category,
    IReadOnlyList<string>? Keywords,
    IntPtr? IconTextureId,
    object? Tag,
    string? IconKey = null);
