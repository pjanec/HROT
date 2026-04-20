using System;
using System.Numerics;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Perception.Systems
{
    /// <summary>
    /// Background-thread system that bridges <see cref="LosCheckRequestEvent"/>s from the
    /// <see cref="Modules.AutonomousPerceptionModule"/> to the physics raycast pipeline (or,
    /// in mock mode, directly confirms visibility for all requests).
    /// <para>
    /// Runs exclusively on the background thread inside
    /// <see cref="Modules.AutonomousPerceptionModule.Tick"/> — after
    /// <see cref="VisionBroadphaseSystem"/> emits requests and before
    /// <see cref="ThreatEvaluationSystem"/> processes the resulting visible-target events.
    /// </para>
    /// <para>
    /// <b>Production mode (default):</b> For each <see cref="LosCheckRequestEvent"/>, performs
    /// an inline 2-D segment-circle sweep using a caller-supplied
    /// <see cref="ColliderRadiusReader"/> delegate to obtain each candidate entity's bounding
    /// radius.  If the delegate is <c>null</c> all candidates are treated as point entities
    /// (radius zero) which creates a degenerate check: only the exact centre point of an
    /// occluder blocks the ray.  For accurate occlusion, supply the
    /// <c>PhysicsCollider.Radius</c> reader via the constructor when physics is active.
    /// </para>
    /// <para>
    /// <b>Mock mode:</b> Skips ray submission and immediately emits a
    /// <see cref="TargetVisibleEvent"/> for every incoming <see cref="LosCheckRequestEvent"/>.
    /// </para>
    /// </summary>
    public sealed class LosRequestBatchingSystem : IEcsModuleSystem
    {
        /// <summary>
        /// When <c>true</c>, every <see cref="LosCheckRequestEvent"/> is immediately resolved
        /// as visible (no actual ray submission). Set to <c>true</c> for Phase 2 testing.
        /// </summary>
        private readonly bool _mockMode;

        /// <summary>
        /// Optional delegate that returns the bounding radius (metres) of a collidable entity.
        /// <para>
        /// This indirection avoids a circular project dependency: <c>FDP.Toolkit.Perception</c>
        /// cannot reference <c>FDP.Toolkit.Physics</c> (Physics already references Perception).
        /// Callers that need physics-accurate occlusion should inject:
        /// <code>
        /// (view, e) => view.HasComponent&lt;PhysicsCollider&gt;(e)
        ///              ? view.GetComponentRO&lt;PhysicsCollider&gt;(e).Radius : 0f
        /// </code>
        /// When <c>null</c>, all candidates are treated as point obstacles (radius = 0).
        /// </para>
        /// </summary>
        public Func<ISimulationView, Entity, float>? ColliderRadiusReader { get; set; }

        /// <param name="mockMode">
        /// <c>true</c> to bypass ray submission and directly emit <see cref="TargetVisibleEvent"/>;
        /// <c>false</c> for production (inline SoD raycast).
        /// </param>
        /// <param name="colliderRadiusReader">
        /// Optional delegate for reading the bounding radius of each candidate collider entity.
        /// See <see cref="ColliderRadiusReader"/>.
        /// </param>
        public LosRequestBatchingSystem(
            bool mockMode = false,
            Func<ISimulationView, Entity, float>? colliderRadiusReader = null)
        {
            _mockMode = mockMode;
            ColliderRadiusReader = colliderRadiusReader;
        }

        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            var requests = view.ReadEvents<LosCheckRequestEvent>();
            if (requests.IsEmpty) return;

            var cmds = view.GetCommandBuffer();
            if (_mockMode)
            {
                // Mock mode: treat broadphase visibility as confirmed LOS.
                // Full Entity handles are passed straight through — generation included.
                foreach (ref readonly var req in requests)
                    cmds.PublishEvent(new TargetVisibleEvent { Observer = req.Observer, Target = req.Target });
                return;
            }

            // ── Production mode: inline SoD raycast ────────────────────────────────────────
            // For each LOS request, sweep every entity that has a SimTransform and a collider
            // radius > 0 (as reported by ColliderRadiusReader).  The target itself is excluded
            // so its own collider cannot self-occlude.
            var colliderQuery = view.Query()
                .With<SimTransform>()
                .WithComponentId(GlobalComponentIds.PhysicsCollider)
                .Build();

            foreach (ref readonly var req in requests)
            {
                if (!view.IsAlive(req.Observer) || !view.IsAlive(req.Target)) continue;
                if (!view.HasComponent<SimTransform>(req.Observer))            continue;
                if (!view.HasComponent<SimTransform>(req.Target))              continue;

                ref readonly var obsTf = ref view.GetComponentRO<SimTransform>(req.Observer);
                ref readonly var tgtTf = ref view.GetComponentRO<SimTransform>(req.Target);

                var obsPos2D = new Vector2(obsTf.Position.X, obsTf.Position.Y);
                var tgtPos2D = new Vector2(tgtTf.Position.X, tgtTf.Position.Y);

                bool blocked = false;

                foreach (var candidate in colliderQuery)
                {
                    // The target's own collider must not block the ray to itself.
                    if (candidate.Index == req.Target.Index) continue;
                    // The observer's own collider must not self-occlude.
                    if (candidate.Index == req.Observer.Index) continue;

                    if (!view.IsAlive(candidate)) continue;

                    float radius = ColliderRadiusReader?.Invoke(view, candidate) ?? 0f;

                    ref readonly var cTf = ref view.GetComponentRO<SimTransform>(candidate);
                    var cPos = new Vector2(cTf.Position.X, cTf.Position.Y);

                    if (IntersectsSegmentCircle(obsPos2D, tgtPos2D, cPos, radius))
                    {
                        blocked = true;
                        break;
                    }
                }

                if (!blocked)
                    cmds.PublishEvent(new TargetVisibleEvent { Observer = req.Observer, Target = req.Target });
            }
        }

        // ── Inline 2-D segment-circle intersection ──────────────────────────────────────
        // Mirrors Intersection2D.RaycastCircle from FDP.Toolkit.Physics without referencing
        // that assembly (circular dependency guard).
        private static bool IntersectsSegmentCircle(Vector2 start, Vector2 end, Vector2 center, float radius)
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
    }
}
    /// in mock mode, directly confirms visibility for all requests).
