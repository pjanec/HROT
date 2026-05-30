using System;
using System.Globalization;
using System.IO;
using System.Text;
using Fdp.Toolkit.Utility;
using Hrot.Diagnostics.Tuning;
using Hrot.Diagnostics.Tuning.Gizmos;
using Xunit;

namespace Hrot.Diagnostics.Tuning.Tests
{
    // SC-P6-2: piecewise translate-on-apply tests.
    // Uses TuningRegistry + TuningConsoleGizmo headlessly (no network, no DDS).
    public sealed class PiecewiseTranslateTests : IDisposable
    {
        // CurveKind.PiecewiseLinear is 8 in the CurveKind enum
        // (Linear=0, InverseLinear=1, Threshold=2, Bell=3, Step=4, Logistic=5,
        //  Quadratic=6, InverseQuadratic=7, PiecewiseLinear=8).
        private const int PiecewiseLinearInt = 8;

        public void Dispose()
        {
            PiecewiseCurveCatalog.ClearAll();
        }

        private static UtilityDecisionDef MakeDecision(string name = "TestDec")
        {
            return new UtilityDecisionDef
            {
                DebugName = name,
                Options = new[]
                {
                    new UtilityOption
                    {
                        OptionId = 0,
                        Mode     = ScoringMode.WeightedProduct,
                        Considerations = new[]
                        {
                            new UtilityConsideration(
                                inputId: 1,
                                context: InputContext.Self,
                                weight:  1f,
                                curve:   new ResponseCurve(CurveKind.Linear, 1f, 1f, 0f)),
                        },
                    },
                },
            };
        }

        // Build a complete JSON object with one curve property.
        // Float values use invariant culture so the JSON is always valid regardless of OS locale.
        private static string BuildCurveJson(string keyName, int pointCount = 2)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append('"').Append(keyName).Append('"').Append(':');
            sb.Append("{\"Kind\":").Append(PiecewiseLinearInt)
              .Append(",\"M\":1.0,\"K\":1.0,\"B\":0.0,\"C\":0.0,\"Points\":[");
            for (int i = 0; i < pointCount; i++)
            {
                if (i > 0) sb.Append(',');
                float x = pointCount == 1 ? 0f : i / (float)(pointCount - 1);
                float y = x;
                sb.Append("{\"X\":")
                  .Append(x.ToString("G", CultureInfo.InvariantCulture))
                  .Append(",\"Y\":")
                  .Append(y.ToString("G", CultureInfo.InvariantCulture))
                  .Append('}');
            }
            sb.Append("]}");   // closes Points array and curve object
            sb.Append('}');    // closes outer JSON object
            return sb.ToString();
        }

        // ── Test 1: RegisterCurve + ApplyCurve + BeginFrame invokes Write ───────

        [Fact]
        public void RegisterCurve_ThenBeginFrame_WriteIsInvoked()
        {
            var reg    = new TuningRegistry();
            var key    = new TuningKey("test.curve");
            UtilityCurve written = default;
            bool writeCalled = false;

            reg.RegisterCurve(key, new CurveTunable
            {
                Read  = () => default,
                Write = uc => { written = uc; writeCalled = true; },
            });

            var expected = new UtilityCurve
            {
                Kind   = CurveKind.PiecewiseLinear,
                M      = 1f,
                K      = 1f,
                Points = new[] { new PiecewisePoint(0f, 0f), new PiecewisePoint(1f, 1f) },
            };
            reg.ApplyCurve(key, expected);
            reg.BeginFrame();

            Assert.True(writeCalled);
            Assert.Equal(CurveKind.PiecewiseLinear, written.Kind);
            Assert.Equal(2, written.Points!.Length);
        }

        // ── Test 2: OnStructUpdate with object property applies curve ────────────

        [Fact]
        public void OnStructUpdate_ObjectProperty_CallsApplyCurve()
        {
            var reg   = new TuningRegistry();
            var def   = MakeDecision("TestDec");
            UtilityTuningBinder.RegisterDecision(reg, def);
            var gizmo = new TuningConsoleGizmo(reg);

            string json = BuildCurveJson("utility.TestDec.0.0.curve");
            gizmo.OnStructUpdate(json);
            reg.BeginFrame();

            var consideration = def.Options[0].Considerations[0];
            Assert.Equal(CurveKind.PiecewiseLinear, consideration.Curve.Kind);

            short curveId = consideration.Curve.CurveId;
            float midVal  = PiecewiseCurveCatalog.Evaluate(curveId, 0.5f);
            Assert.Equal(0.5f, midVal, precision: 4);
        }

