using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.World
{
    /// <summary>What a trace is asked for — the same line, judged by a different rule (<c>docs/DESIGN_World_Query_Seam.md</c> WQ-A).</summary>
    public enum TracePurpose : byte
    {
        /// <summary>Can one see along the line: each crossing's <see cref="TraceCrossing.Loss"/> is a transmittance factor 0..1.</summary>
        Sight = 0,
        /// <summary>What a round or a fragment meets: each crossing's <see cref="TraceCrossing.Loss"/> is a resistance in mm RHA.</summary>
        Fire = 1,
        /// <summary>How much a sound loses: each crossing's <see cref="TraceCrossing.Loss"/> is an attenuation in dB (slice Q3).</summary>
        Sound = 2,
    }

    /// <summary>
    /// One thing a trace passes through, ordered by <see cref="T"/> along the line (0 = start, 1 = end).
    /// <para>🔒 R-252 — engine-neutral: <see cref="ClosedBarrier"/> is a meaning (part of an enclosure — a floor, a door, a building's
    /// wall), not a geometry type. <see cref="Kind"/>, <see cref="Label"/>, <see cref="Material"/>, <see cref="Building"/> and
    /// <see cref="Storey"/> are DESCRIPTIVE only (logs, debug reports): an implementation fills what it knows, and no caller may
    /// branch on their values.</para>
    /// </summary>
    public readonly record struct TraceCrossing(
        float T,
        float PathMetres,
        float Loss,
        float TopZ,
        bool ClosedBarrier,
        string Kind,
        string? Label,
        string? Material,
        string? Building,
        int Storey);

    /// <summary>
    /// ⭐ THE WORLD-QUERY SEAM (<c>docs/DESIGN_World_Query_Seam.md</c>, <c>R-250</c>) — the questions the brain, the simulation stand-in
    /// and the editor ask about the 3-D world. The stand-in answers them from its own terrain (<c>TerrainWorldQuery</c>); a mature
    /// engine answers them from its scene. Obtain one per view with <see cref="WorldQuery.Of"/> — it is bound to that view's dynamic
    /// state (which doors are open), so no caller passes engine state in.
    /// <para>🔒 R-248 / R-252 — no answer may assume the stand-in's simplifications: the ground is not flat, surfaces stack (bridges,
    /// floors, overhangs), and materials are the implementation's business.</para>
    /// </summary>
    public interface IWorldQuery
    {
        /// <summary>The height of the ground (level 0) at (x, y).</summary>
        float GroundHeightAt(float x, float y);

        /// <summary>The surface something at (x, y) stands on when it is near <paramref name="zHint"/>: the ground, a roof, a floor.</summary>
        float SurfaceZ(float x, float y, float zHint);

        /// <summary>The walkable surfaces stacked at (x, y), lowest first; <paramref name="groundIndex"/> is the ground's entry.</summary>
        IReadOnlyList<float> SurfacesAt(float x, float y, out int groundIndex);

        /// <summary>The height of level <paramref name="level"/> at (x, y): 0 = the ground, n = the n-th surface above it (clamped).</summary>
        float ResolveLevel(float x, float y, int level);

        /// <summary>Whether something can stand at (x, y) near <paramref name="zHint"/> — false inside a solid object; <paramref name="z"/> is where.</summary>
        bool TryStandAt(float x, float y, float zHint, out float z);

        /// <summary>The fast line-of-sight question: true when the line is blocked for seeing.</summary>
        bool SightBlocked(Vector3 from, Vector3 to);

        /// <summary>Everything the line passes through for <paramref name="purpose"/>, ordered along it. <paramref name="into"/> is cleared first.</summary>
        void Trace(Vector3 from, Vector3 to, TracePurpose purpose, List<TraceCrossing> into);
    }
}
