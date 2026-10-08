using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace CarKinem.Core
{
    /// <summary>One point of a traced path: where, how far along the path (m, XY arc length, the scale of
    /// <see cref="NavState.ProgressS"/>), and how the mover passes it (a <c>TraversalKind</c>: 3 = a door).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PathTracePoint
    {
        public Vector3 Position;
        public float S;
        public byte Traversal;
    }

    [InlineArray(PathTrace.Capacity)]
    public struct PathTracePoints
    {
        private PathTracePoint _element;
    }

    /// <summary>
    /// ⭐ <c>CE-3117</c> (R-226) — a mover's planned path as the map draws it (<c>PlannedPathGizmo</c>). The trajectory itself lives in
    /// the <see cref="Trajectory.TrajectoryPoolManager"/>, which is NOT in the world (so no recording, no replay); this copy is.
    /// Rebuilt by <c>PathTraceSystem</c> only when the trajectory changes, so its chunk is not re-recorded every frame. The look-ahead
    /// point is NOT stored (it moves every tick): the gizmo recomputes it from <see cref="NavState.ProgressS"/>, the vehicle parameters
    /// and the speed with <c>CarKinematicsSystem.PathLookahead</c>. NoScenario; not replicated. 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5a.
    /// </summary>
    [ComponentId(GlobalComponentIds.PathTrace)]
    [DataPolicy(DataPolicy.NoScenario)]
    [StructLayout(LayoutKind.Sequential)]
    public struct PathTrace
    {
        public const int Capacity = 32;

        /// <summary>The trajectory traced; -1 = none (the mover follows no trajectory).</summary>
        public int TrajectoryId;
        /// <summary>The trajectory's waypoint count when traced (with <see cref="TotalLength"/>, the change signature).</summary>
        public int SourceCount;
        public float TotalLength;
        public byte IsLooped;
        /// <summary>Valid points.</summary>
        public int Count;
        public PathTracePoints Points;

        public Span<PathTracePoint> PointsRW() =>
            MemoryMarshal.CreateSpan(ref Unsafe.As<PathTracePoints, PathTracePoint>(ref Points), Capacity);

        public readonly ReadOnlySpan<PathTracePoint> PointsRO() =>
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<PathTracePoints, PathTracePoint>(ref Unsafe.AsRef(in Points)),
                Math.Clamp(Count, 0, Capacity));

        /// <summary>The point <paramref name="s"/> metres along the traced polyline (clamped, or wrapped on a loop).</summary>
        public readonly Vector3 Sample(float s)
        {
            var pts = PointsRO();
            if (pts.Length == 0) return default;
            if (IsLooped != 0 && TotalLength > 0f) s = ((s % TotalLength) + TotalLength) % TotalLength;
            if (s <= pts[0].S) return pts[0].Position;
            for (int i = 1; i < pts.Length; i++)
            {
                if (s > pts[i].S) continue;
                float span = pts[i].S - pts[i - 1].S;
                float t = span > 1e-5f ? (s - pts[i - 1].S) / span : 0f;
                return Vector3.Lerp(pts[i - 1].Position, pts[i].Position, t);
            }
            return pts[^1].Position;
        }
    }
}
