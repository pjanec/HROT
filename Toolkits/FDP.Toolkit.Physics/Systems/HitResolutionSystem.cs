using Fdp.Kernel;
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Physics.Events;
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
    /// <see cref="HitEvent"/> is defined in this assembly because <c>FDP.Toolkit.Combat</c>
    /// does not yet exist; the event will be moved to (or re-referenced from) the Combat toolkit
    /// in Phase 5. This avoids introducing a stub assembly today.
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
                    int observerIdx = (int)(hit.RayId >> 32);
                    int targetIdx   = (int)(hit.RayId & 0xFFFF_FFFFL);
                    World.Bus.Publish(new TargetVisibleEvent
                    {
                        ObserverEntityIndex = observerIdx,
                        TargetEntityIndex   = targetIdx,
                    });
                }
            }

            // Reset for next frame — verified by HitResolution_ClearsCount_AfterProcessing test.
            batch.Count = 0;
        }
    }
}
