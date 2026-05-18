using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Contracts; // DEBT-031: HitEvent moved from Fdp.Core to Combat.Contracts
// BATCH-10: HitEvent moved from FDP.Toolkit.Combat.Events to Fdp.Core -- no extra using needed.
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Perception.Events;

namespace Fdp.Toolkit.Physics.Systems
{
    /// <summary>
    /// Main-thread system that iterates resolved <see cref="RaycastHit"/>s and emits
    /// the appropriate inter-toolkit event for each hit.
    /// <para>
    /// <b>Execution phase:</b> <see cref="SystemPhase.Input"/>, after
    /// <see cref="RaycastSolverSystem"/> (guaranteed by array position in the module).
    /// </para>
    /// <para>
    /// <b>Event routing:</b>
    /// <list type="bullet">
    ///   <item>
    ///     <term>Bullet ray (<see cref="PhysicsConstants.IsBulletRay"/> == true)</term>
    ///     <description>
    ///       Publishes <see cref="HitEvent"/> (owned by this assembly; consumed by Combat toolkit
    ///       when it is introduced in Phase 5) AND <see cref="DetonationNotification"/> carrying
    ///       the hit position and local ECS entity handles.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term>LOS ray (<see cref="PhysicsConstants.IsBulletRay"/> == false)</term>
    ///     <description>
    ///       Publishes <see cref="TargetVisibleEvent"/> (owned by <c>FDP.Toolkit.Perception</c>;
    ///       consumed by <c>ThreatEvaluationSystem</c>).
    ///     </description>
    ///   </item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Cross-toolkit dependency approach (BATCH-08 Q3):</b>
    /// <c>FDP.Toolkit.Physics</c> references <c>FDP.Toolkit.Perception</c> directly so it
    /// can publish <see cref="TargetVisibleEvent"/> without duplicating the type.
    /// <see cref="HitEvent"/> is now defined in <c>Fdp.Core</c> (BATCH-10: moved from
    /// <c>FDP.Toolkit.Combat.Events</c> to break the circular dependency introduced in
    /// BATCH-09 when Combat systems started needing Physics types).
    /// </para>
    /// <para>
    /// <b>PACK-P003:</b> <see cref="DetonationNotification"/> is always emitted for bullet
    /// impacts (previously gated on a <c>NetworkEntityMap</c> being injected).  The event
    /// now carries local ECS <see cref="Entity"/> handles directly.  In offline / AllInOne
    /// contexts with no <c>MunitionDetonationEgressTranslator</c> registered, the event is
    /// simply not consumed and causes no side-effects.  Network-ID resolution was moved to
    /// the egress translator layer.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public class HitResolutionSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(HitResolutionSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var events = view.ReadEvents<RaycastResultEvent>();
            if (events.IsEmpty) return;

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var hit = ref events[i].Hit;
                if (hit.HasHit == 0) continue;

                if (PhysicsConstants.IsBulletRay(hit.RayId))
                {
                    int bulletIndex = (int)(hit.RayId & 0x7FFF_FFFF_FFFF_FFFFL);
                    var bulletEntity = repo.GetEntityByIndex(bulletIndex);

                    // Bullet hit -> emit HitEvent (Combat toolkit will consume in Phase 5).
                    repo.Bus.Publish(new HitEvent
                    {
                        HitEntity    = hit.HitEntity,
                        BulletEntity = bulletEntity,
                        HitT         = hit.T,
                    });

                    // PACK-P003: Always emit DetonationNotification with local ECS Entity handles.
                    // The shooter entity is hit.IgnoreEntity (set to the bullet's Shooter by
                    // BallisticsSystem -- see BallisticsSystem.cs for the convention).
                    // Network-ID resolution is performed by MunitionDetonationEgressTranslator
                    // on the egress boundary; this system and FDP.Toolkit.Physics have zero
                    // NetworkEntityMap dependency.
                    var hitPos = hit.Start + hit.T * (hit.End - hit.Start);

                    repo.Bus.Publish(new DetonationNotification
                    {
                        Shooter = hit.IgnoreEntity,
                        Target  = hit.HitEntity,
                        HitX    = hitPos.X,
                        HitY    = hitPos.Y,
                        HitZ    = hitPos.Z,
                    });

                    // Transition the bullet to TearDown so BallisticsSystem's Active-filtered
                    // query drops it (preventing further raycasts), while its component memory
                    // remains valid for DamageSystem to read BallisticProjectile.Damage on the
                    // next frame.  DamageSystem is responsible for the final DestroyEntity call.
                    if (repo.IsAlive(bulletEntity))
                    {
                        var cmd = view.GetCommandBuffer();
                        cmd.SetLifecycleState(bulletEntity, EntityLifecycle.TearDown);
                    }
                }
                else
                {
                    // LOS hit -- emit TargetVisibleEvent (Perception toolkit consumes it).
                    // Full Entity handles propagated from RaycastRequest -- no index-only recovery needed.
                    // IsAlive checks are intentionally deferred to ThreatEvaluationSystem (the consumer),
                    // since a one-frame entity destruction between solve and emit is possible but does not
                    // warrant a check here -- the consumer applies the generational guard.
                    repo.Bus.Publish(new TargetVisibleEvent
                    {
                        Observer = hit.Observer,
                        Target   = hit.Target,
                    });
                }
            }
        }
    }
}