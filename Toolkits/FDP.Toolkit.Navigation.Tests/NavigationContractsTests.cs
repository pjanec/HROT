using System.Linq;
using FDP.Toolkit.Navigation;
using FDP.Toolkit.Navigation.Executors;
using Xunit;

namespace FDP.Toolkit.Navigation.Tests
{
    /// <summary>
    /// Tests for MOD1-P1T1 — verifies the NavigationIntent / NavigationStatus ECS
    /// component contracts and enforces the FDP.Toolkit.Navigation assembly boundary
    /// (zero Bagira.* references).
    /// </summary>
    public class NavigationContractsTests
    {
        // ── Enum zero-value tests ─────────────────────────────────────────────

        [Fact]
        public void NavigationMode_None_HasValueZero()
        {
            Assert.Equal(0, (byte)NavigationMode.None);
        }

        [Fact]
        public void NavigationResult_InProgress_HasValueZero()
        {
            Assert.Equal(0, (byte)NavigationResult.InProgress);
        }

        // ── Struct zero-initialisation defaults ───────────────────────────────

        /// <summary>
        /// A zero-initialised <see cref="NavigationIntent"/> must be inactive by default.
        /// Verifies the design requirement: "Mode defaults to None for zero-initialised struct."
        /// </summary>
        [Fact]
        public void NavigationIntent_ZeroInitialised_ModeIsNone()
        {
            var intent = default(NavigationIntent);
            Assert.Equal(NavigationMode.None, intent.Mode);
        }

        /// <summary>
        /// A zero-initialised <see cref="NavigationStatus"/> must show InProgress by default,
        /// matching the "uninitialised state" requirement in the design doc.
        /// </summary>
        [Fact]
        public void NavigationStatus_ZeroInitialised_ResultIsInProgress()
        {
            var status = default(NavigationStatus);
            Assert.Equal(NavigationResult.InProgress, status.Result);
        }

        [Fact]
        public void NavigationIntent_ZeroInitialised_IntentIdIsZero()
        {
            var intent = default(NavigationIntent);
            Assert.Equal(0u, intent.IntentId);
        }

        // ── Assembly boundary: zero Bagira.* references ───────────────────────

        /// <summary>
        /// The <c>FDP.Toolkit.Navigation</c> assembly must contain zero references to
        /// any <c>Bagira.*</c> assembly — confirmed at runtime via reflection.
        /// </summary>
        [Fact]
        public void NavigationAssembly_HasNoBagiraReferences()
        {
            var naviAssembly = typeof(MoveToExecutor).Assembly;

            var bagiraRefs = naviAssembly
                .GetReferencedAssemblies()
                .Where(n => n.Name != null && n.Name.StartsWith("Bagira", System.StringComparison.OrdinalIgnoreCase))
                .Select(n => n.FullName)
                .ToList();

            Assert.Empty(bagiraRefs);
        }
    }
}
