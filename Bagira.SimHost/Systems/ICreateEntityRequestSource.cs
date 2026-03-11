using System;
using Bagira.BDC.SSTM;

namespace Bagira.SimHost.Systems
{
    /// <summary>
    /// Abstraction for polling incoming <see cref="CreateEntityRequest"/> messages.
    /// Decouples <see cref="CreateEntityRequestSystem"/> from the DDS transport layer,
    /// enabling unit testing without a live DDS participant.
    ///
    /// <para>
    /// The callback-based API eliminates the need to allocate a
    /// <c>List&lt;CreateEntityRequest&gt;</c> on every poll call, which was a
    /// primary source of GC pressure on the 10 k-entities-per-frame hot path.
    /// </para>
    /// </summary>
    public interface ICreateEntityRequestSource
    {
        /// <summary>
        /// Iterates all pending requests and drains them from the source, invoking
        /// <paramref name="processor"/> for each valid sample.
        /// No allocation occurs when the source is empty.
        /// </summary>
        void ProcessRequests(Action<CreateEntityRequest> processor);
    }
}
