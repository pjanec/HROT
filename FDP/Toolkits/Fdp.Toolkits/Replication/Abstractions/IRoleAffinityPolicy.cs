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

        /// <summary>
        /// ⭐⭐⭐ <b>The ROLE-DERIVED <c>ownedComponentSet</c> of this node — <i>"which components could I
        /// EVER have authority over?"</i></b> 📄 §3.9.
        ///
        /// <para>⚠⚠ <b>This is NOT <see cref="OwnableMask"/> with the arguments dropped.</b> That one asks
        /// <i>"do I own THIS entity's copy?"</i> and is shard-gated and template-gated; this one is a
        /// <b>composition-time</b> question with no entity in it — ⇒ it is a SUPERSET of every answer
        /// <see cref="OwnableMask"/> can give for a role-owned component, and it is the right driver for
        /// REGISTRATION and for a boot-time diagnostic.</para>
        ///
        /// <para>⛔ <b>It deliberately EXCLUDES the creator's birthright.</b> A creator keeps the
        /// template's <c>BirthCriticalComponents</c> whatever its role (see <paramref name="isCreator"/>
        /// above), and those are per-TEMPLATE — unknowable without the catalogue. ⇒ a node can own a
        /// component that is not in this set. ⚠ A diagnostic reading <i>"can never own"</i> off this
        /// property alone would therefore be wrong for exactly the birth-critical components, which is the
        /// one case where being wrong is loud (the origin-flash failure).</para>
        /// </summary>
        BitMask512 OwnedComponentSet { get; }

        /// <summary>
        /// 🔴🔴 <b>The <c>readComponentSet</c> — components owned ELSEWHERE, replicated IN, and consumed
        /// here.</b> 📄 §3.9.
        ///
        /// <para>🔒 User, <c>2026-09-12</c>: <i>"intents are brain owned components that must be replicated
        /// to muscle so musle can read and act on them. so muscle cant simply stop registwring them because
        /// they are brain ones."</i> 📐 Measured: <c>NavigationIntent</c> (16 wire references) and
        /// <c>MissionPlanQueue</c> (9) are Brain-OWNED and Muscle-READ. ⛔⛔ <b>A node that derived its
        /// registration from the owned set alone would stop receiving its own orders</b> — that is the
        /// mistake this property exists to make unrepresentable.</para>
        ///
        /// <para>⛔ <b>Never contributes to authority.</b> READ means <i>"register it, never claim it"</i>;
        /// if a component appears in both sets for one node, the OWNED half is what
        /// <see cref="OwnableMask"/> answers with and the READ half changes nothing.</para>
        /// </summary>
        BitMask512 ReadComponentSet { get; }

        /// <summary>
        /// ⭐⭐⭐ <b><c>REGISTER = ownedComponentSet ∪ readComponentSet</c></b> — the component types this
        /// node's roles justify registering at all. 📄 §3.9.
        ///
        /// <para>⭐ The whole reason the two sets are exposed SEPARATELY and this union is exposed
        /// EXPLICITLY: a caller that had to OR them itself is a caller that can forget the second half, and
        /// forgetting the second half is the measured failure above. ⇒ registration asks for
        /// <b>this</b>, authority asks for <see cref="OwnableMask"/>, and neither can be mistaken for the
        /// other.</para>
        ///
        /// <para>⚠ <b>It is a FLOOR, not the final registration set.</b> Birth-critical components
        /// (excluded from <see cref="OwnedComponentSet"/>, above) and anything a host registers for
        /// non-role reasons are additions the host still makes. ⛔ Narrowing a host's registry to exactly
        /// this mask without checking those is how a node loses the ability to create its own entities.</para>
        /// </summary>
        BitMask512 RegisterComponentSet { get; }
    }
}
