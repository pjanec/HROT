using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>What <see cref="OverheadCoverTest"/> does with a candidate.</summary>
    public enum OverheadRequire : byte
    {
        /// <summary>Keep only points with something solid above (a filter).</summary>
        Covered = 0,
        /// <summary>Keep every point; a covered one gains <see cref="OverheadCoverTest.Weight"/>.</summary>
        Score = 1,
    }

    /// <summary>
    /// ⭐ <c>CE-2165</c> V3 / V8 (DESIGN_Vehicle_Exposure_Cycle §3, §8) — is there something SOLID ABOVE the self at the candidate: a bridge
    /// deck, a roof slab, a garage — what a vehicle shelters under from a drone. Covered when the vertical line from
    /// <see cref="ClearanceM"/> above the self's height (a vehicle's hull, a person's standing eye) to <see cref="RangeM"/> above that is
    /// BLOCKED for sight (<see cref="EqsTerrainSight.Sight"/> — the terrain's prisms, decks included, and every vehicle's box). The open sky
    /// is not cover. No sight source ⇒ nothing is judged.
    /// </summary>
    public sealed class OverheadCoverTest : IEqsTest, IEqsCostWeight
    {
        private readonly ILosService? _los;

        /// <summary>A test over the resident terrain (production).</summary>
        public OverheadCoverTest() { }

        /// <summary>A test over an explicit sight source (tests, host-specific sight).</summary>
        public OverheadCoverTest(ILosService los) => _los = los;

        /// <summary>How far above the self's height the line starts (m) — so the self's own roof is not its cover. Default 0.1.</summary>
        public float ClearanceM { get; set; } = 0.1f;

        /// <summary>How far up the line reaches (m): solid within it is cover. Default 30.</summary>
        public float RangeM { get; set; } = 30f;

        /// <summary>Filter or score. Default <see cref="OverheadRequire.Covered"/>.</summary>
        public OverheadRequire Require { get; set; } = OverheadRequire.Covered;

        /// <summary>The score a covered point gains in <see cref="OverheadRequire.Score"/> mode. Default 1.</summary>
        public float Weight { get; set; } = 1f;

        /// <inheritdoc/>
        public int CostPerCandidate => EqsCost.Sight;

        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.FilterExpensive;

        /// <inheritdoc/>
        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            var los = EqsTerrainSight.Sight(view, _los);
            if (los == null) return;
            var self = EqsContext.Self(view, observer, sensor);
            float hull = self.IsNull ? 0f : Fdp.Toolkit.Physics.Components.PhysicsColliderReaders.HullHeight(view, self);
            float height = hull > 0f ? hull : EqsTerrainSight.Mount(view, self).Standing;

            for (int i = 0; i < candidates.Length; i++)
            {
                ref var c = ref candidates[i];
                if (c.EntityId == -1L) continue;
                var from = new Vector3(c.PositionX, c.PositionY, c.PositionZ + height + ClearanceM);
                bool covered = !los.HasLineOfSight(from, from + new Vector3(0, 0, RangeM));
                if (Require == OverheadRequire.Score) { if (covered) c.Score += Weight; }
                else if (!covered) c.EntityId = -1L;
            }
        }
    }
}
