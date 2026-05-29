using System;
using System.Collections.Generic;
using Fdp.Toolkit.Utility;
using Hrot.Utility.Editor.Curve;
using Xunit;

namespace Hrot.Utility.Editor.Tests
{
    // Covers CurveWidget internal helpers and UtilityCurve model - no ImGui frame required.

    public class UtilityCurveTests
    {
        // ── FromResponseCurve / ToResponseCurve roundtrip (non-piecewise) ───────

        [Fact]
        public void FromResponseCurve_Linear_PreservesFields()
        {
            var rc = new ResponseCurve(CurveKind.Linear, slope: 0.75f, exponent: 1f, xShift: 0f);
            var uc = UtilityCurve.FromResponseCurve(rc);

            Assert.Equal(CurveKind.Linear, uc.Kind);
            Assert.Equal(0.75f, uc.M);
            Assert.Equal(1f,    uc.K);
            Assert.Equal(0f,    uc.B);
            Assert.Equal(0f,    uc.C); // C always zero after round-trip
            Assert.Null(uc.Points);
        }

        [Fact]
        public void FromResponseCurve_Bell_PreservesFields()
        {
            var rc = new ResponseCurve(CurveKind.Bell, slope: 0.9f, exponent: 3f, xShift: 0.4f);
            var uc = UtilityCurve.FromResponseCurve(rc);

            Assert.Equal(CurveKind.Bell, uc.Kind);
            Assert.Equal(0.9f, uc.M);
            Assert.Equal(3f,   uc.K);
            Assert.Equal(0.4f, uc.B);
            Assert.Null(uc.Points);
        }

        [Fact]
        public void ToResponseCurve_PreservesKindAndMKB()
        {
            var uc = new UtilityCurve
            {
                Kind = CurveKind.Quadratic,
                M    = 1f,
                K    = 2f,
                B    = 0.1f,
                C    = 0.3f, // C is discarded
            };
            var rc = uc.ToResponseCurve();

            Assert.Equal(CurveKind.Quadratic, rc.Kind);
            Assert.Equal(1f,   rc.Slope);
            Assert.Equal(2f,   rc.Exponent);
            Assert.Equal(0.1f, rc.XShift);
        }

        [Fact]
        public void PiecewiseLinear_ToResponseCurve_RegistersAndRoundTrips()
        {
            PiecewiseCurveCatalog.ClearAll();
            var uc = new UtilityCurve
            {
                Kind   = CurveKind.PiecewiseLinear,
                Points = new[]
                {
                    new PiecewisePoint(0f, 0f),
                    new PiecewisePoint(0.5f, 1f),
                    new PiecewisePoint(1f, 0f),
                },
            };

            var rc  = uc.ToResponseCurve();
            var uc2 = UtilityCurve.FromResponseCurve(rc);

            Assert.Equal(CurveKind.PiecewiseLinear, uc2.Kind);
            Assert.NotNull(uc2.Points);
            Assert.Equal(3, uc2.Points!.Length);
            Assert.Equal(0f,   uc2.Points[0].X, precision: 5);
            Assert.Equal(0.5f, uc2.Points[1].X, precision: 5);
            Assert.Equal(1f,   uc2.Points[2].X, precision: 5);
        }
    }

    public class CurveWidgetEvaluateTests
    {
        // ── Evaluate - non-piecewise kinds ───────────────────────────────────────

        [Fact]
        public void Evaluate_Linear_M1_C0_ReturnsX()
        {
            var curve = new UtilityCurve { Kind = CurveKind.Linear, M = 1f, K = 1f, B = 0f, C = 0f };
            float result = CurveWidget.Evaluate(in curve, 0.5f);
            Assert.Equal(0.5f, result, precision: 5);
        }

        [Fact]
        public void Evaluate_Linear_WithC_AddsYShift()
        {
            // M=1, B=0, C=0.1f at x=0.5f -> raw=0.5f+0.1f=0.6f
            var curve = new UtilityCurve { Kind = CurveKind.Linear, M = 1f, K = 1f, B = 0f, C = 0.1f };
            float result = CurveWidget.Evaluate(in curve, 0.5f);
            Assert.Equal(0.6f, result, precision: 5);
        }

