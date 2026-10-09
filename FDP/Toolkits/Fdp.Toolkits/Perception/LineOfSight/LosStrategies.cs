using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly Func<ISimulationView, Entity, float>? _hull;
        private readonly Func<ISimulationView, Entity, StanceId>? _stance;
        private readonly ColliderOcclusion _occlusion = new();   // ⭐ CE-1032 — the 3-D collider test, shared with fragments (W-6′)
        private TerrainWorld? _world;
        // ⭐ R-219 — the doors as the BATCH's view sees them (BeginBatch); a background perception batch runs on its snapshot
        private DoorStates? _doors;

        /// <param name="worldSource">The terrain world now resident; null result = no terrain (colliders only).</param>
        /// <param name="colliderRadiusReader">Collider radius (as the planar strategy).</param>
        /// <param name="colliderHeightReader">Collider height above its Z; 0 = unknown (blocks at any height).</param>
        /// <param name="stanceReader">The entity's current posture. ⚠ Null = Standing — no host composes the stance
        /// runtime yet, so nothing writes <c>StanceStatus</c> on a perception node (<c>CE-3010</c>).</param>
        /// <param name="hullHeightReader">⭐ <c>CE-3116</c> — the height a TARGET's body profile scales by (0 = by posture); null = the
        /// collider height. <see cref="PhysicsColliderReaders.HullHeight"/> on every live host: a person's collider is not a hull.</param>
        public TerrainWorldLosStrategy(
            Func<TerrainWorld?> worldSource,
            Func<ISimulationView, Entity, float>? colliderRadiusReader = null,
            Func<ISimulationView, Entity, float>? colliderHeightReader = null,
            Func<ISimulationView, Entity, StanceId>? stanceReader = null,
            Func<ISimulationView, Entity, float>? hullHeightReader = null)
        {
            _worldSource = worldSource ?? throw new ArgumentNullException(nameof(worldSource));
            _radius = colliderRadiusReader;
            _height = colliderHeightReader;
            _hull = hullHeightReader ?? colliderHeightReader;
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
                stanceReader ?? Hrot.MuscleCharacter.Animation.Components.LogicalStance.Of,    // ⭐ Stage 4 — the logical stance (§3f)
                PhysicsColliderReaders.HullHeight);                                             // ⭐ CE-3116 — a person by posture

        public void BeginBatch(ISimulationView view)
        {
            _world = _worldSource();
            _doors = _world is { Doors.Count: > 0 } ? DoorStates.Of(view, _world) : null;

            _occlusion.Build(view, _radius, _height);
        }

        /// <summary>The height of the entity's eye above its Z for its current posture.</summary>
        public float EyeHeight(ISimulationView view, Entity e)
            => EyeHeightFor(view, e, _stance?.Invoke(view, e) ?? StanceId.Standing);

        /// <summary>
        /// The point on the target the line aims at: half its collider height when it has one (a vehicle), else
        /// half its eye height for its posture (a prone soldier presents a prone silhouette).
        /// </summary>
        public float AimHeight(ISimulationView view, Entity e)
            => AimHeightFor(view, e, _stance?.Invoke(view, e) ?? StanceId.Standing, _hull?.Invoke(view, e) ?? 0f);

        /// <summary>
        /// ⭐ THE eye-height rule for <paramref name="stance"/> — the <see cref="SensorMount"/>, else <see cref="DefaultMount"/>.
        /// ⭐ Buildings §3d P2 (R-217): the fire chain raises the shot's muzzle by the SAME rule (§3f — one profile for being seen
        /// and being shot), so a target the shooter sees over a low wall is a target its round clears the wall to.
        /// </summary>
        public static float EyeHeightFor(ISimulationView view, Entity e, StanceId stance)
        {
            bool registered = view is not EntityRepository repo || repo.IsComponentTypeRegistered<SensorMount>();   // a combat-only world may not register it
            var mount = registered && view.HasComponent<SensorMount>(e) ? view.GetComponentRO<SensorMount>(e) : DefaultMount;
            return mount.For(stance);
        }

        /// <summary>⭐ THE aim-point rule: half <paramref name="colliderHeight"/> when known (&gt; 0), else half the eye height for
        /// <paramref name="stance"/>. Shared by sight (<see cref="AimHeight"/>) and fire (R-217).</summary>
        public static float AimHeightFor(ISimulationView view, Entity e, StanceId stance, float colliderHeight)
            => colliderHeight > 0f ? colliderHeight * 0.5f : EyeHeightFor(view, e, stance) * 0.5f;

        /// <summary>One body point the sight line was tested against, and its verdict.</summary>
        public readonly record struct LosPoint(float Height, Vector3 Aim, bool Clear, TerrainWorld.TraceResult? Terrain, Entity? BlockingEntity, string Verdict);

        /// <summary>⭐ Tuning T-4 / Stage 4 — why a line of sight is (not) clear: the eye, the stances, and every body point's own verdict.</summary>
        public sealed record LosExplanation(bool Visible, Vector3 Eye, StanceId ObserverStance, StanceId TargetStance, float EyeHeight,
            IReadOnlyList<LosPoint> Points, string Verdict)
        {
            /// <summary>The first clear point, else the first point (what a single-line reader expects).</summary>
            public Vector3 Aim => (Points.FirstOrDefault(p => p.Clear) is { Height: > 0f } c ? c : Points[0]).Aim;
            public TerrainWorld.TraceResult? Terrain => (Points.FirstOrDefault(p => p.Clear) is { Height: > 0f } c ? c : Points[0]).Terrain;
            public Entity? BlockingEntity => Visible ? null : Points[0].BlockingEntity;
        }

        /// <summary>
        /// ⭐ Tuning T-4 (<c>GET /perception/los</c>) — the SAME decision as <see cref="IsVisible"/>, with its evidence. Call
        /// <see cref="BeginBatch"/> first. The terrain part is <see cref="TerrainWorld.QuerySight"/>, which agrees with
        /// <see cref="TerrainWorld.SegmentBlocked"/> by construction (rail <c>TerrainWorldTests.Stage3_*</c>).
        /// </summary>
        public LosExplanation Explain(ISimulationView view, Entity observer, Entity target)
        {
            var eye = view.GetComponentRO<SimTransform>(observer).Position;
            var so = _stance?.Invoke(view, observer) ?? StanceId.Standing;
            var st = _stance?.Invoke(view, target) ?? StanceId.Standing;
            float eh = EyeHeight(view, observer);
            eye.Z += eh;
            var points = new List<LosPoint>();
            foreach (var aim in BodyPoints(view, target, st))
            {
                TerrainWorld.TraceResult? trace = _world?.QuerySight(eye, aim, _world.Doors.Count > 0 ? DoorStates.Of(view, _world) : null);   // ⭐ R-219: this view's doors
                float height = aim.Z - view.GetComponentRO<SimTransform>(target).Position.Z;
                if (trace is { } t && t.Transmittance < TerrainWorld.SightThreshold)
                {
                    points.Add(new(height, aim, false, trace, null, $"blocked by terrain: transmittance {t.Transmittance:0.###} < {TerrainWorld.SightThreshold}"));
                    continue;
                }
                var blocker = _occlusion.Blocking(eye, aim, observer, target, out bool unknownHeight);
                points.Add(blocker is { } b
                    ? new(height, aim, false, trace, b, unknownHeight ? "blocked by an entity of unknown height" : "blocked by an entity within its height")
                    : new(height, aim, true, trace, null, trace == null ? "clear (no terrain resident)" : $"clear: transmittance {trace.Value.Transmittance:0.###} ≥ {TerrainWorld.SightThreshold}"));
            }
            bool visible = points.Any(p => p.Clear);
            return new(visible, eye, so, st, eh, points,
                visible ? $"seen: {points.Count(p => p.Clear)} of {points.Count} body points clear" : $"not seen: all {points.Count} body points blocked");
        }

        /// <summary>
        /// ⭐ Buildings Stage 4 (§3f, W5) — the target's BODY POINTS for its stance, as world positions: fractions of that stance's eye
        /// height (standing ≈ 0.2/0.9/1.6 m, crouched ≈ 0.2/0.6/1.0, prone ≈ 0.15/0.3), or of its collider height for a vehicle. The
        /// SAME profile for being seen as for being shot at (fire aims at the mid-silhouette, <see cref="AimHeightFor"/>).
        /// </summary>
        public IEnumerable<Vector3> BodyPoints(ISimulationView view, Entity target, StanceId stance)
        {
            var points = new List<Vector3>(3);
            BodyProfile.Points(view, target, stance, _hull?.Invoke(view, target) ?? 0f, points);
            return points;
        }

        public bool IsVisible(ISimulationView view, Entity observer, Entity target)
        {
            var eye = view.GetComponentRO<SimTransform>(observer).Position;
            eye.Z += EyeHeight(view, observer);
            var basePos = view.GetComponentRO<SimTransform>(target).Position;
            var stance = _stance?.Invoke(view, target) ?? StanceId.Standing;
            float collider = _hull?.Invoke(view, target) ?? 0f;
            float scale = collider > 0f ? collider : EyeHeightFor(view, target, stance);
            // ⭐ Stage 4 — SEEN when ANY body point's line is clear (a standing man's head over a 1.2 m wall is seen)
            foreach (float f in BodyProfile.Fractions(stance, collider > 0f))
            {
                var aim = basePos with { Z = basePos.Z + f * scale };
                if (_world != null && _world.SegmentBlocked(eye, aim, _doors)) continue;
                if (_occlusion.Blocking(eye, aim, observer, target, out _) == null) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1032</c> (R-225 W-6′) — THE 3-D collider test, ONE implementation for sight and for fragments, so the two never
    /// disagree about what hides a body: every <see cref="PhysicsCollider"/> is an upright cylinder (radius, and
    /// <see cref="PhysicsCollider.Height"/> above its Z — 0 = unknown, which blocks at every height). Moved verbatim out of
    /// <see cref="TerrainWorldLosStrategy"/>. ⚠ Not the raycast batch (<c>RaycastSolverSystem</c> is 2-D and resolves a tick later).
    /// <para>Build once per batch (<see cref="Build"/>), then ask per line; not thread-safe — one instance per caller.</para>
    /// </summary>
    public sealed class ColliderOcclusion
    {
        private readonly List<(Entity E, Vector3 P, float R, float H)> _colliders = new();
        private readonly ColliderIndex _index = new();   // ⭐ CE-3032 — the colliders near the segment, not all of them
        private readonly List<int> _near = new();

        /// <summary>
        /// A skip argument that matches no collider — a terrain burst has no struck entity. ⛔ Not <see cref="Entity.Null"/>: index 0 is a
        /// real entity, and a handle a background solver rebuilds from an id can carry generation 0 (<see cref="Entity.IsNull"/>), so
        /// neither "index 0" nor "IsNull" may mean "nobody" (📌 the second silently blinded every visual sensor, CE-1032).
        /// </summary>
        public static readonly Entity Nobody = new(-1, 0);

        /// <summary>The skip is by INDEX — as the sight test always compared (a rebuilt handle's generation is not reliable).</summary>
        private static bool Skips(Entity e, Entity skip) => e.Index == skip.Index;

        /// <summary>Colliders gathered by the last <see cref="Build"/>.</summary>
        public int Count => _colliders.Count;

        /// <summary>Gathers every live collider of <paramref name="view"/>; <paramref name="include"/> = null takes them all.</summary>
        public void Build(ISimulationView view, Func<ISimulationView, Entity, float>? radius, Func<ISimulationView, Entity, float>? height,
            Func<ISimulationView, Entity, bool>? include = null)
        {
            _colliders.Clear();
            foreach (var c in view.Query().With<SimTransform>().WithComponentId(GlobalComponentIds.PhysicsCollider).Build())
            {
                if (!view.IsAlive(c)) continue;
                if (Fdp.Toolkit.Terrain.TerrainObstacles.IsObstacle(view, c)) continue;   // ⭐ CE-3136 P-7a — terrain now (R-243), by its material
                if (include != null && !include(view, c)) continue;
                _colliders.Add((c, view.GetComponentRO<SimTransform>(c).Position,
                    radius?.Invoke(view, c) ?? 0f, height?.Invoke(view, c) ?? 0f));
            }
            _index.Build(_colliders.Count, i => new Vector2(_colliders[i].P.X, _colliders[i].P.Y), i => _colliders[i].R);
        }

        /// <summary>
        /// The first collider (other than <paramref name="skipA"/> and <paramref name="skipB"/>) the line
        /// <paramref name="from"/>→<paramref name="to"/> passes through within its height, or null.
        /// </summary>
        public Entity? Blocking(Vector3 from, Vector3 to, Entity skipA, Entity skipB, out bool unknownHeight)
        {
            unknownHeight = false;
            var a = new Vector2(from.X, from.Y);
            var b = new Vector2(to.X, to.Y);
            _index.Query(a, b, _near);
            foreach (int k in _near)
            {
                var (e, p, r, h) = _colliders[k];
                if (Skips(e, skipA) || Skips(e, skipB)) continue;
                if (!LosGeometry.SegmentCircle(a, b, new Vector2(p.X, p.Y), r, out float t0, out float t1)) continue;
                if (h <= 0f) { unknownHeight = true; return e; }   // unknown height — blocks, as before
                float z0 = from.Z + ((to.Z - from.Z) * t0);
                float z1 = from.Z + ((to.Z - from.Z) * t1);
                if (MathF.Min(z0, z1) < p.Z + h && MathF.Max(z0, z1) > p.Z) return e;
            }
            return null;
        }

        /// <summary>
        /// ⭐ W-7′ — the collider on the horizontal line <paramref name="from"/>→<paramref name="to"/> (other than the two skipped)
        /// that casts the deepest blast SHADOW onto <paramref name="to"/>: one whose top stands above <paramref name="topZ"/> (the
        /// target's highest body point). Returns false when none does; else <paramref name="best"/> = the smallest
        /// <paramref name="factor"/>(its height above <paramref name="baseZ"/>, the horizontal distance from it to <paramref name="to"/>)
        /// — <c>AreaEffect.ShadowFactor</c>. An unknown-height collider is not a shadow (it says nothing about how tall it is).
        /// </summary>
        public bool Shadow(Vector3 from, Vector3 to, Entity skipA, Entity skipB, float topZ, float baseZ, Func<float, float, float> factor,
            out float best)
        {
            best = 1f;
            bool any = false;
            var a = new Vector2(from.X, from.Y);
            var b = new Vector2(to.X, to.Y);
            float length = Vector2.Distance(a, b);
            _index.Query(a, b, _near);
            foreach (int k in _near)
            {
                var (e, p, r, h) = _colliders[k];
                if (Skips(e, skipA) || Skips(e, skipB) || h <= 0f) continue;
                if (p.Z + h <= topZ) continue;
                if (!LosGeometry.SegmentCircle(a, b, new Vector2(p.X, p.Y), r, out float t0, out _)) continue;
                float f = factor(p.Z + h - baseZ, (1f - t0) * length);
                if (f < best) { best = f; any = true; }
            }
            return any;
        }
    }

    /// <summary>
    /// ⭐ Buildings Stage 4 (§3f) — the body profile: the heights, as fractions, a target is sampled at for its stance. Soldiers scale
    /// by their eye height for the stance; a target with a collider height (a vehicle) by that height.
    /// </summary>
    public static class BodyProfile
    {
        private static readonly float[] s_standing = { 0.12f, 0.53f, 0.94f };   // ≈ 0.2 / 0.9 / 1.6 m at a 1.7 m eye
        private static readonly float[] s_crouched = { 0.18f, 0.55f, 0.91f };   // ≈ 0.2 / 0.6 / 1.0 m at 1.1 m
        private static readonly float[] s_prone    = { 0.43f, 0.86f };          // ≈ 0.15 / 0.3 m at 0.35 m
        private static readonly float[] s_hull     = { 0.25f, 0.5f, 0.85f };    // a vehicle's collider height

        /// <summary>The fractions for <paramref name="stance"/> (or the hull, for a target with a collider height).</summary>
        public static IReadOnlyList<float> Fractions(StanceId stance, bool hull)
            => hull ? s_hull : stance switch { StanceId.Prone => s_prone, StanceId.Crouched => s_crouched, _ => s_standing };

        /// <summary>
        /// ⭐ THE body points of <paramref name="target"/> as world positions (cleared, then filled): the stance's fractions of its eye
        /// height, or of <paramref name="hullHeight"/> when that is &gt; 0 (a vehicle — <c>PhysicsColliderReaders.HullHeight</c>).
        /// Sight (<see cref="TerrainWorldLosStrategy.BodyPoints"/>) and the area effect (<c>CE-1032</c> W-6′) both read this.
        /// </summary>
        public static void Points(ISimulationView view, Entity target, StanceId stance, float hullHeight, List<Vector3> into)
        {
            into.Clear();
            var basePos = view.GetComponentRO<SimTransform>(target).Position;
            float scale = hullHeight > 0f ? hullHeight : TerrainWorldLosStrategy.EyeHeightFor(view, target, stance);
            foreach (float f in Fractions(stance, hullHeight > 0f))
                into.Add(basePos with { Z = basePos.Z + f * scale });
        }

        /// <summary>
        /// ⭐ <c>CE-3136</c> P-2 (D2) — the height of a PERSON's body band above its feet for <paramref name="stance"/>: its highest
        /// body point (standing ≈ 1.6 m, crouched ≈ 1.0 m, prone ≈ 0.3 m). A round passing above it misses.
        /// </summary>
        public static float PersonTop(ISimulationView view, Entity person, StanceId stance)
        {
            var f = Fractions(stance, hull: false);
            return f[f.Count - 1] * TerrainWorldLosStrategy.EyeHeightFor(view, person, stance);
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
