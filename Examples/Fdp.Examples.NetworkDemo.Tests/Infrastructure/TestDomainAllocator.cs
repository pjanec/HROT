using System.Threading;

namespace Fdp.Examples.NetworkDemo.Tests.Infrastructure
{
    /// <summary>
    /// Allocates unique CycloneDDS domain IDs for each test so that parallel test
    /// execution does not cause cross-test DDS traffic on the same domain.
    /// Domain IDs start at 16 to avoid clashing with domain 0 (production code) and
    /// domain 15 (reserved for <c>Hrot.Orchestrator.Tests</c>).
    /// CycloneDDS supports domain IDs 0-232; 23 parallel tests fit easily within that range.
    /// </summary>
    internal static class TestDomainAllocator
    {
        private static int _counter = 15; // Next() returns 16 on first call; domain 15 is reserved for orchestrator tests

        /// <summary>Returns a domain ID unique to the calling test.</summary>
        public static uint Next() => (uint)Interlocked.Increment(ref _counter);
    }
}
