using System.Runtime.CompilerServices;
using System.Threading;
using Xunit;

// DDS-using tests are grouped in [Collection("SimHostDds")] and run sequentially
// to avoid cross-test DDS domain collisions. All other tests run in parallel.
[assembly: CollectionBehavior(MaxParallelThreads = 4)]

internal static class ThreadPoolInit
{
    /// <summary>
    /// Pre-warm the thread pool so that DDS-related async continuations aren't delayed
    /// while test threads block in Thread.Sleep within DDS settlement loops.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
        => ThreadPool.SetMinThreads(32, 32);
}
