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
    [TickMode(TickPolicy.WhenTimeAdvances)]
    public class FireProcessingSystem : IEcsModuleSystem
    {
        [Fdp.Core.WorldDerived("scratch refilled per shot")]
        private readonly System.Collections.Generic.List<Vector3> _bodyPoints = new(3);   // ⭐ P-2 scratch (main thread)

        public void Execute(ISimulationView view, float deltaTime)
        {
            if (Fdp.Toolkit.Replay.ReplayState.IsActive(view)) return;   // ⭐ CE-3329 P2 — a replay: the recording owns the world (DESIGN_Replay_Time_Axis_And_Review §12l)
            if (view is not EntityRepository repo)
                throw new InvalidOperationException(
                    $"{nameof(FireProcessingSystem)} requires direct EntityRepository access " +
                    $"and cannot run on a read-only snapshot ({view.GetType().Name}).");

            var events = repo.ReadEvents<WeaponFireIntent>();
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
                // ⭐ Q94 A (R-3018) — a request the muscle cannot honour is ANSWERED, never silently dropped: the brain's ledger
                //   (A-3) clears its reservation on the refusal instead of waiting for its timeout.
                if (!repo.HasComponent<WeaponState>(shooter)) { Refuse(repo, in evt, FireRefusal.CannotFire, now); continue; }
                // ⭐ CE-3232 (B-10, §7.11 LD-3) — a LAID-DOWN mount makes no round, whoever asked (the AI, an operator, any path)
                if (WeaponLaying.IsLaidDown(repo, shooter, evt.WeaponIndex)) { Refuse(repo, in evt, FireRefusal.CannotFire, now); continue; }
                // ⭐⭐ CE-4635 FL-1 (R-3111; DESIGN_Flame_Weapons F-1) — a FLAMETHROWER's burst throws NO round: the jet is the burst record the brain's fuel shot
                //   leaves (FlameJets), and FlameSystem applies it. The request is still ANSWERED as fired (Q94 A-1), so the brain's ledger clears.
                if (CombatTkb.MountOf(repo, shooter, evt.WeaponIndex) is { Category: Fdp.Toolkit.Tkb.Domain.WeaponCategory.Flamethrower })
                {
                    var eye = ShotOrigin.Eye(repo, shooter, out _);
                    var aimAt = evt.AtPoint ? evt.TargetPoint : repo.IsAlive(target) && repo.HasComponent<SimTransform>(target) ? repo.GetComponentRO<SimTransform>(target).Position : eye;
                    var line = aimAt - eye;
                    repo.Bus.Publish(new WeaponFireNotification
                    {
                        Shooter = shooter, Target = target, WeaponIndex = evt.WeaponIndex, AmmoGuid = evt.AmmoGuid, Round = Entity.Null,
                        Origin = eye, Velocity = line.LengthSquared() > 1e-6f ? Vector3.Normalize(line) * FlameStreamMps : Vector3.Zero,
                        Quantity = 1, FiredAt = (SimInstant)now, RequestId = evt.RequestId,
                    });
                    continue;
                }

                // Read muzzle velocity from the shooter's WeaponState.
                var weapon      = repo.GetComponent<WeaponState>(shooter);
                var shooterPos  = repo.GetComponent<SimTransform>(shooter).Position;
                var targetPos   = evt.AtPoint ? evt.TargetPoint : default;
                // ⭐ Buildings §3d P2 (R-217; AQ85 §D's first half) — the shot flies along the SIGHT line: from the shooter's eye to
                //   the middle of the target's silhouette, both for the LOGICAL stance (§3f — one profile for being seen and being
                //   shot). ⛔ Before, it flew feet to feet, so once bullets meet the terrain every low wall and window sill would
                //   have stopped a round the shooter aimed over. The ENTITY hit test stays 2-D (a circle) — body profiles are later.
                // ⭐ WS-3 — a carried / station weapon system aims from its carrier's eye (HitModel.BodyOf; itself otherwise)
                shooterPos = ShotOrigin.Eye(repo, shooter, out var sightBody);   // ⭐ CE-2218 — the one origin rule (the brain's hold reads it too)
                // ⭐⭐ Q94 A-4 (R-3018) — a DIRECT round at a POINT (blind fire at where he was last seen, a burst at a spot) is judged the
                //   same way: the muzzle line on the live terrain. An arcing or dropped round is exempt (MuzzleMasked's own rule).
                // ⭐⭐ CE-2326 (R-3030; Peek and Fire §11.6a) — a point that is a REMEMBERED BODY's feet: aimed as at that body, by the same
                //   rule as a live one (AimPoint.ForBodyAt — the chest, or the middle of the band over a cover that stops a round), then
                //   the muzzle is judged on that line below. ⛔ Before: the brain raised the spot by a fixed metre, blind to the cover.
                if (evt.AtPoint && evt.BodyStance != 0)
                {
                    var bodyMount = CombatTkb.MountOf(repo, shooter, evt.WeaponIndex);
                    float bodyPen = bodyMount != null && bodyMount.DamagePerHit > 0f ? CombatTkb.PenetrationOf(repo, bodyMount) : 0f;
                    targetPos = AimPoint.ForBodyAt(Fdp.Toolkit.World.WorldQuery.Of(repo), shooterPos, evt.TargetPoint,
                                                   (Fdp.Toolkit.Tkb.Domain.StanceId)(evt.BodyStance - 1), bodyPen, _bodyPoints);
                }
                if (evt.AtPoint && LineOfFire.MuzzleMasked(repo, shooter, evt.WeaponIndex, evt.AmmoGuid, targetPos))
                {
                    Refuse(repo, in evt, FireRefusal.Masked, now);
                    continue;
                }
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
                    // ⭐⭐ Q94 A-4 (R-3018, R-2037) — the MUSCLE judges the muzzle line with the live terrain: a round that would stop in
                    //   the wall in front of the barrel (CE-2218) is REFUSED, not flown. ⭐ CE-2329 — judged on the line the round
                    //   FLIES (the aim just chosen). ⛔ SUPERSEDED: judged on the hidden middle first, so a man at a window whose line to
                    //   the enemy's middle clipped his own sill was refused though his aim at the part he saw cleared it.
                    if (LineOfFire.MuzzleMasked(repo, shooter, evt.WeaponIndex, evt.AmmoGuid, targetPos))
                    {
                        Refuse(repo, in evt, FireRefusal.Masked, now);
                        continue;
                    }
                }

                // Compute normalised direction from shooter toward target.
                // ⭐ E4 (DESIGN_Visual_Effects VE-K, §6b) — the round leaves from the BARREL when the type says where it is
                //   (Muzzle.TryOf — the same point the flash, the tracer and the drawn barrel tip use); the aim point above was still
                //   chosen from the eye. ⭐ CE-3059's "never past half way" stays: a barrel more than half way to the target ⇒ today's start.
                var origin = ShotOrigin.From(repo, shooter, evt.WeaponIndex, shooterPos, targetPos, out bool fromBarrel);
                // ⭐ CE-2183 (AT2, R-2021, DESIGN_Air_Operations §7c) — a STRAIGHT round at an AIRCRAFT is aimed where it will be: the ONE
                //   predictor (TargetLead.Point, behaviors' B13), from the round's origin at its own speed, the target's velocity as this
                //   node knows it (ReplicatedMotion). Ground targets, guided and arcing rounds are unchanged. ⭐ Here, not on the Brain: a
                //   shot at a UNIT is aimed by this node at fire time (AimPoint.Middle above) — the Brain's aim point is not used.
                if (!evt.AtPoint) targetPos = LedAtAircraft(repo, shooter, target, evt, origin, targetPos, weapon.MuzzleVelocity);
                // ⭐ CE-4649 (bows BW-2) — an ARROW is aimed where its target will be: led by the ARC's own flight time
                if (!evt.AtPoint) targetPos = LedForBow(repo, shooter, target, evt, origin, targetPos);
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
                // ⭐ CE-4670 (VA-S5) — smoke dischargers are fired TOWARDS a point (a threat), not AT it: the salvo's centre is the mount's range out on
                //   that bearing (DischargerPoint), so the arc solve below aims there and never refuses a far threat as out of reach
                if (firedMount?.Category == Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmokeDischarger)
                    targetPos = DischargerPoint(repo.GetComponent<SimTransform>(shooter).Position, targetPos, firedMount, 1, 0);
                float sigma = HitModel.Sigma(repo, shooter, firedMount, HitModel.Now(repo));
                // ⭐⭐ CE-2436 AU-7 (A-10, R-2110) — at a target in the AIR the aim error is 2-D (the elevation too, on the second stream) and
                //   widens with the target's speed (HitModel.AirFactor): a small fast drone is hard to track. A ground target is unchanged.
                bool atAir = !evt.AtPoint && Fdp.Toolkit.Motion.Locomotions.InAir(repo, target);
                if (atAir) sigma *= HitModel.AirFactor(repo, target);
                int pellets = firedMount != null && firedMount.Pellets > 1 ? firedMount.Pellets : 1;
                uint ordinal = sigma > 0f || pellets > 1 ? HitModel.NextOrdinal(repo, shooter) : 0u;
                float theta = sigma > 0f ? HitModel.Deflection(sigma, ordinal) : 0f;
                var flight = sigma > 0f ? HitModel.Rotate(direction, theta) : direction;
                if (atAir && sigma > 0f) flight = HitModel.Pitch(flight, sigma * HitModel.RangeSequence(ordinal));
                var velocity  = flight * muzzle;

                // ⭐⭐ Stage 6 (CE-1032, W-1/W-4/W-8) — the round's warhead, read ONCE here: an area effect makes a terrain stop a
                //   detonation; a time fuze starts counting; an INDIRECT warhead flies a gravity arc to the aim point (high for a
                //   launcher, a throw for a hand weapon). The flight never looks the warhead up again.
                // ⭐⭐ M8 (D-I) — the LOADED round: the one the weapon brain chose from the mount's loadout (the intent carries it),
                //   else the mount's own. Its warhead, penetration and damage are that round's (CombatTkb.RoundOf).
                long mountAmmo = firedMount != null ? unchecked((long)firedMount.AmmoGuid) : 0L;
                long ammo = evt.AmmoGuid != 0 ? evt.AmmoGuid : mountAmmo;
                byte warheadBits = 0; float fuze = 0f, burn = 0f; uint arcLifetime = 0;
                if (CombatTkb.WarheadOf(repo, ammo) is { } wh)   // ⭐ CE-3158 G3 — the one lookup
                {
                    // ⭐ CE-3239 — a cloud round bursts too · ⭐ CE-3235 (B-13) — an inert hand item lingers where it stops
                    warheadBits = WarheadRound.BitsOf(wh, CombatTkb.HandItemOf(repo, ammo), out fuze);
                    // ⭐ CE-3235 (§7.10 PU-6) — a live round thrown back keeps the fuze it had left when it was picked up
                    if (evt.FuzeSeconds > 0f && (warheadBits & WarheadRound.TimeFuze) != 0) fuze = evt.FuzeSeconds;
                    // ⭐ CE-3236 (B-14, VC-2/VC-7) — a canister's burn: what a thrown-back one had left, else its warhead's
                    if ((warheadBits & WarheadRound.Vents) != 0) burn = evt.BurnSeconds > 0f ? evt.BurnSeconds : wh.CloudSeconds;
                    if (wh.Indirect)
                    {
                        // ⭐ WL-4 L-1 (Weapon_Layers §5f) — a throw whose low arc the terrain would stop (the wall the target is
                        //   behind) is LOBBED: ThrowArc walks the arc through the same terrain rule the flight uses.
                        float arcPen = firedMount != null && firedMount.DamagePerHit > 0f ? CombatTkb.PenetrationOf(repo, firedMount) : 0f;
                        // ⭐⭐ Indirect fire IF-1 (I3) — the brain's fire solution: the intent's CHARGE at the intent's ARC, flown as given
                        //   (no second decision here). Charge 0 = no solution: today's arc (ThrowArc — a hand throw, every older mount).
                        var charges = evt.Charge > 0 && firedMount != null
                            ? FireSolution.ChargesOf(repo.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>() ? repo.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>() : null, firedMount, ammo)
                            : null;
                        if (evt.Charge == FireSolution.SolveHere && firedMount != null)
                        {
                            // ⭐⭐ CE-3269 A-5 (Q94, R-3018) — the MUSCLE solves on its live terrain (the brain asked, it no longer solves with
                            //   the ground): the lowest charge that reaches on the asked arc, or a refusal with its reason.
                            var solved = FireSolution.For(Fdp.Toolkit.World.WorldQuery.Of(repo), origin, targetPos, firedMount, charges,
                                                          (ArcPreference)evt.HighArc);
                            if (!solved.Ok) { Refuse(repo, in evt, solved.Refusal, now); continue; }
                            velocity = solved.Launch;
                        }
                        else if (charges != null && evt.Charge <= charges.Count)
                            velocity = ArcLaunch(origin, targetPos, charges[evt.Charge - 1], high: evt.HighArc != 0);
                        else if (firedMount?.Category == Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bow)
                        {
                            // ⭐⭐ CE-4649 (bows BW-2) — the arrow flies the LOW arc at its own speed (60 / 90 m/s is above a throw's 40, which ThrowArc
                            //   would lob HIGH): the muscle solves it as it solves any arc — the low solution, or a refusal with its reason
                            var solved = FireSolution.For(Fdp.Toolkit.World.WorldQuery.Of(repo), origin, targetPos, firedMount, null, ArcPreference.Low, arcPen);
                            if (!solved.Ok) { Refuse(repo, in evt, solved.Refusal, now); continue; }
                            velocity = solved.Launch;
                        }
                        else if (!ThrowArc.TryLaunch(Fdp.Toolkit.World.WorldQuery.Of(repo), origin, targetPos, muzzle, arcPen, out velocity,
                                                     (warheadBits & WarheadRound.TimeFuze) != 0 && fuze > 0f ? fuze : float.PositiveInfinity))
                        {
                            // ⭐ CE-3296 (Q94 A, R-2037) — a THROW no arc clears is refused here, where the geometry is: the brain's
                            //   ledger holds it (the weapon brain's hidden-target throw no longer bursts an M67 on the jamb at its feet)
                            Refuse(repo, in evt, FireRefusal.Masked, now);
                            continue;
                        }
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

                // ⭐ CE-4649 (bows BW-4) — a round whose look STICKS keeps its pose where it stops (BallisticsSystem)
                if (CombatTkb.VisualOf(repo, ammo) is { Sticks: true }) warheadBits |= WarheadRound.Sticks;

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
                var muzzlePos = fromBarrel ? (WorldPoint)origin
                              : shooterPos + direction * MathF.Min(CombatConstants.MuzzleOffsetMeters, delta.Length() * 0.5f);
                if ((warheadBits & WarheadRound.Arc) != 0) muzzlePos = origin;   // an arc leaves from the hand / tube

                // ⭐⭐ CE-2436 AU-7 (A-10a) — a SHOTGUN: the shot is `pellets` rounds, each on its own place of the pattern (half-width
                //   PelletSpreadMils, both axes) around the aim just made; this bullet is pellet 0, the others are spawned below.
                float pattern = pellets > 1 ? firedMount!.PelletSpreadMils / 1000f : 0f;
                var aimedVelocity = velocity;
                // ⭐ CE-4670 (VA-S5, VA-9) — SMOKE DISCHARGERS: the salvo is a FAN, not a shotgun's random pattern — round i bursts at its own point
                //   (DischargerPoint: the mount's Range out, the bearing to the asked point ± its share of the fan), each on its own low arc. Same
                //   on every node: no dispersion draw.
                bool discharger = firedMount?.Category == Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmokeDischarger;
                var ground = repo.GetComponent<SimTransform>(shooter).Position;
                if (discharger) velocity = ArcLaunch(origin, DischargerPoint(ground, targetPos, firedMount!, pellets, 0), muzzle, high: false);
                else if (pellets > 1) velocity = PelletVelocity(aimedVelocity, pattern, ordinal, pellets, 0);

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
                var projectile = new BallisticProjectile
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
                    BurnSeconds      = burn,          // ⭐ CE-3236 (B-14) — 0 = not a canister
                    OriginalShooter  = evt.OriginalShooter,   // ⭐ CE-4155 (SR-82) — a thrown-back round keeps its first thrower
                    RangeLimitM      = pellets > 1 && firedMount != null && firedMount.Category != Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmokeDischarger
                                       ? firedMount.Range : 0f,   // ⭐ CE-2436 AU-7 — a pellet is spent past its range; ⭐ CE-4670 — a discharger's Range is where it BURSTS (its fuze), never a limit
                };
                repo.AddComponent(bullet, projectile);

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
                    // SD-S4 (DESIGN_Simulated_Devices sec 3.13b D-95) - the launch is the DEVICE's by seq echo: the weapon's fire-control device moved its LaunchSeq past the flight it
                    //   last echoed => this round is its (the flight is written on the device); an AI shot moves no seq and flies today's line
                    var fireControl = Fdp.Toolkit.Behavior.Devices.FireControls.ClaimLaunch(repo, shooter, bullet, missile.Guidance);
                    repo.AddComponent(bullet, new GuidedRound
                    {
                        Device        = fireControl,
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
                        TurnRateDegPerSec = missile.TurnRateDegPerSec,   // ⭐ CE-2222 — the profile TopAttackFits just fitted
                        ClimbAngleDeg     = missile.ClimbAngleDeg,
                        // ⭐ CE-10002 (AC-6) — the motor: from the launcher's own speed to SpeedMps over the burn; the g limit, the coast, the lag
                        SpeedNow      = missile.MaxG > 0f ? LaunchSpeed(repo, shooter) : 0f,
                        BurnLeftS     = missile.MaxG > 0f ? missile.BurnSeconds : 0f,
                        BoostMps2     = missile.MaxG > 0f && missile.BurnSeconds > 0f ? MathF.Max(0f, missile.SpeedMps - LaunchSpeed(repo, shooter)) / missile.BurnSeconds : 0f,
                        MaxG          = missile.MaxG,
                        DragPerS      = missile.DragPerS,
                        MinSpeedMps   = missile.MinSpeedMps,
                        SteerLagS     = missile.SteerLagS,
                        SteerDir      = velocity.LengthSquared() > 1e-6f ? Vector3.Normalize(velocity) : Vector3.UnitX,
                    });
                    // ⭐ CE-2189 (DESIGN_Missiles_And_Ammo §3c, AD4) — a GUIDED missile launched at an aircraft warns it: its brain (local, or
                    //   over the wire) learns where from, the seeker and the flight time. One warning per round, at launch.
                    //   ⭐ CE-4670 VA-S5 (behaviors, cross-lane) — a ground VEHICLE too: its crew sees the launch (the "pop smoke" trigger).
                    if (missile.Guidance != Fdp.Toolkit.Tkb.Domain.GuidanceKind.Unguided && !evt.AtPoint && repo.IsAlive(target)
                        && (Fdp.Toolkit.Motion.Locomotions.IsAircraft(repo, target) || Fdp.Toolkit.Motion.Locomotions.IsVehicle(repo, target))   // ⭐ B-6 — not a jumper
                        && repo.Bus.IsRegistered<Events.MissileWarningEvent>())
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
                // (⭐ CE-10002 — a motor round leaves at its launcher's speed: LaunchSpeed below)
                ShotLog.For(repo).Add(new ShotRecord
                {
                    Tick = currentTick, Shooter = shooter, Target = target, Bullet = bullet, WeaponIndex = evt.WeaponIndex,
                    Muzzle = muzzlePos, Aim = targetPos, Ordinal = ordinal, Sigma = sigma, Deflection = theta,
                    BurstId = evt.BurstId, BurstRounds = evt.BurstRounds, BurstIndex = evt.BurstIndex,   // ⭐ CE-11033
                    Penetration = penetration, PenetrationSource = penetrationSource, Damage = damage,
                    RaisedAt = evt.RaisedAt, FiredAt = (SimInstant)now, CaughtUpSeconds = late * BombBallistics.DefaultDt, CaughtUpFrames = late, OwedFrames = owed, FrameDt = deltaTime,   // ⭐ CE-2180
                });

                // 5. Physics collider — small sphere for broadphase candidate selection.
                repo.AddComponent(bullet, new PhysicsCollider
                {
                    Radius         = CombatConstants.BulletColliderRadius,
                    CollisionLayer = CombatConstants.BulletCollisionLayer,
                });

                // ⭐ CE-2436 AU-7 — the shotgun's other pellets: the same round from the same muzzle, each on its own place of the pattern
                for (int pellet = 1; pellet < pellets; pellet++)
                {
                    var pelletRound = repo.CreateEntity();
                    repo.AddComponent(pelletRound, new SimTransform { Position = muzzlePos, Rotation = Quaternion.Identity });
                    repo.AddComponent(pelletRound, new SimVelocity { Linear = discharger
                        ? ArcLaunch(origin, DischargerPoint(ground, targetPos, firedMount!, pellets, pellet), muzzle, high: false)
                        : PelletVelocity(aimedVelocity, pattern, ordinal, pellets, pellet), Angular = Vector3.Zero });
                    repo.AddComponent(pelletRound, projectile);
                    repo.AddComponent(pelletRound, new PhysicsCollider { Radius = CombatConstants.BulletColliderRadius, CollisionLayer = CombatConstants.BulletCollisionLayer });
                }

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
                    FiredAt     = (SimInstant)now,
                    RequestId   = evt.RequestId,   // ⭐ Q94 A-1 (R-3018) — the answer names its request
                    BurstId     = evt.BurstId, BurstRounds = evt.BurstRounds, BurstIndex = evt.BurstIndex,   // ⭐ CE-11033 (§5l) — the brain's burst, per round
                });
            }
        }

        /// <summary>
        /// ⭐ Q94 A (R-3018; Architect Question 94 §4) — the muscle's NO: a notification with no round (<see cref="WeaponFireNotification.Quantity"/>
        /// 0) and the reason, echoing the request's id. Every reader that draws, hears or fears a shot skips it
        /// (<see cref="WeaponFireNotification.Fired"/>); the brain's ledger clears the reservation and takes the reason as its hold.
        /// </summary>
        /// <summary>⭐ CE-10002 (AC-6) — the speed a motor round leaves at: its launcher's (an aircraft's), at least a tube's 30 m/s.</summary>
        public const float TubeSpeedMps = 30f;
        /// <summary>⭐ CE-4635 — the speed a flame stream is reported to leave at (m/s; the fire notification's velocity — a direction for its readers).</summary>
        public const float FlameStreamMps = 40f;
        private static float LaunchSpeed(EntityRepository repo, Entity shooter)
            => repo.IsAlive(shooter) && repo.HasComponent<SimVelocity>(shooter) ? MathF.Max(TubeSpeedMps, repo.GetComponentRO<SimVelocity>(shooter).Linear.Length()) : TubeSpeedMps;

        private static void Refuse(EntityRepository repo, in WeaponFireIntent evt, FireRefusal why, double now)
            => repo.Bus.Publish(new WeaponFireNotification
            {
                Shooter     = evt.Shooter,
                Target      = evt.Target,
                WeaponIndex = evt.WeaponIndex,
                AmmoGuid    = evt.AmmoGuid,
                Quantity    = 0,
                FiredAt     = (SimInstant)now,
                RequestId   = evt.RequestId,
                Refusal     = why,
                BurstId     = evt.BurstId, BurstRounds = evt.BurstRounds, BurstIndex = evt.BurstIndex,   // ⭐ CE-11033
            });

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
            repo.AddComponent(round, new TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)(munitionType) });
            repo.AddComponent(round, new Fdp.Toolkit.Replication.Components.LightweightRound { LaunchedAt = (SimInstant)Fdp.Toolkit.Time.SimClock.TotalTimeOf(repo) });   // ⭐ CE-11011 (RR-11) — its launch time
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

        /// <summary>
        /// ⭐ <c>CE-4649</c> (bows BW-2) — where to aim an ARROW at a MOVING target: its place after the arc's own flight time (the low solution at the
        /// mount's speed, solved three times so the flight time follows the lead). The target's velocity as this node knows it
        /// (<c>ReplicatedMotion</c>); a still target, another weapon, or no solution ⇒ <paramref name="aim"/> as it was.
        /// </summary>
        public static Vector3 LedForBow(EntityRepository repo, Entity shooter, Entity target, in WeaponFireIntent evt, Vector3 origin, Vector3 aim)
        {
            if (target.IsNull) return aim;
            var mount = CombatTkb.MountOf(repo, shooter, evt.WeaponIndex);
            if (mount is not { Category: Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bow, MuzzleVelocity: > 0f }) return aim;
            if (!Fdp.Toolkit.Replication.ReplicatedMotion.Of(repo, target, out _, out var velocity) || velocity.LengthSquared() < 1e-4f) return aim;
            var led = aim;
            for (int i = 0; i < 3; i++)
            {
                if (!FireSolution.TryArc(origin, led, mount.MuzzleVelocity, false, out _, out _, out float flight)) break;
                led = aim + velocity * flight;
            }
            return led;
        }

        /// <summary>⭐ <c>CE-2436</c> AU-7 (A-10a) — pellet <paramref name="i"/>'s velocity: the aimed one turned across and pitched by its place
        /// in the pattern (<see cref="HitModel.PelletOffset"/> × <paramref name="patternRad"/>), same speed.</summary>
        public static Vector3 PelletVelocity(Vector3 aimed, float patternRad, uint ordinal, int pellets, int i)
        {
            var (az, el) = HitModel.PelletOffset(ordinal, pellets, i);
            return HitModel.Pitch(HitModel.Rotate(aimed, patternRad * az), patternRad * el);
        }

        /// <summary>
        /// ⭐ <c>CE-4670</c> (VA-S5) — where smoke-discharger round <paramref name="i"/> of <paramref name="rounds"/> bursts: on the ground the vehicle stands on
        /// (<paramref name="vehicleAt"/>'s height), <c>Range</c> metres out (10 m when the mount states none), on the bearing from the vehicle to
        /// <paramref name="towards"/> turned by its share of the fan (<c>PelletSpreadMils</c> wide, evenly from one edge to the other; one round: the bearing).
        /// </summary>
        public static Vector3 DischargerPoint(Vector3 vehicleAt, Vector3 towards, Fdp.Toolkit.Tkb.Domain.WeaponMountDto mount, int rounds, int i)
        {
            var flat = new Vector2(towards.X - vehicleAt.X, towards.Y - vehicleAt.Y);
            float bearing = flat.LengthSquared() > 1e-6f ? MathF.Atan2(flat.Y, flat.X) : 0f;
            float fan = mount.PelletSpreadMils / 1000f;
            float a = bearing + (rounds > 1 ? ((float)i / (rounds - 1) - 0.5f) * fan : 0f);
            float range = mount.Range > 0f ? mount.Range : 10f;
            return new Vector3(vehicleAt.X + MathF.Cos(a) * range, vehicleAt.Y + MathF.Sin(a) * range, vehicleAt.Z);
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
