using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Abstractions;

namespace Fdp.Toolkit.Replication.Services
{
    /// <summary>
    /// ⭐⭐⭐ <b>The role-affinity rule, implemented once: union the component masks of the roles this node
    /// actually serves, plus the creator's birthright.</b>
    ///
    /// <para>📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.3, §3.8, §3.9, §6 step <c>1</c>. The
    /// rule:</para>
    /// <code>
    /// OwnableMask(template, isCreator, key):            // AUTHORITY — per entity
    ///     mask = ∅
    ///     for each role R in declaredRoles:
    ///         if shard.ServesRole(R, key):  mask |= ownedComponentsPerRole[R]
    ///     if isCreator:                     mask |= template.BirthCriticalComponents
    ///     return mask                       // the CALLER intersects with the live component mask
    ///
    /// OwnedComponentSet  = ⋃ ownedComponentsPerRole[R] for R in declaredRoles   // no shard, no template
    /// ReadComponentSet   = ⋃ readComponentsPerRole [R] for R in declaredRoles
    /// RegisterComponentSet = OwnedComponentSet ∪ ReadComponentSet               // REGISTER — per node
    /// </code>
    ///
    /// <para>🔴🔴 <b>TWO SETS, NOT ONE — §3.9, and the one-set version of this class was WRONG in a way
    /// that would have broken the cluster.</b> 🔒 User, <c>2026-09-12</c>: <i>"intents are brain owned
    /// components that must be replicated to muscle so musle can read and act on them. so muscle cant
    /// simply stop registwring them because they are brain ones."</i> ⇒ <c>REGISTER = owned ∪ read</c> and
    /// <c>AUTHORITY = owned</c>; ⛔ deriving registration from the owned set alone makes a Muscle node stop
    /// receiving its own orders.</para>
    ///
    /// <para>⚠⚠ <b>PER ROLE, not one flat mask.</b> §3.3 first drew a single
    /// <c>roleOwnedComponents</c> union — ⛔ that cannot work once <see cref="IRoleShardProvider"/>
    /// exists, because each declared role must be shard-tested SEPARATELY: a node may serve
    /// <c>Brain</c> for this entity and not for that one. A pre-unioned mask has already thrown away the
    /// per-role structure the shard test needs.</para>
    ///
    /// <para>⛔⛔ <b>This class knows nothing about what a role MEANS.</b> 🔒 User, <c>2026-09-12</c>:
    /// <i>"fdp should not understand what a brain and muscle really mean."</i> Both tables are handed in by
    /// the APPLICATION at its composition root; here they are opaque maps from a label to a bag of
    /// component ids — ⛔ nothing in this assembly knows that <c>NavigationIntent</c> is the component that
    /// forced the second one to exist.</para>
    ///
    /// <para>⭐ <b>Determinism is the safety property.</b> Every input is composition-time or
    /// entity-derived — there is no clock, no counter, no local metric, and no mutable state. ⇒ two nodes
    /// evaluating this over the same entity return the same answer, which is what makes "no two nodes
    /// claim the same component" true by construction rather than by handshake.</para>
    /// </summary>
    public sealed class RoleAffinityPolicy : IRoleAffinityPolicy
    {
        private readonly NodeRole                                   _declaredRoles;
        private readonly IReadOnlyDictionary<NodeRole, BitMask512>  _ownedComponentsPerRole;
        private readonly IReadOnlyDictionary<NodeRole, BitMask512>? _readComponentsPerRole;
        private readonly IRoleShardProvider                         _shard;

        /// <param name="declaredRoles">
        /// The roles this node declares — normally the host's own constant
        /// (<c>CgfSubsystem.DefaultRole</c>, <c>SimHostApp.DefaultRole</c>, …).
        /// ⭐ <see cref="NodeRole.None"/> is legitimate: a presentation-only node owns nothing by role,
        /// and still keeps its birth-critical components when it creates an entity.
        /// </param>
        /// <param name="ownedComponentsPerRole">
        /// ⭐⭐ The <c>ownedComponentSet</c> per role — which component ids this role has AUTHORITY over.
        /// ⚠ Roles absent from this table contribute nothing, and entries for roles this node does not
        /// declare are never consulted — so one shared table may be handed to every host, which is the
        /// cheapest way to keep the cluster's roles consistent.
        /// ⚠⚠ <b>Renamed from <c>componentsPerRole</c> (§3.9)</b>: the old name did not say WHICH of a
        /// role's two component sets it was, and that ambiguity is what let registration be reasoned about
        /// as if this were the whole story.
        /// </param>
        /// <param name="shard">
        /// Decides whether THIS node serves a given role for a given entity.
        /// ⭐ Pass a <see cref="SingleNodePerRoleShardProvider"/> over <paramref name="declaredRoles"/> for
        /// today's one-node-per-role cluster.
        /// </param>
        /// <param name="readComponentsPerRole">
        /// 🔴 The <c>readComponentSet</c> per role — owned ELSEWHERE, replicated IN, consumed here.
        /// ⭐ Contributes to <see cref="RegisterComponentSet"/> and to nothing else; ⛔ it can never grant
        /// authority.
        /// <para>⚠ <b>Optional, and <c>null</c> means "this node's roles read nothing beyond what they
        /// own"</b> — which keeps every existing three-argument call site compiling and behaving
        /// identically. ⛔ It is NOT the silent-default defect: a caller that HAS a read table and does not
        /// pass it is the defect, and step 4's composition roots are where that becomes checkable.</para>
        /// </param>
        public RoleAffinityPolicy(
            NodeRole                                   declaredRoles,
            IReadOnlyDictionary<NodeRole, BitMask512>  ownedComponentsPerRole,
            IRoleShardProvider                         shard,
            IReadOnlyDictionary<NodeRole, BitMask512>? readComponentsPerRole = null)
        {
            _declaredRoles          = declaredRoles;
            _ownedComponentsPerRole = ownedComponentsPerRole ?? throw new ArgumentNullException(nameof(ownedComponentsPerRole));
            _shard                  = shard                  ?? throw new ArgumentNullException(nameof(shard));
            _readComponentsPerRole  = readComponentsPerRole;

            // ⭐⭐ Precomputed, because they are composition-time answers by definition — no entity, no
            //   shard, no template. ⚠ Doing it here also makes the "no mutable state" determinism property
            //   (below) hold for the new members for free: they cannot drift, because nothing recomputes
            //   them.
            OwnedComponentSet = UnionOverDeclaredRoles(_ownedComponentsPerRole);
            ReadComponentSet  = UnionOverDeclaredRoles(_readComponentsPerRole);

            var register = OwnedComponentSet;
            var read     = ReadComponentSet;
            register.BitwiseOr(in read);
            RegisterComponentSet = register;
        }

