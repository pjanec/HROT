using Fdp.Core;

namespace Fdp.Toolkit.Replication.Messages
{
    /// <summary>
    /// CE-276 — which descriptors an entity-ownership transfer moves. Descriptors are a NED network concept
    /// (ruling 2026-09-15), so a transfer is expressed per network descriptor, never per ECS component.
    /// </summary>
    public enum TransferScope
    {
        /// <summary>Move only the <c>EntityMaster</c> descriptor (if this node owns it) — hands off entity
        /// identity + save ownership, leaves every other descriptor where it is.</summary>
        MasterOnly = 0,

        /// <summary>Move every descriptor THIS node currently owns for the entity. The common case; naturally
        /// excludes descriptors owned by other roles (e.g. the Muscle's world-position descriptor).</summary>
        AllOwnedByThisNode = 1,

        /// <summary>Move the named subset (of the descriptors this node owns) — see
        /// <see cref="TransferEntityOwnershipRequest.DescriptorTypeIds"/>.</summary>
        SpecificDescriptors = 2,
    }

    /// <summary>
    /// CE-276 — request to HAND an entity (or a chosen subset of its descriptors) to another node. Published on
    /// the local bus by game/mission logic or the ai-debug HTTP surface; consumed by
    /// <c>OwnershipTransferInitiationSystem</c> (NED), which — per descriptor in scope that this node owns —
    /// writes the new owner into <c>DescriptorOwnership</c>, clears this node's <c>AuthorityMask</c> for that
    /// descriptor's components, publishes an <see cref="OwnershipUpdate"/> to the wire, and (for the
    /// <c>EntityMaster</c> descriptor) mirrors <c>NetworkAuthority.PrimaryOwnerId</c> so the save gate follows.
    /// 📄 docs/DESIGN_Entity_Ownership_Transfer.md.
    /// </summary>
    [EventId(9032)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct TransferEntityOwnershipRequest
    {
        /// <summary>The entity's network id (resolved to an <c>Entity</c> via <c>NetworkEntityMap</c>).</summary>
        public long NetworkId;

        /// <summary>The node to hand the descriptors to.</summary>
        public int NewOwnerNodeId;

        /// <summary>Which descriptors move.</summary>
        public TransferScope Scope;

        /// <summary>Only for <see cref="TransferScope.SpecificDescriptors"/> — the NED descriptor ordinals
        /// (<c>EDescriptorType</c> values) to move. Filtered to the ones this node actually owns.</summary>
        public long[]? DescriptorTypeIds;
    }
}
