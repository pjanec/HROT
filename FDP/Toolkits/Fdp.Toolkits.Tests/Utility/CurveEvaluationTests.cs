using Fdp.Toolkit.Utility;
using Xunit;

namespace Fdp.Toolkit.Tests.Utility
{
    /// <summary>
    /// Unit tests for ResponseCurve.Evaluate and PiecewiseCurveCatalog (TASK-UAI-P1-02).
    /// </summary>
    public class CurveEvaluationTests : IDisposable
    {
        // Use isolated curve IDs per test class to avoid cross-test catalog pollution
        private const short PiecewiseCurveId = 42;

        public CurveEvaluationTests()
        {
            // Register the reference 3-point curve used in piecewise tests
            PiecewiseCurveCatalog.Register(PiecewiseCurveId,
                [(0f, 0f), (0.5f, 0.8f), (1f, 1f)]);
        }

        public void Dispose()
        {
            PiecewiseCurveCatalog.ClearAll();
        }

        // ── Linear ──────────────────────────────────────────────────────────────

        [Fact]
        public void Linear_Baseline_ThreePoints()
        {
            // Slope=1, Exponent=1, XShift=0 → output = x
            var c = new ResponseCurve(CurveKind.Linear, slope: 1f, exponent: 1f, xShift: 0f);
            Assert.Equal(0f,   c.Evaluate(0f),   precision: 5);
            Assert.Equal(0.5f, c.Evaluate(0.5f), precision: 5);
            Assert.Equal(1f,   c.Evaluate(1f),   precision: 5);
        }

        // ── InverseLinear ────────────────────────────────────────────────────────

        [Fact]
        public void InverseLinear_Baseline_ThreePoints()
        {
            // Slope=1 → output = 1 - x
            var c = new ResponseCurve(CurveKind.InverseLinear, slope: 1f, exponent: 1f, xShift: 0f);
            Assert.Equal(1f,   c.Evaluate(0f),   precision: 5);
            Assert.Equal(0.5f, c.Evaluate(0.5f), precision: 5);
            Assert.Equal(0f,   c.Evaluate(1f),   precision: 5);
        }

        // ── Threshold ────────────────────────────────────────────────────────────

        [Fact]
        public void Threshold_BelowThreshold_ReturnsZero()
        {
            var c = new ResponseCurve(CurveKind.Threshold, slope: 1f, exponent: 1f, xShift: 0.5f);
            Assert.Equal(0f, c.Evaluate(0.49f), precision: 5);
        }

        [Fact]
        public void Threshold_AtAndAboveThreshold_ReturnsOne()
        {
            var c = new ResponseCurve(CurveKind.Threshold, slope: 1f, exponent: 1f, xShift: 0.5f);
            Assert.Equal(1f, c.Evaluate(0.5f), precision: 5);
            Assert.Equal(1f, c.Evaluate(1.0f), precision: 5);
        }

        // SC-P1-02-3: exact boundary check
        [Fact]
        public void Threshold_JustBelowThreshold_IsZero_AtThreshold_IsOne()
        {
            var c = new ResponseCurve(CurveKind.Threshold, slope: 1f, exponent: 1f, xShift: 0.5f);
            Assert.Equal(0f, c.Evaluate(0.499f));
            Assert.True(c.Evaluate(0.5f) >= 0.95f);
        }

        // ── Bell ─────────────────────────────────────────────────────────────────

        [Fact]
        public void Bell_Peak_IsNearOne()
        {
            // Gaussian at x=b: Slope * exp(0) = 1
            var c = new ResponseCurve(CurveKind.Bell, slope: 1f, exponent: 10f, xShift: 0.5f);
            Assert.True(c.Evaluate(0.5f) > 0.99f, "Bell peak at b should be ~1");
        }

        [Fact]
        public void Bell_FarFromPeak_IsNearZero()
        {
            // exp(-10 * (0.0 - 0.5)^2) = exp(-2.5) ≈ 0.082
            var c = new ResponseCurve(CurveKind.Bell, slope: 1f, exponent: 10f, xShift: 0.5f);
            Assert.True(c.Evaluate(0.0f) < 0.1f, "Bell far from peak should be near 0");
        }

        // ── Step ─────────────────────────────────────────────────────────────────

        [Fact]
        public void Step_BelowThreshold_ReturnsZero()
        {
            var c = new ResponseCurve(CurveKind.Step, slope: 1f, exponent: 1f, xShift: 0.5f);
            Assert.Equal(0f, c.Evaluate(0.49f));
        }

        // SC-P1-02-3: Step threshold behaviour mirrors Threshold
        [Fact]
        public void Step_AtThreshold_IsAtLeastPointNineFive()
        {
            var c = new ResponseCurve(CurveKind.Step, slope: 1f, exponent: 1f, xShift: 0.5f);
            Assert.True(c.Evaluate(0.5f) >= 0.95f);
        }

        // ── Logistic ─────────────────────────────────────────────────────────────

        [Fact]
        public void Logistic_AtInflectionPoint_IsHalf()
        {
            // 1/(1+exp(-10*(0.5-0.5)))*1 = 0.5
            var c = new ResponseCurve(CurveKind.Logistic, slope: 1f, exponent: 10f, xShift: 0.5f);
            Assert.Equal(0.5f, c.Evaluate(0.5f), precision: 4);
        }