        [Fact]
        public void Evaluate_Linear_ClampedAtOne()
        {
            // M=1, C=0.8f, x=0.5f -> raw=0.5+0.8=1.3 -> clamped to 1.0
            var curve = new UtilityCurve { Kind = CurveKind.Linear, M = 1f, K = 1f, B = 0f, C = 0.8f };
            float result = CurveWidget.Evaluate(in curve, 0.5f);
            Assert.Equal(1f, result, precision: 5);
        }

        [Fact]
        public void Evaluate_Linear_ClampedAtZero()
        {
            // M=1, C=-0.9f, x=0.1f -> raw=0.1-0.9=-0.8 -> clamped to 0.0
            var curve = new UtilityCurve { Kind = CurveKind.Linear, M = 1f, K = 1f, B = 0f, C = -0.9f };
            float result = CurveWidget.Evaluate(in curve, 0.1f);
            Assert.Equal(0f, result, precision: 5);
        }

        // ── Evaluate - PiecewiseLinear interpolation ─────────────────────────────

        [Fact]
        public void Evaluate_PiecewiseLinear_InterpolatesCorrectly()
        {
            // Triangle shape: (0,0) -> (0.5,1) -> (1,0)
            // At x=0.25 (midpoint of first segment) -> y=0.5
            var curve = new UtilityCurve
            {
                Kind   = CurveKind.PiecewiseLinear,
                Points = new[]
                {
                    new PiecewisePoint(0f, 0f),
                    new PiecewisePoint(0.5f, 1f),
                    new PiecewisePoint(1f, 0f),
                },
            };
            float result = CurveWidget.Evaluate(in curve, 0.25f);
            Assert.Equal(0.5f, result, precision: 5);
        }

        [Fact]
        public void Evaluate_PiecewiseLinear_BeyondUpperEndpoint_ReturnsLastY()
        {
            var curve = new UtilityCurve
            {
                Kind   = CurveKind.PiecewiseLinear,
                Points = new[] { new PiecewisePoint(0f, 0f), new PiecewisePoint(1f, 1f) },
            };
            Assert.Equal(1f, CurveWidget.Evaluate(in curve, 1.5f), precision: 5);
        }

        [Fact]
        public void Evaluate_PiecewiseLinear_NullPoints_ReturnsZero()
        {
            var curve = new UtilityCurve { Kind = CurveKind.PiecewiseLinear, Points = null };
            Assert.Equal(0f, CurveWidget.Evaluate(in curve, 0.5f), precision: 5);
        }

        // ── IsParamEditable ──────────────────────────────────────────────────────

        [Theory]
        [InlineData(CurveKind.Linear,          "m", true)]
        [InlineData(CurveKind.Linear,          "k", false)]
        [InlineData(CurveKind.Linear,          "b", false)]
        [InlineData(CurveKind.Linear,          "c", true)]
        [InlineData(CurveKind.InverseLinear,   "m", true)]
        [InlineData(CurveKind.InverseLinear,   "b", false)]
        [InlineData(CurveKind.InverseLinear,   "c", true)]
        [InlineData(CurveKind.Bell,            "m", false)]
        [InlineData(CurveKind.Bell,            "k", true)]
        [InlineData(CurveKind.Bell,            "b", true)]
        [InlineData(CurveKind.Bell,            "c", true)]
        [InlineData(CurveKind.Threshold,       "b", true)]
        [InlineData(CurveKind.Threshold,       "c", true)]
        [InlineData(CurveKind.Threshold,       "m", false)]
        [InlineData(CurveKind.Threshold,       "k", false)]
        [InlineData(CurveKind.Step,            "c", true)]
        [InlineData(CurveKind.Logistic,        "k", true)]
        [InlineData(CurveKind.Logistic,        "c", false)]
        [InlineData(CurveKind.PiecewiseLinear, "m", true)]
        [InlineData(CurveKind.PiecewiseLinear, "k", true)]
        [InlineData(CurveKind.PiecewiseLinear, "b", true)]
        [InlineData(CurveKind.PiecewiseLinear, "c", true)]
        public void IsParamEditable_ReturnsCorrectValue(CurveKind kind, string param, bool expected)
        {
            Assert.Equal(expected, CurveWidget.IsParamEditable(kind, param));
        }

