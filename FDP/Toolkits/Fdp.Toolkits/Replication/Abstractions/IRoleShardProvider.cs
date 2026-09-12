using Fdp.Core;

namespace Fdp.Toolkit.Replication.Abstractions
{
    /// <summary>
    /// ⭐⭐⭐ <b>"Does THIS node serve role R for THIS entity?"</b> — the seam that lets one role be split
    /// across several nodes later, without touching either ownership insertion point.
    ///
    /// <para>📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.8, §6 step <c>1a</c>.
    /// 🔒 <b>User ruling, <c>2026-09-10</c>:</b> <i>"its not just multi brain, it is also multi muscle or
    /// multi perception. we might need performance balancing or nodes specialized to some types of
    /// entities or whatever. i need the shard provider interface for these, implemented for single brain
    /// and single muscle case we have now, but reimplementable later."</i></para>
    ///
    /// <para>⭐ <b>Only the SEAM ships now.</b> <see cref="SingleNodePerRoleShardProvider"/> is the one
    /// implementation, and it is byte-identical to today's single-Brain / single-Muscle behaviour. ⛔ No
    /// shard table, no transport, no balancing metric — those are explicitly out of scope.</para>
    ///
    /// <para>⚠ <b>Nothing calls this yet.</b> Its consumer is <c>IRoleAffinityPolicy</c> (step 1), which
    /// unions the component mask of every role this node actually serves.</para>
    /// </summary>
    public interface IRoleShardProvider
    {
        /// <summary>
        /// ⭐ True when this node is the one that serves <paramref name="roleBit"/> for the entity
        /// described by <paramref name="key"/>.
        ///
        /// <para>⛔⛔ <b>CONTRACT ①, AND IT KILLS THE OBVIOUS IMPLEMENTATION: this method may read ONLY
        /// inputs that are IDENTICAL ON EVERY NODE.</b> A node may <b>not</b> decide from its own CPU
        /// load, queue depth, entity count, or any locally-observed metric. The entire safety property of
        /// role-affinity ownership is that two nodes independently evaluating the same function
        /// <i>cannot disagree</i>; if node A reads *its* load and node B reads *its* load, both can answer
        /// <c>true</c> for one entity ⇒ <b>two owners</b>, which is the exact conflict this design exists
        /// to remove. ⇒ 🔴 <b>"performance balancing" cannot be a node measuring itself.</b> It must be a
        /// shard assignment published by ONE authority and replicated, which every node then reads
        /// identically — an implementation is <i>handed</i> that table, it does not compute one.</para>
        ///
        /// <para>⛔ <b>CONTRACT ②: the mapping must be STABLE for an entity's lifetime.</b> The policy is
        /// evaluated at <b>birth</b> and at <b>promotion</b> only. If the mapping changes while entities
        /// are live, existing entities keep their birth assignment while newly-promoted ghosts follow the
        /// new one — ownership becomes history-dependent. ⚠ Making it dynamic needs a re-evaluation path,
        /// which is a SECOND ownership mechanism and therefore a design of its own, not an implementation
        /// detail of this seam.</para>
        ///
        /// <para>⭐ <b>Answering <c>false</c> for everyone is legitimate and benign</b> — e.g. a shard
        /// table that has not arrived yet. The component is then owned by no one, which is the case the
        /// design already rules on: log once per entity, no fallback, plus a boot warning. A fallback
        /// (<i>"the creator keeps it after N frames"</i>) would reintroduce exactly the race this removes.</para>
        /// </summary>
        /// <param name="roleBit">
        /// ⚠⚠ <b>An OPAQUE role bit, deliberately NOT a <c>NodeRole</c>.</b> 📐 <c>NodeRole</c> lives in
        /// <c>Hrot.Core</c>, and <c>Hrot.Core</c> REFERENCES this assembly — the dependency cannot run the
        /// other way, so §3.8's literal <c>ServesRole(NodeRole, ...)</c> signature could not compile here.
        /// ⭐ More importantly it <b>should not</b>: this is the same discipline §2.3 already imposed on
        /// components, where a role's ownable set is a <c>BitMask512</c> of component ids and the engine
        /// never learns what a "Brain component" is. Roles get the same treatment — the engine compares
        /// bits, the application supplies the meaning, and the caller casts at its composition root
        /// (<c>(int)NodeRole.Brain</c>). Callers should pass a SINGLE bit; see
        /// <see cref="SingleNodePerRoleShardProvider.ServesRole"/> for what a multi-bit argument means.
        /// </param>
        /// <param name="key">What is known about the entity at both insertion points. See
        /// <see cref="RoleShardKey"/>.</param>
        bool ServesRole(int roleBit, in RoleShardKey key);
    }

    /// <summary>
    /// ⭐⭐ <b>What an <see cref="IRoleShardProvider"/> is allowed to shard on.</b>
    /// 📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.8.
    ///
    /// <para>⭐ <b>Extensible ON PURPOSE</b> — 🔒 the user's <i>"or whatever"</i>. Adding a Faction or Zone
    /// discriminator later touches neither call site nor any existing implementation, because both
    /// insertion points construct the whole key and every implementation reads only the fields it wants.</para>
    ///
    /// <para>📐 <b>Both insertion points can fill it — measured, and this is why the shape is safe:</b>
    /// on CREATE, <c>NetworkSpawningSystem</c> has <c>networkId</c> as a local, <c>cmd.TkbType</c>, and the
    /// DIS value it computes before stamping the header; on PROMOTE, the ghost already carries
    /// <c>NetworkIdentity</c> (added by <c>GhostCreationSystem</c>) and <c>TkbIdentity</c>, and the
    /// template is resolved by then.</para>
    ///
    /// <para>⚠ <b>Every field may legitimately be zero</b>, and an implementation must cope. A networkless
    /// node has no <c>NetworkIdentity</c>, so <see cref="NetworkId"/> is <c>0</c> — which is precisely why
    /// the default implementation reads none of them.</para>
    /// </summary>
    public readonly struct RoleShardKey
    {
        /// <summary>
        /// ⭐ Stable per-entity discriminator ⇒ the axis a future <b>BALANCING</b> implementation would
        /// shard on. ⚠ <c>0</c> on a networkless node, and on an entity that has not been assigned an id yet.
        /// </summary>
        public readonly long NetworkId;

        /// <summary>
        /// ⭐ The entity TYPE ⇒ the axis a future <b>SPECIALISATION</b> implementation would shard on
        /// (<i>"this node serves the rotary-wing platforms"</i>).
        /// ⚠⚠ <b><c>long</c>, not the <c>int</c> §3.8 first wrote.</b> Every `TkbType` in the system is a
        /// <c>long</c> (<c>TkbIdentity</c>, <c>SpawnEntityCommand</c>, <c>TkbTemplate</c>), so an <c>int</c>
        /// here would silently truncate and make two distinct types shard identically.
        /// </summary>
        public readonly long TkbType;

        /// <summary>⭐ Already stamped in the entity header, so it costs nothing to carry. A coarser
        /// specialisation axis than <see cref="TkbType"/> (kind / domain / country).</summary>
        public readonly DISEntityType DisType;

        public RoleShardKey(long networkId, long tkbType, DISEntityType disType)
        {
            NetworkId = networkId;
            TkbType   = tkbType;
            DisType   = disType;
        }
    }
}
