using System.Collections.Generic;
using Bagira.BDC.SSTM;

namespace Bagira.SimHost.Systems
{
    /// <summary>
    /// Abstraction for polling incoming <see cref="CreateEntityRequest"/> messages.
    /// Decouples <see cref="CreateEntityRequestSystem"/> from the DDS transport layer,
    /// enabling unit testing without a live DDS participant.
    /// </summary>
    public interface ICreateEntityRequestSource
    {
        /// <summary>
        /// Returns all pending requests and drains them from the source.
        /// The returned list may be empty if no requests are available.
        /// </summary>
        List<CreateEntityRequest> TakeRequests();
    }
}
