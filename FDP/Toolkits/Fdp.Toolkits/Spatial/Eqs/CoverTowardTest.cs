using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ <c>CE-2342</c> (R-2066 A) — keeps a candidate only where an obstacle stands between it and the point in
    /// <see cref="ContextSlotIndex"/>: a LOW line (<see cref="HeightMetres"/> over the candidate's ground) toward that point is blocked within
    /// <see cref="ReachMetres"/>. A man there lies or crouches behind the obstacle and fires over it — a 1 m berm counts.
    /// <para>📌 Why not <see cref="CheapLineOfSightTest"/> (the target's standing eye to the candidate's crouched eye): measured on
    /// <c>pc-defend-house</c>'s 1 m berm, it kept NEITHER face from a sector point in the open (the crouched eye clears the berm), and BOTH
    /// from a sector point inside a building (every line ends in its walls) — the support then posted on the enemy's face. This test reads
    /// only the obstacle in front of the man, so where the far point lands does not matter.</para>
    /// <para>⭐ No terrain resident ⇒ sight unknown ⇒ the test does nothing. 📄 docs/designs/group-maneuvers/DESIGN_Defend_And_Attack_Position.md §8f.2.</para>
    /// </summary>
    public sealed class CoverTowardTest : IEqsTest, IEqsCostWeight
    {
        private readonly ILosService? _los;

        /// <summary>A test over the resident terrain (production).</summary>
        public CoverTowardTest() { }

        /// <summary>A test over an explicit sight source.</summary>
        public CoverTowardTest(ILosService los) => _los = los;

        /// <summary>The height of the line over the candidate's ground (m): a man low behind the obstacle.</summary>
        public const float HeightMetres = 0.5f;

        /// <summary>How far toward the point the obstacle may stand (m): the cover point's stand-off plus the obstacle's depth.</summary>
        public const float ReachMetres = 3f;

        /// <summary>The context slot (0..2) of the point the obstacle must face. Default 1.</summary>
        public byte ContextSlotIndex { get; set; } = 1;

        /// <inheritdoc/>
        public int CostPerCandidate => EqsCost.Sight;

        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.FilterCheap;

        /// <inheritdoc/>
        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            var los = EqsTerrainSight.Sight(view, _los);
            if (los == null) return;
            if (!EqsContext.AnchorPosition(view, observer, sensor, ContextSlotIndex, out var toward)) return;
            for (int i = 0; i < candidates.Length; i++)
            {
                ref var c = ref candidates[i];
                if (c.EntityId == -1L) continue;
                var at = new Vector3(c.PositionX, c.PositionY, c.PositionZ);
                var d = new Vector2(toward.X - at.X, toward.Y - at.Y);
                if (d.LengthSquared() < 1e-4f) { c.EntityId = -1L; continue; }
                d = Vector2.Normalize(d) * ReachMetres;
                var from = at + new Vector3(0f, 0f, HeightMetres);
                if (los.HasLineOfSight(from, from + new Vector3(d.X, d.Y, 0f))) c.EntityId = -1L;   // nothing in front of him
            }
        }
    }
}
