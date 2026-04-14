using ModuleHost.Core.Network.Interfaces;

namespace FDP.Toolkit.NetworkSpawning.Tests.Helpers
{
    /// <summary>
    /// Deterministic in-memory ID allocator for unit tests.
    /// No DDS or network required.
    /// </summary>
    public sealed class StubIdAllocator : INetworkIdAllocator
    {
        private long _next;

        /// <summary>Last ID returned by <see cref="AllocateId"/>.</summary>
        public long LastAllocatedId { get; private set; }

        /// <param name="startId">First ID to return (default 1).</param>
        public StubIdAllocator(long startId = 1) => _next = startId;

        /// <inheritdoc />
        public long AllocateId()
        {
            LastAllocatedId = _next;
            return _next++;
        }

        /// <inheritdoc />
        public void Reset(long startId = 0) => _next = startId;

        /// <inheritdoc />
        public void Dispose() { }
    }
}
