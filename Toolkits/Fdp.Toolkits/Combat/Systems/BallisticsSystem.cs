using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Physics;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Physics.Systems;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// Per-frame housekeeping for all live bullet entities.
    /// <para>
    /// <b>Execution phase:</b> <see cref="PostSimulationSystemGroup"/>, <b>after</b>
    /// <c>LinearKinematicsSystem</c> (which advances positions) and before
    /// <c>SpatialHashSystem</c> (which rebuilds the grid).
    /// </para>
    /// <para>
    /// <b>Execution order rationale (Phase 0 Adaptation):</b><br/>
    /// Bullet movement is delegated to <c>LinearKinematicsSystem</c> which runs
    /// <c>pos += vel * dt</c>.  BallisticsSystem must run <em>before</em> that, so:
    /// <list type="number">
    ///   <item>
    ///     The raycast segment <c>Start = PreviousPosition, End = SimTransform.Position</c>
    ///     covers exactly the distance traversed in the <em>previous</em> frame.
    ///   </item>
    ///   <item>
    ///     <c>PreviousPosition</c> is then updated to <c>SimTransform.Position</c> so the
    ///     next frame's segment picks up from the correct starting point.
    ///   </item>
    /// </list>
    /// Required ordering: BallisticsSystem → LinearKinematicsSystem → RaycastSolverSystem.
    /// </para>
    /// <para>
    /// <b>Capacity guard (DEBT-021 pattern):</b> The batch is never written beyond
    /// <see cref="PhysicsConstants.RaycastBatchCapacity"/>; excess bullets are silently
    /// skipped this frame (no crash, no exception).
    /// </para>
    /// <!-- [UpdateBefore(typeof(LinearKinematicsSystem))] — LinearKinematicsSystem is not yet
    ///      defined in a referenceable assembly (Phase 0). Attribute will be added once the class
    ///      is introduced. Ordering is maintained by the host application's registration order. -->
    /// </summary>
    [UpdateInPhase(SystemPhase.PostSimulation)]
    // [UpdateAfter(typeof(HitResolutionSystem))] — ordering maintained by array position in CombatModule.
    public class BallisticsSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(BallisticsSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            // Guard: if the RaycastBatchData singleton has not been initialised by
            // PhysicsToolkitModule, skip silently — bullets will not be tested this frame.
            if (!repo.HasSingleton<RaycastBatchData>()) return;

            uint currentTick = repo.HasSingleton<GlobalTime>()
                ? (uint)repo.GetSingleton<GlobalTime>().FrameNumber
                : 0u;

            ref var batch = ref repo.GetSingleton<RaycastBatchData>();

            var query = repo.Query()
                .With<BallisticProjectile>()
                .With<SimTransform>()
                .Build();

            foreach (var entity in query)
            {
                ref var proj = ref repo.GetComponentRW<BallisticProjectile>(entity);

                // ── 1. Lifetime check ────────────────────────────────────────────
                // Unsigned subtraction handles tick-counter wrap correctly.
                if (currentTick - proj.SpawnTick >= CombatConstants.BulletLifetimeTicks)
                {
                    repo.DestroyEntity(entity);
                    continue;   // do NOT submit a raycast for a just-destroyed bullet
                }

                // ── 2. Submit swept-segment raycast ──────────────────────────────
                var tf = repo.GetComponent<SimTransform>(entity);

                // Capacity guard: silently drop if the batch is already full.
                if (batch.Count < PhysicsConstants.RaycastBatchCapacity)
                {
                    batch.Requests[batch.Count++] = new RaycastRequest
                    {
                        Start        = proj.PreviousPosition,
                        End          = tf.Position,
                        RayId        = PhysicsConstants.PackBulletRayId(entity.Index),
                        LayerMask    = ~CombatConstants.BulletCollisionLayer,  // hit everything except other bullets
                        IgnoreEntity = proj.Shooter,
                    };
                }

                // ── 3. Update PreviousPosition ───────────────────────────────────
                // Record the bullet's current position so the next frame's raycast
                // sweeps the correct segment (after LinearKinematicsSystem advances it).
                proj.PreviousPosition = tf.Position;
            }
        }
    }
}
