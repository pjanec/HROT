using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared.Catalog;

namespace Hrot.Editor.AiShared;

/// <summary>
/// ⭐⭐⭐ <b>The TWO questions <c>HsmValidator</c>'s rules 8 and 8b ask about a hosted sub-tree, answered
/// ONCE.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.17.
///
/// <para>🔴🔴 <b>Why this exists, and it is a defect I introduced myself in <c>CE-338</c>.</b>
/// 📐 Measured <c>2026-09-26</c>: <c>EditorSubsystem</c> and <c>CgfSubsystem</c> carried these two
/// predicates as <b>BYTE-IDENTICAL private methods</b>. ⇒ the editor and CGF were running two copies of
/// one policy — ⛔ exactly the *"no two implementations of one concept"* ruling, and exactly the
/// mechanism that had let CGF sit silently without rules 8/8b in the first place.</para>
///
/// <para>⛔⛔ <b>The justification for the duplication had EXPIRED and I did not notice.</b> The
/// resolvers were delegates because the predicate needed a <c>switch</c> over BOTH
/// <c>BehaviorTreeAsset</c> and <c>HsmAsset</c>, which only an assembly referencing both editors can
/// write. ⭐ <see cref="IStatefulScopeAsset"/> — added in the SAME commit — removed that constraint
/// entirely: the expression below compiles anywhere <c>Hrot.Editor.AiShared</c> is referenced. 🔒 I built
/// the seam and then wrote the duplicate anyway; the seam law's usual failure is not adopting an
/// existing seam, and this is the sharper version — <b>not adopting the seam you just built.</b></para>
///
/// <para>⭐ <b>A null catalogue answers "no"</b>, which reproduces the historical defaults
/// (<c>_ =&gt; false</c> / <c>_ =&gt; empty</c>) exactly, so a host without a catalogue behaves as
/// before rather than throwing.</para>
/// </summary>
public static class StatefulScopeQueries
{
    /// <summary>
    /// ⭐ Rule 8's question: does this asset carry shared working state, so running two copies of it
    /// concurrently would collide?
    /// </summary>
    public static bool IsStatefulSubtree(this IAssetCatalog? catalog, Guid assetId)
        => catalog?.FindByAssetId(assetId) is IStatefulScopeAsset a && a.HasAnyStatefulNode();

    /// <summary>
    /// ⭐ Rule 8b's question: which shared scope keys does this asset occupy, so two DIFFERENT
    /// sub-trees resolving to one key can be caught?
    /// </summary>
    public static IReadOnlyCollection<int> SharedScopeKeysOf(this IAssetCatalog? catalog, Guid assetId)
        => catalog?.FindByAssetId(assetId) is IStatefulScopeAsset a
               ? a.GetSharedScopeKeys()
               : Array.Empty<int>();
}
