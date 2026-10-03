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
    /// <b>Production mode (default):</b> For each <see cref="LosCheckRequestEvent"/>, asks an
    /// <see cref="LineOfSight.ILosStrategy"/>. Without an injected one it performs
    /// the original 2-D segment-circle sweep using a caller-supplied
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
    [UpdateInPhase(SystemPhase.Manual)]
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

        /// <summary>
        /// ⭐ The line-of-sight test, when a host injects one (<see cref="LineOfSight.TerrainWorldLosStrategy"/> on
        /// every host with a terrain world). Null ⇒ <see cref="LineOfSight.PlanarCircleLosStrategy"/> over
        /// <see cref="ColliderRadiusReader"/> — the 2-D sweep this system always did. 📄 docs/DESIGN_Terrain_World.md §4.3.
        /// </summary>
        private readonly LineOfSight.ILosStrategy _strategy;

        /// <param name="mockMode">
        /// <c>true</c> to bypass ray submission and directly emit <see cref="TargetVisibleEvent"/>;
        /// <c>false</c> for production (inline SoD raycast).
        /// </param>
        /// <param name="colliderRadiusReader">
        /// Optional delegate for reading the bounding radius of each candidate collider entity.
        /// See <see cref="ColliderRadiusReader"/>. Used only when <paramref name="losStrategy"/> is null.
        /// </param>
        /// <param name="losStrategy">The sight test; null ⇒ the planar 2-D sweep.</param>
        public LosRequestBatchingSystem(
            bool mockMode = false,
            Func<ISimulationView, Entity, float>? colliderRadiusReader = null,
            LineOfSight.ILosStrategy? losStrategy = null)
        {
            _mockMode = mockMode;
            ColliderRadiusReader = colliderRadiusReader;
            // The planar default reads the PROPERTY on each call, so a reader assigned after construction still applies.
            _strategy = losStrategy ?? new LineOfSight.PlanarCircleLosStrategy(
                (view, e) => ColliderRadiusReader?.Invoke(view, e) ?? 0f);
        }

        /// <summary>The sight test this system runs (diagnostics, rails).</summary>
        public LineOfSight.ILosStrategy Strategy => _strategy;

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

            // ── Production mode: ask the strategy ─────────────────────────────────────────
            _strategy.BeginBatch(view);

            foreach (ref readonly var req in requests)
            {
                if (!view.IsAlive(req.Observer) || !view.IsAlive(req.Target)) continue;
                if (!view.HasComponent<SimTransform>(req.Observer))            continue;
                if (!view.HasComponent<SimTransform>(req.Target))              continue;

                if (_strategy.IsVisible(view, req.Observer, req.Target))
                    cmds.PublishEvent(new TargetVisibleEvent { Observer = req.Observer, Target = req.Target });
            }
        }
    }
}
