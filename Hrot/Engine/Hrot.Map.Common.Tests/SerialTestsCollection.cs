using Xunit;

namespace Hrot.Map.Common.Tests
{
    /// <summary>
    /// ⭐ Test classes that call <c>ComponentTypeRegistry.Clear()</c> — the PROCESS-WIDE registry — run alone: cleared while another class
    /// registers its components, that class throws <i>"Component type ID 51 is not registered"</i> from <c>RegisterComponent</c>
    /// (📐 measured <c>2026-10-16</c>: three different tests red across runs, the identity rotating). Same shape as Fdp.ModuleHost.Tests'.
    /// </summary>
    [CollectionDefinition("SerialTests", DisableParallelization = true)]
    public sealed class SerialTestsCollection { }
}