        [Fact]
        public void Logistic_HighX_IsNearOne()
        {
            // 1/(1+exp(-10*(0.9-0.5)))*1 = 1/(1+exp(-4)) ≈ 0.982
            var c = new ResponseCurve(CurveKind.Logistic, slope: 1f, exponent: 10f, xShift: 0.5f);
            Assert.True(c.Evaluate(0.9f) > 0.98f);
        }

        [Fact]
        public void Logistic_LowX_IsNearZero()
        {
            // 1/(1+exp(-10*(0.1-0.5)))*1 = 1/(1+exp(4)) ≈ 0.018
            var c = new ResponseCurve(CurveKind.Logistic, slope: 1f, exponent: 10f, xShift: 0.5f);
            Assert.True(c.Evaluate(0.1f) < 0.02f);
        }

        // ── Quadratic ────────────────────────────────────────────────────────────

        [Fact]
        public void Quadratic_Baseline_TwoPoints()
        {
            // m*(x-b)^2 with m=1, b=0
            var c = new ResponseCurve(CurveKind.Quadratic, slope: 1f, exponent: 2f, xShift: 0f);
            Assert.Equal(0.25f, c.Evaluate(0.5f), precision: 5);
            Assert.Equal(1.0f,  c.Evaluate(1.0f), precision: 5);
        }

        // ── InverseQuadratic ─────────────────────────────────────────────────────

        [Fact]
        public void InverseQuadratic_Baseline_ThreePoints()
        {
            // 1 - m*(x-b)^2 with m=1, b=0
            var c = new ResponseCurve(CurveKind.InverseQuadratic, slope: 1f, exponent: 2f, xShift: 0f);
            Assert.Equal(1f,    c.Evaluate(0f),   precision: 5);
            Assert.Equal(0.75f, c.Evaluate(0.5f), precision: 5);
            Assert.Equal(0f,    c.Evaluate(1f),   precision: 5);
        }

        // ── PiecewiseLinear ──────────────────────────────────────────────────────

        [Fact]
        public void PiecewiseLinear_ExactControlPoints_Match()
        {
            // Registered: (0,0), (0.5,0.8), (1,1)
            var c = new ResponseCurve(CurveKind.PiecewiseLinear, curveId: PiecewiseCurveId);
            Assert.Equal(0f,   c.Evaluate(0f),   precision: 5);
            Assert.Equal(0.8f, c.Evaluate(0.5f), precision: 5);
            Assert.Equal(1f,   c.Evaluate(1f),   precision: 5);
        }

        [Fact]
        public void PiecewiseLinear_MidSegment_Lerps()
        {
            // At x=0.25: lerp between (0,0) and (0.5,0.8) → t=0.5 → 0 + 0.5*0.8 = 0.4
            var c = new ResponseCurve(CurveKind.PiecewiseLinear, curveId: PiecewiseCurveId);
            Assert.Equal(0.4f, c.Evaluate(0.25f), precision: 5);
        }

        [Fact]
        public void PiecewiseLinear_BelowFirstPoint_ClampsToFirst()
        {
            var c = new ResponseCurve(CurveKind.PiecewiseLinear, curveId: PiecewiseCurveId);
            Assert.Equal(0f, c.Evaluate(-1f), precision: 5);
        }

        [Fact]
        public void PiecewiseLinear_AboveLastPoint_ClampsToLast()
        {
            var c = new ResponseCurve(CurveKind.PiecewiseLinear, curveId: PiecewiseCurveId);
            Assert.Equal(1f, c.Evaluate(2f), precision: 5);
        }

        // SC-P1-02-4: PiecewiseLinear monotonic check
        [Fact]
        public void PiecewiseLinear_FiveReferenceValues_AreMonotonic()
        {
            var c = new ResponseCurve(CurveKind.PiecewiseLinear, curveId: PiecewiseCurveId);
            float v0   = c.Evaluate(0.0f);
            float v025 = c.Evaluate(0.25f);
            float v05  = c.Evaluate(0.5f);
            float v075 = c.Evaluate(0.75f);
            float v1   = c.Evaluate(1.0f);
            Assert.True(v0 <= v025 && v025 <= v05 && v05 <= v075 && v075 <= v1,
                $"Not monotonic: {v0} {v025} {v05} {v075} {v1}");
        }

        // SC-P1-02-2: Property test — all curve outputs in [0,1] for 100 inputs in [0,1]
        [Theory]
        [InlineData(CurveKind.Linear)]
        [InlineData(CurveKind.InverseLinear)]
        [InlineData(CurveKind.Threshold)]
        [InlineData(CurveKind.Bell)]
        [InlineData(CurveKind.Step)]
        [InlineData(CurveKind.Logistic)]
        [InlineData(CurveKind.Quadratic)]
        [InlineData(CurveKind.InverseQuadratic)]
        public void AllCurveKinds_OutputInRange_For100Inputs(CurveKind kind)
        {
            // Use parameters that are representative for each kind
            var c = kind switch
            {
                CurveKind.Bell     => new ResponseCurve(kind, slope: 1f, exponent: 10f, xShift: 0.5f),
                CurveKind.Logistic => new ResponseCurve(kind, slope: 1f, exponent: 10f, xShift: 0.5f),
                _                  => new ResponseCurve(kind, slope: 1f, exponent: 1f,  xShift: 0f)
            };

            for (int i = 0; i <= 100; i++)
            {
                float x = i / 100f;
                float y = c.Evaluate(x);
                Assert.True(y >= 0f && y <= 1f,
                    $"CurveKind.{kind} at x={x:F2} returned {y} — outside [0,1]");
            }
        }
    }
}
