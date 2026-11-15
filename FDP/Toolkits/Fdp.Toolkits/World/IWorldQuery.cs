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
        /// <summary>⭐ <c>CE-4598</c> (B-1; 📄 docs/DESIGN_Vehicle_Breaching.md §7 V-1, V-5, D-4) — can a vehicle's HULL pass along the line (the
        /// hull guard, R-3025): <see cref="TraceCrossing.ClosedBarrier"/> marks EVERY wall panel — a building's and a free wall's (a garden wall,
        /// a fence, a hedge) — a door and a slab; a panel within <see cref="HullTrace.HullBandM"/> below the line is met even when the line runs over its top
        /// (a low wall is met, never climbed); a breach in a BUILDING's panel never opens for a hull (it never enters a building), a breach in a
        /// free wall does. <see cref="TraceCrossing.Loss"/> is the fire resistance (mm RHA), as for <see cref="Fire"/>.</summary>
        Hull = 3,
    }

    /// <summary>⭐ <c>CE-4598</c> (B-1) — the hull purpose's constants.</summary>
    public static class HullTrace
    {
        /// <summary>The height (m) below a hull trace's line that a wall panel still meets the hull: the hull's body from its wheels to the
        /// probe (<c>CarKinematicsSystem.HullProbeM</c>, the line's height above the ground). A panel whose top is within this below the line
        /// is crossed (D-4 — a 0.4 m garden wall is met, not passed over).</summary>
        public const float HullBandM = 0.5f;
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
        int Storey)
    {
        /// <summary>⭐ <c>CE-1128</c> (docs/DESIGN_Visual_Effects.md §10 IM-4) — the outward normal of the face the line ENTERS; zero =
        /// unknown (an implementation that cannot tell). Geometry, not description: an impact mark lies on this face.</summary>
        public Vector3 EntryNormal { get; init; }

        /// <summary>⭐ <c>CE-1128</c> — the outward normal of the face the line LEAVES through; zero = unknown, or it does not leave.</summary>
        public Vector3 ExitNormal { get; init; }
        /// <summary>⭐ <c>CE-3249</c> (WS-6) — a MEANING, like <see cref="ClosedBarrier"/>: an obscurant that will clear (smoke, gas), not a
        /// thing that stands. False = unknown or solid. 📄 <c>docs/DESIGN_World_Survey_Sensor.md</c> §5.</summary>
        public bool Obscurant { get; init; }
        /// <summary>⭐ <c>CE-4369</c> (S4, R-4190 T-6) — the fraction of rounds this crossing stops REGARDLESS of their penetration (a wood's
        /// trunks: <see cref="Loss"/> is 0, most rounds pass); 0 = none. A round's damage is scaled by <c>1 − Stop</c> (the expected value).</summary>
        public float Stop { get; init; }
        /// <summary>⭐ <c>CE-4600</c> (B-3; 📄 docs/DESIGN_Vehicle_Breaching.md §3.1 <c>BarrierHit</c>) — an OPAQUE id of the piece crossed, the implementation's own
        /// (the stand-in: its prism / door-leaf index); −1 = unknown. Only the implementation that made it may interpret it (the contact rule hands it back to the
        /// terrain); ⛔ no caller branches on its value.</summary>
        public int Piece { get; init; } = -1;
    }

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

        /// <summary>⭐ <c>CE-4598</c> (B-1, 📄 docs/DESIGN_Vehicle_Breaching.md §7 D-4) — the surface a vehicle's HULL rests on at (x, y): as
        /// <see cref="SurfaceZ"/>, except that a wall panel's top is never one (a low garden wall is met, never climbed onto). A world with no
        /// panels answers <see cref="SurfaceZ"/>.</summary>
        float HullSurfaceZ(float x, float y, float zHint) => SurfaceZ(x, y, zHint);

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
