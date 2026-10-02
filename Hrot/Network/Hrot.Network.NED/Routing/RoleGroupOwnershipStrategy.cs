using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication.Abstractions;

namespace Hrot.Network.Routing
{
    /// <summary>
    /// ⭐⭐⭐ <b>Push-only ownership: the creator grants each ROLE GROUP to a node serving that role.</b>
    /// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §2 (G-1, G-4, G-5, G-6), §5.1, §5.2; Q79 §0.7 (R-164);
    /// fixes <c>CE-500</c> (a SimHost-created entity's brain group was never granted to a Brain node, so CGF's
    /// <c>NavigationIntent</c> was never sent).
    /// <list type="bullet">
    ///   <item>G-4 — a group is granted only when it applies to the template (a brain, kinematics, perception).</item>
    ///   <item>G-6 — per group, independently: the least-loaded node serving that role.</item>
    ///   <item>G-5 — no node for the role, or the chosen node IS the creator ⇒ no grant; the creator keeps it.</item>
    ///   <item>G-1 — a granted group is every descriptor bound to it, all to the same node.</item>
    /// </list>
    /// <para>⚠ Replaces <c>BrainMuscleOwnershipStrategy</c>, which granted only <c>dtWorldPos</c> +
    /// <c>dtNavigationStatus</c> to a Muscle and never granted the brain — the creator kept it even when the creator
    /// was SimHost, which has no brain.</para>
    /// </summary>
    public sealed class RoleGroupOwnershipStrategy : IOwnershipDistributionStrategy
    {
        private readonly IClusterStateCache _clusterCache;
        private readonly OwnershipGroupTable _groups;
        private readonly IReadOnlyDictionary<NodeRole, IReadOnlyList<long>> _descriptorsByRole;

        /// <param name="clusterCache">Which nodes serve which role, and their load.</param>
        /// <param name="groups">The ownership groups (<c>HrotOwnershipGroups.Table</c>).</param>
        /// <param name="descriptorsByRole">Per role, the descriptors its group binds to
        /// (<c>NedOwnershipGroupBinding.GroupDescriptors</c>).</param>
        public RoleGroupOwnershipStrategy(
            IClusterStateCache clusterCache,
            OwnershipGroupTable groups,
            IReadOnlyDictionary<NodeRole, IReadOnlyList<long>> descriptorsByRole)
        {
            _clusterCache      = clusterCache      ?? throw new ArgumentNullException(nameof(clusterCache));
            _groups            = groups            ?? throw new ArgumentNullException(nameof(groups));
            _descriptorsByRole = descriptorsByRole ?? throw new ArgumentNullException(nameof(descriptorsByRole));
        }

        /// <inheritdoc/>
        public IReadOnlyList<DescriptorGrant> GetInitialGrants(in GrantRequest request)
        {
            var template = request.Template;
            if (template == null) return Array.Empty<DescriptorGrant>();

            List<DescriptorGrant>? grants = null;
            foreach (var (role, group) in _groups.Groups)
            {
                if (!group.AppliesTo(template)) continue;
                if (!_descriptorsByRole.TryGetValue(role, out var descriptors) || descriptors.Count == 0) continue;

                int? node = _clusterCache.GetLeastLoadedNode(role);
                if (!node.HasValue || node.Value == request.MasterNodeId) continue;

                grants ??= new List<DescriptorGrant>();
                foreach (long d in descriptors)
                    grants.Add(new DescriptorGrant { DescriptorTypeId = d, NodeId = node.Value });
            }
            return grants ?? (IReadOnlyList<DescriptorGrant>)Array.Empty<DescriptorGrant>();
        }
    }
}
