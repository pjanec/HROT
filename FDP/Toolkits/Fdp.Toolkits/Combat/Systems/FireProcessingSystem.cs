using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Physics.Components;
using Fdp.Toolkit.Replication.Components;

namespace Fdp.Toolkit.Combat.Systems
{
    /// <summary>
    /// Consumes <see cref="WeaponFireIntent"/> events and spawns a bullet entity for each shot.
    ///
    /// <para>
    /// <b>BS1-T007:</b> Replaces the old <see cref="FireRequestEvent"/>-based firing loop.
    /// <see cref="WeaponFireIntent"/> now carries local ECS <see cref="Entity"/> handles
    /// directly (PACK-P003); this system uses them without any <c>NetworkEntityMap</c> lookup.
    /// </para>
    ///
    /// <para>
    /// <b>Execution phase:</b> <see cref="InputSystemGroup"/> — runs after the weapon dispatcher
    /// group so that fire-intent events are available in the current frame's consume window.
    /// </para>
    ///
    /// <para>
    /// <b>Per event:</b>
    /// <list type="number">
    ///   <item>Uses <see cref="WeaponFireIntent.Shooter"/> and <see cref="WeaponFireIntent.Target"/> directly.</item>
    ///   <item>Creates a new entity.</item>
    ///   <item>Adds <see cref="SimTransform"/> — position at the shot origin.</item>
    ///   <item>Adds <see cref="SimVelocity"/> — linear velocity = <c>direction × MuzzleVelocity</c>.</item>
    ///   <item>Adds <see cref="BallisticProjectile"/> — records shooter, damage, spawn tick.</item>
    ///   <item>Adds <see cref="PhysicsCollider"/> — small bounding circle for raycast broadphase.</item>
    ///   <item>Publishes <see cref="WeaponFireNotification"/> so the egress translator can
    ///         forward a muzzle-flash event to the IG.</item>
    /// </list>
    /// If the shooter entity is no longer alive the event is skipped.
    /// </para>
    /// </summary>
    [UpdateInPhase(SystemPhase.Input)]
    public class FireProcessingSystem : IEcsModuleSystem
    {
        public void Execute(ISimulationView view, float deltaTime)
        {
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(FireProcessingSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var events = repo.Bus.Read<WeaponFireIntent>();
            if (events.Length == 0) return;

            uint currentTick = repo.HasSingleton<GlobalTime>()
                ? (uint)repo.GetSingleton<GlobalTime>().FrameNumber
                : 0u;

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];

                var shooter = evt.Shooter;
                var target  = evt.Target;

                // Skip if either entity is no longer alive.
                if (!repo.IsAlive(shooter)) continue;
                if (!repo.IsAlive(target))  continue;

                // ⛔⛔ CE-198 — THERE IS DELIBERATELY NO NetworkAuthority GATE HERE.
                //
                // TD-6 added one ("only spawn if this node is authoritative over the shooter").
                // It could never pass on the only node that runs this system, and it silently
                // disabled the entire kill chain:
                //
                //   • NetworkAuthority.HasAuthority is PrimaryOwnerId == LocalNodeId.
                //   • The BRAIN is the primary owner — NetworkSpawningSystem stamps
                //     new NetworkAuthority(cmd.OwnerNodeId, _localNodeId) at creation, and
                //     HealthApplicationSystem relies on exactly that to apply damage.
                //   • On the MUSCLE these entities are ghosts, and EntityMasterIngressTranslator
                //     deliberately stamps the unknown-owner sentinel PrimaryOwnerId = -1.
                //   ⇒ measured live on hill-attack-close, every combatant on SimHost:
                //     { HasAuthority = false, PrimaryOwnerId = -1, LocalNodeId = 1 }
                //     ⇒ every WeaponFireIntent was skipped, WeaponFire/EntityHitDamage
                //       published 0 samples, and nothing could be killed.
                //
                // ⚠ SCOPE: this bit on DISTRIBUTED topologies only — the 4-process cluster and
                // --mode all, where the Muscle's combatants are ghosts. In --mode editor there is
                // ONE world at node 0 and the entities are created locally, so NetworkAuthority
                // reads { 0, 0 }, the gate passed, and kills always worked there. That is why the
                // defect survived: the topology people watch most is the one it did not affect.
                //
                // The design is explicit that the Muscle executes the shot the Brain ordered
                // (BS-1-DESIGN.md §2.1: "Brain ── WeaponFireIntent ─► WeaponFireRequest ─► Muscle
                // ── spawns bullet"), so the Muscle is CORRECTLY not the primary owner.
                //
                // TD-6's real concern was several nodes spawning duplicate bullets. That is a
                // COMPOSITION property, and it is now structurally enforced: only the node whose
                // role composes the combat capability schedules this system (measured live on
                // --mode all: exactly one of three subsystems carries FireProcessingSystem).
                // A runtime flag that is false by construction cannot express it.

                // Skip if the shooter does not yet have a WeaponState (e.g. incomplete spawn).
                if (!repo.HasComponent<WeaponState>(shooter)) continue;

                // Read muzzle velocity from the shooter's WeaponState.
                var weapon      = repo.GetComponent<WeaponState>(shooter);
                var shooterPos  = repo.GetComponent<SimTransform>(shooter).Position;
                var targetPos   = repo.GetComponent<SimTransform>(target).Position;
                // ⭐ Buildings §3d P2 (R-217; AQ85 §D's first half) — the shot flies along the SIGHT line: from the shooter's eye to
                //   the middle of the target's silhouette, both for the LOGICAL stance (§3f — one profile for being seen and being
                //   shot). ⛔ Before, it flew feet to feet, so once bullets meet the terrain every low wall and window sill would
                //   have stopped a round the shooter aimed over. The ENTITY hit test stays 2-D (a circle) — body profiles are later.
                shooterPos.Z += Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.EyeHeightFor(repo, shooter, HitModel.LogicalStance(repo, shooter));
                targetPos.Z  += Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.AimHeightFor(repo, target, HitModel.LogicalStance(repo, target),
                    Fdp.Toolkit.Physics.Components.PhysicsColliderReaders.Height(repo, target));

                // Compute normalised direction from shooter toward target.
                var delta     = targetPos - shooterPos;
                var direction = delta.LengthSquared() > 0f
                    ? Vector3.Normalize(delta)
                    : Vector3.UnitX;    // fallback: fire east if entities are co-located
                // ⭐ CE-3089 (G7, W5) — the FIRED mount's muzzle velocity (TKB, read by type — A3); the shooter's WeaponState when the
                //   type has no template or the mount declares none (a TOW no longer flies at the 25 mm's speed).
                float muzzle = weapon.MuzzleVelocity;
                if (evt.WeaponIndex > 0 && CombatTkb.MountOf(repo, shooter, evt.WeaponIndex) is { MuzzleVelocity: > 0f } fired)
                    muzzle = fired.MuzzleVelocity;
                // ⭐⭐ AQ85 A–C (R-216) — the shot's DEFLECTION: θ = σ · d(k), σ from the fired mount's dispersion × the shooter's
                //   state (moving, under fire, logical stance), k its shot count. σ = 0 (no DispersionMils) ⇒ exact aim, as before.
                //   The muzzle offset below stays on the aim line; only the flight direction turns.
                var firedMount = CombatTkb.MountOf(repo, shooter, evt.WeaponIndex);
                float sigma = HitModel.Sigma(repo, shooter, firedMount, HitModel.Now(repo));
                uint ordinal = sigma > 0f ? HitModel.NextOrdinal(repo, shooter) : 0u;
                float theta = sigma > 0f ? HitModel.Deflection(sigma, ordinal) : 0f;
                var flight = sigma > 0f ? HitModel.Rotate(direction, theta) : direction;
                var velocity  = flight * muzzle;

                // ⭐ CE-3059 — the shot starts MuzzleOffsetMeters along the aim line (never past half way to the target), so a
                //   bullet does not spawn inside a squad-mate standing on the shooter's spot. 📐 Measured on the split cluster:
                //   four dismounted soldiers on one point, 90 rounds, every hit on the squad.
                var muzzlePos = shooterPos + direction * MathF.Min(CombatConstants.MuzzleOffsetMeters, delta.Length() * 0.5f);

                // 1. Spawn the bullet entity.
                var bullet = repo.CreateEntity();

                // 2. Spatial transform — position at the shot origin.
                repo.AddComponent(bullet, new SimTransform
                {
                    Position = muzzlePos,
                    Rotation = Quaternion.Identity,
                });

                // 3. Kinematics — velocity inherited from muzzle velocity; no angular spin.
                repo.AddComponent(bullet, new SimVelocity
                {
                    Linear  = velocity,
                    Angular = Vector3.Zero,
                });

                // 4. Ballistic tag — used by BallisticsSystem and DamageSystem.
                //    ⭐ CE-3071 — the bullet carries the FIRED mount's munition from the TKB (by the shooter's type and the
                //    request's WeaponIndex), so the hit knows what struck. No TKB numbers ⇒ an unknown munition: damage stays
                //    the flat default and penetration 0 (ArmorModel.HitDamage).
                var mount = firedMount;
                var (penetration, penetrationSource) = mount != null && mount.DamagePerHit > 0f
                    ? CombatTkb.PenetrationWithSourceOf(repo, mount)                     // ⭐ R-217 P1 — the ammo × weapon pair first
                    : (0f, "unknown munition (no DamagePerHit) — ignores armour");
                float damage = Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.DamageOrFallback(mount?.DamagePerHit ?? 0f);
                repo.AddComponent(bullet, new BallisticProjectile
                {
                    Shooter          = shooter,
                    PreviousPosition = muzzlePos,
                    Damage           = damage,
                    Penetration      = penetration,
                    SpawnTick        = currentTick,
                    Muzzle           = muzzlePos,      // ⭐ R-217 — a hit carries the round from here, whenever it resolves
                    FrontDamage      = damage,
                    FrontPenetration = penetration,
                    TerrainFlags     = 1,
                });

                // ⭐ T-4 — the shot's record, with the inputs it was fired with (GET /combat/shots)
                ShotLog.For(repo).Add(new ShotRecord
                {
                    Tick = currentTick, Shooter = shooter, Target = target, Bullet = bullet, WeaponIndex = evt.WeaponIndex,
                    Muzzle = muzzlePos, Aim = targetPos, Ordinal = ordinal, Sigma = sigma, Deflection = theta,
                    Penetration = penetration, PenetrationSource = penetrationSource, Damage = damage,
                });

                // 5. Physics collider — small sphere for broadphase candidate selection.
                repo.AddComponent(bullet, new PhysicsCollider
                {
                    Radius         = CombatConstants.BulletColliderRadius,
                    CollisionLayer = CombatConstants.BulletCollisionLayer,
                });

                // 6. Notify egress translator (muzzle flash for IG).
                //    Entity handles are passed; WeaponFireNotificationEgressTranslator resolves
                //    them to network IDs on the egress boundary.
                repo.Bus.Publish(new WeaponFireNotification
                {
                    Shooter     = shooter,
                    Target      = target,
                    WeaponIndex = evt.WeaponIndex,
                });
            }
        }
    }
}
