using System.Collections.Generic;

namespace Hrot.Editor.AiShared;

/// <summary>
/// ⭐⭐⭐ <b>An asset that can tell you whether it carries SHARED working state — the two questions
/// <c>HsmValidator</c>'s rules 8 and 8b ask about a hosted sub-tree.</b>
/// 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.15.
///
/// <para>🔴 <b>Why this exists, and it is a seam that was missing rather than a new idea.</b>
/// <c>BehaviorTreeAsset</c> and <c>HsmAsset</c> have carried <b>these exact two members, with these
/// exact signatures</b>, since <c>E4</c> — and no common type. ⇒ every caller that wanted the answer
/// had to <c>switch</c> on both concrete types, which only an assembly referencing BOTH editors can
/// do. 📐 Measured <c>2026-09-26</c>: that is exactly two subsystems, and only one of them did it.</para>
///
/// <para>⛔⛔ <b>What the missing seam COST.</b> <c>EditorSubsystem</c> wrote the switch and wired the
/// resolvers to the Diagnostics window; <c>CgfSubsystem</c>, which holds the same
/// <c>IAssetCatalog</c>, could not — so rules 8/8b were structurally unreachable there. ⚠ And the
/// resolvers' own remarks forbid the obvious workaround: <i>"two copies would let the node badges and
/// the Diagnostics window disagree about which sub-trees are stateful"</i>.</para>
///
/// <para>⭐ With this interface the predicate is <c>catalog.FindByAssetId(id) is IStatefulScopeAsset a
/// &amp;&amp; a.HasAnyStatefulNode()</c> — <b>no switch, no duplication, and available anywhere an
/// <c>IAssetCatalog</c> is.</b> ⚠ Implementing it required no new code on either asset: both already
/// had the members.</para>
/// </summary>
public interface IStatefulScopeAsset
{
    /// <summary>⭐ Does this asset carry any shared (<c>Behavior</c>/<c>Entity</c>-scoped) working
    /// state? ⛔ Rule 8's question: running two copies of a STATEFUL sub-tree concurrently collides.</summary>
    bool HasAnyStatefulNode();

    /// <summary>⭐ The shared scope keys this asset occupies. ⛔ Rule 8b's question: two DIFFERENT
    /// sub-trees resolving to the SAME key race on one slot.</summary>
    IReadOnlyCollection<int> GetSharedScopeKeys();
}
