namespace Fdp.Toolkit.Utility
{
    // Editor-side curve model - all four m/k/b/c params plus optional piecewise points.
    // The runtime ResponseCurve (UtilityCore.cs) is the blittable subset used at tick time.
    public struct UtilityCurve
    {
        public CurveKind Kind;
        public float M;   // slope (m)
        public float K;   // exponent (k)
        public float B;   // x-shift (b)
        public float C;   // y-shift (c)

        // Null unless Kind == PiecewiseLinear.
        // Must stay x-sorted at all times (enforced by CurveWidget).
        public PiecewisePoint[]? Points;

        // Convenience factory from the runtime struct (C defaults to 0 - runtime has no YShift).
        // For PiecewiseLinear reads the existing control points from PiecewiseCurveCatalog via CurveId.
        public static UtilityCurve FromResponseCurve(ResponseCurve rc)
        {
            var uc = new UtilityCurve
            {
                Kind = rc.Kind,
                M    = rc.Slope,
                K    = rc.Exponent,
                B    = rc.XShift,
                C    = 0f,
            };

            if (rc.Kind == CurveKind.PiecewiseLinear && rc.CurveId != 0)
            {
                var raw = PiecewiseCurveCatalog.GetPoints(rc.CurveId);
                if (raw != null)
                {
                    uc.Points = new PiecewisePoint[raw.Length];
                    for (int i = 0; i < raw.Length; i++)
                        uc.Points[i] = new PiecewisePoint(raw[i].x, raw[i].y);
                }
            }

            return uc;
        }

        // Convert to the runtime struct.
        // For PiecewiseLinear, registers the points in PiecewiseCurveCatalog (reusing an existing
        // registration when the content hash matches) and returns the ResponseCurve with the
        // resulting CurveId. C is discarded (runtime has no YShift).
        public ResponseCurve ToResponseCurve()
        {
            if (Kind == CurveKind.PiecewiseLinear)
            {
                var pts = Points ?? Array.Empty<PiecewisePoint>();
                var raw = new (float x, float y)[pts.Length];
                for (int i = 0; i < pts.Length; i++)
                    raw[i] = (pts[i].X, pts[i].Y);

                // Derive a stable short ID from point content hash (non-zero).
                int h    = ComputePointsHash(raw);
                short id = (short)(h & 0x7FFF);
                if (id == 0) id = 1; // 0 is the "no curve" sentinel in ResponseCurve

                if (raw.Length >= 2)
                    PiecewiseCurveCatalog.Register(id, raw);

                return new ResponseCurve(Kind, slope: M, exponent: K, xShift: B, curveId: id);
            }

            return new ResponseCurve(Kind, slope: M, exponent: K, xShift: B);
        }

        private static int ComputePointsHash((float x, float y)[] pts)
        {
            var hash = new HashCode();
            foreach (var p in pts)
            {
                hash.Add(p.x);
                hash.Add(p.y);
            }
            return hash.ToHashCode();
        }
    }

    // Immutable control-point for PiecewiseLinear curves.
    public readonly struct PiecewisePoint
    {
        public readonly float X; // in [0, 1]
        public readonly float Y; // in [0, 1]
        public PiecewisePoint(float x, float y) { X = x; Y = y; }
    }
}
