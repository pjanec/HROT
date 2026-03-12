using System;

namespace Bagira.Map.Common.Systems;

/// <summary>
/// Abstraction over the DDS writer that sends <see cref="Bagira.BDC.SSTM.CreateUpdateDeleteEntityAck"/>
/// messages. Allows the system to be unit-tested without a live DDS participant.
/// </summary>
public interface IUpdateEntityAttributeAckSink
{
    /// <summary>
    /// Writes an acknowledgement for the request identified by <paramref name="requestId"/>.
    /// </summary>
    /// <param name="requestId">The <c>RequestId</c> from the original request.</param>
    /// <param name="errorCode">0 = success; any non-zero value indicates an error.</param>
    void WriteAck(Guid requestId, int errorCode);
}