        // ── AddPiecewisePoint / RemovePiecewisePoint ─────────────────────────────

        [Fact]
        public void AddPiecewisePoint_XSortsResult()
        {
            var pts = CurveWidget.AddPiecewisePoint(null, 0.7f, 0.5f);
            pts = CurveWidget.AddPiecewisePoint(pts, 0.3f, 0.2f);
            pts = CurveWidget.AddPiecewisePoint(pts, 0.5f, 0.9f);

            Assert.Equal(3, pts.Length);
            Assert.Equal(0.3f, pts[0].X, precision: 5);
            Assert.Equal(0.5f, pts[1].X, precision: 5);
            Assert.Equal(0.7f, pts[2].X, precision: 5);
        }

        [Fact]
        public void AddPiecewisePoint_ClampsCoords()
        {
            var pts = CurveWidget.AddPiecewisePoint(null, -0.5f, 1.5f);
            Assert.Equal(0f, pts[0].X, precision: 5);
            Assert.Equal(1f, pts[0].Y, precision: 5);
        }

        [Fact]
        public void RemovePiecewisePoint_RemovesCorrectIndex()
        {
            var pts = new[]
            {
                new PiecewisePoint(0f, 0f),
                new PiecewisePoint(0.5f, 1f),
                new PiecewisePoint(1f, 0f),
            };
            var result = CurveWidget.RemovePiecewisePoint(pts, 1);
            Assert.Equal(2, result.Length);
            Assert.Equal(0f, result[0].X, precision: 5);
            Assert.Equal(1f, result[1].X, precision: 5);
        }

        // ── ComputeSamples ───────────────────────────────────────────────────────

        [Fact]
        public void ComputeSamples_IdentityLinear_IsLinearlyIncreasing()
        {
            var curve = new UtilityCurve { Kind = CurveKind.Linear, M = 1f, K = 1f, B = 0f, C = 0f };
            Span<float> samples = stackalloc float[5];
            CurveWidget.ComputeSamples(in curve, 5, samples);

            // x = 0, 0.25, 0.5, 0.75, 1.0  -> expect same y values
            Assert.Equal(0f,    samples[0], precision: 4);
            Assert.Equal(0.25f, samples[1], precision: 4);
            Assert.Equal(0.5f,  samples[2], precision: 4);
            Assert.Equal(0.75f, samples[3], precision: 4);
            Assert.Equal(1f,    samples[4], precision: 4);
        }

        // ── Cross-check: CurveWidget.Evaluate vs ResponseCurve.Evaluate ─────────

        public static IEnumerable<object[]> SixteenSamples =>
            Enumerable.Range(0, 16).Select(i => new object[] { i / 15f });

        [Theory]
        [MemberData(nameof(SixteenSamples))]
        public void Evaluate_Linear_MatchesResponseCurve(float x)
        {
            var rc = new ResponseCurve(CurveKind.Linear, slope: 0.8f, exponent: 1f, xShift: 0.1f);
            var uc = new UtilityCurve { Kind = CurveKind.Linear, M = 0.8f, K = 1f, B = 0.1f, C = 0f };
            // CurveWidget.Evaluate delegates to ResponseCurve.Evaluate then clamps; C=0 so result is identical.
            float expected = Math.Clamp(rc.Evaluate(x), 0f, 1f);
            Assert.Equal(expected, CurveWidget.Evaluate(in uc, x), precision: 5);
        }

        [Theory]
        [MemberData(nameof(SixteenSamples))]
        public void Evaluate_Logistic_MatchesResponseCurve(float x)
        {
            var rc = new ResponseCurve(CurveKind.Logistic, slope: 1f, exponent: 6f, xShift: 0.5f);
            var uc = new UtilityCurve { Kind = CurveKind.Logistic, M = 1f, K = 6f, B = 0.5f, C = 0f };
            float expected = Math.Clamp(rc.Evaluate(x), 0f, 1f);
            Assert.Equal(expected, CurveWidget.Evaluate(in uc, x), precision: 5);
        }
    }
}
