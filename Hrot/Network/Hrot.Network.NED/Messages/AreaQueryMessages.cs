using System.Collections.Generic;
using CycloneDDS.Schema;

namespace Hrot.NED.Messages
{
    /// <summary>
    /// A single EQS area-query request in wire format. All fields are value types.
    /// Embedded inside <see cref="AreaQueryRequestBatch"/>.
    /// </summary>
    [DdsStruct]
    [DdsIdlFile("hrot-eqs-msgs")]
    public partial struct DdsAreaQueryRequest
    {
        /// <summary>Stable request identifier for correlation (encodes requestingEntity.Index + batchSlot).</summary>
        public long RequestId;
        /// <summary>Network ID of the area polygon entity on the Muscle node.</summary>
        public long TargetAreaNetworkId;
        /// <summary>Brain node ID that submitted the request — for return routing.</summary>
        public int SourceNodeId;
        /// <summary>Force affiliation filter as integer cast of <c>ForceId</c>.</summary>
        public int ForceId;
    }

    /// <summary>
    /// A single EQS area-query response in wire format. Contains the list of target
    /// network IDs found inside the polygon.
    /// Embedded inside <see cref="AreaQueryResponseBatch"/>.
    /// </summary>
    [DdsStruct]
    [DdsIdlFile("hrot-eqs-msgs")]
    public partial struct DdsAreaQueryResponse
    {
        /// <summary>Echoed request identifier for correlation.</summary>
        public long RequestId;
        /// <summary>Number of target entities found inside the polygon.</summary>
        public int TargetCount;
        /// <summary>Network IDs of the hostile entities found inside the polygon.</summary>
        [DdsManaged]
        public List<long> TargetNetworkIds;
    }

    /// <summary>
    /// Batched EQS area-query requests published by a Brain node to the Muscle node.
    /// Transported on the <c>"AreaQueryRequestBatch"</c> DDS topic
    /// (ordinal <see cref="Hrot.NED.Descriptors.EDescriptorType.dtAreaQueryRequestBatch"/> = 93).
    /// </summary>
    [DdsTopic("AreaQueryRequestBatch")]
    [DdsIdlFile("hrot-eqs-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
    public partial struct AreaQueryRequestBatch
    {
        /// <summary>Originating Brain node ID.</summary>
        [DdsKey]
        public int SourceNodeId;
        /// <summary>Per-request entries.</summary>
        [DdsManaged]
        public List<DdsAreaQueryRequest> Requests;
    }

    /// <summary>
    /// Batched EQS area-query responses returned by the Muscle node to the requesting Brain.
    /// Transported on the <c>"AreaQueryResponseBatch"</c> DDS topic
    /// (ordinal <see cref="Hrot.NED.Descriptors.EDescriptorType.dtAreaQueryResponseBatch"/> = 94).
    /// </summary>
    [DdsTopic("AreaQueryResponseBatch")]
    [DdsIdlFile("hrot-eqs-msgs")]
    [DdsQos(Reliability = DdsReliability.Reliable, Durability = DdsDurability.Volatile)]
    public partial struct AreaQueryResponseBatch
    {
        /// <summary>Brain node ID this response is addressed to.</summary>
        [DdsKey]
        public int TargetNodeId;
        /// <summary>Per-request responses.</summary>
        [DdsManaged]
        public List<DdsAreaQueryResponse> Responses;
    }
}
