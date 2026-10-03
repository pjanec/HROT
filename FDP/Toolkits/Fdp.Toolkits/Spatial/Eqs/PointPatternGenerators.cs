using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Spatial.Eqs
{
    /// <summary>
    /// ⭐ The §5.5 geometric generators share one rule (docs/designs/eqs-2/EQS_Design_v1.3_final.md §19.5): points are laid
    /// out around a context anchor (<see cref="EqsContext.AnchorPosition"/>), put on the ground of the resident terrain
    /// (<see cref="EqsTerrainSight.TryPlace"/> — dropped inside a building or wall), and emitted as positional candidates
    /// (EntityId 0). The radius is the sensor's <see cref="EqsSensor.SearchRadius"/>; the shape is a template constant.
    /// No anchor ⇒ the query cannot be evaluated yet (negative return — the solver publishes nothing, §IEqsGenerator).
    /// </summary>
    public abstract class PointPatternGenerator : IEqsGenerator
    {
        /// <summary>The context slot the pattern is centred on. Default 0 (Self).</summary>
        public byte AnchorSlot { get; set; }

        /// <inheritdoc/>
        public int Generate(Entity observer, ref EqsSensor sensor, ISimulationView view, Span<EqsResult> candidates)
        {
            if (!EqsContext.AnchorPosition(view, observer, sensor, AnchorSlot, out var anchor)) return -1;
            var world = EqsTerrainSight.World(view);
            var emit = new Emitter(world, anchor.Z, candidates);
            Layout(view, observer, ref sensor, new Vector2(anchor.X, anchor.Y), ref emit);
            return emit.Count;
        }

        /// <summary>Calls <see cref="Emitter.Add"/> for every planar point of the pattern.</summary>
        protected abstract void Layout(ISimulationView view, Entity observer, ref EqsSensor sensor, Vector2 anchor, ref Emitter emit);

        /// <summary>Collects placed points into the candidate span until it is full.</summary>
        protected ref struct Emitter
        {
            private readonly Terrain.TerrainWorld? _world;
            private readonly float _zHint;
            private readonly Span<EqsResult> _out;
            public int Count;

            public Emitter(Terrain.TerrainWorld? world, float zHint, Span<EqsResult> output)
            {
                _world = world; _zHint = zHint; _out = output; Count = 0;
            }

            public bool Full => Count >= _out.Length;

            public void Add(Vector2 p)
            {
                if (Full || !EqsTerrainSight.TryPlace(_world, p, _zHint, out var placed)) return;
                _out[Count++] = new EqsResult { EntityId = 0L, PositionX = placed.X, PositionY = placed.Y, PositionZ = placed.Z };
            }
        }
    }

    /// <summary>⭐ §5.5 <c>Donut</c>: <see cref="Rings"/> rings between <see cref="InnerFraction"/>×radius and the radius, each with
    /// <see cref="PointsPerRing"/> points (alternate rings rotated half a step so the pattern does not form spokes).</summary>
    public sealed class DonutGenerator : PointPatternGenerator
    {
        public int Rings { get; set; } = 3;
        public int PointsPerRing { get; set; } = 16;
        /// <summary>Inner radius as a fraction of the sensor's search radius. Default 0.3.</summary>
        public float InnerFraction { get; set; } = 0.3f;

        protected override void Layout(ISimulationView view, Entity observer, ref EqsSensor sensor, Vector2 anchor, ref Emitter emit)
        {
            float outer = sensor.SearchRadius, inner = outer * InnerFraction;
            if (outer <= 0f || Rings <= 0 || PointsPerRing <= 0) return;
            for (int r = 0; r < Rings && !emit.Full; r++)
            {
                float radius = Rings == 1 ? outer : inner + ((outer - inner) * r / (Rings - 1));
                float phase  = (r % 2) * MathF.PI / PointsPerRing;
                for (int k = 0; k < PointsPerRing && !emit.Full; k++)
                {
                    float a = phase + (2f * MathF.PI * k / PointsPerRing);
                    emit.Add(anchor + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius);
                }
            }
        }
    }

    /// <summary>⭐ §5.5 <c>Grid</c>: a square grid of <see cref="Spacing"/> metres covering the search radius (inside the circle),
    /// nearest rings first so a full candidate span keeps the closest points.</summary>
    public sealed class GridGenerator : PointPatternGenerator
    {
        public float Spacing { get; set; } = 5f;

        protected override void Layout(ISimulationView view, Entity observer, ref EqsSensor sensor, Vector2 anchor, ref Emitter emit)
        {
            float radius = sensor.SearchRadius;
            if (radius <= 0f || Spacing <= 0f) return;
            int n = (int)(radius / Spacing);
            for (int ring = 1; ring <= n && !emit.Full; ring++)     // square rings outward; the anchor itself is not a move
                for (int i = -ring; i <= ring && !emit.Full; i++)
                    for (int j = -ring; j <= ring && !emit.Full; j++)
                    {
                        if (Math.Max(Math.Abs(i), Math.Abs(j)) != ring) continue;
                        var off = new Vector2(i, j) * Spacing;
                        if (off.Length() <= radius) emit.Add(anchor + off);
                    }
        }
    }

    /// <summary>⭐ §5.5 <c>Cone</c>: points in a cone from the anchor towards <see cref="TowardsSlot"/> (default 1, the target),
    /// <see cref="HalfAngleDeg"/> wide, <see cref="Rings"/> × <see cref="PointsPerRing"/> out to the search radius.</summary>
    public sealed class ConeGenerator : PointPatternGenerator
    {
        public byte TowardsSlot { get; set; } = 1;
        public float HalfAngleDeg { get; set; } = 30f;
        public int Rings { get; set; } = 4;
        public int PointsPerRing { get; set; } = 7;

        protected override void Layout(ISimulationView view, Entity observer, ref EqsSensor sensor, Vector2 anchor, ref Emitter emit)
        {
            if (!EqsContext.AnchorPosition(view, observer, sensor, TowardsSlot, out var towards)) return;
            var axis = new Vector2(towards.X, towards.Y) - anchor;
            if (axis.LengthSquared() < 1e-6f || sensor.SearchRadius <= 0f || Rings <= 0 || PointsPerRing <= 0) return;
            float baseAngle = MathF.Atan2(axis.Y, axis.X), half = HalfAngleDeg * MathF.PI / 180f;
            for (int r = 1; r <= Rings && !emit.Full; r++)
            {
                float radius = sensor.SearchRadius * r / Rings;
                for (int k = 0; k < PointsPerRing && !emit.Full; k++)
                {
                    float t = PointsPerRing == 1 ? 0f : -1f + (2f * k / (PointsPerRing - 1));
                    float a = baseAngle + (t * half);
                    emit.Add(anchor + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius);
                }
            }
        }
    }

    /// <summary>⭐ §5.5 <c>OffsetFromContext</c>: fixed offsets from the anchor in its frame — X along the anchor → <see cref="FacingSlot"/>
    /// direction (forward), Y to its left. Without a facing entity the offsets are taken in world axes.</summary>
    public sealed class OffsetFromContextGenerator : PointPatternGenerator
    {
        /// <summary>The slot that defines "forward"; 255 = world axes. Default 1 (Target).</summary>
        public byte FacingSlot { get; set; } = 1;
        /// <summary>The offsets (metres, forward/left).</summary>
        public Vector2[] Offsets { get; set; } = Array.Empty<Vector2>();

        protected override void Layout(ISimulationView view, Entity observer, ref EqsSensor sensor, Vector2 anchor, ref Emitter emit)
        {
            var fwd = Vector2.UnitX;
            if (FacingSlot != 255 && EqsContext.AnchorPosition(view, observer, sensor, FacingSlot, out var f))
            {
                var d = new Vector2(f.X, f.Y) - anchor;
                if (d.LengthSquared() > 1e-6f) fwd = Vector2.Normalize(d);
            }
            var left = new Vector2(-fwd.Y, fwd.X);
            foreach (var o in Offsets)
            {
                if (emit.Full) break;
                emit.Add(anchor + (fwd * o.X) + (left * o.Y));
            }
        }
    }
}
