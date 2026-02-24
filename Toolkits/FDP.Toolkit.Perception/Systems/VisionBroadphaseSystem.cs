using System;
using System.Numerics;
using FDP.Toolkit.Perception.Components;
using FDP.Toolkit.Perception.Events;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;

namespace FDP.Toolkit.Perception.Systems
{
    /// <summary>
    /// Async vision broadphase — runs inside <see cref="PerceptionModule"/> on the
    /// background thread via the Snapshot-on-Demand (SoD) pattern.
    /// <para>
    /// For each observer entity with a <see cref="PerceptionReceptor"/>:
    /// <list type="number">
    ///   <item>Finds all entities with <see cref="SimTransform"/> + <see cref="Faction"/> within VisionRange.</item>
    ///   <item>Skips candidates of the same faction as the observer.</item>
    ///   <item>Performs a dot-product FOV cone check using the precomputed <c>FieldOfViewCos</c> cosine.</item>
    ///   <item>Emits a <see cref="LosCheckRequestEvent"/> via the entity command buffer for candidates that pass.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>SoD rules (strictly enforced):</b>
    /// <list type="bullet">
    ///   <item>Only <c>view.GetComponentRO&lt;T&gt;</c> — no <c>GetComponentRW</c>.</item>
    ///   <item>All writes are queued via <c>view.GetCommandBuffer().PublishEvent</c>.</item>
    ///   <item>The snapshot is treated as immutable throughout execution.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Forward vector:</b> Derived from <see cref="SimTransform.Rotation"/> using
    /// <c>Vector3.Transform(Vector3.UnitX, tf.Rotation)</c>. <c>Vector3.UnitX</c> is the
    /// forward-east axis in FDP's coordinate system (X = east, Y = north, Z = up).
    /// Using <c>Vector3.UnitY</c> would point north regardless of yaw — a BATCH-01 regression.
    /// </para>
    /// <para>
    /// <b>Phase 2 note:</b> This implementation uses brute-force double iteration
    /// (O(observers × targets)). A SpatialHashGrid optimisation can be added in a later
    /// phase once the grid is included in the SoD snapshot or passed via constructor injection
    /// with proper entity-generation metadata.
    /// </para>
    /// </summary>
    public class VisionBroadphaseSystem : IModuleSystem
    {
        /// <inheritdoc/>
        public void Execute(ISimulationView view, float deltaTime)
        {
            var ecb = view.GetCommandBuffer();

            // Query all observer entities (must have a receptor, faction, and spatial presence).
            var observerQuery = view.Query()
                .With<PerceptionReceptor>()
                .With<Faction>()
                .With<SimTransform>()
                .Build();

            // Query all potential targets (need faction and spatial presence).
            var targetQuery = view.Query()
                .With<Faction>()
                .With<SimTransform>()
                .Build();

            foreach (var observer in observerQuery)
            {
                ref readonly var receptor   = ref view.GetComponentRO<PerceptionReceptor>(observer);
                ref readonly var obsFaction = ref view.GetComponentRO<Faction>(observer);
                ref readonly var obsTf      = ref view.GetComponentRO<SimTransform>(observer);

                var obsPos2D  = new Vector2(obsTf.Position.X, obsTf.Position.Y);
                float visionRangeSq = receptor.VisionRange * receptor.VisionRange;

                // Derive 2-D forward from quaternion — X-forward (east) convention.
                // Using Vector3.UnitX (not UnitY) to match the FDP yaw convention:
                //   yaw=0 → facing east (+X), yaw=90° → facing north (+Y).
                Vector3 fwd3D   = Vector3.Transform(Vector3.UnitX, obsTf.Rotation);
                Vector2 forward = Vector2.Normalize(new Vector2(fwd3D.X, fwd3D.Y));

                foreach (var target in targetQuery)
                {
                    if (target.Index == observer.Index) continue; // skip self

                    ref readonly var targetFaction = ref view.GetComponentRO<Faction>(target);

                    // Same-faction exclusion — allies are invisible to the broadphase.
                    if (targetFaction.FactionId == obsFaction.FactionId) continue;

                    ref readonly var targetTf = ref view.GetComponentRO<SimTransform>(target);
                    var targetPos2D = new Vector2(targetTf.Position.X, targetTf.Position.Y);

                    // Distance check (squared to avoid sqrt on exclusions).
                    Vector2 toTarget = targetPos2D - obsPos2D;
                    float distSq = toTarget.LengthSquared();
                    if (distSq > visionRangeSq) continue; // outside vision range

                    float dist = MathF.Sqrt(distSq);
                    if (dist < float.Epsilon) continue; // degenerate case

                    // FOV cone check: dot(forward, dir_to_target) >= cos(half_FOV).
                    Vector2 toTargetNorm = toTarget / dist;
                    float dot = Vector2.Dot(forward, toTargetNorm);
                    if (dot < receptor.FieldOfViewCos) continue; // outside FOV cone

                    // Passed all broadphase filters → queue a line-of-sight check.
                    ecb.PublishEvent(new LosCheckRequestEvent
                    {
                        ObserverEntityIndex = observer.Index,
                        TargetEntityIndex   = target.Index,
                    });
                }
            }
        }
    }
}
