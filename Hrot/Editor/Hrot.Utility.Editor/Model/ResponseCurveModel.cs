using System.Collections.Generic;
using Fdp.Toolkit.Utility;

namespace Hrot.Utility.Editor.Model;

// Editor-side mutable representation of a response curve.
// Maps directly to the runtime ResponseCurve + PiecewiseCurveCatalog side-table.
public sealed class ResponseCurveModel
{
    public CurveKind Kind  = CurveKind.Linear;
    // m / slope
    public float M  = 1f;
    // k / exponent
    public float K  = 1f;
    // b / horizontal shift
    public float B  = 0f;
    // c / vertical shift
    public float C  = 0f;
    // Points used when Kind == PiecewiseLinear; null or empty for all other kinds.
    public List<(float x, float y)>? Points;

    // Converts this model to a runtime ResponseCurve (no side-table registration).
    // Call UtilityCurveConverter.ToRuntime for full conversion including piecewise side-table.
    public ResponseCurve ToRuntime()
        => new ResponseCurve(Kind, M, K, B);
}
