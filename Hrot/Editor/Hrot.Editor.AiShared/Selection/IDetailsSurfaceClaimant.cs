using System;

namespace Hrot.Editor.AiShared.Selection;

/// <summary>
/// ⭐⭐⭐ <b>A surface that DRIVES the Details panel, and therefore claims it while focused.</b>
///
/// <para>📌 <b>User ruling, <c>2026-08-18</c>:</b> <i>"the editor selection cache should contain what
/// the selected UI item comes from (what panel etc.). Otherwise we would need to report and handle the
/// click to every possible UI component."</i> ⇒ ⭐ each surface declares itself ONCE, here; ⛔ nothing
/// has to report clicks.</para>
///
/// <para>⚠⚠ <b>Implementing this is an OPT-IN, and the list must stay short.</b> The Watch, the
/// Inspector and the Details panel itself must NOT implement it — a surface that does not drive the
/// panel taking focus would otherwise steal it, and clicking INTO Details to edit a value would flip
/// the panel out from under the designer mid-edit.</para>
///
/// <para>⭐ <b>Shared across Blueprint, BTree and HSM</b> *(<c>Q32</c> ruling 6 — one Details panel for
/// every asset type)*, which is why it lives beside the store rather than in a host.</para>
/// </summary>
public interface IDetailsSurfaceClaimant
{
    /// <summary>Which surface this window IS, for <see cref="EditorSelectionStore.FocusedSurface"/>.</summary>
    SelectionOrigin DetailsOrigin { get; }

    /// <summary>
    /// ⭐ Set by the registrar; invoked by the window every frame it holds focus.
    /// ⛔ A settable callback rather than a store reference, so a window shared by three perspectives
    /// never depends on one perspective's store.
    /// </summary>
    Action? NotifyFocusClaim { get; set; }
}
