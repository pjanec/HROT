using System;
using Fdp.Toolkit.Blueprints.Partitioning;

namespace Fdp.Toolkit.Behavior;

/// <summary>
/// ⭐⭐⭐ <b>The reserved slot names and the occurrence-identity arithmetic.</b>
/// <c>O4</c> / task <c>C1</c> — 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §19.
///
/// <para>⛔⛔ <b>Why the names are RESERVED and prefixed.</b> A slot key folds a
/// <c>variableId</c> that is otherwise an author-chosen blackboard variable name. These two are NOT
/// author-chosen — they name storage the RUNTIME owns — so they carry a prefix no editor variable
/// can produce. ⚠ A collision here is a silent cross-occurrence alias, which is the exact failure
/// <c>OccurrenceSlotKey</c>'s header says <c>A1</c> exists to kill.</para>
/// </summary>
public static class OccurrenceSlots
{
    /// <summary>
    /// The prefix no author-supplied variable name can carry. ⛔ Do not spell it inline anywhere else.
    /// </summary>
    public const string ReservedPrefix = Shared.OccurrenceSlotKey.ReservedPrefix;

    /// <summary>
    /// ⭐ The hosted occurrence's own <c>BehaviorTreeState</c> — the 64 bytes that <c>O4</c> stops
    /// sharing with the host (§3.1, §18).
    /// </summary>
    public const string TreeState = Shared.OccurrenceSlotKey.TreeStateVariableId;

    /// <summary>
    /// ⭐⭐ <b>The occurrence's IDENTITY — a number, not storage.</b>
    ///
    /// <para>🔴🔴 <b>This exists because <c>ComputeNested</c> returns the ROOT form verbatim when
    /// <c>hostKey == 0</c>, dropping <c>siteId</c> entirely</b> (deliberately — it is what keeps
    /// <c>A1</c>'s existing keys byte-identical). ⇒ a hosted child CANNOT be keyed as
    /// <c>(hostKey: 0, siteId: N)</c>: two sites hosting the same child asset would collide and the
    /// root case would swallow the difference without a word. The host must pass a <b>non-zero</b>
    /// key that identifies IT.</para>
    ///
    /// <para>⭐⭐⭐ <b>And identity is all that is required</b> — §19.7 ① first concluded that the ROOT
    /// occurrence therefore needed its own SLOT, so <c>O4</c>'s two halves had to ship together.
    /// ⛔ <b>That was too strong.</b> 📐 Measured: the root <c>BehaviorTreeState</c> lives in the
    /// <c>BrainBTreeState</c> COMPONENT (<c>BTreeTickSystem:159</c>). <c>hostKey</c> is a
    /// disambiguator folded into a hash — it never dereferences anything — so a canonical derived
    /// number serves, and WHERE the host's state physically lives is independent. ⇒ moving the root
    /// state into a slot is a separate change, and <c>O4</c> does not need it.</para>
    /// </summary>
    public static int IdentityOf(Guid assetId) => Shared.OccurrenceSlotKey.ComputeIdentity(assetId);

    /// <summary>
    /// ⭐ <b><c>D5</c> — the hosting SITE, folded from the author's stable node <c>Guid</c>.</b>
    ///
    /// <para>⛔ <b>NOT a node ordinal.</b> <c>OccurrenceSlotKey.ComputeNested</c>'s own doc: the
    /// <c>siteId</c> <i>"must be stable across a recompile, or the child's slot moves;
    /// <c>StructureHash</c> catches the drift but the state is lost"</i> — and an ordinal shifts the
    /// moment a node is inserted above it. ⭐ Authors already supply a stable <c>Guid</c> per node for
    /// every <c>StatefulAction</c>; this folds the same one.</para>
    ///
    /// <para>⚠ <c>Guid.Empty</c> folds to a non-zero value on purpose: a site is never "no site", and
    /// a zero here would be indistinguishable from "unset" at the call site.</para>
    /// </summary>
    public static int SiteId(Guid nodeVisualId) => Shared.OccurrenceSlotKey.ComputeSiteId(nodeVisualId);

    /// <summary>
    /// ⭐⭐ The hosted child's <see cref="TreeState"/> slot key — the one number a hosting site needs.
    ///
    /// <para>⭐ <c>D3</c>: this is computable at RUNTIME, so it serves the hand-written path as well as
    /// the generated one. The emitter bakes the result as a literal; a hand-written host calls this.
    /// ⛔ Both must agree byte-for-byte, which is why there is ONE function.</para>
    /// </summary>
    public static int TreeStateKeyFor(Guid hostAssetId, Guid siteNodeVisualId, Guid childAssetId)
        => Shared.OccurrenceSlotKey.ComputeTreeStateKey(hostAssetId, siteNodeVisualId, childAssetId);
}
