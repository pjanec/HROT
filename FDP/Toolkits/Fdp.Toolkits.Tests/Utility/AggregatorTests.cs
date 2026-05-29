using Fdp.Toolkit.Utility;
using Xunit;

namespace Fdp.Toolkit.Tests.Utility
{
    /// <summary>
    /// Unit tests for Aggregator.Aggregate (TASK-UAI-P1-03).
    /// All tests assert exact numeric values — not just "no exception" or "result > 0".
    /// </summary>
    public class AggregatorTests
    {
        // ── SC-P1-03-1: Single consideration ────────────────────────────────────

        [Fact]
        public void Product_SingleConsideration_Curve0p5_Weight1_Returns0p5()
        {
            // n=1 → modFactor=0 → makeUp=0 → finalScore=rawProduct=0.5^1=0.5
            float result = Aggregator.Aggregate([0.5f], [1f], ScoringMode.WeightedProduct);
            Assert.Equal(0.5f, result, precision: 5);
        }

        [Fact]
        public void Sum_SingleConsideration_Curve0p5_Weight1_Returns0p5()
        {
            // (1*0.5) / 1 = 0.5
            float result = Aggregator.Aggregate([0.5f], [1f], ScoringMode.WeightedSum);
            Assert.Equal(0.5f, result, precision: 5);
        }

        // ── SC-P1-03-2: Two product considerations ───────────────────────────────

        [Fact]
        public void Product_TwoConsiderations_0p5_0p5_Weight1_1_Returns0p34375()
        {
            // rawProduct = 0.5^1 * 0.5^1 = 0.25
            // modFactor  = 1 - 1/2 = 0.5
            // makeUp     = (1 - 0.25) * 0.5 = 0.375
            // finalScore = 0.25 + 0.375 * 0.25 = 0.34375
            float result = Aggregator.Aggregate([0.5f, 0.5f], [1f, 1f], ScoringMode.WeightedProduct);
            Assert.Equal(0.34375f, result, precision: 5);
        }

        // ── SC-P1-03-3: Three sum considerations ─────────────────────────────────

        [Fact]
        public void Sum_ThreeConsiderations_Returns0p35()
        {
            // (1*0.6 + 2*0.4 + 1*0.0) / (1+2+1) = (0.6 + 0.8 + 0.0) / 4 = 1.4/4 = 0.35
            float result = Aggregator.Aggregate([0.6f, 0.4f, 0.0f], [1f, 2f, 1f], ScoringMode.WeightedSum);
            Assert.Equal(0.35f, result, precision: 5);
        }

        // ── SC-P1-03-4: Hard-gate — zero term collapses product ──────────────────

        [Fact]
        public void Product_ZeroTerm_FinalScoreIsZero()
        {
            // rawProduct = 0.8^1 * 0^1 = 0
            // compensation is applied to 0: finalScore = 0 + (1-0)*(1-1/2)*0 = 0
            float result = Aggregator.Aggregate([0.8f, 0f], [1f, 1f], ScoringMode.WeightedProduct);
            Assert.Equal(0f, result, precision: 5);
        }

        // ── Edge cases ───────────────────────────────────────────────────────────

        [Fact]
        public void Product_EmptySpan_ReturnsZero()
        {
            float result = Aggregator.Aggregate(ReadOnlySpan<float>.Empty, ReadOnlySpan<float>.Empty, ScoringMode.WeightedProduct);
            Assert.Equal(0f, result);
        }

        [Fact]
        public void Sum_AllZeroWeights_ReturnsZero_NotNaN()
        {
            // denominator=0 → return 0, not NaN
            float result = Aggregator.Aggregate([0.5f, 0.8f], [0f, 0f], ScoringMode.WeightedSum);
            Assert.Equal(0f, result);
            Assert.False(float.IsNaN(result));
        }

        [Fact]
        public void Product_SingleHighWeightConsideration_Curve0p9_Weight2_Returns0p81()
        {
            // n=1 → modFactor=0 → finalScore = rawProduct = 0.9^2 = 0.81
            float result = Aggregator.Aggregate([0.9f], [2f], ScoringMode.WeightedProduct);
            Assert.Equal(0.81f, result, precision: 5);
        }
    }
}