        /// <inheritdoc/>
        public BitMask512 OwnedComponentSet { get; }

        /// <inheritdoc/>
        public BitMask512 ReadComponentSet { get; }

        /// <inheritdoc/>
        public BitMask512 RegisterComponentSet { get; }

        /// <summary>
        /// ⭐ The declared-roles union of a per-role table, with ⛔ <b>no shard test</b> — deliberately.
        ///
        /// <para>⚠ <see cref="IRoleShardProvider"/> answers PER ENTITY ("does another node serve Brain for
        /// <i>this</i> one?"), and registration has no entity. ⇒ a node must register everything it could
        /// need for ANY entity, so the shard-gated answer would be the wrong — and unsafely narrow —
        /// one.</para>
        /// </summary>
        private BitMask512 UnionOverDeclaredRoles(IReadOnlyDictionary<NodeRole, BitMask512>? table)
        {
            var mask = default(BitMask512);
            if (table == null) return mask;

            int declared = (int)_declaredRoles;
            for (int bit = 0; bit < 31; bit++)
            {
                int value = 1 << bit;
                if ((declared & value) == 0) continue;

                if (table.TryGetValue((NodeRole)value, out var roleMask))
                    mask.BitwiseOr(in roleMask);
            }

            return mask;
        }

        /// <inheritdoc/>
        public BitMask512 OwnableMask(TkbTemplate template, bool isCreator, in RoleShardKey key)
        {
            var mask = default(BitMask512);   // ⭐ all-zero: own nothing unless something says otherwise

            // ⭐⭐ ONE BIT AT A TIME. ⛔ Not `foreach (var kv in _ownedComponentsPerRole)` — the shard test is
            //   per ROLE, and iterating the table would consult roles this node never declared. Walking
            //   the DECLARED bits keeps "I only answer for roles I claim" structural.
            //   ⚠ `NodeRole` is [Flags] over an int, so 31 is the full domain and the loop is bounded by
            //   the type, not by the table's size.
            int declared = (int)_declaredRoles;
            for (int bit = 0; bit < 31; bit++)
            {
                int value = 1 << bit;
                if ((declared & value) == 0) continue;

                var role = (NodeRole)value;

                // ⭐ The shard gate. Answering false for every role is legitimate and benign — e.g. a
                //   shard table that has not arrived. The component is then owned by NO ONE, which is
                //   the design's ruled behaviour: log once, no fallback. ⛔ A fallback ("the creator
                //   keeps it after N frames") would reintroduce the very race this removes.
                if (!_shard.ServesRole(role, key)) continue;

                // ⚠ A declared role with no table entry contributes nothing, deliberately and silently:
                //   it means "this node claims the role but owns no components for it here", which is
                //   true of a host that has not registered those component types at all (tkb-1 §6.5b
                //   gate ②, the narrowing lever). It is not the silent-default defect — there is no
                //   value the caller HELD and failed to pass.
                // ⛔⛔ THE OWNED TABLE ONLY (§3.9). A role's readComponentSet is what it CONSUMES, not what
                //   it claims — ORing it here would hand a Muscle node authority over NavigationIntent, so
                //   the Brain and the Muscle would both own the intent and their egress would fight. That
                //   is precisely the two-owner conflict this design exists to remove.
                if (_ownedComponentsPerRole.TryGetValue(role, out var roleMask))
                    mask.BitwiseOr(in roleMask);
            }

            // ⭐⭐⭐ THE CREATOR'S BIRTHRIGHT, and it is unconditional on role. See
            //   IRoleAffinityPolicy.OwnableMask's `isCreator` remarks for why declining spatial state
            //   would write a position that is never published.
            if (isCreator && template != null)
            {
                var birth = template.BirthCriticalComponents;
                for (int i = 0; i < birth.Count; i++)
                {
                    int id = birth[i];
                    if ((uint)id < FdpConfig.MAX_COMPONENT_TYPES) mask.SetBit(id);
                }
            }

            return mask;
        }
    }
}
