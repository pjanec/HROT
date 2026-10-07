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
    ///     <term>Any other ray (<see cref="PhysicsConstants.IsBulletRay"/> == false)</term>
    ///     <description>
    ///       Nothing — the requester reads its own result (the EQS accurate-LOS test, the BTree raycast query).
    ///       ⛔ <c>CE-3052</c>: it used to publish <c>TargetVisibleEvent</c> for every such hit; with the toolkit's
    ///       LOS chain retired the only non-bullet rays left were EQS cover rays, where a hit means BLOCKED.
    ///     </description>
    ///   </item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Cross-toolkit dependency approach (BATCH-08 Q3):</b>
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
                    // ⭐ AQ85 E (R-216) — a hit suppresses the unit struck (its own aim is spoiled for a while).
                    Fdp.Toolkit.Combat.HitModel.StampUnderFire(repo, hit.HitEntity, Fdp.Toolkit.Combat.HitModel.Now(repo));

                    // PACK-P003: Always emit DetonationNotification with local ECS Entity handles.
                    // The shooter entity is hit.IgnoreEntity (set to the bullet's Shooter by
                    // BallisticsSystem -- see BallisticsSystem.cs for the convention).
                    // Network-ID resolution is performed by MunitionDetonationEgressTranslator
                    // on the egress boundary; this system and FDP.Toolkit.Physics have zero
                    // NetworkEntityMap dependency.
                    var hitPos = hit.Start + hit.T * (hit.End - hit.Start);

                    // ⭐ CE-3071 — carry the bullet's munition to the damage step (same assembly; the bullet is still alive
                    //   here — its TearDown is queued below).
                    float penetration = 0f, damage = 0f;
                    if (repo.IsAlive(bulletEntity)
                        && repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.BallisticProjectile>()
                        && repo.HasComponent<Fdp.Toolkit.Combat.Components.BallisticProjectile>(bulletEntity))
                    {
                        ref readonly var bp = ref repo.GetComponentRO<Fdp.Toolkit.Combat.Components.BallisticProjectile>(bulletEntity);
                        penetration = bp.Penetration;
                        damage      = bp.Damage;
                        // ⭐ Buildings §3d P2 (R-217) — the walls/fences/floors crossed on THIS segment before the struck unit: the round
                        //   is carried from the segment's start to the hit point (BallisticsSystem held the far-end values back, so
                        //   bp is still what the round had at the start). A unit in FRONT of a fence takes the full round.
                        if (repo.HasSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>()
                            && repo.GetSingletonManaged<Fdp.Toolkit.Terrain.TerrainWorld>() is Fdp.Toolkit.Terrain.TerrainWorld terrain)
                            Fdp.Toolkit.Combat.TerrainPenetration.Carry(terrain, hit.Start, hitPos, ref damage, ref penetration, out _);
                    }

                    repo.Bus.Publish(new DetonationNotification
                    {
                        Shooter     = hit.IgnoreEntity,
                        Target      = hit.HitEntity,
                        HitX        = hitPos.X,
                        HitY        = hitPos.Y,
                        HitZ        = hitPos.Z,
                        Penetration = penetration,
                        Damage      = damage,
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
            }
        }
    }
}