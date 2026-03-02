using Xunit;

namespace ModuleHost.Core.Tests
{
    /// <summary>
    /// Test collection that disables parallelization for tests that measure
    /// process-wide memory (GC.GetTotalMemory) or have other global-state sensitivity.
    /// </summary>
    [CollectionDefinition("SerialTests", DisableParallelization = true)]
    public class SerialTestsCollection { }
}
