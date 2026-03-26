using System.Numerics;
using Fdp.Kernel;
using FDP.Toolkit.Combat.Contracts; // DEBT-031: HitEvent moved from Fdp.Kernel to Combat.Contracts
// BATCH-10: HitEvent moved from FDP.Toolkit.Combat.Events to Fdp.Kernel — no extra using needed.
using FDP.Toolkit.Physics.Components;
using FDP.Toolkit.Perception.Events;
using FDP.Toolkit.Replication.Services;

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
    /// <b>BS1-T010:</b> When a <see cref="NetworkEntityMap"/> is provided at construction,
    /// bullet impacts also emit a <see cref="DetonationNotification"/> carrying the world-space
    /// hit position (interpolated from <see cref="RaycastRequest.Start"/>,
    /// <see cref="RaycastRequest.End"/>, and <see cref="RaycastHit.T"/>) and both entity
    /// network IDs.  LOS-check rays never produce a <see cref="DetonationNotification"/>.
    /// When no <see cref="NetworkEntityMap"/> is injected (e.g. in legacy unit tests) the
    /// notification path is skipped and the system behaves as before.
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
        private readonly NetworkEntityMap? _entityMap;

        /// <summary>Default constructor (no <see cref="DetonationNotification"/> emitted).</summary>
        public HitResolutionSystem() { }

        /// <summary>
        /// Constructor used in a Muscle-node context where network entity IDs are available.
        /// With a valid <paramref name="entityMap"/>, each bullet impact additionally emits
        /// a <see cref="DetonationNotification"/>.
        /// </summary>
        public HitResolutionSystem(NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new System.ArgumentNullException(nameof(entityMap));
        }

        protected override void OnUpdate()
        {
            if (!World.HasSingleton<RaycastBatchData>()) return;
            ref var batch = ref World.GetSingleton<RaycastBatchData>();

            for (int i = 0; i < batch.Count; i++)
            {
                ref readonly var hit     = ref batch.Hits[i];
                ref readonly var request = ref batch.Requests[i];
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

                    // BS1-T010: Emit DetonationNotification when entity map is available.
                    // The shooter network ID is obtained from request.IgnoreEntity (set to the
                    // bullet's Shooter by BallisticsSystem) — this avoids any direct reference
                    // to BallisticProjectile (which is in FDP.Toolkit.Combat, a downstream assembly).
                    if (_entityMap is not null)
                    {
                        // Compute world-space hit position by interpolating along the swept ray.
                        var hitPos = request.Start + hit.T * (request.End - request.Start);

                        _entityMap.TryGetNetworkId(hit.HitEntity,         out long hitNetId);
                        _entityMap.TryGetNetworkId(request.IgnoreEntity,  out long shooterNetId);

                        World.Bus.Publish(new DetonationNotification
                        {
                            ShooterEntityId = shooterNetId,
                            HitEntityId     = hitNetId,
                            HitX            = hitPos.X,
                            HitY            = hitPos.Y,
                            HitZ            = hitPos.Z,
                        });
                    }
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
