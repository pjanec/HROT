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
            double now = HitModel.Now(repo);   // ⭐ CE-2180 — synced sim time

            for (int i = 0; i < events.Length; i++)
            {
                ref readonly var evt = ref events[i];

                var shooter = evt.Shooter;
                var target  = evt.Target;
                int late = 0, owed = 0;   // ⭐ CE-2180 — frames a bomb was flown forward / owed to make up the trip from the brain

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

                // ⭐ CE-2188 / CE-1111 (BS-1-DESIGN §5.4c) — a flare / chaff DISPENSER's release is not a shot: CountermeasureSystem
                //   throws its decoy and publishes the fire. No round, no aim.
                if (Countermeasures.IsDispenser(repo, shooter, evt.WeaponIndex)) continue;

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
                // ⭐ WS-3 — a carried / station weapon system aims from its carrier's eye (HitModel.BodyOf; itself otherwise)
                var sightBody = HitModel.BodyOf(repo, shooter);
                if (sightBody != shooter) shooterPos = repo.GetComponent<SimTransform>(sightBody).Position;
                shooterPos.Z += Fdp.Toolkit.Perception.LineOfSight.TerrainWorldLosStrategy.EyeHeightFor(repo, sightBody, HitModel.LogicalStance(repo, sightBody));
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
                // ⭐ CE-2183 (AT2, R-2021, DESIGN_Air_Operations §7c) — a STRAIGHT round at an AIRCRAFT is aimed where it will be: the ONE
                //   predictor (TargetLead.Point, behaviors' B13), from the round's origin at its own speed, the target's velocity as this
                //   node knows it (ReplicatedMotion). Ground targets, guided and arcing rounds are unchanged. ⭐ Here, not on the Brain: a
                //   shot at a UNIT is aimed by this node at fire time (AimPoint.Middle above) — the Brain's aim point is not used.
                if (!evt.AtPoint) targetPos = LedAtAircraft(repo, shooter, target, evt, origin, targetPos, weapon.MuzzleVelocity);
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
                byte warheadBits = 0; float fuze = 0f; uint arcLifetime = 0;
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
                        // ⭐⭐ Indirect fire IF-1 (I3) — the brain's fire solution: the intent's CHARGE at the intent's ARC, flown as given
                        //   (no second decision here). Charge 0 = no solution: today's arc (ThrowArc — a hand throw, every older mount).
                        var charges = evt.Charge > 0 && firedMount != null
                            ? FireSolution.ChargesOf(repo.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>() ? repo.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() : null, firedMount, ammo)
                            : null;
                        velocity = charges != null && evt.Charge <= charges.Count
                            ? ArcLaunch(origin, targetPos, charges[evt.Charge - 1], high: evt.HighArc != 0)
                            : ThrowArc.Launch(Fdp.Toolkit.World.WorldQuery.Of(repo), origin, targetPos, muzzle, arcPen);
                        // ⭐⭐ IF-1 (I4, IF-F) — the arc keeps the mount's SPREAD (it replaced the dispersed direction before): the azimuth by
                        //   θ (the same sequence a direct round turns by), the range by a speed factor on a second stream — ½·σ·d₂, since the
                        //   range goes with v², so range and azimuth errors are alike (≈ σ × the range). σ = 0 ⇒ exactly as before.
                        if (sigma > 0f)
                        {
                            velocity = HitModel.Rotate(velocity, theta) * (1f + 0.5f * sigma * HitModel.RangeSequence(ordinal));
                        }
                        // ⭐ IF-1 (I5) — it lives its time of flight + 10 s (never less than the old flat 60 s): a high-angle howitzer
                        //   round flies longer than 60 s and vanished in the air.
                        arcLifetime = ArcLifetimeTicks(origin, targetPos, velocity);
                    }
                }

                // ⭐⭐ M4 (D-D; DESIGN_Missiles_And_Ammo §3) — a MISSILE flies at its own speed (⛔ the generated range × 0.5 muzzle
                //   speed made a TOW ~7× too fast) and, guided, gets a GuidedRound below: GuidedRoundSystem steers it on this node.
                var missile = CombatTkb.MissileOf(repo, ammo);
                if (missile != null && missile.SpeedMps > 0f && (warheadBits & WarheadRound.Arc) == 0) velocity = flight * missile.SpeedMps;

                // ⭐⭐ Air Ops slice 4 (DESIGN_Air_Operations §8, A4) — an AIR-domain shooter launches with its own velocity: a BOMB leaves
                //   its rack at the carrier's velocity + 1 m/s down from the body itself and falls (Arc) — the same step BombBallistics
                //   predicts; any other round adds the carrier's velocity to its own. ⛔ A ground shooter is unchanged.
                //   ⭐ CE-2173 slice 6d (§9f) — the carrier is the BODY the shot leaves from (HitModel.BodyOf, as the aim above): a door gun
                //   (a weapon system in the 404's station) has no locomotion of its own, so its round left at the muzzle speed alone.
                if (Fdp.Toolkit.Motion.Locomotions.InAir(repo, sightBody) && repo.HasComponent<SimVelocity>(sightBody))
                {
                    var carrier = repo.GetComponentRO<SimVelocity>(sightBody).Linear;
                    if (firedMount?.Category == Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb)
                    {
                        velocity = BombBallistics.Launch(carrier);
                        warheadBits |= WarheadRound.Arc;
                        origin = repo.GetComponentRO<SimTransform>(shooter).Position;
                        // ⭐⭐ CE-2180 (§8a) — the bomb leaves the carrier as it was when the brain RELEASED it, flown forward to now: the
                        //   cue ran on the brain, the intent reached this node frames later, and every frame of that trip was ~150 m/s of
                        //   overshoot (measured live: 20–27 m long). The carrier is stepped back along its own velocity (the run-in is a
                        //   straight line), the bomb flown the same time by the CUE's own steps (BombBallistics.DefaultDt — so it rejoins
                        //   the very path the cue predicted, whatever this node's frame). One world ⇒ 0 steps, as before.
                        late = owed = BombBallistics.LateSteps(evt.RaisedAt, now, deltaTime);
                        if (late > 0)
                        {
                            origin -= carrier * (late * BombBallistics.DefaultDt);
                            var terrainBelow = Fdp.Toolkit.World.WorldQuery.Of(repo);
                            late = BombBallistics.FlyFor(ref origin, ref velocity, late,
                                                         terrainBelow == null ? null : terrainBelow.GroundHeightAt, BombBallistics.DefaultDt);
                        }
                    }
                    else velocity += carrier;
                }

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
                    ArcLifetimeTicks = arcLifetime,   // ⭐ IF-1 (I5) — 0 = the flat arc lifetime
                });

                // ⭐⭐ LR-2 (§8.5, R-271) — a VISIBLE round (its munition has a look: a shell, a bomb, a grenade, a rocket, a missile, a
                //   tracer) becomes a LIGHTWEIGHT REPLICATED entity: its munition type as TkbIdentity + the LightweightRound tag. The
                //   network side gives it an id and publishes EntityMaster (Lightweight) + WorldPos — no lifecycle, no acks. An
                //   ordinary bullet stays local: it is the hit test, nothing a viewer draws.
                MarkIfVisible(repo, bullet, roundType);

                // ⭐ M4 — the guided round's guidance, target and lifetime (an unguided missile gets its lifetime and speed only)
                if (missile != null && repo.IsComponentTypeRegistered<GuidedRound>())
                {
                    // ⭐ CE-2201 (§3) — top attack only where the climb-and-dive FITS the range, to the highest apex that does; else direct
                    float apex = 0f;
                    bool top = !evt.AtPoint && GuidedRoundSystem.TopAttackFits(missile, velocity.Length(),
                                   new Vector2(targetPos.X - muzzlePos.X, targetPos.Y - muzzlePos.Y).Length(), muzzlePos.Z - targetPos.Z, out apex);
                    repo.AddComponent(bullet, new GuidedRound
                    {
                        Guidance      = missile.Guidance,
                        Phase         = top ? GuidedPhase.Climb : GuidedPhase.Cruise,
                        TopAttack     = (byte)(top ? 1 : 0),
                        Target        = evt.AtPoint ? Entity.Null : target,
                        Gunner        = shooter,
                        SpeedMps      = velocity.Length(),
                        LifetimeTicks = missile.MaxFlightSeconds > 0f ? (uint)MathF.Ceiling(missile.MaxFlightSeconds * GuidedRound.TicksPerSecond) : 0u,
                        CruiseZ       = muzzlePos.Z + (top ? apex : missile.CruiseAltitudeM),
                        DiveAngleDeg  = missile.DiveAngleDeg,
                        LastSeen      = targetPos,
                        // ⭐ CE-3198 (§3b) — the overfly top attack, at a HULL only (a man is attacked direct)
                        Overfly       = (byte)(missile.Overfly && missile.Guidance == Fdp.Toolkit.Tkb.Domain.GuidanceKind.FireAndForget && !evt.AtPoint
                                               && Fdp.Toolkit.Physics.Components.PhysicsColliderReaders.HullHeight(repo, target) > 0f ? 1 : 0),
                        OverflyHeightM = missile.OverflyHeightM > 0f ? missile.OverflyHeightM : 1f,
                        // ⭐ CE-2187 / CE-2188 (DESIGN_Missiles_And_Ammo §3c) — the seeker's cone, the proximity fuze, the decoy rejection
                        SeekerCos     = missile.SeekerConeDeg > 0f ? MathF.Cos(0.5f * missile.SeekerConeDeg * (MathF.PI / 180f)) : 0f,
                        ProximityM    = CombatTkb.WarheadOf(repo, ammo) is { Fuze: Fdp.Toolkit.Tkb.Domain.FuzeKind.Proximity, ProximityFuzeM: > 0f } pw
                                        ? pw.ProximityFuzeM : 0f,
                        Rejection     = missile.Guidance == Fdp.Toolkit.Tkb.Domain.GuidanceKind.Radar ? missile.ChaffRejection : missile.FlareRejection,
                    });
                    // ⭐ CE-2189 (DESIGN_Missiles_And_Ammo §3c, AD4) — a GUIDED missile launched at an aircraft warns it: its brain (local, or
                    //   over the wire) learns where from, the seeker and the flight time. One warning per round, at launch.
                    if (missile.Guidance != Fdp.Toolkit.Tkb.Domain.GuidanceKind.Unguided && !evt.AtPoint && repo.IsAlive(target)
                        && Fdp.Toolkit.Motion.Locomotions.IsAircraft(repo, target) && repo.Bus.IsRegistered<Events.MissileWarningEvent>())   // ⭐ B-6 — an aircraft, not a jumper
                    {
                        float speed = MathF.Max(velocity.Length(), 1f);
                        repo.Bus.Publish(new Events.MissileWarningEvent
                        {
                            Unit = target, FromX = muzzlePos.X, FromY = muzzlePos.Y, FromZ = muzzlePos.Z, Seeker = (byte)missile.Guidance,
                            TimeToImpactS = Vector3.Distance(muzzlePos, targetPos) / speed,
                        });
                    }
                }

                // ⭐ T-4 — the shot's record, with the inputs it was fired with (GET /combat/shots)
                ShotLog.For(repo).Add(new ShotRecord
                {
                    Tick = currentTick, Shooter = shooter, Target = target, Bullet = bullet, WeaponIndex = evt.WeaponIndex,
                    Muzzle = muzzlePos, Aim = targetPos, Ordinal = ordinal, Sigma = sigma, Deflection = theta,
                    Penetration = penetration, PenetrationSource = penetrationSource, Damage = damage,
                    RaisedAt = evt.RaisedAt, FiredAt = now, CaughtUpSeconds = late * BombBallistics.DefaultDt, CaughtUpFrames = late, OwedFrames = owed, FrameDt = deltaTime,   // ⭐ CE-2180
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
                    // ⭐ CE-1111 (BS-1-DESIGN §5.4b F1) — the shot itself: the round FIRED (the tracer when it was one), where and how
                    //   fast it left, when. Viewers draw this — never the slot's default round.
                    AmmoGuid    = roundType,
                    Round       = bullet,
                    Origin      = muzzlePos,
                    Velocity    = velocity,
                    Quantity    = 1,
                    FiredAt     = now,
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

        /// <summary>The seconds an arc round launched at <paramref name="velocity"/> outlives its flight to <paramref name="to"/>'s height.</summary>
        public const float ArcLifetimeMarginSeconds = 10f;

        /// <summary>
        /// ⭐ Indirect fire IF-1 (I5) — an arc round's lifetime in ticks: its time of flight (the descending root of
        /// <c>z(t) = z₀ + v_z·t − ½·g·t²</c> at the aim point's height) + <see cref="ArcLifetimeMarginSeconds"/>, never less than the flat
        /// <see cref="CombatConstants.ArcRoundLifetimeTicks"/> (so no round lives shorter than before).
        /// </summary>
        /// <summary>
        /// ⭐ <c>CE-2183</c> (AT2, R-2021) — the point a round fired at <paramref name="target"/> is aimed at: when the target is in the
        /// AIR domain and the round flies STRAIGHT (not an indirect / bomb arc, not a guided missile — those steer or fall on their own),
        /// <see cref="TargetLead.Point"/> from <paramref name="origin"/> at the round's speed (an unguided rocket's own, else the fired
        /// mount's muzzle velocity, else <paramref name="weaponMuzzle"/>), with the shooter's own velocity when it flies (A4) and the
        /// target's as this node knows it (<see cref="Fdp.Toolkit.Replication.ReplicatedMotion"/>). Otherwise <paramref name="aim"/>.
        /// </summary>
        public static Vector3 LedAtAircraft(EntityRepository repo, Entity shooter, Entity target, in WeaponFireIntent evt, Vector3 origin,
                                            Vector3 aim, float weaponMuzzle)
        {
            if (target.IsNull || !Fdp.Toolkit.Motion.Locomotions.InAir(repo, target)) return aim;
            var mount = CombatTkb.MountOf(repo, shooter, evt.WeaponIndex);
            if (mount?.Category == Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb) return aim;
            long ammo = evt.AmmoGuid != 0 ? evt.AmmoGuid : mount != null ? unchecked((long)mount.AmmoGuid) : 0L;
            if (CombatTkb.WarheadOf(repo, ammo) is { Indirect: true }) return aim;
            var missile = CombatTkb.MissileOf(repo, ammo);
            if (missile != null && missile.Guidance != Fdp.Toolkit.Tkb.Domain.GuidanceKind.Unguided) return aim;
            float speed = missile is { SpeedMps: > 0f } ? missile.SpeedMps
                        : mount is { MuzzleVelocity: > 0f } ? mount.MuzzleVelocity
                        : weaponMuzzle;
            if (!Fdp.Toolkit.Replication.ReplicatedMotion.Of(repo, target, out _, out var targetVelocity)) return aim;
            // ⭐ CE-2173 slice 6d — the carrier the round inherits (A4, above) is the body it leaves from: a door gun's helicopter
            var body = HitModel.BodyOf(repo, shooter);
            var shooterVelocity = Fdp.Toolkit.Motion.Locomotions.InAir(repo, body) && repo.HasComponent<SimVelocity>(body)
                ? repo.GetComponentRO<SimVelocity>(body).Linear : Vector3.Zero;
            return TargetLead.Point(origin, shooterVelocity, speed, aim, targetVelocity);
        }

        public static uint ArcLifetimeTicks(Vector3 from, Vector3 to, Vector3 velocity)
        {
            float g = CombatConstants.Gravity, drop = from.Z - to.Z;
            float disc = velocity.Z * velocity.Z + 2f * g * drop;
            float tof = disc > 0f ? (velocity.Z + MathF.Sqrt(disc)) / g : 0f;
            uint ticks = (uint)MathF.Ceiling((tof + ArcLifetimeMarginSeconds) * GuidedRound.TicksPerSecond);
            return Math.Max(ticks, CombatConstants.ArcRoundLifetimeTicks);
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