        // ── Test 3: Points clamped to MaxPiecewisePoints, warning emitted ────────

        [Fact]
        public void OnStructUpdate_PointsClamped_EmitsWarning()
        {
            var reg   = new TuningRegistry();
            var def   = MakeDecision("ClampDec");
            UtilityTuningBinder.RegisterDecision(reg, def);
            var gizmo = new TuningConsoleGizmo(reg);

            int overLimit = TuningRegistry.MaxPiecewisePoints + 5;
            string json   = BuildCurveJson("utility.ClampDec.0.0.curve", overLimit);

            var errorWriter = new StringWriter();
            var prev        = Console.Error;
            Console.SetError(errorWriter);
            try
            {
                gizmo.OnStructUpdate(json);
            }
            finally
            {
                Console.SetError(prev);
            }

            string warning = errorWriter.ToString();
            Assert.Contains("clamped", warning);

            reg.BeginFrame();

            var consideration = def.Options[0].Considerations[0];
            Assert.Equal(CurveKind.PiecewiseLinear, consideration.Curve.Kind);

            short curveId = consideration.Curve.CurveId;
            // GetPoints is internal; Hrot.Diagnostics.Tuning.Tests has InternalsVisibleTo in Fdp.Toolkits.
            var pts = PiecewiseCurveCatalog.GetPoints(curveId);
            Assert.NotNull(pts);
            Assert.True(pts!.Length <= TuningRegistry.MaxPiecewisePoints,
                $"Expected at most {TuningRegistry.MaxPiecewisePoints} points but got {pts.Length}");
        }

        // ── Test 4: Non-curve object property is ignored gracefully ─────────────

        [Fact]
        public void OnStructUpdate_NonCurveObject_IsIgnoredGracefully()
        {
            var gizmo = new TuningConsoleGizmo(new TuningRegistry());
            // Must not throw even when the object has no recognisable curve fields.
            gizmo.OnStructUpdate("{\"some.key\":{\"Foo\":1}}");
        }

        // ── Test 5: Float and curve in same batch, both applied ──────────────────

        [Fact]
        public void OnStructUpdate_FloatAndCurveInSameBatch_BothApplied()
        {
            var reg   = new TuningRegistry();
            var def   = MakeDecision("MixDec");
            UtilityTuningBinder.RegisterDecision(reg, def);
            var gizmo = new TuningConsoleGizmo(reg);

            // Build JSON with both a float field and a curve object field.
            // Merge the two into one JSON object manually to keep both properties.
            string curveJson = BuildCurveJson("utility.MixDec.0.0.curve");
            // curveJson = {"utility.MixDec.0.0.curve":{...}}
            // Insert the weight property before the curve property.
            string json = curveJson.Substring(0, 1)
                + "\"utility.MixDec.0.0.weight\":2.5,"
                + curveJson.Substring(1);

            gizmo.OnStructUpdate(json);
            reg.BeginFrame();

            Assert.Equal(2.5f, def.Options[0].Considerations[0].Weight, precision: 4);
            Assert.Equal(CurveKind.PiecewiseLinear, def.Options[0].Considerations[0].Curve.Kind);
        }

        // ── Test 6: Kind as integer is parsed correctly ──────────────────────────

        [Fact]
        public void DeserializeUtilityCurve_KindAsInteger_IsHandled()
        {
            var reg   = new TuningRegistry();
            var def   = MakeDecision("IntKindDec");
            UtilityTuningBinder.RegisterDecision(reg, def);
            var gizmo = new TuningConsoleGizmo(reg);

            // Kind=8 as integer (not string) must parse to PiecewiseLinear.
            string json = BuildCurveJson("utility.IntKindDec.0.0.curve");

            gizmo.OnStructUpdate(json);
            reg.BeginFrame();

            Assert.Equal(CurveKind.PiecewiseLinear,
                def.Options[0].Considerations[0].Curve.Kind);
        }
    }
}
