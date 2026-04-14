using System.Runtime.CompilerServices;
using System.Threading;
using Xunit;

// Each HrotRunnerHarness uses a unique DDS domain ID (Interlocked.Increment from base 100),
// so test classes are safe to run in parallel — different domains never see each other's traffic.
// MaxParallelThreads = 4 limits simultaneous DDS participants to a manageable level.
[assembly: CollectionBehavior(MaxParallelThreads = 4)]

// EditorOfflineTests use RCU hot-plug which schedules background drain tasks on the thread
// pool.  Running this collection in parallel with DDS-heavy tests exhausts all 4 parallel
// slots, starves the thread pool, and causes SwitchToExternalAsync to time out.  Marking the
// collection non-parallel ensures the RCU drain tasks always find a free thread.
[CollectionDefinition("EditorOfflineTests", DisableParallelization = true)]
public sealed class EditorOfflineTestsCollection { }

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
