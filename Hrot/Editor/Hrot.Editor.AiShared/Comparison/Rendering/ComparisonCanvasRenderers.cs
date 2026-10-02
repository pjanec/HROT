using System;
using System.Collections.Generic;
using NodeEditor.Core.Interfaces;

namespace Hrot.Editor.AiShared.Comparison.Rendering;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-071</c> — the ONE place a comparison canvas renderer is composed for a document.</b>
/// 📄 <c>docs/DESIGN_Comparison_Ui_Mounting.md</c> §4 `D5` *(as-built: `D5` was FLIPPED — see below)*.
///
/// <para>⛔⛔ <b>The gap this closes.</b> 📐 Measured `2026-08-27`: <see cref="ComparisonAnnotationRenderer"/>
/// had <b>zero production constructions</b>. The feature design *(§6 "re-import and visualization",
/// <c>Visual_Asset_Comparison_Detailed_Design.md:1081</c>)* specifies it as an
/// <see cref="ICustomCanvasRenderer"/> at the <c>AfterNodes</c> pass — ⇒ ⭐ the comparison round-trip
/// completed and <b>nothing was ever drawn on the canvas.</b></para>
///
/// <para>⭐⭐ <b>Why this is CHEAP, and why the design's own lean was wrong.</b> §4 `D5` proposed DEFERRING
/// the renderer as *"NodeEditor-host work, a different surface from window registration"*. 📐 Measured: all
/// three document factories already compose <b>"built-in set + caller extras"</b> through their own
/// <c>BuildRenderers</c>, and each kind ships 4–6 <b>live</b> renderers today *(BTree: heatmap, subtree
/// boundary, observer-guard badge, variable-binding badge, breakpoint gutter, runtime overlay)*.
/// ⇒ ⭐⭐⭐ <b>the renderer joins an existing, exercised list — no factory signature changes at all</b>,
/// which makes it the CHEAPEST piece of the mount rather than the one to defer.</para>
///
/// <para>⭐ <b>Per DOCUMENT, not per host.</b> The renderer annotates ONE asset, and a document knows which
/// asset it is — so the asset is bound at construction here instead of being pushed later.
/// ⚠ That is the same <c>B3</c> lesson as the panels: <c>SetActiveAsset</c> had no callers, so binding must
/// happen where the information already is.</para>
/// </summary>
public static class ComparisonCanvasRenderers
{
    /// <summary>
    /// ⭐⭐ The extra-renderer list for one document's canvas.
    ///
    /// <para>⭐ Returns EMPTY when <paramref name="sessionRegistry"/> is <see langword="null"/> — the host
    /// has no comparison capability, so the annotation is <b>absent rather than inert</b> *(ruling 49)*.
    /// ⛔ It does not return a renderer that would silently draw nothing.</para>
    ///
    /// <para>⚠ Callers should pass this straight into a document factory's <c>extraRenderers</c>. ⛔ Do not
    /// build a second list beside it — 📌 six <c>Build</c> call sites *(three kinds × two hosts)* are six
    /// chances to diverge, which is exactly how the panels came to be unreachable.</para>
    /// </summary>
    /// <param name="sessionRegistry">
    /// The host's shared comparison session registry, or <see langword="null"/> when it has none.
    /// </param>
    /// <param name="assetId">The document's asset — what this renderer will annotate.</param>
    public static IReadOnlyList<ICustomCanvasRenderer> For(
        ComparisonSessionRegistry? sessionRegistry,
        Guid assetId)
    {
        if (sessionRegistry is null) return Array.Empty<ICustomCanvasRenderer>();

        var renderer = new ComparisonAnnotationRenderer(sessionRegistry);
        renderer.SetActiveAsset(assetId);
        return new ICustomCanvasRenderer[] { renderer };
    }
}
