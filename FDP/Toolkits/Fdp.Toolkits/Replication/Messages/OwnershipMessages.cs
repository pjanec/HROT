using System;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Replication.Messages
{
    /// <summary>
    /// Network message sent to update ownership of a specific descriptor.
    /// FDP-REP-201
    /// </summary>
    [EventId(9030)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct OwnershipUpdate
    {
        public NetworkIdentity NetworkId;
        public long PackedKey;
        public int NewOwnerNodeId;
        public int OriginNodeId;

        public override string ToString()
        {
            return $"OwnershipUpdate(NetId={NetworkId.Value}, Key={Fdp.Interfaces.PackedKey.ToString(PackedKey)}, Owner={NewOwnerNodeId}, Origin={OriginNodeId})";
        }
    }

    /// <summary>
    /// Local event raised when authority over a descriptor changes.
    /// FDP-REP-202
    /// </summary>
    [EventId(9031)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct DescriptorAuthorityChanged
    {
        public Entity Entity;
        public long PackedKey;
        public bool IsAuthoritative;
        
        public override string ToString()
        {
            return $"DescriptorAuthorityChanged(Entity={Entity}, Key={Fdp.Interfaces.PackedKey.ToString(PackedKey)}, Auth={IsAuthoritative})";
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4079</c> ② — local event: the creator's grant of a descriptor was CONFIRMED by the grantee (F7), so this node's
    /// writer should let go of that instance (<c>IDescriptorTranslator.ReleaseInstance</c> — an unregister, never a dispose) and the
    /// grantee becomes its sole writer. Raised by <c>OwnershipRecomputeSystem</c> where it ends the creator's
    /// <c>OutgoingGrantsPending</c> mark; consumed by <c>CycloneNetworkCleanupSystem</c>.
    /// 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.3.
    /// </summary>
    [EventId(9034)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct DescriptorHandedOver
    {
        public NetworkIdentity NetworkId;
        public long PackedKey;

        public override string ToString()
            => $"DescriptorHandedOver(NetId={NetworkId.Value}, Key={Fdp.Interfaces.PackedKey.ToString(PackedKey)})";
    }

    /// <summary>
    /// ⭐ Local event: a cluster node has LEFT (crashed or exited) — this node no longer hears its heartbeat. Raised by
    /// the network implementation's cluster-membership ingest; consumed by <c>PartialOwnerReclaimSystem</c>, which
    /// returns what the departed node owned to each entity's primary owner (R-167: a direct call, never a network
    /// message). 📄 <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.3, §5.6 S7.
    /// </summary>
    [EventId(9033)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct NodeDeparted
    {
        public int NodeId;

        public override string ToString() => $"NodeDeparted(Node={NodeId})";
    }
}
