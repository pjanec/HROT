using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Interfaces;

namespace Fdp.Toolkit.Replication.Abstractions
{
    /// <summary>
    /// ⭐ <b>One ownership group = the components ONE role owns for an entity.</b> Network-agnostic: a component
    /// mask, never a descriptor. 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §2 (R-172: groups are per
    /// role, never per node). A grant hands a whole group to one node serving <see cref="Role"/>; which node is the
    /// owner's per-entity choice and is not part of the group.
    /// </summary>
    public sealed class OwnershipGroup
    {
        private readonly Func<TkbTemplate, bool> _appliesTo;

        public OwnershipGroup(NodeRole role, BitMask512 members, Func<TkbTemplate, bool> appliesTo)
        {
            Role       = role;
            Members    = members;
            _appliesTo = appliesTo ?? throw new ArgumentNullException(nameof(appliesTo));
        }

        /// <summary>The role whose nodes own this group.</summary>
        public NodeRole Role { get; }

        /// <summary>Every component of the group — the ones on the wire and the ones linked to it (R-165).</summary>
        public BitMask512 Members { get; }

        /// <summary>
        /// Whether an entity of <paramref name="template"/> has this group at all (design §2 G-4) — a tank without a
        /// brain is granted no Brain group. An empty group never applies.
        /// </summary>
        public bool AppliesTo(TkbTemplate template) => !Members.IsEmpty() && _appliesTo(template);
    }

    /// <summary>
    /// ⭐ <b>The table of ownership groups, one per <see cref="NodeRole"/></b>, plus the LOCAL components that belong
    /// to no group (each node keeps its own copy). Anything in neither is the CREATOR's remainder (R-160).
    /// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §2, §5.1.
    /// </summary>
    public sealed class OwnershipGroupTable
    {
        private readonly Dictionary<NodeRole, OwnershipGroup> _groups = new();

        /// <exception cref="ArgumentException">
        /// A component is in two groups, a group shares a component with <paramref name="local"/>, or a role
        /// appears twice — the table must place every component in at most one box.
        /// </exception>
        public OwnershipGroupTable(IEnumerable<OwnershipGroup> groups, BitMask512 local)
        {
            if (groups == null) throw new ArgumentNullException(nameof(groups));
            Local = local;
            var seen = default(BitMask512);
            foreach (var g in groups)
            {
                if (_groups.ContainsKey(g.Role))
                    throw new ArgumentException($"Ownership group for role {g.Role} declared twice.", nameof(groups));
                var members = g.Members;
                if (BitMask512.HasAny(seen, members))
                    throw new ArgumentException($"Ownership group {g.Role} shares a component with another group.", nameof(groups));
                if (BitMask512.HasAny(local, members))
                    throw new ArgumentException($"Ownership group {g.Role} contains a LOCAL component.", nameof(groups));
                seen.BitwiseOr(in members);
                _groups.Add(g.Role, g);
            }
        }

        /// <summary>The groups, keyed by their role.</summary>
        public IReadOnlyDictionary<NodeRole, OwnershipGroup> Groups => _groups;

        /// <summary>Components no group and no owner decides — node-local copies (bookkeeping, caches, shadows).</summary>
        public BitMask512 Local { get; }

        /// <summary>The role whose group holds <paramref name="componentId"/>, or <see cref="NodeRole.None"/> when it
        /// is LOCAL or the creator's remainder.</summary>
        public NodeRole GroupOf(int componentId)
        {
            foreach (var kv in _groups)
                if (kv.Value.Members.IsSet(componentId)) return kv.Key;
            return NodeRole.None;
        }

        /// <summary>Whether <paramref name="componentId"/> is LOCAL (in no group, owned by nobody).</summary>
        public bool IsLocal(int componentId) => Local.IsSet(componentId);
    }
}
