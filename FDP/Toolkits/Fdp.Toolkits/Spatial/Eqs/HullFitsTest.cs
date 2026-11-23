using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ <c>CE-5048</c> (c) (user, 2026-11-23: "i saw many time the tank driving to wall also when trying to peek out of the cover") — keeps a
    /// candidate only where a VEHICLE querier's hull fits in the pose it arrives in, driven out from the anchor in <see cref="FromSlot"/> (its
    /// hide): <see cref="VehicleFootprint.FitsArriving"/> — the one hull check the hide generator uses. 📐 Motion Domains §5e recorded the firing
    /// point query as having "no clearance check at all". A person querier, or no terrain, is not judged.
    /// </summary>
    public sealed class HullFitsTest : IEqsTest, IEqsCostWeight
    {
        /// <summary>The context slot the hull drives out from (the hide). Default 0.</summary>
        public byte FromSlot { get; set; }

        /// <inheritdoc/>
        public int CostPerCandidate => EqsCost.Sight;

        /// <inheritdoc/>
        public EqsTestPhase Phase => EqsTestPhase.FilterExpensive;

        /// <inheritdoc/>
        public void ExecuteBatch(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            var world = EqsTerrainSight.World(view);
            if (world == null) return;
            var self = EqsContext.Self(view, observer, sensor);
            if (self.IsNull || !VehicleFootprint.Hull(view, self, out float length, out float width, out float height)) return;
            if (!EqsContext.AnchorPosition(view, observer, sensor, FromSlot, out var from)) return;
            for (int i = 0; i < candidates.Length; i++)
            {
                ref var c = ref candidates[i];
                if (c.EntityId == -1L) continue;
                var at = new Vector3(c.PositionX, c.PositionY, c.PositionZ);
                if (!VehicleFootprint.FitsArriving(world, at, new Vector2(at.X - from.X, at.Y - from.Y), length, width, height)) c.EntityId = -1;
            }
        }
    }
}
