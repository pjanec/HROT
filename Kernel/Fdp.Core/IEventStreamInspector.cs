using System;
using System.Collections.Generic;

namespace Fdp.Kernel
{
    /// <summary>
    /// Interface for inspecting event streams without generic constraints.
    /// Used primarily for debugging and editor tools.
    /// </summary>
    public interface IEventStreamInspector
    {
        int EventTypeId { get; }
        Type EventType { get; }
        
        /// <summary>
        /// Count of events in the Read (Current) buffer.
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Returns all events in the Read (Current) buffer as boxed objects.
        /// ALLOCATION WARNING: Uses boxing. Only use for Debugging/Inspector.
        /// </summary>
        IEnumerable<object> InspectReadBuffer();

        /// <summary>
        /// Returns all events in the Write (Pending) buffer as boxed objects.
        /// ALLOCATION WARNING: Uses boxing. Only use for Debugging/Inspector.
        /// </summary>
        IEnumerable<object> InspectWriteBuffer();
    }
}
