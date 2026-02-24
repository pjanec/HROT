using System;
using System.Numerics;

namespace FDP.Toolkit.Physics.Math
{
    /// <summary>
    /// Pure-static 2-D geometric intersection utilities.
    /// All methods are side-effect-free and testable without an ECS world.
    /// </summary>
    public static class Intersection2D
    {
        /// <summary>
        /// Tests whether the line segment [<paramref name="start"/>, <paramref name="end"/>]
        /// intersects a circle.
        /// </summary>
        /// <param name="start">Segment start (2-D ground plane).</param>
        /// <param name="end">Segment end (2-D ground plane).</param>
        /// <param name="center">Circle centre (2-D ground plane).</param>
        /// <param name="radius">Circle radius (metres).</param>
        /// <param name="t">
        /// Output: hit parameter ∈ [0,1] along <paramref name="start"/>→<paramref name="end"/>
        /// at the <em>entry</em> point (smallest valid t). When the ray starts inside the circle,
        /// t1 &lt; 0 so the method returns t2 (the exit point), which callers may use to detect
        /// bullets spawned inside a collider. Undefined if no hit.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the segment hits the circle; <see langword="false"/> otherwise.
        /// </returns>
        /// <remarks>
        /// Uses the standard quadratic discriminant method:
        /// <code>
        ///   d = end − start              (segment direction vector)
        ///   f = start − center           (vector from center to start)
        ///   At² + Bt + C = 0 where:
        ///     A = d·d
        ///     B = 2(f·d)
        ///     C = f·f − r²
        /// discriminant = B²−4AC
        ///   &lt; 0  →  no intersection
        ///   ≥ 0  →  t = (−B ± √discriminant) / 2A
        /// Only t values in [0,1] represent hits on the segment.
        /// </code>
        /// </remarks>
        public static bool RaycastCircle(
            Vector2 start, Vector2 end, Vector2 center, float radius, out float t)
        {
            Vector2 d = end - start;
            Vector2 f = start - center;

            float a = Vector2.Dot(d, d);
            float b = 2f * Vector2.Dot(f, d);
            float c = Vector2.Dot(f, f) - radius * radius;

            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0f)
            {
                t = 0f;
                return false;
            }

            float sqrtDisc = MathF.Sqrt(discriminant);
            float t1 = (-b - sqrtDisc) / (2f * a);
            float t2 = (-b + sqrtDisc) / (2f * a);

            // Return the smallest t that lies within [0, 1].
            // When the ray starts inside the circle, t1 < 0 and we fall through to t2 (the exit).
            if (t1 >= 0f && t1 <= 1f) { t = t1; return true; }
            if (t2 >= 0f && t2 <= 1f) { t = t2; return true; }

            t = 0f;
            return false;
        }
    }
}
