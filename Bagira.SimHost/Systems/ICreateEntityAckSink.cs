using Bagira.BDC.SSTM;

namespace Bagira.SimHost.Systems
{
    /// <summary>
    /// Abstraction for writing <see cref="CreateEntityAck"/> responses.
    /// Decouples <see cref="CreateEntityRequestSystem"/> from the DDS transport layer,
    /// enabling unit testing without a live DDS participant.
    /// </summary>
    public interface ICreateEntityAckSink
    {
        /// <summary>Sends an acknowledgment to the original requester.</summary>
        void WriteAck(CreateEntityAck ack);
    }
}
