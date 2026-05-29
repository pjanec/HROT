using System.Collections.Generic;

namespace Fdp.Toolkit.Utility
{
    /// <summary>
    /// Thread-safe static side-table for PiecewiseLinear response curve control points.
    /// Keyed by <see cref="ResponseCurve.CurveId"/>. Points must be sorted by X ascending.
    /// Registration is a startup-time operation; <c>Evaluate</c> is called per-tick.
    /// </summary>
    public static class PiecewiseCurveCatalog
    {
        private static readonly Dictionary<short, (float x, float y)[]> _table = new();

        /// <summary>
        /// Register control points for a PiecewiseLinear curve.
        /// Replaces any previously registered points for the same <paramref name="curveId"/>.
        /// </summary>
        /// <param name="curveId">Key matching <see cref="ResponseCurve.CurveId"/>.</param>
        /// <param name="points">Control points sorted by X ascending. Must contain at least 2 points.</param>
        /// <exception cref="ArgumentException">Thrown when fewer than 2 points are provided.</exception>
        public static void Register(short curveId, (float x, float y)[] points)
        {
            if (points == null || points.Length < 2)
                throw new ArgumentException("PiecewiseLinear curve requires at least 2 control points.", nameof(points));
            lock (_table)
                _table[curveId] = points;
        }

        /// <summary>
        /// Evaluate a registered PiecewiseLinear curve at <paramref name="x"/>.
        /// Returns 0 when <paramref name="curveId"/> is not registered.
        /// Clamps to the first/last Y value outside the control-point range.
        /// </summary>
        public static float Evaluate(short curveId, float x)
        {
            lock (_table)
            {
                if (!_table.TryGetValue(curveId, out var pts))
                    return 0f;

                // Clamp to endpoints
                if (x <= pts[0].x)  return pts[0].y;
                if (x >= pts[^1].x) return pts[^1].y;

                // Binary search for the enclosing segment
                int lo = 0, hi = pts.Length - 1;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) >> 1;
                    if (pts[mid].x <= x) lo = mid; else hi = mid;
                }

                // Linear interpolation within the segment
                float t = (x - pts[lo].x) / (pts[hi].x - pts[lo].x);
                return pts[lo].y + t * (pts[hi].y - pts[lo].y);
            }
        }

        /// <summary>
        /// Retrieve the raw control points registered for a PiecewiseLinear curve.
        /// Returns null when <paramref name="curveId"/> is not registered.
        /// Intended for editor-side round-tripping (FromResponseCurve).
        /// </summary>
        internal static (float x, float y)[]? GetPoints(short curveId)
        {
            lock (_table)
            {
                return _table.TryGetValue(curveId, out var pts) ? pts : null;
            }
        }

        /// <summary>
        /// Remove all registered curves. Intended for use in tests only.
        /// </summary>
        internal static void ClearAll()
        {
            lock (_table)
                _table.Clear();
        }
    }
}
