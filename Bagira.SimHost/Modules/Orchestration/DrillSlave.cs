using System.Collections.Generic;
using System.Linq;

namespace Bagira.SimHost.Modules.Orchestration
{
    /// <summary>
    /// Skeleton implementation of the per-node drill state machine slave.
    /// Manages registered <see cref="IDsmHandler"/> instances and dispatches
    /// DSM lifecycle commands to them.
    /// <para>
    /// The full implementation (DDS heartbeat, 2PC command dispatch,
    /// <c>NodeOpStatus</c> publishing) will be added in a future batch once
    /// <c>NodeOpCommand</c> and the DDS orchestration layer are defined.
    /// </para>
    /// </summary>
    public sealed class DrillSlave
    {
        private readonly List<IDsmHandler> _handlers = new();

        /// <summary>All registered DSM handlers.</summary>
        public IReadOnlyList<IDsmHandler> RegisteredHandlers => _handlers;

        /// <summary>
        /// Registers a <see cref="IDsmHandler"/> so that future DSM commands are
        /// dispatched to it.  A handler may be registered only once.
        /// </summary>
        public void RegisterHandler(IDsmHandler handler)
        {
            if (!_handlers.Contains(handler))
                _handlers.Add(handler);
        }

        /// <summary>
        /// Returns <c>true</c> when at least one handler of type
        /// <typeparamref name="T"/> is registered.
        /// </summary>
        public bool IsHandlerRegistered<T>() where T : IDsmHandler =>
            _handlers.OfType<T>().Any();
    }
}
