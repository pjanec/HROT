using Hrot.NED.Messages;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// Abstraction for writing <see cref="CreateUpdateDeleteEntityAck"/> responses.
    /// Decouples entity request systems from the DDS transport layer,
    /// enabling unit testing without a live DDS participant.
    /// </summary>
    public interface ICreateUpdateDeleteEntityAckSink
    {
        /// <summary>Sends an acknowledgment to the original requester.</summary>
        void WriteAck(CreateUpdateDeleteEntityAck ack);
    }
}
