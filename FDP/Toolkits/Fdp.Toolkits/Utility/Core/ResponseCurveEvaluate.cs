namespace Fdp.Toolkit.Utility
{
    public readonly partial struct ResponseCurve
    {
        // This partial half adds the Evaluate(float) method.
        // The struct definition (fields, constructor) lives in UtilityCore.cs.
        // YShift (c) is not a field; it is implicitly 0 for all Phase-1 curves.

        /// <summary>
        /// Evaluate the response curve at <paramref name="x"/> (expected in [0,1]).
        /// Returns a value clamped to [0,1].
        /// </summary>
        public float Evaluate(float x)
        {
            float result;
            switch (Kind)
            {
                case CurveKind.Linear:
                    // output = m * (x - b)
                    result = Slope * (x - XShift);
                    break;

                case CurveKind.InverseLinear:
                    // output = 1 - m * (x - b)
                    result = 1f - Slope * (x - XShift);
                    break;

                case CurveKind.Threshold:
                    // 0 below XShift, 1 at or above (Slope/Exponent ignored)
                    result = x >= XShift ? 1f : 0f;
                    break;

                case CurveKind.Bell:
                    // Gaussian bell: m * exp(-k * (x - b)^2)
                    float bellDx = x - XShift;
                    result = Slope * MathF.Exp(-Exponent * bellDx * bellDx);
                    break;

                case CurveKind.Step:
                    // Like Threshold but uses Slope as the above-threshold output (default 1)
                    result = x >= XShift ? (Slope > 0f ? Slope : 1f) : 0f;
                    break;

                case CurveKind.Logistic:
                    // Sigmoid: 1 / (1 + exp(-k * (x - b))) * m
                    result = 1f / (1f + MathF.Exp(-Exponent * (x - XShift))) * Slope;
                    break;

                case CurveKind.Quadratic:
                    // m * (x - b)^2
                    // Note: the Exponent field is ignored; the curve always applies a fixed x^2 power.
                    // To use a general power curve, MathF.Pow(x, Exponent) would be required,
                    // but that variant is not implemented in Phase 1.
                    float qDx = x - XShift;
                    result = Slope * (qDx * qDx);
                    break;

                case CurveKind.InverseQuadratic:
                    // 1 - m * (x - b)^2
                    // Note: the Exponent field is ignored; the curve always applies a fixed x^2 power.
                    // To use a general power curve, MathF.Pow(x, Exponent) would be required,
                    // but that variant is not implemented in Phase 1.
                    float iqDx = x - XShift;
                    result = 1f - Slope * (iqDx * iqDx);
                    break;

                case CurveKind.PiecewiseLinear:
                    result = PiecewiseCurveCatalog.Evaluate(CurveId, x);
                    break;

                default:
                    // Passthrough fallback — map x through unchanged
                    result = x;
                    break;
            }
            return Math.Clamp(result, 0f, 1f);
        }
    }
}
