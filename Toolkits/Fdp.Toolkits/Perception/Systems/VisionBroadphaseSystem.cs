using System;
using System.Numerics;
using CarKinem.Spatial;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Perception.Systems
{
    /// <summary>
    /// Async vision broadphase â€” runs inside <see cref="PerceptionModule"/> on the
    /// background thread via the Snapshot-on-Demand (SoD) pattern.
    /// <para>
    /// For each observer entity with a <see cref="PerceptionReceptor"/>:
    /// <list type="number">
    ///   <item>Queries the module-private <see cref="SpatialHashGrid"/> for candidates within VisionRange.</item>
    ///   <item>Skips candidates that are not alive, lack a <see cref="Faction"/>, or share the observer's faction.</item>
    ///   <item>Performs a dot-product FOV cone check using the precomputed <c>FieldOfViewCos</c> cosine.</item>
    ///   <item>Emits a <see cref="LosCheckRequestEvent"/> via the entity command buffer for candidates that pass.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Grid injection:</b> The grid is supplied via the constructor by <see cref="PerceptionModule"/>.
    /// <see cref="LocalGridBuilderSystem"/> populates the grid before this system executes each tick,
    /// so the broadphase never performs a brute-force world scan.
    /// </para>
    /// <para>
    /// <b>SoD rules (strictly enforced):</b>
    /// <list type="bullet">
    ///   <item>Only <c>view.GetComponentRO&lt;T&gt;</c> â€” no <c>GetComponentRW</c>.</item>
    ///   <item>All writes are queued via <c>view.GetCommandBuffer().PublishEvent</c>.</item>
    ///   <item>The snapshot is treated as immutable throughout execution.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Forward vector:</b> Derived from <see cref="SimTransform.Rotation"/> using
    /// <c>Vector3.Transform(Vector3.UnitX, tf.Rotation)</c>. <c>Vector3.UnitX</c> is the
    /// forward-east axis in FDP's coordinate system (X = east, Y = north, Z = up).
    /// Using <c>Vector3.UnitY</c> would point north regardless of yaw â€” a BATCH-01 regression.
    /// </para>
    /// </summary>
    public class VisionBroadphaseSystem : IEcsModuleSystem
    {
        private const int MaxCandidatesPerObserver = 256;

        // Value-copy of PerceptionModule._localGrid; shares the same native-memory pointers.
        // LocalGridBuilderSystem populates the grid before this system runs.
        private readonly SpatialHashGrid _grid;

        /// <summary>
        /// Initialises the system with the module-private spatial grid.
        /// The grid struct is copied by value; native-memory arrays are shared.
        /// </summary>
        public VisionBroadphaseSystem(SpatialHashGrid grid)
        {
            _grid = grid;
        }

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

            Span<(Entity entity, Vector2 pos)> candidates =
                stackalloc (Entity, Vector2)[MaxCandidatesPerObserver];

            foreach (var observer in observerQuery)
            {
                ref readonly var receptor   = ref view.GetComponentRO<PerceptionReceptor>(observer);
                ref readonly var obsFaction = ref view.GetComponentRO<Faction>(observer);
                ref readonly var obsTf      = ref view.GetComponentRO<SimTransform>(observer);

                var obsPos2D = new Vector2(obsTf.Position.X, obsTf.Position.Y);

                // Derive 2-D forward from quaternion â€” X-forward (east) convention.
                // Using Vector3.UnitX (not UnitY) to match the FDP yaw convention:
                //   yaw=0 â†’ facing east (+X), yaw=90Â° â†’ facing north (+Y).
                Vector3 fwd3D   = Vector3.Transform(Vector3.UnitX, obsTf.Rotation);
                Vector2 forward = Vector2.Normalize(new Vector2(fwd3D.X, fwd3D.Y));

                // Query the module-private grid for all entities within vision range.
                int count = _grid.QueryNeighbors(obsPos2D, receptor.VisionRange, candidates);

                for (int i = 0; i < count; i++)
                {
                    var (target, targetPos2D) = candidates[i];

                    if (target.Index == observer.Index) continue; // skip self

                    // Generational liveness check â€” grid stores full Entity handles.
                    if (!view.IsAlive(target)) continue;

                    // Target must have a faction to participate in vision checks.
                    if (!view.HasComponent<Faction>(target)) continue;

                    ref readonly var targetFaction = ref view.GetComponentRO<Faction>(target);

                    // Same-faction exclusion â€” allies are invisible to the broadphase.
                    if (targetFaction.FactionId == obsFaction.FactionId) continue;

                    // Distance is already filtered by QueryNeighbors (radius = VisionRange).
                    Vector2 toTarget = targetPos2D - obsPos2D;
                    float dist = toTarget.Length();
                    if (dist < float.Epsilon) continue; // degenerate case

                    // FOV cone check: dot(forward, dir_to_target) >= cos(half_FOV).
                    Vector2 toTargetNorm = toTarget / dist;
                    float dot = Vector2.Dot(forward, toTargetNorm);
                    if (dot < receptor.FieldOfViewCos) continue; // outside FOV cone

                    // Passed all broadphase filters â†’ queue a line-of-sight check.
                    ecb.PublishEvent(new LosCheckRequestEvent
                    {
                        Observer = observer,
                        Target   = target,
                    });
                }
            }
        }
    }
}
