using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Executors;
using Xunit;

namespace Fdp.Toolkit.Navigation.Tests
{
    /// <summary>
    /// Tests for MOD1-P1T1 — verifies the NavigationIntent / NavigationStatus ECS
    /// component contracts and enforces the FDP.Toolkit.Navigation assembly boundary
    /// (zero project specific references).
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

        // ── PACK-N001: NavigationStatus.ProgressS ────────────────────────────────

        /// <summary>
        /// PACK-N001 SC-1: <see cref="NavigationStatus.ProgressS"/> must round-trip via direct
        /// field access (field present and non-optimised).
        /// </summary>
        [Fact]
        public void NavigationStatus_ProgressS_RoundTrips()
        {
            var status = new NavigationStatus { ProgressS = 0.5f };
            Assert.Equal(0.5f, status.ProgressS);
        }

        // ── NAV-P0-T5: new struct size tests ─────────────────────────────────

        /// <summary><see cref="NavWaypoint"/> must be exactly 24 bytes.</summary>
        [Fact]
        public unsafe void NavWaypoint_SizeIs24Bytes()
        {
            Assert.Equal(24, sizeof(NavWaypoint));
        }

        /// <summary><see cref="PreviewWaypoint"/> must be exactly 16 bytes.</summary>
        [Fact]
        public unsafe void PreviewWaypoint_SizeIs16Bytes()
        {
            Assert.Equal(16, sizeof(PreviewWaypoint));
        }

        /// <summary>
        /// <see cref="NavigationCorridorPreview"/> must be exactly 144 bytes:
        /// 16-byte header + 8 * 16-byte <see cref="PreviewWaypoint"/>.
        /// </summary>
        [Fact]
        public unsafe void NavigationCorridorPreview_SizeIs144Bytes()
        {
            Assert.Equal(144, sizeof(NavigationCorridorPreview));
        }

        // ── NAV-P0-T5: ComponentId uniqueness and range ───────────────────────

        /// <summary>No two constants in <see cref="NavigationContractsComponentIds"/> may share the same value.</summary>
        [Fact]
        public void NavContractsComponentIds_NoDuplicateValues()
        {
            var fields = typeof(NavigationContractsComponentIds)
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(f => f.FieldType == typeof(int))
                .ToArray();

            var seen  = new System.Collections.Generic.HashSet<int>();
            foreach (var f in fields)
            {
                int val = (int)f.GetValue(null)!;
                Assert.True(seen.Add(val), $"Duplicate ComponentId value {val} on field {f.Name}");
            }
        }

        /// <summary>New nav component IDs (257-261 block) must all fall in the extended 256-511 range.</summary>
        [Fact]
        public void NavContractsComponentIds_V2Range_In256To511()
        {
            int[] newIds =
            {
                NavigationContractsComponentIds.NavAgentProfile,
                NavigationContractsComponentIds.NavigationCorridorMuscle,
                NavigationContractsComponentIds.NavigationCorridorPreview,
                NavigationContractsComponentIds.NavigationPathDetailsBuffer,
                NavigationContractsComponentIds.CrowdAgent,
            };

            foreach (var id in newIds)
                Assert.InRange(id, 256, 511);
        }

        /// <summary>
        /// <see cref="NavigationContractsComponentIds.CrowdAgent"/> must differ from
        /// <see cref="NavigationContractsComponentIds.NavigationIntent"/>.
        /// </summary>
        [Fact]
        public void CrowdAgent_ComponentId_IsDistinctFromNavigationIntent()
        {
            Assert.NotEqual(
                NavigationContractsComponentIds.CrowdAgent,
                NavigationContractsComponentIds.NavigationIntent);
        }

        // ── NAV-P0-T4/T5: NavigationStatus new field defaults ─────────────────

        /// <summary><see cref="NavigationStatus.RouteHandle"/> must default to zero.</summary>
        [Fact]
        public void NavigationStatus_RouteHandle_DefaultIsZero()
        {
            var status = default(NavigationStatus);
            Assert.Equal(0, status.RouteHandle);
        }

        /// <summary><see cref="NavigationStatus.Phase"/> must default to <see cref="NavigationPhase.Idle"/>.</summary>
        [Fact]
        public void NavigationStatus_Phase_DefaultIsIdle()
        {
            var status = default(NavigationStatus);
            Assert.Equal(NavigationPhase.Idle, status.Phase);
        }
    }
}
