using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.IG.Components;

namespace Hrot.SimHost.Systems
{
    /// <summary>
    /// EQS generator: every entity whose position lies inside the polygon of the area entity in
    /// <see cref="EqsSensor.ContextSlot1"/>. The EQS 1.3 form of the area query (design §5.5
    /// <c>EntitiesInArea</c>).
    /// </summary>
    /// <remarks>
    /// <para>⭐ <b>Parity with <see cref="AreaQuerySolverSystem"/> by construction</b>: the same candidate
    /// set (every entity with <see cref="SimTransform"/> — what <c>LocalGridBuilderSystem</c> puts in the
    /// perception grid), the same relative-to-origin polygon (<see cref="EditablePolyline"/> points are
    /// offsets from the area's <see cref="SimTransform"/>), and the SAME
    /// <see cref="AreaQuerySolverSystem.PointInPolygon"/> call. Force and wreck filtering are the
    /// template's tests, not this generator's.</para>
    ///
    /// <para>⚠ <b>A walk, not a grid query</b> (user, <c>2026-09-30</c>: "slow walk is ok for now"). The
    /// perception grid is handed by constructor to <c>CognitiveSpatialModule</c> and rebuilt on its
    /// thread, so reading it from <c>EqsModule</c> would race; the CarKinem <c>SpatialGridData</c> holds
    /// only collider entities and would silently drop targets. A polygon bounding-box test prunes the walk.</para>
    ///
    /// <para>⭐ <b>No area ⇒ no answer.</b> When the area entity is absent, not yet replicated here, or
    /// has no polygon, this returns <c>-1</c> and the solver publishes nothing — the reader keeps waiting
    /// instead of reading an empty result as "the area is clear".</para>
    /// </remarks>
    public sealed class EntitiesInAreaGenerator : IEqsGenerator
    {
        // Same vertex cap as AreaQuerySolverSystem.MaxPolyVertices.
        private const int MaxPolyVertices = 64;

        /// <inheritdoc/>
        public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            Entity area = sensor.ContextSlot1;
            if (area.IsNull || !view.IsAlive(area)) return -1;
            if (!view.HasComponent<SimTransform>(area)) return -1;

            var polyline = view.GetManagedComponentRO<EditablePolyline>(area);
            if (polyline?.Points == null || polyline.Points.Count < 3) return -1;

            var points = polyline.Points;
            int nVerts = Math.Min(points.Count, MaxPolyVertices);

            ref readonly var areaTf = ref view.GetComponentRO<SimTransform>(area);
            var origin = new Vector2(areaTf.Position.X, areaTf.Position.Y);

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int v = 0; v < nVerts; v++)
            {
                var p = points[v];
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            int count = 0;
            foreach (var candidate in view.Query().With<SimTransform>().Build())
            {
                if (count >= candidates.Length) break;

                ref readonly var tf = ref view.GetComponentRO<SimTransform>(candidate);
                var local = new Vector2(tf.Position.X, tf.Position.Y) - origin;
                if (local.X < minX || local.X > maxX || local.Y < minY || local.Y > maxY) continue;
                if (!AreaQuerySolverSystem.PointInPolygon(local, points, nVerts)) continue;

                candidates[count++] = new EqsResult
                {
                    EntityId  = (long)candidate.PackedValue,
                    PositionX = tf.Position.X,
                    PositionY = tf.Position.Y,
                    PositionZ = tf.Position.Z,
                };
            }

            return count;
        }
    }

    /// <summary>
    /// EQS template: the entities of one force inside an area polygon, excluding wrecks — the area query
    /// as an EQS sensor. ⭐ Sensor inputs: <see cref="EqsSensor.ContextSlot1"/> = the area entity,
    /// <see cref="EqsSensor.FactionFilter"/> = <c>1 &lt;&lt; (int)force</c> (see <see cref="SensorFor"/>).
    /// </summary>
    /// <remarks>Top-K is 16 (design §4.3); the generator considers up to 256 candidates, as the area
    /// query's broad phase does. 📄 <c>docs/designs/eqs-2/EQS_Design_v1.3_final.md</c> §17.</remarks>
    [EqsTemplate(AssetId)]
    public static class EntitiesOfForceInArea
    {
        /// <summary>The template asset's stable identity.</summary>
        public const string AssetId = "3e5a7c91-2b4d-4f86-a0c3-5d7e9f1b2a64";

        /// <summary>
        /// FNV-1a over <see cref="AssetId"/>'s 16 bytes — the id a blueprint <c>SpawnEqsSensor</c> bakes.
        /// A rail asserts it equals <see cref="EqsTemplateRegistry.BlueprintIdOf"/>(<see cref="AssetId"/>).
        /// </summary>
        public const uint BlueprintId = 0xD571A109u;

        /// <summary>Upper bound of generated candidates (the area query's broad-phase cap).</summary>
        public const int MaxCandidates = 256;

        /// <summary>Builds the compiled template. Static and pure.</summary>
        public static EqsQueryTemplate Build(IEqsTemplateBuilder b) => new EqsQueryTemplate
        {
            BlueprintId   = BlueprintId,
            Generator     = new EntitiesInAreaGenerator(),
            FilterCheap   = new IEqsTest[] { new FactionFilterTest(), new AliveFilterTest() },
            MaxCandidates = MaxCandidates,
        };

        /// <summary>The sensor that asks "which <paramref name="force"/> entities are inside <paramref name="area"/>".</summary>
        public static EqsSensor SensorFor(Entity area, ForceId force, uint epoch = 1u) => new EqsSensor
        {
            BlueprintId   = BlueprintId,
            Epoch         = epoch,
            FactionFilter = 1u << (int)force,
            ContextSlot1  = area,
        };
    }
}
