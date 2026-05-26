using Fdp.Core;
using Fdp.Toolkit.ReplayBrowser.Federation;
using Fdp.Toolkit.ReplayBrowser.Support;
using Xunit;

namespace Fdp.Toolkit.ReplayBrowser.Federation.Tests
{
    /// <summary>
    /// Tests for <see cref="RepositoryPriming"/> (RBF-P3T6).
    /// </summary>
    public sealed class RepositoryPrimingTests
    {
        // ── RBF-P3T6 ─────────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="RepositoryPriming.RegisterDiscoveredComponents"/> must register
        /// component types annotated with <see cref="ComponentIdAttribute"/> that are
        /// already loaded in the current AppDomain.
        /// <para>
        /// We use <see cref="HarnessPosition"/> (ComponentId 202) as the probe because it
        /// is compiled into this test assembly and is therefore guaranteed to be loaded at
        /// test execution time.
        /// </para>
        /// </summary>
        [Fact]
        public void RBF_P3T6_Priming_RegistersComponentsOnFreshRepo()
        {
            ComponentTypeRegistry.Clear();
            using var repo = new EntityRepository();

            RepositoryPriming.RegisterDiscoveredComponents(repo);

            // HarnessPosition (ComponentId 202) lives in this test assembly and must be found.
            // If registration failed, SetComponent or HasComponent would throw.
            var entity = repo.CreateEntity();
            repo.SetComponent(entity, new HarnessPosition { X = 7f, Y = 0f, Z = 0f });
            Assert.True(repo.HasComponent<HarnessPosition>(entity),
                "HarnessPosition must be registerable and accessible after RepositoryPriming.");
            Assert.Equal(7f, repo.GetComponent<HarnessPosition>(entity).X);
        }
    }
}
