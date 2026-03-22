using System;
using Bagira.BDC.SSTM;

namespace Bagira.SimHost.Systems
{
    /// <summary>
    /// Source of incoming <see cref="DeleteEntityRequest"/> messages.
    /// Implementations may read from DDS or from an in-process stub for testing.
    /// </summary>
    public interface IDeleteEntityRequestSource
    {
        /// <summary>
        /// Drains all pending requests and invokes <paramref name="processor"/> for each.
        /// </summary>
        void ProcessRequests(Action<DeleteEntityRequest> processor);
    }
}
