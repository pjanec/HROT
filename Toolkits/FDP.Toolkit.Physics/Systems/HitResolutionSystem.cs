using Fdp.Kernel;
using FDP.Toolkit.Combat.Contracts; // DEBT-031: HitEvent moved from Fdp.Kernel to Combat.Contracts
// BATCH-10: HitEvent moved from FDP.Toolkit.Combat.Events to Fdp.Kernel — no extra using needed.
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Perception.Events;

namespace FDP.Toolkit.Physics.Systems
{
    /// <summary>
    /// Main-thread system that iterates resolved <see cref="RaycastHit"/>s and emits
    /// the appropriate inter-toolkit event for each hit.
    /// <para>
    /// <b>Execution phase:</b> <see cref="InputSystemGroup"/>, after
    /// <see cref="RaycastSolverSystem"/> (guaranteed by <c>[UpdateAfter]</c>).
    /// </para>
    /// <para>
    /// <b>Event routing:</b>
    /// <list type="bullet">
    ///   <item>
    ///     <term>Bullet ray (<see cref="PhysicsConstants.IsBulletRay"/> == true)</term>
    ///     <description>
    ///       Publishes <see cref="HitEvent"/> (owned by this assembly; consumed by Combat toolkit
    ///       when it is introduced in Phase 5).
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
    /// <see cref="HitEvent"/> is now defined in <c>Fdp.Kernel</c> (BATCH-10: moved from
    /// <c>FDP.Toolkit.Combat.Events</c> to break the circular dependency introduced in
    /// BATCH-09 when Combat systems started needing Physics types).
    /// </para>
    /// <para>
    /// <b>Count reset:</b> After all hits are dispatched <see cref="RaycastBatchData.Count"/>
    /// is set to zero so the next frame starts with a clean batch.
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(InputSystemGroup))]
    [UpdateAfter(typeof(RaycastSolverSystem))]
    public class HitResolutionSystem : ComponentSystem
    {
        protected override void OnUpdate()
        {
            if (!World.HasSingleton<RaycastBatchData>()) return;
            ref var batch = ref World.GetSingleton<RaycastBatchData>();

            for (int i = 0; i < batch.Count; i++)
            {
                ref readonly var hit = ref batch.Hits[i];
                if (hit.HasHit == 0) continue;

                if (PhysicsConstants.IsBulletRay(hit.RayId))
                {
                    // Bullet hit → emit HitEvent (Combat toolkit will consume in Phase 5).
                    World.Bus.Publish(new HitEvent
                    {
                        HitEntity   = hit.HitEntity,
                        BulletIndex = (int)(hit.RayId & 0x7FFF_FFFF_FFFF_FFFFL),
                        HitT        = hit.T,
                    });
                }
                else
                {
                    // LOS hit → emit TargetVisibleEvent (Perception toolkit consumes it).
                    // Full Entity handles propagated from RaycastRequest — no index-only recovery needed.
                    // IsAlive checks are intentionally deferred to ThreatEvaluationSystem (the consumer),
                    // since a one-frame entity destruction between solve and emit is possible but does not
                    // warrant a check here — the consumer applies the generational guard.
                    World.Bus.Publish(new TargetVisibleEvent
                    {
                        Observer = hit.Observer,
                        Target   = hit.Target,
                    });
                }
            }

            // Reset for next frame — verified by HitResolution_ClearsCount_AfterProcessing test.
            batch.Count = 0;
        }
    }
}
