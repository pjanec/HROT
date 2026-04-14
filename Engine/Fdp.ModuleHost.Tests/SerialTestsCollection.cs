using System.Runtime.CompilerServices;
using System.Threading;
using Xunit;

namespace Fdp.ModuleHost.Tests
{
    /// <summary>
    /// Test collection that disables parallelization for tests that measure
    /// process-wide memory (GC.GetTotalMemory) or have other global-state sensitivity.
    /// </summary>
    [CollectionDefinition("SerialTests", DisableParallelization = true)]
    public class SerialTestsCollection { }

    internal static class ThreadPoolInit
    {
        /// <summary>
        /// Pre-warm the thread pool so async continuations in Task.Run kernel loops
        /// don't stall while test threads are blocked in Task.Delay/Thread.Sleep.
        /// </summary>
        [ModuleInitializer]
        internal static void Initialize()
            => ThreadPool.SetMinThreads(32, 32);
    }
}
