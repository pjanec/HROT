using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
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
        private readonly System.Collections.Generic.List<Vector3> _bodyPoints = new(3);   // ⭐ P-2 scratch (main thread)

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
                if (!evt.AtPoint && !repo.IsAlive(target)) continue;   // ⭐ CE-1032 W-8 — a point needs no target entity

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
                var targetPos   = evt.AtPoint ? evt.TargetPoint : default;
                // ⭐ Buildings §3d P2 (R-217; AQ85 §D's first half) — the shot flies along the SIGHT line: from the shooter's eye to
                //   the middle of the target's silhouette, both for the LOGICAL stance (§3f — one profile for being seen and being
                //   shot). ⛔ Before, it flew feet to feet, so once bullets meet the terrain every low wall and window sill would
                //   have stopped a round the shooter aimed over. The ENTITY hit test stays 2-D (a circle) — body profiles are later.
                shooterPos.Z += Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.EyeHeightFor(repo, shooter, HitModel.LogicalStance(repo, shooter));
                if (!evt.AtPoint)
                {
                    // ⭐ CE-3116 — a person by posture; ⭐ WL-2 — the same middle the turret aims at (AimPoint.Middle)
                    targetPos = AimPoint.Middle(repo, target, out var targetStance, out float hull);
                    // ⭐ CE-3136 P-2 (D1, revised) — the middle when seen or shot through a weak cover, else the middle of the SEEN
                    //   part of the body (AimPoint). The round's own penetration decides "weak" — the fired mount's, as below.
                    var terrain = Fdp.Toolkit.World.WorldQuery.Of(repo);   // ⭐ CE-1035 Q1 — bound to this view's doors (R-219)
                    if (terrain != null)
                    {
                        var aimMount = CombatTkb.MountOf(repo, shooter, evt.WeaponIndex);
                        float aimPen = aimMount != null && aimMount.DamagePerHit > 0f ? CombatTkb.PenetrationOf(repo, aimMount) : 0f;
                        targetPos = AimPoint.For(repo, terrain, shooterPos, targetPos, target, targetStance, hull, aimPen, _bodyPoints);
                    }
                }

                // Compute normalised direction from shooter toward target.
                // ⭐ E4 (DESIGN_Visual_Effects VE-K, §6b) — the round leaves from the BARREL when the type says where it is
                //   (Muzzle.TryOf — the same point the flash, the tracer and the drawn barrel tip use); the aim point above was still
                //   chosen from the eye. ⭐ CE-3059's "never past half way" stays: a barrel more than half way to the target ⇒ today's start.
                var origin = shooterPos;
                bool fromBarrel = Muzzle.TryOf(repo, shooter, evt.WeaponIndex, out var barrel)
                                  && Vector3.Distance(barrel, shooterPos) <= 0.5f * Vector3.Distance(targetPos, shooterPos);
                if (fromBarrel) origin = barrel;
                var delta     = targetPos - origin;
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

                // ⭐⭐ Stage 6 (CE-1032, W-1/W-4/W-8) — the round's warhead, read ONCE here: an area effect makes a terrain stop a
                //   detonation; a time fuze starts counting; an INDIRECT warhead flies a gravity arc to the aim point (high for a
                //   launcher, a throw for a hand weapon). The flight never looks the warhead up again.
                // ⭐⭐ M8 (D-I) — the LOADED round: the one the weapon brain chose from the mount's loadout (the intent carries it),
                //   else the mount's own. Its warhead, penetration and damage are that round's (CombatTkb.RoundOf).
                long mountAmmo = firedMount != null ? unchecked((long)firedMount.AmmoGuid) : 0L;
                long ammo = evt.AmmoGuid != 0 ? evt.AmmoGuid : mountAmmo;
                byte warheadBits = 0; float fuze = 0f;
                if (CombatTkb.WarheadOf(repo, ammo) is { } wh)   // ⭐ CE-3158 G3 — the one lookup
                {
                    if (wh.HasAreaEffect) warheadBits |= WarheadRound.Area;
                    if (wh.Fuze == Fdp.Toolkit.Tkb.Domain.FuzeKind.Time && wh.FuzeDelayS > 0f) { warheadBits |= WarheadRound.TimeFuze; fuze = wh.FuzeDelayS; }
                    if (wh.Indirect)
                    {
                        warheadBits |= WarheadRound.Arc;
                        // ⭐ WL-4 L-1 (Weapon_Layers §5f) — a throw whose low arc the terrain would stop (the wall the target is
                        //   behind) is LOBBED: ThrowArc walks the arc through the same terrain rule the flight uses.
                        float arcPen = firedMount != null && firedMount.DamagePerHit > 0f ? CombatTkb.PenetrationOf(repo, firedMount) : 0f;
                        velocity = ThrowArc.Launch(Fdp.Toolkit.World.WorldQuery.Of(repo), origin, targetPos, muzzle, arcPen);
                    }
                }

                // ⭐⭐ M4 (D-D; DESIGN_Missiles_And_Ammo §3) — a MISSILE flies at its own speed (⛔ the generated range × 0.5 muzzle
                //   speed made a TOW ~7× too fast) and, guided, gets a GuidedRound below: GuidedRoundSystem steers it on this node.
                var missile = CombatTkb.MissileOf(repo, ammo);
                if (missile != null && missile.SpeedMps > 0f && (warheadBits & WarheadRound.Arc) == 0) velocity = flight * missile.SpeedMps;

                // ⭐ LR-1 (DESIGN_Visual_Effects.md §8.5, R-271) — the round's munition TYPE: the mount's ammo, or, one shot in
                //   TracerEvery, its tracer. Only WHAT the round is changes (drawn + replicated, PR-F) — damage, penetration and
                //   warhead stay the mount's, computed above and below from the mount.
                long roundType = ammo;
                if (ammo == mountAmmo && firedMount != null && firedMount.TracerEvery > 0 && firedMount.TracerAmmoGuid != 0 && repo.IsComponentTypeRegistered<ShotOrdinal>())
                {
                    if (!repo.HasComponent<ShotOrdinal>(shooter)) repo.AddComponent(shooter, default(ShotOrdinal));
                    ref var shots = ref repo.GetComponentRW<ShotOrdinal>(shooter);
                    shots.Fired++;
                    if (shots.Fired % (uint)firedMount.TracerEvery == 0) roundType = unchecked((long)firedMount.TracerAmmoGuid);
                }

                // ⭐ CE-3059 — the shot starts MuzzleOffsetMeters along the aim line (never past half way to the target), so a
                //   bullet does not spawn inside a squad-mate standing on the shooter's spot. 📐 Measured on the split cluster:
                //   four dismounted soldiers on one point, 90 rounds, every hit on the squad.
                var muzzlePos = fromBarrel ? origin
                              : shooterPos + direction * MathF.Min(CombatConstants.MuzzleOffsetMeters, delta.Length() * 0.5f);
                if ((warheadBits & WarheadRound.Arc) != 0) muzzlePos = origin;   // an arc leaves from the hand / tube

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
                float roundDamage = mount?.DamagePerHit ?? 0f;
                if (ammo != mountAmmo && mount != null)                                  // ⭐ M8 — another round of the loadout
                {
                    (penetration, roundDamage) = CombatTkb.RoundOf(repo, mount, ammo);
                    penetrationSource = $"loaded round {ammo} (M8 loadout)";
                }
                float damage = Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.DamageOrFallback(roundDamage);
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
                    Ammo             = roundType,   // ⭐ CE-1032 W-2 — what the round IS (⭐ LR-1: the tracer, one in N)
                    Warhead          = warheadBits,
                    FuzeRemaining    = fuze,
                });

                // ⭐⭐ LR-2 (§8.5, R-271) — a VISIBLE round (its munition has a look: a shell, a bomb, a grenade, a rocket, a missile, a
                //   tracer) becomes a LIGHTWEIGHT REPLICATED entity: its munition type as TkbIdentity + the LightweightRound tag. The
                //   network side gives it an id and publishes EntityMaster (Lightweight) + WorldPos — no lifecycle, no acks. An
                //   ordinary bullet stays local: it is the hit test, nothing a viewer draws.
                MarkIfVisible(repo, bullet, roundType);

                // ⭐ M4 — the guided round's guidance, target and lifetime (an unguided missile gets its lifetime and speed only)
                if (missile != null && repo.IsComponentTypeRegistered<GuidedRound>())
                {
                    bool top = missile.Guidance == Fdp.Toolkit.Tkb.Domain.GuidanceKind.FireAndForget && missile.TopAttack && !evt.AtPoint
                               && Vector3.Distance(origin, targetPos) >= missile.TopAttackMinRangeM;
                    repo.AddComponent(bullet, new GuidedRound
                    {
                        Guidance      = missile.Guidance,
                        Phase         = top ? GuidedPhase.Climb : GuidedPhase.Cruise,
                        TopAttack     = (byte)(top ? 1 : 0),
                        Target        = evt.AtPoint ? Entity.Null : target,
                        Gunner        = shooter,
                        SpeedMps      = velocity.Length(),
                        LifetimeTicks = missile.MaxFlightSeconds > 0f ? (uint)MathF.Ceiling(missile.MaxFlightSeconds * GuidedRound.TicksPerSecond) : 0u,
                        CruiseZ       = muzzlePos.Z + missile.CruiseAltitudeM,
                        DiveAngleDeg  = missile.DiveAngleDeg,
                        LastSeen      = targetPos,
                    });
                }

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

        /// <summary>⭐ LR-2 (§8.5, R-271) — tags a round whose munition type has a <c>Munition.Visual</c>: <see cref="TkbIdentity"/>
        /// (the munition), <see cref="Fdp.Toolkit.Replication.Components.LightweightRound"/>, its DIS type, and never-saved.</summary>
        public static bool MarkIfVisible(EntityRepository repo, Entity round, long munitionType)
        {
            if (munitionType == 0 || !repo.HasSingletonManaged<ITkbDatabase>()
                || !repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.LightweightRound>()
                || !repo.IsComponentTypeRegistered<TkbIdentity>()) return false;
            var db = repo.GetSingletonManaged<ITkbDatabase>();
            if (db == null || !db.TryGetByType(munitionType, out var type) || type == null
                || type.GetDescriptor<Fdp.Toolkit.Tkb.Domain.MunitionVisualDto>() == null) return false;
            repo.AddComponent(round, new TkbIdentity { TkbType = munitionType });
            repo.AddComponent(round, default(Fdp.Toolkit.Replication.Components.LightweightRound));
            repo.SetDisType(round, type.DisType);
            if (repo.IsComponentTypeRegistered<Fdp.Toolkit.Scenario.ScenarioIgnoreTag>())
                repo.AddComponent(round, new Fdp.Toolkit.Scenario.ScenarioIgnoreTag());
            return true;
        }

        /// <summary>
        /// ⭐ Stage 6 (<c>CE-1032</c>, W-8) — the launch velocity that carries a round at <paramref name="speed"/> from
        /// <paramref name="from"/> to <paramref name="to"/> under gravity: the HIGH solution (a mortar — steep, onto roofs and over
        /// walls) or the LOW one (a throw). Out of reach ⇒ 45°, the furthest it can go (it falls short).
        /// </summary>
        public static Vector3 ArcLaunch(Vector3 from, Vector3 to, float speed, bool high)
        {
            var flat = new Vector2(to.X - from.X, to.Y - from.Y);
            float d = flat.Length();
            var dir = d > 1e-4f ? flat / d : Vector2.UnitX;
            float h = to.Z - from.Z, g = CombatConstants.Gravity, v2 = speed * speed;
            float disc = v2 * v2 - g * (g * d * d + 2f * h * v2);
            float angle = disc < 0f || d < 1e-4f
                ? MathF.PI / 4f
                : MathF.Atan((v2 + (high ? 1f : -1f) * MathF.Sqrt(disc)) / (g * d));
            return new Vector3(dir.X * MathF.Cos(angle), dir.Y * MathF.Cos(angle), MathF.Sin(angle)) * speed;
        }
    }
}
