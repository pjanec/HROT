using Xunit;

namespace Fdp.Examples.UrbanCombat.Tests
{
    /// <summary>
    /// Test collection that disables parallelization for tests with shared static state.
    /// </summary>
    [CollectionDefinition("SerialTests", DisableParallelization = true)]
    public class SerialTestsCollection { }
}
