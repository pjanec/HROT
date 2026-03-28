using System.Runtime.CompilerServices;
using System.Threading;
using Xunit;

// Each BagiraRunnerHarness uses a unique DDS domain ID (Interlocked.Increment from base 100),
// so test classes are safe to run in parallel — different domains never see each other's traffic.
// MaxParallelThreads = 4 limits simultaneous DDS participants to a manageable level.
[assembly: CollectionBehavior(MaxParallelThreads = 4)]

internal static class ThreadPoolInit
{
    /// <summary>
    /// Pre-warm the thread pool to avoid the 500ms starvation delay that occurs when
    /// async continuations (DdsCommandClient TCS, gateway await chains) need new threads
    /// while the 4 parallel test threads are blocking in PumpUntil.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
        => ThreadPool.SetMinThreads(32, 32);
}
