using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Perception.LineOfSight
{
    /// <summary>
    /// ⭐⭐ <b>The line-of-sight seam.</b> The visual sensor's <c>StrategySightTest</c> asks a strategy, so each
    /// host chooses how sight is tested without the system changing. 📄 docs/DESIGN_Terrain_World.md §3, §4.3.
    /// <para>⭐ Called once per batch (<see cref="BeginBatch"/>) then once per request — a strategy gathers what
    /// it needs (colliders, the terrain world) once, not per pair.</para>
    /// </summary>
    public interface ILosStrategy
    {
        /// <summary>Called once before a tick's requests are tested.</summary>
        void BeginBatch(ISimulationView view);

        /// <summary>True when <paramref name="observer"/> can see <paramref name="target"/>. Both are alive and
        /// carry a <see cref="SimTransform"/> — the caller checks.</summary>
        bool IsVisible(ISimulationView view, Entity observer, Entity target);
    }

    /// <summary>
    /// The test the batching system always did, unchanged: a 2-D segment against every collider circle, any
    /// height blocks. ⭐ The default where no host injects a strategy — behaviour is what the inline sweep did.
    /// </summary>
    public sealed class PlanarCircleLosStrategy : ILosStrategy
    {
        private readonly Func<ISimulationView, Entity, float>? _radius;
        private readonly List<(Entity E, Vector2 P, float R)> _colliders = new();
        private readonly ColliderIndex _index = new();   // ⭐ CE-3032 — the colliders near the segment, not all of them
        private readonly List<int> _near = new();

        public PlanarCircleLosStrategy(Func<ISimulationView, Entity, float>? colliderRadiusReader = null)
            => _radius = colliderRadiusReader;

        public void BeginBatch(ISimulationView view)
        {
            _colliders.Clear();
            foreach (var c in view.Query().With<SimTransform>().WithComponentId(GlobalComponentIds.PhysicsCollider).Build())
            {
                if (!view.IsAlive(c)) continue;
                var p = view.GetComponentRO<SimTransform>(c).Position;
                _colliders.Add((c, new Vector2(p.X, p.Y), _radius?.Invoke(view, c) ?? 0f));
            }
            _index.Build(_colliders.Count, i => _colliders[i].P, i => _colliders[i].R);
        }

        public bool IsVisible(ISimulationView view, Entity observer, Entity target)
        {
            var a3 = view.GetComponentRO<SimTransform>(observer).Position;
            var b3 = view.GetComponentRO<SimTransform>(target).Position;
            var a = new Vector2(a3.X, a3.Y);
            var b = new Vector2(b3.X, b3.Y);
            _index.Query(a, b, _near);
            foreach (int k in _near)
            {
                var (e, p, r) = _colliders[k];
                if (e.Index == observer.Index || e.Index == target.Index) continue;
                if (LosGeometry.LegacyCrossing(a, b, p, r)) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Sight through the terrain world</b> — the 3-D strategy (docs/DESIGN_Terrain_World.md §4.3, R-181/R-182).
    /// The sight line runs from the observer's EYE to the middle of the target's silhouette, both raised from the
    /// entity's Z by a height that follows POSTURE (🔒 <i>"infantry can lay on ground or squat, sensor height must
    /// follow posture"</i>). It is blocked by the terrain world (buildings and walls within their height, slabs
    /// and ramps it passes through) and by entity colliders within THEIR height
    /// (<see cref="PhysicsCollider.Height"/>; 0 = unknown, which blocks at every height exactly as before).
    /// <para>⚠ The world arrives through <c>worldSource</c>, not the view: the strategy runs inside a background
    /// module's scoped view, which exposes no singletons. The parsed <see cref="TerrainWorld"/> is immutable and is
    /// replaced by reference on a terrain load, so reading it from the background thread sees one whole world.</para>
    /// </summary>
    public sealed class TerrainWorldLosStrategy : ILosStrategy
    {
        /// <summary>Eye heights when the entity carries no <see cref="SensorMount"/> — a standing, crouched and
        /// prone soldier.</summary>
        public static readonly SensorMount DefaultMount = new()
        {
            Standing = Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightStanding,   // ⭐ Stage 0 — one home for the defaults
            Crouched = Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightCrouched,
            Prone    = Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.EyeHeightProne,
        };

        private readonly Func<TerrainWorld?> _worldSource;
        private readonly Func<ISimulationView, Entity, float>? _radius;
        private readonly Func<ISimulationView, Entity, float>? _height;
        private readonly Func<ISimulationView, Entity, StanceId>? _stance;
        private readonly List<(Entity E, Vector3 P, float R, float H)> _colliders = new();
        private readonly ColliderIndex _index = new();   // ⭐ CE-3032 — the colliders near the segment, not all of them
        private readonly List<int> _near = new();
        private TerrainWorld? _world;

        /// <param name="worldSource">The terrain world now resident; null result = no terrain (colliders only).</param>
        /// <param name="colliderRadiusReader">Collider radius (as the planar strategy).</param>
        /// <param name="colliderHeightReader">Collider height above its Z; 0 = unknown (blocks at any height).</param>
        /// <param name="stanceReader">The entity's current posture. ⚠ Null = Standing — no host composes the stance
        /// runtime yet, so nothing writes <c>StanceStatus</c> on a perception node (<c>CE-3010</c>).</param>
        public TerrainWorldLosStrategy(
            Func<TerrainWorld?> worldSource,
            Func<ISimulationView, Entity, float>? colliderRadiusReader = null,
            Func<ISimulationView, Entity, float>? colliderHeightReader = null,
            Func<ISimulationView, Entity, StanceId>? stanceReader = null)
        {
            _worldSource = worldSource ?? throw new ArgumentNullException(nameof(worldSource));
            _radius = colliderRadiusReader;
            _height = colliderHeightReader;
            _stance = stanceReader;
        }

        /// <summary>
        /// ⭐ The composition every ECS host uses: the terrain world read from <paramref name="world"/>'s
        /// singleton, colliders read from <see cref="PhysicsCollider"/>.
        /// </summary>
        public static TerrainWorldLosStrategy ForLiveWorld(
            EntityRepository world, Func<ISimulationView, Entity, StanceId>? stanceReader = null)
            => new(
                TerrainWorldSource.Live(world),   // CE-3018 — the one live source, shared with the perception grid
                PhysicsColliderReaders.Radius,
                PhysicsColliderReaders.Height,
                stanceReader);

        public void BeginBatch(ISimulationView view)
        {
            _world = _worldSource();

            _colliders.Clear();
            foreach (var c in view.Query().With<SimTransform>().WithComponentId(GlobalComponentIds.PhysicsCollider).Build())
            {
                if (!view.IsAlive(c)) continue;
                _colliders.Add((c, view.GetComponentRO<SimTransform>(c).Position,
                    _radius?.Invoke(view, c) ?? 0f, _height?.Invoke(view, c) ?? 0f));
            }
            _index.Build(_colliders.Count, i => new Vector2(_colliders[i].P.X, _colliders[i].P.Y), i => _colliders[i].R);
        }

        /// <summary>The height of the entity's eye above its Z for its current posture.</summary>
        public float EyeHeight(ISimulationView view, Entity e)
        {
            var mount = view.HasComponent<SensorMount>(e) ? view.GetComponentRO<SensorMount>(e) : DefaultMount;
            return mount.For(_stance?.Invoke(view, e) ?? StanceId.Standing);
        }

        /// <summary>
        /// The point on the target the line aims at: half its collider height when it has one (a vehicle), else
        /// half its eye height for its posture (a prone soldier presents a prone silhouette).
        /// </summary>
        public float AimHeight(ISimulationView view, Entity e)
        {
            float h = _height?.Invoke(view, e) ?? 0f;
            return h > 0f ? h * 0.5f : EyeHeight(view, e) * 0.5f;
        }

        public bool IsVisible(ISimulationView view, Entity observer, Entity target)
        {
            var eye = view.GetComponentRO<SimTransform>(observer).Position;
            var aim = view.GetComponentRO<SimTransform>(target).Position;
            eye.Z += EyeHeight(view, observer);
            aim.Z += AimHeight(view, target);

            if (_world != null && _world.SegmentBlocked(eye, aim)) return false;

            var a = new Vector2(eye.X, eye.Y);
            var b = new Vector2(aim.X, aim.Y);
            _index.Query(a, b, _near);
            foreach (int k in _near)
            {
                var (e, p, r, h) = _colliders[k];
                if (e.Index == observer.Index || e.Index == target.Index) continue;
                if (!LosGeometry.SegmentCircle(a, b, new Vector2(p.X, p.Y), r, out float t0, out float t1)) continue;
                if (h <= 0f) return false;                       // unknown height — blocks, as before
                float z0 = eye.Z + ((aim.Z - eye.Z) * t0);
                float z1 = eye.Z + ((aim.Z - eye.Z) * t1);
                if (MathF.Min(z0, z1) < p.Z + h && MathF.Max(z0, z1) > p.Z) return false;
            }
            return true;
        }
    }

    internal static class LosGeometry
    {
        /// <summary>The batching system's original inline test, verbatim: true only when the segment CROSSES
        /// the circle's edge.</summary>
        public static bool LegacyCrossing(Vector2 start, Vector2 end, Vector2 center, float radius)
        {
            Vector2 d = end - start;
            Vector2 f = start - center;
            float a = Vector2.Dot(d, d);
            float b = 2f * Vector2.Dot(f, d);
            float c = Vector2.Dot(f, f) - radius * radius;
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return false;
            float sqrtDisc = MathF.Sqrt(disc);
            float t1 = (-b - sqrtDisc) / (2f * a);
            float t2 = (-b + sqrtDisc) / (2f * a);
            return (t1 >= 0f && t1 <= 1f) || (t2 >= 0f && t2 <= 1f);
        }

        /// <summary>
        /// 2-D segment a→b against a circle; on a hit, the parameter interval [t0, t1] ⊆ [0, 1] of the segment
        /// inside the circle. ⭐ Unlike <see cref="LegacyCrossing"/> this also counts a segment that starts or
        /// ends inside the circle — the 3-D test needs the interval, not just the crossing.
        /// </summary>
        public static bool SegmentCircle(Vector2 a, Vector2 b, Vector2 c, float r, out float t0, out float t1)
        {
            t0 = t1 = 0f;
            Vector2 d = b - a;
            Vector2 f = a - c;
            float qa = Vector2.Dot(d, d);
            if (qa < 1e-12f) return false;
            float qb = 2f * Vector2.Dot(f, d);
            float qc = Vector2.Dot(f, f) - (r * r);
            float disc = (qb * qb) - (4f * qa * qc);
            if (disc < 0f) return false;
            float s = MathF.Sqrt(disc);
            float e0 = (-qb - s) / (2f * qa);
            float e1 = (-qb + s) / (2f * qa);
            t0 = MathF.Max(e0, 0f);
            t1 = MathF.Min(e1, 1f);
            return t0 <= t1;
        }
    }
}
