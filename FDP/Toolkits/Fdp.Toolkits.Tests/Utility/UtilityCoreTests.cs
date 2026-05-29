using System.Runtime.InteropServices;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Utility;
using Xunit;

namespace Fdp.Toolkit.Tests.Utility
{
    /// <summary>
    /// Unit tests for scoring core data structures (TASK-UAI-P1-01).
    /// </summary>
    public unsafe class UtilityCoreTests
    {
        // ── Struct size invariants ───────────────────────────────────────────────

        [Fact]
        public void ResponseCurve_SizeIs16Bytes()
        {
            // Layout: Kind(1) + Padding0(1) + CurveId(2) + Slope(4) + Exponent(4) + XShift(4) = 16 bytes.
            Assert.Equal(16, sizeof(ResponseCurve));
        }

        [Fact]
        public void InputParams_SizeIs16Bytes()
        {
            // Explicit layout with Size=16.
            Assert.Equal(16, sizeof(InputParams));
        }

        [Fact]
        public void UtilityConsideration_SizeIsDeterministic()
        {
            // Field layout:
            //   InputId  (ushort)        2 bytes
            //   Context  (InputContext)  1 byte
            //   Padding0 (byte)          1 byte
            //   Weight   (float)         4 bytes   <- sub-total header: 8 bytes
            //   Curve    (ResponseCurve) 16 bytes  <- 8 + 16 = 24 bytes
            //   Params   (InputParams)   16 bytes  <- 24 + 16 = 40 bytes
            // Expected sizeof(UtilityConsideration) == 40 bytes.
            int size = sizeof(UtilityConsideration);
            Assert.Equal(40, size);
        }

        // ── Constants ────────────────────────────────────────────────────────────

        [Fact]
        public void UtilityConstants_TopN_Is16()
        {
            Assert.Equal(16, UtilityConstants.TopN);
        }

        [Fact]
        public void CapInvariant_MaxTrackedTargets_LessOrEqualTopN()
        {
            Assert.True(PerceptionConstants.MaxTrackedTargets <= UtilityConstants.TopN,
                $"MaxTrackedTargets={PerceptionConstants.MaxTrackedTargets} exceeds TopN={UtilityConstants.TopN}");
        }

        // ── Enum smoke test ──────────────────────────────────────────────────────

        [Fact]
        public void CurveKind_AllValues_AreDistinctBytes()
        {
            var values = Enum.GetValues<CurveKind>();
            var distinct = values.Select(v => (byte)v).Distinct().ToArray();
            Assert.Equal(values.Length, distinct.Length);
            // Verify PiecewiseLinear is the last value and is accessible
            Assert.Contains(CurveKind.PiecewiseLinear, values);
        }

        [Fact]
        public void ScoringMode_Values_AreDistinct()
        {
            var values = Enum.GetValues<ScoringMode>();
            Assert.Equal(2, values.Length);
            Assert.Contains(ScoringMode.WeightedProduct, values);
            Assert.Contains(ScoringMode.WeightedSum, values);
        }
    }
}
