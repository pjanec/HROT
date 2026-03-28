using System.Runtime.CompilerServices;
using System.Threading;
using Xunit;

// Tests that write to LogManager.Configuration (global NLog state) or depend on
// AssertLogContains must remain sequential so they get exclusive log ownership.
// All other tests are safe to run in parallel once each get their own DDS domain ID.
[assembly: CollectionBehavior(MaxParallelThreads = 4)]

internal static class ThreadPoolInit
{
    /// <summary>
    /// Pre-warm the thread pool so async continuations don't stall waiting for new threads
    /// while test threads are blocked in WaitForCondition polling loops.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
        => ThreadPool.SetMinThreads(32, 32);
}
