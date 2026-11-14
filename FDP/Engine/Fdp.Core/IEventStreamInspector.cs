using System;
using System.Collections.Generic;

namespace Fdp.Core
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
        /// ⭐ CE-4565 (X-10) — event <paramref name="index"/> of the Read buffer (0 ≤ index &lt; <see cref="Count"/>), with no iterator: a managed
        /// event is returned as is (no allocation), an unmanaged one boxed (its one allocation). The event history's per-tick capture uses it.
        /// </summary>
        object ReadItem(int index);

        /// <summary>
        /// Returns all events in the Write (Pending) buffer as boxed objects.
        /// ALLOCATION WARNING: Uses boxing. Only use for Debugging/Inspector.
        /// </summary>
        IEnumerable<object> InspectWriteBuffer();
    }
}
