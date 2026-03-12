using System;
using Bagira.DDS.DM;

namespace Bagira.Map.Common.Systems;

/// <summary>
/// Abstraction over the DDS writer that sends <see cref="Bagira.BDC.SSTM.CreateUpdateDeleteEntityAck"/>
/// messages. Allows the system to be unit-tested without a live DDS participant.
/// </summary>
public interface IUpdateEntityAttributeAckSink
{
    /// <summary>
    /// Writes a success acknowledgement for the request identified by <paramref name="requestId"/>,
    /// indicating which ECS components were authorised and mutated by this node.
    /// Only sent when the request carries <c>RequireAck=true</c> AND at least one component was applied.
    /// </summary>
    /// <param name="requestId">The <c>RequestId</c> from the original request.</param>
    /// <param name="errorCode">0 = success; any non-zero value indicates an error.</param>
    /// <param name="respondingNode">The NodeId of the node sending this acknowledgment.</param>
    /// <param name="opaqueData">
    /// 32-byte execution receipt.  FDP nodes encode a 256-bit bitmask here
    /// (bit N = ECS component type ID N was mutated).  Must be exactly 32 bytes.
    /// </param>
    void WriteAck(Guid requestId, int errorCode, NodeId respondingNode, ReadOnlySpan<byte> opaqueData);

    /// <summary>
    /// Writes an error acknowledgement (e.g. entity not found) with a zeroed opaque payload.
    /// Sent when the request specifies <c>RequireAck=true</c> and processing fails before
    /// any ECS mutations could occur.
    /// </summary>
    void WriteErrorAck(Guid requestId, int errorCode);
}
