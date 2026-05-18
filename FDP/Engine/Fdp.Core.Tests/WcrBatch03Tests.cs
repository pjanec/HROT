using Xunit;
using Fdp.Core;

namespace Fdp.Tests
{
    /// <summary>
    /// Tests for WCR-BATCH-03: Technical Debt Burndown.
    /// Covers DEBT-WCR-03 — EntityRepository.ResetGlobalVersion().
    /// </summary>
    public class WcrBatch03Tests
    {
        // ================================================================
        // DEBT-WCR-03: EntityRepository.ResetGlobalVersion()
        // ================================================================

        /// <summary>
        /// Registers components (which may internally advance _globalVersion via side-effects),
        /// then calls ResetGlobalVersion() and verifies the version lands on the expected value.
        /// </summary>
        [Fact]
        public void ResetGlobalVersion_AfterComponentRegistration_SetsVersionToOne()
        {
            using var repo = new EntityRepository();

            // Component registration advances _globalVersion unpredictably;
            // snapshot the version after setup so we can confirm reset works regardless.
            repo.RegisterComponent<IntComponent>();
            repo.RegisterComponent<FloatComponent>();

            // Advance via Tick() a few times so the starting value is definitely > 1
            repo.Tick();
            repo.Tick();
            repo.Tick();

            Assert.True(repo.GlobalVersion > 1, "Pre-condition: version should be > 1 after ticks.");

            // Act
            repo.ResetGlobalVersion();

            // Assert
            Assert.Equal(1u, repo.GlobalVersion);
        }

        /// <summary>
        /// Verifies that ResetGlobalVersion(n) sets the version to exactly n,
        /// and that subsequent Tick() calls start from that baseline.
        /// </summary>
        [Fact]
        public void ResetGlobalVersion_WithExplicitValue_SetsVersionPrecisely()
        {
            using var repo = new EntityRepository();

            repo.RegisterComponent<IntComponent>();
            repo.Tick();
            repo.Tick();

            // Act — reset to a known sentinel value
            const uint BaseVersion = 42u;
            repo.ResetGlobalVersion(BaseVersion);

            Assert.Equal(BaseVersion, repo.GlobalVersion);

            // One Tick() must advance to BaseVersion + 1
            repo.Tick();
            Assert.Equal(BaseVersion + 1u, repo.GlobalVersion);
        }
    }
}
