using System;
using System.Collections.Generic;

namespace Hrot.Editor.AiShared;

/// <summary>
/// ⭐⭐⭐ <b>An asset that hosts other assets as sub-trees — the FORWARD edge of the asset graph.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.16 (<c>E5</c> item 7).
///
/// <para>🔴 <b>This edge was recorded NOWHERE, and that absence — not the algorithm — is what item 7
/// actually is.</b> 📐 Measured <c>2026-09-26</c>, two seams look like they already hold it and
/// neither does:</para>
///
/// <para>⛔ <b>① <c>IAssetCatalog.WhereDependsOn(Guid)</c> is a STUB.</b> <c>AssetCatalog</c> returns
/// <c>Array.Empty&lt;IEditableAsset&gt;()</c> with the comment <i>"reverse-dependency tracking comes in
/// Phase 5/6"</i>, and its own rail is named <c>WhereDependsOn_ReturnsEmpty</c>. ⚠ Building a cycle
/// rule on it would have produced a rule that can never fire. ⭐ It is also the REVERSE edge; a cycle
/// walk needs forward ones.</para>
///
/// <para>⛔ <b>② <c>ReferenceCatalog</c> models a DIFFERENT edge.</b> <c>AssetReference</c> points at a
/// <b>sub-element</b> by string key — action/guard FQNs, machine-scoped event names, blackboard
/// variables — and <c>SubElementKind</c> has no hosted-subtree member. 📐 <c>HsmReferenceContributor</c>
/// emits nothing at all for <c>StateNode.SubtreeAssetId</c>.</para>
///
/// <para>⭐⭐ <b>Why a SEPARATE interface from <see cref="IStatefulScopeAsset"/>,</b> which the same two
/// assets implement: that one answers a <b>storage-footprint</b> question (<i>does this asset occupy
/// shared working state?</i>); this one answers a <b>composition</b> question (<i>what does this asset
/// pull in?</i>). ⛔ Merging them would force every future implementer to answer a question it may have
/// no notion of.</para>
///
/// <para>⚠ <b>Declaration-only, deliberately.</b> Like <see cref="IStatefulScopeAsset"/>, the intent is
/// that an implementer already knows the answer — ⛔ the interface exists so a caller holding an
/// <c>IAssetCatalog</c> can ask it <b>without referencing either editor assembly</b>, which is the
/// constraint that shaped <c>CE-338</c>.</para>
/// </summary>
public interface ISubtreeHostingAsset
{
    /// <summary>
    /// ⭐ Every asset id this asset hosts as a sub-tree, deduplicated. ⛔ Never contains
    /// <see cref="Guid.Empty"/> — an unresolved host contributes no edge.
    /// ⚠ The same id may legitimately be hosted from several places; this is a SET, because the
    /// cycle walk cares only whether the edge exists.
    /// </summary>
    IReadOnlyCollection<Guid> GetHostedSubtreeAssetIds();
}
