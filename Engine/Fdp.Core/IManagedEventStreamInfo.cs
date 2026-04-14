using System;
using System.Collections;

namespace Fdp.Kernel
{
    /// <summary>
    /// Information about a managed event stream for recording purposes.
    /// </summary>
    public interface IManagedEventStreamInfo
    {
        int TypeId { get; }
        Type EventType { get; }
        IList PendingEvents { get; }
    }

    /// <summary>
    /// Implementation of managed event stream info.
    /// </summary>
    public class ManagedEventStreamInfo : IManagedEventStreamInfo
    {
        public int TypeId { get; set; }
        public Type EventType { get; set; } = null!;
        public IList PendingEvents { get; set; } = null!;
    }
}
