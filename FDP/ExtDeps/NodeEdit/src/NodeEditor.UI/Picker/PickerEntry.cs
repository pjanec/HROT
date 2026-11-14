using System.Collections.Generic;
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
/// <param name="IsEnabled">
/// <see langword="false"/> shows the entry dimmed and never returns it from a confirm — for an option the
/// host wants VISIBLE (so the list tells the truth about what exists) but not choosable right now; its
/// <paramref name="Description"/> should say why.
/// </param>
/// <param name="RecentContext">
/// ⭐ CE-4098 (AQ108 D19) — the <see cref="RecentStore"/> list this entry is remembered in. <see langword="null"/>
/// ⇒ the picker's own <see cref="PickerRequest.ContextKey"/>. ⭐ A source whose entries belong to a KIND (a scenario,
/// a recording, a blueprint) names that kind here, so ONE list per kind is shared by every picker that offers it
/// (the Open-Asset picker, the asset browser) and kinds never mix.
/// </param>
/// <param name="ImageKey">
/// ⭐ CE-4168 (AQ108 D26 P5) — optional key (a path) of this entry's picture, resolved lazily through the shared
/// <see cref="PickerImageCache"/>: the Grid tile and the side pane draw it when present. Distinct from
/// <see cref="IconTextureId"/> (an already-loaded handle) and <see cref="IconKey"/> (an atlas cell).
/// </param>
/// <param name="Chips">
/// ⭐ CE-4330 (docs/DESIGN_Recording_Storage.md §8 ⑤) — small coloured pills drawn after the name in the list layouts: a glyph and a short
/// label, each keyed by a <see cref="Core.Widgets.Icons"/> key so the colour belongs to the key (R-174), never to the call site. <see langword="null"/> ⇒ none.
/// </param>
/// <param name="Hint">⭐ CE-4330 — a muted note drawn right-aligned on the row (what choosing it does: <i>"opens at once"</i>, <i>"imports first"</i>).</param>
/// <param name="GlyphKey">⭐ Terrain authoring follow-up 2 — a <see cref="Core.Widgets.Icons"/> key whose coloured glyph is the entry's PICTURE when it has no image (a clutter item's tile, mockup 3): drawn large on the tile and in the side pane. <see langword="null"/> ⇒ the entry's icon.</param>
/// <param name="Actions">⭐ CE-4330 ⑦ — what else can be done to this entry, offered in its row's right-click menu after a separator (<i>Delete from archive…</i>). <see langword="null"/> ⇒ none.</param>
public sealed record PickerEntry(
    string Id,
    string Name,
    string? Description,
    string? Category,
    IReadOnlyList<string>? Keywords,
    IntPtr? IconTextureId,
    object? Tag,
    string? IconKey = null,
    bool IsEnabled = true,
    string? RecentContext = null,
    string? ImageKey = null,
    IReadOnlyList<PickerChip>? Chips = null,
    string? Hint = null,
    IReadOnlyList<PickerRowAction>? Actions = null,
    string? GlyphKey = null);

/// <summary>⭐ CE-4330 — one chip of a <see cref="PickerEntry"/>: a <see cref="Core.Widgets.Icons"/> key (glyph + colour) and its label.</summary>
public readonly record struct PickerChip(string IconKey, string Label);

/// <summary>⭐ CE-4330 ⑦ — one action of a <see cref="PickerEntry"/>'s right-click menu: a <see cref="Core.Widgets.Icons"/> key, its label, what it does, and — when it
/// cannot be done now — why (the item is then greyed with the reason as its tooltip).</summary>
public sealed record PickerRowAction(string IconKey, string Label, System.Action Run, string? DisabledReason = null);
