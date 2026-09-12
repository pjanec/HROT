using Fdp.Core;
using Fdp.Interfaces;

namespace Fdp.Toolkit.Replication.Abstractions
{
    /// <summary>
    /// ⭐⭐⭐ <b>"Which components should THIS node own for an entity of this template?"</b> — the sibling
    /// of <c>IOwnershipDistributionStrategy</c>, which answers <i>"which grants do I hand OUT?"</i>.
    ///
    /// <para>📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.3, §3.8, §6 step <c>1</c>.</para>
    ///
    /// <para>⭐⭐ <b>The whole design in one sentence.</b> 🔒 User, <c>2026-09-01</c>: <i>"SimHost having a
    /// muscle role should not instantiate any brain related components… by applying 'auto-takeover' rules
    /// (i do not have brain role -&gt; i will not own brain components, the brain will) it can create the
    /// components as unowned while CGF (applying same rule - I am brain, i will own the brain components)
    /// creates them as owned. No authority conflict."</i> ⇒ ownership stops being something a creator
    /// HANDS OUT and becomes something every node DERIVES, using the same function. ⭐⭐⭐ <b>Two nodes
    /// running the same function over the same entity cannot disagree</b> — that is the safety property,
    /// and it is why <see cref="IRoleShardProvider"/> may read only node-identical inputs.</para>
    ///
    /// <para>⛔⛔ <b>THE ENGINE NEVER LEARNS WHAT A ROLE MEANS.</b> 🔒 User, <c>2026-09-12</c>: <i>"the
    /// bitmask for components is correct approach, fdp should not understand what a brain and muscle
    /// really mean."</i> ⇒ ⭐ the role→components table is <b>supplied by the application</b> at its
    /// composition root; nothing in this assembly maps <c>NodeRole.Brain</c> to a component. §2.3 is the
    /// same rule one level down: the table is a <see cref="BitMask512"/> of COMPONENT IDS, ⛔ never a
    /// descriptor set — <c>DescriptorOwnershipMap</c> is populated per NETWORK IMPLEMENTATION (NED fills
    /// it from NED translators, BDC differently, an offline node not at all), so a rule keyed on it would
    /// mean something different on every stack and nothing offline.</para>
    ///
    /// <para>⚠ <b>Nothing calls this yet.</b> Its two insertion points are step 2
    /// (<c>NetworkSpawningSystem</c>, the CREATE leg) and step 3 (<c>GhostPromotionSystem</c>, the
    /// PROMOTE leg). ⛔ And note that even once they land, <b>authority gates REPLICATION, not
    /// EXECUTION</b> — the cognitive tick systems carry no authority filter, so the design is not
    /// complete until step 3b. 📄 §3.5.</para>
    /// </summary>
    public interface IRoleAffinityPolicy
    {
        /// <summary>
        /// ⭐ The component ids this node should own for an entity of <paramref name="template"/>.
        ///
        /// <para>⚠ <b>The caller INTERSECTS this with the entity's live component mask</b> —
        /// <c>AuthorityMask = componentMask ∧ OwnableMask(...)</c>. ⇒ ⭐ naming a component the entity
        /// never receives contributes nothing, which is what makes over-declaring safe and why
        /// <i>"only for entities having one"</i> needs no special case.</para>
        /// </summary>
        /// <param name="template">The entity's TKB template — the source of
        /// <c>BirthCriticalComponents</c>.</param>
        /// <param name="isCreator">
        /// ⭐⭐ True when THIS node is materialising the entity for the first time (the CREATE leg), false
        /// when it is promoting a ghost that arrived from elsewhere.
        /// <para>⛔ <b>It is not a convenience flag — it is the architect's correction.</b> Applied
        /// symmetrically, role affinity would make a Brain-role creator produce <c>SimTransform</c>
        /// UNOWNED; every egress translator gates on <c>HasAuthority</c>, so the spawn coordinate would be
        /// written correctly and NEVER PUBLISHED, and every peer's ghost would sit at the origin. ⇒ a
        /// creator keeps the template's birth-critical components <b>whatever its role</b>, then hands
        /// them off through the existing <c>DeferredTakeOwnership</c> path.</para>
        /// </param>
        /// <param name="key">Identifies the entity for shard purposes. See <see cref="RoleShardKey"/>.</param>
        BitMask512 OwnableMask(TkbTemplate template, bool isCreator, in RoleShardKey key);
    }
}
