using System.Threading;

namespace Fdp.Examples.NetworkDemo.Tests.Infrastructure
{
    /// <summary>
    /// Allocates unique CycloneDDS domain IDs for each test so that parallel test
    /// execution does not cause cross-test DDS traffic on the same domain.
    /// Domain IDs start at 10 to avoid clashing with domain 0 used by production code.
    /// CycloneDDS supports domain IDs 0-232; 23 parallel tests fit easily within that range.
    /// </summary>
    internal static class TestDomainAllocator
    {
        private static int _counter = 9; // Next() returns 10 on first call

        /// <summary>Returns a domain ID unique to the calling test.</summary>
        public static uint Next() => (uint)Interlocked.Increment(ref _counter);
    }
}
