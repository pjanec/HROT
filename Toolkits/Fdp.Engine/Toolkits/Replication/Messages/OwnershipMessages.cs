using System;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Components;

namespace FDP.Toolkit.Replication.Messages
{
    /// <summary>
    /// Network message sent to update ownership of a specific descriptor.
    /// FDP-REP-201
    /// </summary>
    [EventId(9030)]
    public struct OwnershipUpdate
    {
        public NetworkIdentity NetworkId;
        public long PackedKey;
        public int NewOwnerNodeId;

        public override string ToString()
        {
            return $"OwnershipUpdate(NetId={NetworkId.Value}, Key={Fdp.Interfaces.PackedKey.ToString(PackedKey)}, Owner={NewOwnerNodeId})";
        }
    }

    /// <summary>
    /// Local event raised when authority over a descriptor changes.
    /// FDP-REP-202
    /// </summary>
    [EventId(9031)]
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
}
