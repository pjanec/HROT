using Fdp.Core.Collections;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Components;

namespace Fdp.Examples.Common.Helpers
{
    /// <summary>
    /// Deterministic <see cref="ITerrainProvider"/> for use in offline test scenarios.
    /// Returns a simple piecewise height profile based on the query X-coordinate:
    /// <list type="bullet">
    ///   <item>0 – 20 m: Z = 0 (flat)</item>
    ///   <item>20 – 80 m: Z = (x − 20) × 0.2 (linear ramp)</item>
    ///   <item>x ≈ 40 m: Z = 100 (spike / bad-raycast anomaly)</item>
    /// </list>
    /// Produces bit-identical results on all hardware (no floating-point branching).
    /// </summary>
    public sealed class MockTerrainProvider : ITerrainProvider
    {
        private const float RampStart     = 20f;
        private const float RampEnd       = 80f;
        private const float RampSlope     = 0.2f;
        private const float SpikeX        = 40f;
        private const float SpikeTolerance = 0.5f;
        private const float SpikeHeight   = 100f;

        /// <inheritdoc/>
        public void QueryBatch(
            NativeArray<TerrainQueryRequest> requests,
            int count,
            NativeArray<TerrainQueryResult> results)
        {
            for (int i = 0; i < count; i++)
            {
                float x = requests[i].QueryX;
                results[i] = new TerrainQueryResult
                {
                    HitZ   = ComputeHeight(x),
                    HasHit = true
                };
            }
        }

        private static float ComputeHeight(float x)
        {
            // Spike takes priority — check before ramp.
            if (MathF.Abs(x - SpikeX) < SpikeTolerance)
                return SpikeHeight;

            if (x >= RampStart && x < RampEnd)
                return (x - RampStart) * RampSlope;

            // Flat zone (x < RampStart) or past RampEnd.
            return 0f;
        }
    }
}
