using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fbt;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Behavior.Executors;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;

namespace Fdp.Toolkit.Combat.Executors
{
    /// <summary>
    /// Executor for the AimAndFire weapon action.
    /// Registered with <see cref="Fdp.Toolkit.Behavior.Systems.WeaponDispatcherSystem"/> as the
    /// handler for the AimAndFire action ID.
    ///
    /// PACK-P003: Publishes WeaponFireIntent with local ECS Entity handles (Shooter and Target)
    /// instead of long network IDs. NetworkEntityMap is no longer needed here; network-ID
    /// resolution is deferred to WeaponFireIntentEgressTranslator at the network boundary.
    ///
    /// Phase 0 Adaptation: Uses SimTransform for position, consistent with the Phase 0 refactor.
    /// </summary>
    public sealed class AimAndFireExecutor : IActionExecutor<WeaponChannel>
    {
        /// <summary>
        /// ⭐ <c>CE-2075</c> — how long after being hit a <see cref="RoeFire.ReturnFire"/> unit may fire. 5 s is the window the
        /// design's own SOP example uses (<c>DESIGN_Decision_Layer.md</c> §4.3, "was hit within 5 s"). ⚠ A constant until the
        /// ROE carries its own window. ⭐ <c>CE-3064</c> (R-206): a near miss counts as being fired upon too.
        /// </summary>
        public const double ReturnFireWindowSeconds = RoeOf.DefaultReturnFireWindowSeconds;   // ⭐ CE-2095: the DEFAULT; the unit's ROE may set its own

        /// <summary>⭐ <c>CE-2075</c> — may <paramref name="shooter"/> fire under its ROE right now?</summary>
        public static bool RoePermitsFire(EntityRepository world, Entity shooter)
        {
            if (!world.IsComponentTypeRegistered<Roe>()) return true;   // no ROE in this world ⇒ fire at will
            return RoeOf.Fire(world, shooter) switch
            {
                RoeFire.HoldFire   => false,
                // ⭐ CE-3064 (R-206) — fired upon = hit OR near-missed within the window; a heard shot alone is not.
                RoeFire.ReturnFire => world.IsComponentTypeRegistered<RecentSenses>()
                                      && (RecentSensesOf.Within(world, shooter, Fdp.Toolkit.Perception.Events.SensorChange.Hit, RoeOf.ReturnFireWindowSeconds(world, shooter))
                                          || RecentSensesOf.Within(world, shooter, Fdp.Toolkit.Perception.Events.SensorChange.NearMiss, RoeOf.ReturnFireWindowSeconds(world, shooter))),
                _                  => true,
            };
        }

        // OnEnter

        public unsafe void OnEnter(Entity entity, ref WeaponChannel channel, EntityRepository world)
        {
            AimAndFireParams p;
            fixed (byte* src = channel.Params)
                p = *(AimAndFireParams*)src;

            fixed (byte* dst = channel.State)
                *(Entity*)dst = p.Target;
            AimTimer(ref channel) = default;   // ⭐ CE-3136 P-3 — a new action aims afresh
            RoundsFired(ref channel) = 0;      // ⭐ CE-3136 B7

            channel.Status = NodeStatus.Running;
        }

        // Execute

        public unsafe void Execute(Entity entity, ref WeaponChannel channel, EntityRepository world, float dt)
        {
            AimAndFireParams p;
            fixed (byte* src = channel.Params)
                p = *(AimAndFireParams*)src;

            if (p.Operator != 0) { OperatorFire(entity, ref channel, world, in p, dt); return; }   // ⭐ CE-3172 S4d (M-8)

            // ⭐ CE-466 rule (CombatLife): done when the target is gone OR knocked out (Health ≤ 0) — CE-267's revert keeps the
            //   body in the world, so existence alone never ends the action (CE-321: the squad emptied its rifles into a corpse).
            //   ⭐ CE-2318 (Injury States I4) — and done when he is DOWN (incapacitated): a man out of the fight is no target.
            if (!Injury.IsInTheFight(world, p.Target))
            {
                ReleaseTurrets(world, entity);   // ⭐ WL-3 — the turret stops following a dead target
                channel.Status = NodeStatus.Success;
                Say(world, entity, ActionReason.Done, p.Target);
                return;
            }

            // ⭐ CE-3136 B7 — the asked rounds are fired: done.
            if (p.Rounds > 0 && RoundsFired(ref channel) >= p.Rounds)
            {
                ReleaseTurrets(world, entity);
                channel.Status = NodeStatus.Success;
                Say(world, entity, ActionReason.Done, p.Target);
                return;
            }

            // ⭐ CE-3089 (G7) — the mount that fires: chosen per shot (Auto), a named mount, or the primary (0, the default). Its
            //   ammo and cooldown live on ITS WeaponState — the owner for mount 0, the mount child otherwise.
            // ⭐ WL-4 W-12a (Weapon_Layers §5f L-4) — Auto chooses ONCE per engagement (a new target, or the chosen mount ran
            //   dry), not per shot: a per-frame flip started a new turret epoch and lost a round the muscle had fired (§5e).
            Entity mountEntity = entity;
            int mountIndex = p.Mount == AimAndFireParams.MountAuto ? EngagementMount(world, entity, ref channel, p.Target, out mountEntity)
                           : p.Mount == 0 ? WeaponSystems.PrimaryMount(world, entity, out mountEntity)   // ⭐ WS-3 — a bare host's primary is its station weapon
                           : MountByIndex(world, entity, p.Mount, out mountEntity);
            WeaponPostureOf.SetActiveMount(world, entity, mountIndex);   // ⭐ ui handoff P4 (W-13e) — the weapon in his hands, on the wire
            // ⛔ CE-2188 (BS-1-DESIGN §5.4c) — a flare / chaff DISPENSER is not a weapon (an unarmed UH-60's only mount): nothing to fire with
            if (!world.HasComponent<WeaponState>(mountEntity)
                || !WeaponSystems.IsControlledIndex(mountIndex) && Countermeasures.IsDispenser(world, entity, mountIndex))
            {
                // ⭐ WS-3 — nothing to fire with (a bare host whose station is empty): a failure, never an exception
                ReleaseTurrets(world, entity);
                channel.Status = NodeStatus.Failure;
                Say(world, entity, ActionReason.Failed, p.Target);
                return;
            }
            // ⭐⭐ Weapon systems WS-3 (W-D, R-2016; DESIGN_Weapon_Systems §4a, §6) — a weapon system this unit controls (its station's
            //   M2, the Javelin it carries, the tripod it mans) fires AS ITSELF: its own mount 0, its own turret part, its own muzzle.
            //   This brain keeps the decision, the sight, the ROE and the status. A weapon system — controlled, or a sentry's own brain —
            //   fires only while its one controller is this unit and it may fire (CanFire: the gunner, the deploy state, the arc).
            int platformMount = mountIndex;
            Entity platform = WeaponSystems.IsControlledIndex(mountIndex) ? mountEntity : entity;
            if (platform != entity) platformMount = 0;
            if (WeaponSystems.IsWeaponSystem(world, platform) && SystemHold(world, entity, platform, p.Target) is { } systemHold)
            {
                ReleaseTurrets(world, entity);
                channel.Status = NodeStatus.Running;
                Say(world, entity, systemHold, p.Target);
                return;
            }
            // ⭐ CE-3232 (B-10, §7.11 LD-3) — a LAID-DOWN built-in mount fires for nobody: held, "not deployed"
            if (platform == entity && WeaponLaying.IsLaidDown(world, entity, mountIndex))
            {
                ReleaseTurrets(world, entity);
                channel.Status = NodeStatus.Running;
                Say(world, entity, ActionReason.NotDeployed, p.Target);
                return;
            }
            ref var weapon = ref world.GetComponentRW<WeaponState>(mountEntity);
            if (weapon.Ammo == 0)
            {
                ReleaseTurrets(world, entity);
                channel.Status = NodeStatus.Failure;
                Say(world, entity, ActionReason.OutOfAmmo, p.Target);
                return;
            }

            // ⭐⭐ WL-3 (DESIGN_Weapon_Layers §5e, W-1/W-4) — a turreted mount is fought through its turret's WeaponTask: this
            //   weapon brain keeps the gates (target, rounds, ammo, magazine, ROE, seen-now) and GRANTS rounds; the weapon muscle
            //   aims, times and fires them. The profile decides (W-5); the per-shot path below is unchanged for the rest.
            var mountDto = CombatTkb.MountOf(world, platform, platformMount);
            var profile = WeaponBrainProfiles.For(mountDto?.Category ?? Fdp.Toolkit.Tkb.Domain.WeaponCategory.Unknown);   // ⭐ W-12b
            // ⭐⭐ M8 (D-I; DESIGN_Missiles_And_Ammo §2) — the ROUND for this engagement, beside the mount: chosen once per target from
            //   the mount's loadout with the one ArmorModel (AmmoChoice); a different round costs one reload, and the weapon holds
            //   meanwhile. A mount with no loadout never switches. The behaviour never names a round (R-261).
            bool switching = AmmoChoice.Choose(world, mountEntity, mountDto, platform, p.Target, weapon.ReloadSeconds, Math.Max(1, (int)p.Rounds));
            long loadedAmmo = AmmoChoice.LoadedAmmo(world, mountEntity);
            // ⭐⭐ M4 / D-L (DESIGN_Missiles_And_Ammo §3–§4) — the weapon brain's two new holds, on BOTH paths, before a round is
            //   granted: a missile below its minimum range (it cannot arm or turn in time), and a weapon that does not fire on the
            //   move while its unit moves (an ATGM, a mortar — HitModel.IsMoving, the same test that doubles the spread).
            ActionReason? hold = switching ? ActionReason.Reloading : HoldOf(world, entity, p.Target, mountDto, loadedAmmo);
            if (profile.UsesTurretTask(world, platform, mountDto))
            {
                TurretPath(entity, ref channel, world, in p, platformMount, TurretParts.Find(world, platform, mountDto!.TurretIndex), ref weapon,
                           profile.RoundsInFlight, mountDto, dt, mountEntity, loadedAmmo, hold);
                return;
            }
            if (hold is { } held)
            {
                channel.Status = NodeStatus.Running;
                if (held == ActionReason.Reloading) Say(world, entity, held, p.Target, 0f, weapon.ReloadSeconds);
                else Say(world, entity, held, p.Target);
                return;
            }

            // ⭐⭐ CE-3136 P-3 (peek-and-fire D3 + D4) — an aimed shot needs the target SEEN NOW (D4), continuously for the aim time
            //   (D3). The aim clock runs beside the cooldown, never after it; it restarts on lost sight, a new target or the
            //   shooter's stance change, and OnEnter starts it fresh. Once aimed, later rounds need only the cooldown. Not seen ⇒
            //   hold (Running, no round spent), like the ROE hold. Blind fire (FireAtPoint) has neither gate.
            ref var aim = ref AimTimer(ref channel);
            // ⭐ Air Ops slice 4 — a BOMB is released on its cue alone (HoldOf held it until now): the run-in was flown at the target's
            //   known position, and a release has no aim clock, no body twist and no line of fire to a friend.
            bool bomb = mountDto?.Category == Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb;
            bool seen = bomb || Fdp.Toolkit.Perception.SightNow.Sees(world, entity, p.Target);
            // ⭐⭐ WL-4 W-12e (Weapon_Layers §5f L-2) — not seen: the weapon brain may THROW at where the target was last seen (a
            //   grenade over the cover it hides behind). It counts as a round of this action; nothing else changes.
            if (!seen && HiddenTargetThrow.TryThrow(world, entity, p.Target))
            {
                aim = aim.Restarted(default, 0);
                int thrown = ++RoundsFired(ref channel);
                channel.Status = p.Rounds > 0 && thrown >= p.Rounds ? NodeStatus.Success : NodeStatus.Running;
                Say(world, entity, ActionReason.Throwing, p.Target, thrown, p.Rounds);
                return;
            }
            if (!seen)
                aim = aim.Restarted(default, 0);
            else
            {
                byte stance = (byte)ShooterStance(world, entity);
                if (aim.Target != p.Target || aim.Stance != stance)
                    aim = aim.Restarted(p.Target, stance);
                if (aim.Ready == 0)
                {
                    aim.Elapsed += dt;
                    // ⭐ CE-2323 (R-2063) — each man's own aim time at this target, so a volley does not leave on one frame
                    if (bomb || aim.Elapsed >= PacedAimSecondsFor(world, platform, platformMount, p.Target)) aim.Ready = 1;
                }
            }

            // Fire only after the cooldown reaches zero. ⭐ CE-2166 — WeaponDispatcherSystem drains it, once a frame for every weapon
            //   and mount (it already did): this drained it AGAIN while an aim ran, so a 10 s cooldown fired every ~5 s.
            if (weapon.CooldownSecondsRemaining > 0f)
            {
                channel.Status = NodeStatus.Running;
                if (!seen && p.BlindFire != 0) Say(world, entity, ActionReason.BlindFire, p.Target, RoundsFired(ref channel), p.Rounds);
                else if (!seen || aim.Ready == 0) SayAim(world, entity, p.Target, seen, in aim, platform, platformMount);
                else Say(world, entity, ActionReason.Cooldown, p.Target, weapon.CooldownSecondsRemaining);
                return;
            }

            // ⭐⭐ AQ89 A (R-269) — SUPPRESS: not seen and nothing thrown ⇒ slow rounds at where the memory last placed the target,
            //   instead of holding. They strike the cover he hides behind and stamp UnderFire on him (a near miss — AQ85 E).
            if (!seen && p.BlindFire != 0)
            {
                FireBlind(entity, ref channel, world, in p, platformMount, mountDto, ref weapon, mountEntity, loadedAmmo, platform);
                return;
            }

            if (!seen || aim.Ready == 0)
            {
                channel.Status = NodeStatus.Running;
                SayAim(world, entity, p.Target, seen, in aim, platform, platformMount);
                return;
            }

            // ⭐ CE-3136 P-5 (D9) — an empty magazine RELOADS instead of failing (the dispatcher runs the reload every frame, so it
            //   also finishes while the unit is hidden); the action holds meanwhile. Out of ammunition altogether is still Failure.
            if (!Magazine.Ready(weapon))
            {
                Magazine.StartIfEmpty(ref weapon);
                channel.Status = NodeStatus.Running;
                // ⭐ Q94 A-3 — every round left is ASKED and on its way (the muscle's answer has not come back yet): not a reload
                if (BarrelHeat.IsOverheated(weapon)) Say(world, entity, ActionReason.Overheated, p.Target, weapon.Heat, BarrelHeat.ResumeFraction);   // ⭐ CE-1358
                else if (ShotLedger.Pending(weapon) > 0 && !Magazine.Reloading(weapon)) Say(world, entity, ActionReason.Cooldown, p.Target, 0f);
                else Say(world, entity, ActionReason.Reloading, p.Target, weapon.ReloadSeconds - weapon.ReloadSecondsRemaining, weapon.ReloadSeconds);
                return;
            }

            // ⭐⭐ CE-3195 (R-3003, W-14a) — a person fires only within his twist of his heading: beyond it the round is held (Running,
            //   nothing spent) and the posture stays Aimed, so the Muscle's gait turns him. The aim clock keeps running — the shot
            //   leaves the moment he is round (a quick shot at a target that appears behind him).
            if (!bomb && BodyAim.TargetPoint(world, p.Target, out var targetAt) && BodyAim.HoldsFire(world, entity, targetAt))
            {
                channel.Status = NodeStatus.Running;
                Say(world, entity, ActionReason.Turning, p.Target);
                return;
            }

            // ⭐ CE-321 — hold fire while a friendly is on the line (LineOfFire): the action keeps running and no round is spent,
            //   so a unit that moves clear (or a friendly that moves away) fires again.
            if (!bomb && LineOfFire.BlockedByFriendly(world, platform, p.Target))
            {
                channel.Status = NodeStatus.Running;
                Say(world, entity, ActionReason.HoldFriendlyOnLine, p.Target);
                return;
            }
            // ⭐ AW-20 (R-2026; DESIGN_Civilians_And_Crowd_Control §5h) — nor while a NEUTRAL person is on the line or inside the spread
            //   at his range: held the same way, so the round leaves the moment he is clear
            if (!bomb && LineOfFire.BlockedByNeutral(world, platform, p.Target, HitModel.Sigma(world, platform, mountDto, HitModel.Now(world))))
            {
                channel.Status = NodeStatus.Running;
                Say(world, entity, ActionReason.HoldNeutralOnLine, p.Target);
                return;
            }

            // ⭐ CE-2218 → ⭐⭐ Q94 A-4 (R-3018, R-2037) — the round that would stop in the wall in front of the muzzle is now REFUSED by the
            //   MUSCLE (FireProcessingSystem, live terrain); its refusal holds this weapon a moment (ShotLedger.Held). Running, nothing
            //   spent — still Masked, so the behaviour may move to a better spot. The brain no longer reads the terrain for it.
            if (ShotLedger.Held(in weapon, out var refused))
            {
                channel.Status = NodeStatus.Running;
                Say(world, entity, HoldReasonOf(refused), p.Target);
                return;
            }

            // ⭐ CE-2075 (R-200) — the unit's ROE, enforced ONCE here so no behaviour can forget it. HoldFire never fires;
            //   ReturnFire fires only within ReturnFireWindowSeconds of being hit. Holds like the friendly-line guard (Running, no
            //   round spent), so an ROE change or a fresh hit resumes fire without re-issuing the action.
            if (!RoePermitsFire(world, entity))
            {
                channel.Status = NodeStatus.Running;
                Say(world, entity, ActionReason.HoldRoe, p.Target);
                return;
            }

            world.Bus.Publish(new WeaponFireIntent
            {
                Shooter     = platform,        // ⭐ WS-3 — a controlled weapon system's round leaves its own muzzle
                Target      = p.Target,
                WeaponIndex = platformMount,
                AmmoGuid    = loadedAmmo,   // ⭐ M8 — the loaded round (0 = the mount's own)
                RequestId   = ShotLedger.Ask(ref weapon),   // ⭐⭐ Q94 A-3 (R-3018) — ASKED: reserved now, spent when the muscle says it flew
                // ⭐ CE-2180 — when it was raised; a bomb: the moment its cue says the release IS (the SimHost starts it from there)
                RaisedAt    = bomb && world.HasComponent<SimTransform>(p.Target)
                              ? ReleaseAt(world, entity, world.GetComponentRO<SimTransform>(p.Target).Position) : HitModel.Now(world),
            });

            // ⭐ CE-3136 P-5 / M8 — the total, the magazine and the loaded type are spent by the muscle's ANSWER (ShotLedger.Answer), not here
            // ⭐ CE-2199 — 0 = the mount's own rate · ⭐ CE-2323 (R-2063) — each gap his own, so men do not stay in step
            weapon.CooldownSecondsRemaining = FirePacing.Gap(CooldownFor(p.CooldownSeconds, mountDto), platform, RoundsFired(ref channel), channel.ActionInstanceId);

            int fired = ++RoundsFired(ref channel);
            channel.Status = p.Rounds > 0 && fired >= p.Rounds ? NodeStatus.Success : NodeStatus.Running;
            Say(world, entity, ActionReason.Firing, p.Target, fired, p.Rounds);
        }

        /// <summary>
        /// ⭐ <c>CE-2199</c> — the interval between two rounds of a burst: the one the behaviour asked for, or — asked 0 ("the weapon's own",
        /// as <c>VehicleFightFromCover</c> and <c>AirSense.FireAt</c> pass it) — the FIRED mount's own 60 / <c>RateOfFire</c> (only the
        /// executor knows which mount <c>MountAuto</c> picked: a tank's 120 mm 10 s, its coax 0.1 s). A mount stating no rate keeps 0. 🔴 It
        /// used to take 0 as given: a tank fired 8 main-gun rounds in ~1.3 s against a catalogue 6 per minute (live <c>ua-tank-corner</c>).
        /// </summary>
        /// <summary>
        /// ⭐ <c>Q94 A</c> (R-3018) — the hold a weapon's last <see cref="FireRefusal"/> reads as on its action (the same words the brain's
        /// own checks said before the muscle made them): the round masked by the wall in front of the muzzle, too close or out of reach for
        /// its arc, a bomb whose release cue passed, or a mount that could not fire at all.
        /// </summary>
        public static ActionReason HoldReasonOf(FireRefusal refusal) => refusal switch
        {
            FireRefusal.Masked => ActionReason.Masked,
            FireRefusal.TooClose => ActionReason.TooClose,
            FireRefusal.TooFar => ActionReason.OutOfRange,
            FireRefusal.Expired => ActionReason.AwaitingRelease,
            FireRefusal.CannotFire => ActionReason.NotDeployed,
            _ => ActionReason.Failed,
        };

        public static float CooldownFor(float asked, Fdp.Toolkit.Tkb.Domain.WeaponMountDto? mount)
            => asked > 0f ? asked : mount is { RateOfFire: > 0f } m ? 60f / m.RateOfFire : 0f;

        /// <summary>
        /// ⭐⭐ <c>CE-3172</c> S4d (M-8, C-2; docs/DESIGN_Entity_Control.md §8b) — OPERATOR fire: a possessing station aims at
        /// <see cref="AimAndFireParams.Point"/> and fires while <see cref="AimAndFireParams.Trigger"/> is held. The operator is the
        /// eyes and the decision: no seen-now gate, no aim time, no ROE hold. The rules that are not decisions stay — the ammunition,
        /// the magazine, the cooldown, and no direct round through a friend. A turreted mount is aimed through its turret's
        /// <see cref="WeaponTask"/> at the point (the muscle slews and times the rounds, as for the AI); any other mount fires the
        /// per-shot round at a point (<see cref="FireAtPointExecutor.ShootAtPoint"/>). Never ends on its own: the operator
        /// behaviour stops it.
        /// </summary>
        private static void OperatorFire(Entity entity, ref WeaponChannel channel, EntityRepository world, in AimAndFireParams p, float dt)
        {
            channel.Status = NodeStatus.Running;
            Entity mountEntity = entity;
            int mountIndex = p.Mount == 0 || p.Mount == AimAndFireParams.MountAuto ? WeaponSystems.PrimaryMount(world, entity, out mountEntity)
                           : MountByIndex(world, entity, p.Mount, out mountEntity);
            WeaponPostureOf.SetActiveMount(world, entity, mountIndex);   // ⭐ CE-1222 — the weapon the OPERATOR picked, on the wire (as the AI path, :102)
            if (!world.HasComponent<WeaponState>(mountEntity)) return;
            ref var weapon = ref world.GetComponentRW<WeaponState>(mountEntity);
            // ⭐ WS-3 — an operator firing a controlled weapon system fires it as itself (its mount 0, turret, muzzle), when it may fire
            Entity platform = entity;
            if (WeaponSystems.IsControlledIndex(mountIndex)) { platform = mountEntity; mountIndex = 0; }
            if (WeaponSystems.IsWeaponSystem(world, platform) && SystemHold(world, entity, platform, Entity.Null) is { } systemHold)
            {
                ReleaseTurrets(world, entity);
                SayAt(world, entity, systemHold, p.Point);
                return;
            }
            if (platform == entity && WeaponLaying.IsLaidDown(world, entity, mountIndex))   // ⭐ CE-3232 (LD-3)
            {
                ReleaseTurrets(world, entity);
                SayAt(world, entity, ActionReason.NotDeployed, p.Point);
                return;
            }

            var mountDto = CombatTkb.MountOf(world, platform, mountIndex);
            var profile = WeaponBrainProfiles.For(mountDto?.Category ?? Fdp.Toolkit.Tkb.Domain.WeaponCategory.Unknown);
            long loadedAmmo = AmmoChoice.LoadedAmmo(world, mountEntity);   // ⭐ M8 — what is loaded
            // ⭐ CE-1079 (§8e D-3) — the operator's round switch holds the weapon like a reload, on both paths
            bool switching = AmmoChoice.Switching(world, mountEntity);
            if (profile.UsesTurretTask(world, platform, mountDto))
            {
                TurretPath(entity, ref channel, world, in p, mountIndex, TurretParts.Find(world, platform, mountDto!.TurretIndex), ref weapon,
                           profile.RoundsInFlight, mountDto, dt, mountEntity, loadedAmmo, switching ? ActionReason.Reloading : null);
                return;
            }

            // ⭐ CE-1078 (§8e D-1) — a locked shot aims at its unit (followed as it moves); else the station's point.
            //   ⭐ CE-1125 / CE-1126 — AT THE POINT THE CROSSHAIR PUT ON HIS PART (the brain carries it with him); the middle only with no point.
            var point = !p.Target.IsNull && world.IsAlive(p.Target) && world.HasComponent<SimTransform>(p.Target)
                ? AimPoint.Operator(world, p.Target, p.Point) : p.Point;
            if (weapon.Ammo == 0) { SayAt(world, entity, ActionReason.OutOfAmmo, point); return; }
            if (switching) { SayAt(world, entity, ActionReason.Reloading, point); return; }
            // ⭐ CE-1078 — in operator mode Rounds is a CAP on the action's rounds (one locked shot = fired + 1), never an end
            bool capped = p.Rounds > 0 && RoundsFired(ref channel) >= p.Rounds;
            if (p.Trigger == 0 || capped)
            {
                Magazine.StartIfEmpty(ref weapon);   // ⭐ CE-2166 — the cooldown drains in the dispatcher, not here too
                SayAt(world, entity, ActionReason.Aiming, point);
                return;
            }
            // ⭐ CE-1358 (§5k) — the operator asks 0 = the mount's own cyclic rate (CooldownFor); the barrel heat blocks the bursts
            float gap = FirePacing.Gap(CooldownFor(p.CooldownSeconds, mountDto), platform, RoundsFired(ref channel), channel.ActionInstanceId);
            var reason = FireAtPointExecutor.ShootAtPoint(world, platform, mountIndex, ref weapon, point, gap, dt, obeyRoe: false, obeyArc: false,
                                                          ammo: loadedAmmo);
            if (reason == ActionReason.Firing)
            {
                RoundsFired(ref channel)++;   // ⭐ CE-1079 / Q94 A-3 — the loaded type's count is spent by the muscle's answer (ShotLedger)
            }
            SayAt(world, entity, reason, point);
        }

        private static void SayAt(EntityRepository world, Entity unit, ActionReason reason, Vector3 point)
        {
            ActionStatusOf.Weapon(world, unit, CombatConstants.ActionIdAimAndFire, reason, Entity.Null);
            WeaponPostureOf.Report(world, unit, reason, Entity.Null, point);
        }

        /// <summary>⭐ AQ89 A — a blind round aims this far above the remembered ground point: a crouched man's middle, so it strikes
        /// a wall he crouches behind (a near miss) rather than flying over it.</summary>
        public const float BlindAimHeightMetres = 0.5f;

        /// <summary>⭐ AQ89 A — blind rounds go this many times slower than aimed ones (bursts with pauses, not a magazine dump).</summary>
        public const float BlindCooldownFactor = 2.5f;

        /// <summary>
        /// ⭐⭐ AQ89 A (R-269) — one suppressive round at the hidden target's last-known point: the at-point path a peek's blind burst
        /// flies (<see cref="WeaponFireIntent.AtPoint"/>), with the aimed shot's gates — a live, identified memory slot
        /// (<see cref="HiddenTargetThrow.LastKnown"/>), the magazine, the friendly line to the POINT, the ROE. An indirect mount
        /// never fires blind (that is the throw's decision, W-12e). It counts as a round of this action.
        /// </summary>
        /// <summary>
        /// ⭐ <c>CE-2200</c> (DESIGN_Missiles_And_Ammo §3, M4 D-D) — does this mount LOCK before it fires: a guided round (its
        /// <see cref="Fdp.Toolkit.Tkb.Domain.MissileDto"/>, any guidance but <c>Unguided</c>) from a mount that declares an aim time
        /// (the lock clock). Such a mount never fires a blind round — it has nothing to lock on. An RPG / LAW (unguided) keeps its blind round.
        /// </summary>
        public static bool NeedsLock(EntityRepository world, Fdp.Toolkit.Tkb.Domain.WeaponMountDto? mountDto, long loadedAmmo)
            => mountDto is { AimSeconds: > 0f }
               && CombatTkb.MissileOf(world, loadedAmmo != 0 ? loadedAmmo : unchecked((long)mountDto.AmmoGuid))
                  is { Guidance: not Fdp.Toolkit.Tkb.Domain.GuidanceKind.Unguided };

        private static void FireBlind(Entity entity, ref WeaponChannel channel, EntityRepository world, in AimAndFireParams p,
                                      int mountIndex, Fdp.Toolkit.Tkb.Domain.WeaponMountDto? mountDto, ref WeaponState weapon,
                                      Entity mountEntity, long loadedAmmo, Entity shooter)
        {
            channel.Status = NodeStatus.Running;
            // ⭐ CE-2200 — nor does a mount that LOCKS: a blind round has no lock (M4 D-D, "the lock IS the aim time")
            if (CombatTkb.WarheadOf(world, mountDto) is { Indirect: true } || NeedsLock(world, mountDto, loadedAmmo)
                || !HiddenTargetThrow.LastKnown(world, entity, p.Target, out var feet, out byte seenStance))
            {
                Say(world, entity, ActionReason.HoldNotSeen, p.Target);
                return;
            }
            // the brain's own checks below judge the line to a crouched man's middle there; ⭐⭐ CE-2326 (R-3030; Peek and Fire §11.6a) —
            // when the memory knows the stance he was SEEN in, the round itself names that body (BodyStance) and the MUSCLE aims it (its
            // chest, or the band that showed over a cover that stops a round). ⛔ Before: always the spot + 0.5 m, blind to the cover.
            var point = feet with { Z = feet.Z + BlindAimHeightMetres };
            if (!Magazine.Ready(weapon))
            {
                Magazine.StartIfEmpty(ref weapon);
                if (BarrelHeat.IsOverheated(weapon)) Say(world, entity, ActionReason.Overheated, p.Target, weapon.Heat, BarrelHeat.ResumeFraction);   // ⭐ CE-1358
                else Say(world, entity, ActionReason.Reloading, p.Target, weapon.ReloadSeconds - weapon.ReloadSecondsRemaining, weapon.ReloadSeconds);
                return;
            }
            // ⭐ CE-3195 (W-14a) — blind rounds too wait for the body to face where they go
            //   (aimed at the remembered POINT, so the gait turns him to where the round goes, not to where the hidden man really is)
            if (BodyAim.HoldsFire(world, entity, point)) { SayAt(world, entity, ActionReason.Turning, point); return; }
            if (BodyAim.OutsideFixedCone(world, entity, mountDto, point)) { SayAt(world, entity, ActionReason.OutOfArc, point); return; }   // ⭐ Air Ops 6a (AW-1)
            if (LineOfFire.BlockedByFriendly(world, shooter, point)) { Say(world, entity, ActionReason.HoldFriendlyOnLine, p.Target); return; }
            if (LineOfFire.BlockedByNeutral(world, shooter, point, HitModel.Sigma(world, shooter, mountDto, HitModel.Now(world))))
            { Say(world, entity, ActionReason.HoldNeutralOnLine, p.Target); return; }   // ⭐ AW-20
            if (ShotLedger.Held(in weapon, out var refused)) { Say(world, entity, HoldReasonOf(refused), p.Target); return; }   // ⭐ CE-2218 → Q94 A-4: the muscle's refusal
            if (Fdp.Toolkit.Behavior.Components.EngagementAreas.IsNoFire(world, entity, point)) { SayAt(world, entity, ActionReason.HoldNoFireArea, point); return; }   // ⭐ CE-2282
            if (!RoePermitsFire(world, entity)) { Say(world, entity, ActionReason.HoldRoe, p.Target); return; }

            world.Bus.Publish(new WeaponFireIntent
            {
                Shooter     = shooter,   // ⭐ WS-3 — the controlled weapon system itself, else the unit
                Target      = Entity.Null,
                WeaponIndex = mountIndex,
                AtPoint     = true,
                TargetPoint = seenStance != 0 ? feet : point,
                BodyStance  = seenStance,   // ⭐ CE-2326
                AmmoGuid    = loadedAmmo,   // ⭐ M8 — the loaded round
                RaisedAt    = HitModel.Now(world),   // ⭐ CE-2180
                RequestId   = ShotLedger.Ask(ref weapon),   // ⭐⭐ Q94 A-3 — asked; the muscle's answer spends it
            });
            weapon.CooldownSecondsRemaining = FirePacing.Gap(MathF.Max(p.CooldownSeconds, 0.1f) * BlindCooldownFactor, shooter,   // ⭐ CE-2323
                                                             RoundsFired(ref channel), channel.ActionInstanceId);
            int fired = ++RoundsFired(ref channel);
            if (p.Rounds > 0 && fired >= p.Rounds) channel.Status = NodeStatus.Success;
            Say(world, entity, ActionReason.BlindFire, p.Target, fired, p.Rounds);
        }

        /// <summary>
        /// ⭐ WL-3 (§5e) — the turret path. The task's epoch moves only with a new target or mount, so a Track ↔ Engage switch keeps
        /// it and the muscle's <see cref="WeaponTaskStatus.RoundsFired"/> keeps counting; every round it reports is spent HERE once
        /// (magazine, total, the action's count — W-4). The grant is what was fired under this epoch plus what the magazine holds
        /// now (capped by the rounds asked), so it stays constant while rounds are in flight and drops to Track for a reload.
        /// </summary>
        /// <summary>
        /// ⭐ <c>CE-1307</c> (docs/DESIGN_Weapon_Layers.md W-8b) — the operator's point, moved inside the turret's reach: aimed below its
        /// depression (a chase camera looks down past the hull) or outside its traverse, the turret goes to its limit and stays there,
        /// instead of the muscle failing <c>OutOfArc</c> and the turret freezing. A brain's aim is never clamped (W-8).
        /// </summary>
        private static Vector3 OperatorPoint(EntityRepository world, Entity part, Fdp.Toolkit.Tkb.Domain.WeaponMountDto? mountDto, Vector3 point)
        {
            if (mountDto is not { TurretIndex: >= 0 } || part.IsNull || !world.HasComponent<Fdp.Toolkit.Replication.Components.PartMetadata>(part)) return point;
            var hull = world.GetComponent<Fdp.Toolkit.Replication.Components.PartMetadata>(part).ParentEntity;
            return world.IsAlive(hull) && CombatTkb.TurretOf(world, hull, mountDto.TurretIndex) is { } turret
                ? BallisticAim.Reachable(world, hull, turret, mountDto, point) : point;
        }

        /// <summary>⭐ <c>CE-1359</c> (W-8c) — the operator's point is beyond the turret's limited traverse (<see cref="BallisticAim.BeyondTraverse"/>).</summary>
        private static bool OperatorBeyondTraverse(EntityRepository world, Entity part, Fdp.Toolkit.Tkb.Domain.WeaponMountDto? mountDto, Vector3 point)
        {
            if (mountDto is not { TurretIndex: >= 0 } || part.IsNull || !world.HasComponent<Fdp.Toolkit.Replication.Components.PartMetadata>(part)) return false;
            var hull = world.GetComponent<Fdp.Toolkit.Replication.Components.PartMetadata>(part).ParentEntity;
            return world.IsAlive(hull) && CombatTkb.TurretOf(world, hull, mountDto.TurretIndex) is { } turret
                && BallisticAim.BeyondTraverse(world, hull, turret, mountDto, point);
        }

        private static void TurretPath(Entity entity, ref WeaponChannel channel, EntityRepository world, in AimAndFireParams p,
                                       int mountIndex, Entity part, ref WeaponState weapon,
                                       int roundsInFlight = 0, Fdp.Toolkit.Tkb.Domain.WeaponMountDto? mountDto = null, float dt = 0f,
                                       Entity mountEntity = default, long loadedAmmo = 0L, ActionReason? held = null)
        {
            // ⭐ W-12c (§5g G-2/G-3) — a guided mount's hold: the estimated flight of the last round it saw fired. The aim clock is
            //   free on this path (the muscle aims), so it carries the hold.
            ref var aimState = ref AimTimer(ref channel);
            ref var hold = ref aimState.Elapsed;
            var current = world.HasComponent<WeaponTask>(part) ? world.GetComponent<WeaponTask>(part) : default;
            // ⭐ CE-3172 S4d — operator fire aims at a POINT that moves every frame; only a new mount (or an idle task) is a new
            //   order, so the rounds in flight keep counting while the operator sweeps.
            // ⭐ CE-1078 (§8e D-1) — an operator may LOCK a shot on a unit (Target set): the muscle then follows the unit, as for the AI.
            bool op = p.Operator != 0;
            bool atPoint = op && p.Target.IsNull;
            bool fresh = current.Mode == WeaponTaskMode.Idle || current.Target != p.Target
                      || current.MountIndex != (byte)mountIndex;
            // ⭐⭐ M4 (D-D: "the lock IS the aim time") — a turret mount that DECLARES an aim time locks before its first grant: the
            //   target seen continuously that long, the clock restarting on lost sight or a new target. The aim clock carries it (Ready
            //   = locked); once locked the same clock carries the guided-flight hold, as before. A mount with no AimSeconds never locks.
            float lockSeconds = !op && mountDto is { AimSeconds: > 0f } ? mountDto.AimSeconds : 0f;   // C-2: the operator has no aim time
            if (lockSeconds > 0f && (fresh && current.Target != p.Target || aimState.Target != p.Target))
                aimState = aimState.Restarted(p.Target, aimState.Stance);
            bool locking = lockSeconds > 0f && aimState.Ready == 0;
            if (!locking && hold > 0f) hold = MathF.Max(0f, hold - dt);
            uint epoch = fresh ? current.Epoch + 1 : current.Epoch;
            ref int accounted = ref Accounted(ref channel);
            if (fresh) accounted = 0;

            var status = world.IsComponentTypeRegistered<WeaponTaskStatus>() && world.HasComponent<WeaponTaskStatus>(part)
                ? world.GetComponent<WeaponTaskStatus>(part) : default;
            bool mine = status.Epoch == epoch && !fresh;
            if (mine)
            {
                for (int n = status.RoundsFired - accounted; n > 0 && weapon.Ammo > 0; n--)
                {
                    Magazine.Spend(ref weapon);
                    if (!mountEntity.IsNull) AmmoChoice.SpendLoaded(world, mountEntity);   // ⭐ M8
                    RoundsFired(ref channel)++;
                    if (roundsInFlight > 0) hold = GuidedFlightSeconds(world, entity, p.Target, mountDto, weapon.MuzzleVelocity, loadedAmmo);
                }
                if (status.RoundsFired > accounted) accounted = status.RoundsFired;
                if (status.State == WeaponTaskState.Failed)
                {
                    ReleaseTurrets(world, entity);
                    channel.Status = NodeStatus.Failure;
                    Say(world, entity, status.Reason == WeaponTaskFailure.OutOfArc ? ActionReason.OutOfArc : ActionReason.Failed, p.Target);
                    return;
                }
            }
            // ⭐ CE-1078 — for the OPERATOR, Rounds is a cap (the grant below stops at it); the action never ends on it
            if (!op && p.Rounds > 0 && RoundsFired(ref channel) >= p.Rounds)
            {
                ReleaseTurrets(world, entity);
                channel.Status = NodeStatus.Success;
                Say(world, entity, ActionReason.Done, p.Target);
                return;
            }

            // ⭐ CE-3172 S4d (C-2) — the operator is the eyes and overrides the ROE: the trigger is the only gate
            bool seen = op ? p.Trigger != 0 : Fdp.Toolkit.Perception.SightNow.Sees(world, entity, p.Target);
            bool roe = op || RoePermitsFire(world, entity);
            Magazine.StartIfEmpty(ref weapon);
            bool loaded = Magazine.Ready(weapon) && held != ActionReason.Reloading;   // ⭐ M8 — a round switch holds the grants like a reload
            if (locking)
            {
                if (seen) hold += dt; else hold = 0f;
                if (hold >= lockSeconds) { aimState.Ready = 1; hold = 0f; locking = false; }
            }
            bool otherHold = held is { } h && h != ActionReason.Reloading;   // ⭐ M4 too close · D-L moving
            // ⭐ CE-1359 (W-8c) — the operator's crosshair beyond a door gun's traverse: the gun turns to its limit and holds its fire
            bool beyond = atPoint && OperatorBeyondTraverse(world, part, mountDto, p.Point);
            int grant = accounted;
            if (seen && roe && loaded && !locking && !otherHold && !beyond)
            {
                int inHand = weapon.MagazineSize > 0 ? weapon.MagazineRounds : weapon.Ammo;
                if (p.Rounds > 0) inHand = System.Math.Min(inHand, p.Rounds - RoundsFired(ref channel));
                // ⭐ W-12c — a guided weapon is granted its next round only once the last one's flight is over.
                if (roundsInFlight > 0) inHand = hold > 0f ? 0 : System.Math.Min(inHand, roundsInFlight);
                inHand = System.Math.Min(inHand, BarrelHeat.RoundsLeft(weapon));   // ⭐ CE-1358 (§5k) — the burst stops at the round that makes it too hot
                grant += System.Math.Max(0, inHand);
            }

            var next = new WeaponTask
            {
                Mode = grant > accounted ? WeaponTaskMode.Engage : WeaponTaskMode.Track,
                Target = p.Target,
                Point = atPoint ? OperatorPoint(world, part, mountDto, p.Point) : default,
                MountIndex = (byte)mountIndex,
                RoundsGranted = (ushort)System.Math.Min(grant, ushort.MaxValue),
                CooldownSeconds = CooldownFor(p.CooldownSeconds, mountDto),   // ⭐ CE-2199 — the muscle times the rounds by it
                Epoch = epoch,
                AmmoGuid = unchecked((ulong)loadedAmmo),   // ⭐ M8 — the loaded round (0 = the mount's own); it rides EntityWeaponTask
                AlongBarrel = atPoint ? p.AlongBarrel : (byte)0,   // ⭐ CE-4143 (§8r) — a player's SIGHT shot fires along the barrel
            };
            if (fresh || !current.Equals(next)) world.SetComponent(part, next);

            channel.Status = NodeStatus.Running;
            if (op)
            {
                SayAt(world, entity, beyond && p.Trigger != 0 ? ActionReason.OutOfArc   // ⭐ CE-1359
                    : BarrelHeat.IsOverheated(weapon) ? ActionReason.Overheated   // ⭐ CE-1358
                    : !loaded ? ActionReason.Reloading : p.Trigger == 0 ? ActionReason.Aiming
                    : mine && status.State == WeaponTaskState.Slewing ? ActionReason.Slewing : ActionReason.Firing, p.Point);
                return;
            }
            if (!seen) Say(world, entity, ActionReason.HoldNotSeen, p.Target);
            else if (!roe) Say(world, entity, ActionReason.HoldRoe, p.Target);
            else if (otherHold) Say(world, entity, held!.Value, p.Target);
            else if (held == ActionReason.Reloading) Say(world, entity, ActionReason.Reloading, p.Target, 0f, weapon.ReloadSeconds);
            else if (BarrelHeat.IsOverheated(weapon)) Say(world, entity, ActionReason.Overheated, p.Target, weapon.Heat, BarrelHeat.ResumeFraction);   // ⭐ CE-1358
            else if (!loaded) Say(world, entity, ActionReason.Reloading, p.Target, weapon.ReloadSeconds - weapon.ReloadSecondsRemaining, weapon.ReloadSeconds);
            else if (locking) Say(world, entity, ActionReason.Aiming, p.Target, hold, lockSeconds);
            else if (mine && status.State == WeaponTaskState.Slewing) Say(world, entity, ActionReason.Slewing, p.Target);
            else if (roundsInFlight > 0 && hold > 0f) Say(world, entity, ActionReason.Guiding, p.Target, hold);
            else Say(world, entity, ActionReason.Firing, p.Target, RoundsFired(ref channel), p.Rounds);
        }

        /// <summary>
        /// ⭐ M4 / D-L — the weapon brain's holds before a round leaves: <see cref="ActionReason.TooClose"/> when the round is a
        /// missile and the target is inside its <c>MinRangeM</c>; <see cref="ActionReason.HoldMoving"/> when the mount does not fire on
        /// the move (<see cref="CombatTkb.FiresOnTheMove"/>) and the unit moves; <see cref="ActionReason.AwaitingRelease"/> for a bomb
        /// before its cue; <see cref="ActionReason.OutOfArc"/> for a fixed mount whose target is outside its nose cone (Air Ops 6a). Null = fire.
        /// </summary>
        public static ActionReason? HoldOf(EntityRepository world, Entity unit, Entity target, Fdp.Toolkit.Tkb.Domain.WeaponMountDto? mount, long loadedAmmo)
        {
            long ammo = loadedAmmo != 0 ? loadedAmmo : mount != null ? unchecked((long)mount.AmmoGuid) : 0L;
            // ⭐ CE-3255 (BR-2; DESIGN_Building_Clearing CB-6) — a STUNNED shooter of any class fires nothing until the stun ends (before, only
            //   the civilian code read Effects.IsStunned). First: whatever the target, a dazed man does not shoot.
            if (Effects.IsStunned(world, unit)) return ActionReason.HoldStunned;
            // ⭐ CE-3256 (BR-4, CU-5) — nor a man whose hands are tied
            if (Fdp.Toolkit.Civilians.CustodyRules.IsRestrained(world, unit)) return ActionReason.HoldRestrained;
            // ⭐⭐ CE-2233 (CIV-0, the safety gate; DESIGN_Civilians_And_Crowd_Control CV-8) — the AI never fires at a NEUTRAL, whoever
            //   picked it (a ranking, a squad assignment, an order): the last gate before a round, on both paths. A possessed unit's
            //   fire (OperatorFire) does not come here — a human decides.
            if (Fdp.Toolkit.Perception.ThreatDanger.IsNeutral(world, target)) return ActionReason.HoldNeutral;
            // ⭐ CE-2282 DA-L — never a target inside a NoFire area of his chain (friends' rooms, the assault element): always, both paths
            if (Fdp.Toolkit.Behavior.Components.EngagementAreas.IsNoFire(world, unit, target)) return ActionReason.HoldNoFireArea;
            // ⭐ CV-13a (R-2024) — nor at a man showing HANDS UP, even Hostile (a surrender is honoured; a feint is read live)
            if (Fdp.Toolkit.Perception.ThreatDanger.ShowsHandsUp(world, target)) return ActionReason.HoldSurrender;
            // ⭐ CE-2238 part 4 (AW-21/AW-22, §5h.7b CH-1) — CONDUCT: no lethal round at a civilian this unit neither sees armed nor knows
            if (!ForceLevels.ConductPermits(world, unit, target, ForceLevels.IsLethalRound(world, ammo, mount))) return ActionReason.HoldConduct;
            // ⭐ CE-2239 part 3 (B2, §5k EL-3/EL-6) — the security forces' LADDER: warn an armed man before anything rises; at the Warn rung,
            //   the warning is all — no round
            if (EscalationLadder.Applies(world, unit, target))
            {
                EscalationLadder.WarnIfDue(world, unit, target);
                if (ForceLevels.LevelFor(world, unit, target) == RoeForceLevel.Warn) return ActionReason.HoldWarn;
            }
            // ⭐ CE-2239 part 1 (CV-8, §5j FL-2/FL-3) — the unit's FORCE LEVEL: no round above it, unless THIS target is a self-defence threat
            if (!ForceLevels.Permits(world, unit, target, ammo, mount)) return ActionReason.HoldForceLevel;
            // ⭐ CE-2187 (DESIGN_Missiles_And_Ammo §3c) — never a MANPADS at a truck, an ATGM at a flying helicopter: the choice values
            //   them at 0 (AmmoChoice.InDomain), and when nothing else is left the weapon holds rather than fire the wrong one
            if (!AmmoChoice.InDomain(world, target, ammo)) return ActionReason.WrongDomain;
            if (CombatTkb.MissileOf(world, ammo) is { MinRangeM: > 0f } missile
                && world.HasComponent<SimTransform>(unit) && world.HasComponent<SimTransform>(target)
                && Vector3.Distance(world.GetComponentRO<SimTransform>(unit).Position, world.GetComponentRO<SimTransform>(target).Position) < missile.MinRangeM)
                return ActionReason.TooClose;
            if (!CombatTkb.FiresOnTheMove(mount) && HitModel.IsMoving(world, unit)) return ActionReason.HoldMoving;
            // ⭐⭐ Air Ops slice 4 (DESIGN_Air_Operations §8, A6) — the RELEASE profile: a bomb leaves only when the cue says now (where a
            //   bomb dropped now lands has reached the target, within 10 m across the track). The behaviour flies the run-in; the
            //   weapon brain releases — the same gate for a player who holds the trigger (R-261).
            if (mount?.Category == Fdp.Toolkit.Tkb.Domain.WeaponCategory.Bomb && !ReleaseCue(world, unit, target)) return ActionReason.AwaitingRelease;
            // ⭐ Air Ops slice 6a (AW-1, §7a) — a FIXED gun / pod fires only inside its nose cone: the behaviour points the aircraft.
            if (BodyAim.TargetPoint(world, target, out var targetAt) && BodyAim.OutsideFixedCone(world, unit, mount, targetAt)) return ActionReason.OutOfArc;
            return null;
        }

        /// <summary>⭐ Air Ops slice 4 — <see cref="BombBallistics.ReleaseNow"/> for <paramref name="unit"/> over <paramref name="target"/>'s
        /// position, over this world's terrain.</summary>
        public static bool ReleaseCue(EntityRepository world, Entity unit, Entity target)
        {
            if (target.IsNull || !world.IsAlive(target) || !world.HasComponent<SimTransform>(target)) return false;
            return ReleaseCue(world, unit, world.GetComponentRO<SimTransform>(target).Position);
        }

        /// <summary>⭐ CE-2173 slice 5 — <see cref="ReleaseCue(EntityRepository, Entity, Entity)"/> over a POINT (a strike on a place,
        /// <c>FireAtPointExecutor</c>).</summary>
        public static bool ReleaseCue(EntityRepository world, Entity unit, Vector3 point)
        {
            // ⭐ CE-2173 (Air Ops §9c) — the carrier as THIS node knows it: on the brain node a plane's motion is SimHost's, its
            //   SimVelocity is never refreshed there (dead reckoning skips it), so the cue read a standing plane and never released.
            if (!Fdp.Toolkit.Replication.ReplicatedMotion.Of(world, unit, out var p, out var v)) return false;
            var terrain = Fdp.Toolkit.World.WorldQuery.Of(world);
            return BombBallistics.ReleaseNow(p, v, point, terrain == null ? null : terrain.GroundHeightAt);
        }

        /// <summary>
        /// ⭐ <c>CE-2180</c> (Air Ops §8b) — the synced sim time the release cue says a bomb from <paramref name="unit"/> on
        /// <paramref name="point"/> is released: now + the cue's seconds-to-release (≤ ½ step ahead, up to <see cref="BombBallistics.MissedPassSeconds"/>
        /// behind). ⭐ The brain sees "now" only on its own tick; a slow tick releases late by up to one tick — stamping the cue's own
        /// moment lets the SimHost start the bomb from there (it steps its carrier back to it). ⛔ It decides nothing: the gate is
        /// <see cref="ReleaseCue(EntityRepository, Entity, Vector3)"/>, unchanged. 0 without a clock.
        /// </summary>
        public static double ReleaseAt(EntityRepository world, Entity unit, Vector3 point)
        {
            double now = HitModel.Now(world);
            if (now <= 0d || !Fdp.Toolkit.Replication.ReplicatedMotion.Of(world, unit, out var p, out var v)) return now;
            var terrain = Fdp.Toolkit.World.WorldQuery.Of(world);
            var (_, _, seconds) = BombBallistics.Cue(p, v, point, terrain == null ? null : terrain.GroundHeightAt);
            return float.IsFinite(seconds)
                ? now + Math.Clamp(seconds, -BombBallistics.MissedPassSeconds, 0.5f * BombBallistics.DefaultDt)
                : now;
        }

        /// <summary>⭐ W-12c (§5g G-2) — added to a guided round's flight estimate: launch and detonation are not instants.</summary>
        public const float GuidedFlightMarginSeconds = 0.5f;

        /// <summary>
        /// ⭐ W-12c (§5g G-2) — how long a guided round just fired at <paramref name="target"/> flies: the flat distance over the round's
        /// speed, plus <see cref="GuidedFlightMarginSeconds"/>. The speed: ⭐ <c>CE-2170</c> — the MISSILE's own
        /// (<see cref="CombatTkb.MissileOf"/>, the same lookup <c>FireProcessingSystem</c> spawns it with; <paramref name="loadedAmmo"/>, else
        /// the mount's ammo), else the mount's muzzle velocity (the TKB's; else the mount state's; else the engine fallback). ⛔ It read the
        /// mount first, whose BDC value is the generated range × 0.5 (a TOW: 1875 m/s against its 280) ⇒ a 2 km hold of 1.6 s for a 7.6 s
        /// flight. ⚠ An expected value, not the detonation (R-212): a round stopped early by cover still holds the launcher for the estimate.
        /// </summary>
        public static float GuidedFlightSeconds(EntityRepository world, Entity unit, Entity target,
                                                Fdp.Toolkit.Tkb.Domain.WeaponMountDto? mount, float stateMuzzleVelocity, long loadedAmmo = 0L)
        {
            long ammo = loadedAmmo != 0 ? loadedAmmo : mount != null ? unchecked((long)mount.AmmoGuid) : 0L;
            float speed = CombatTkb.MissileOf(world, ammo) is { SpeedMps: > 0f } missile ? missile.SpeedMps
                        : mount is { MuzzleVelocity: > 0f } ? mount.MuzzleVelocity
                        : stateMuzzleVelocity > 0f ? stateMuzzleVelocity
                        : Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.MuzzleVelocity;
            if (!world.HasComponent<SimTransform>(unit) || !world.IsAlive(target) || !world.HasComponent<SimTransform>(target))
                return GuidedFlightMarginSeconds;
            var a = world.GetComponentRO<SimTransform>(unit).Position;
            var b = world.GetComponentRO<SimTransform>(target).Position;
            return new Vector2(b.X - a.X, b.Y - a.Y).Length() / speed + GuidedFlightMarginSeconds;
        }

        /// <summary>⭐ WL-3 — every turret of <paramref name="unit"/> with an order goes back to Idle (it holds its pose); the epoch is
        /// kept so the next order is a new one.</summary>
        public static void ReleaseTurrets(EntityRepository world, Entity unit)
        {
            if (!world.IsComponentTypeRegistered<WeaponTask>() || !world.IsComponentTypeRegistered<TurretPose>()) return;
            ReleaseOwnTurrets(world, unit);
            // ⭐ WS-3 — and the turrets of the weapon systems it controls (the M2 in its bed stops following too)
            System.Span<Entity> controlled = stackalloc Entity[WeaponSystems.MaxControlled];
            int n = WeaponSystems.Controlled(world, unit, controlled);
            for (int k = 0; k < n; k++) ReleaseOwnTurrets(world, controlled[k]);
        }

        private static void ReleaseOwnTurrets(EntityRepository world, Entity unit)
        {
            for (int i = 0; i < TurretParts.MaxTurrets; i++)
            {
                var part = TurretParts.Find(world, unit, i);
                if (part.IsNull) break;
                if (!world.HasComponent<WeaponTask>(part)) continue;
                ref var task = ref world.GetComponentRW<WeaponTask>(part);
                if (task.Mode != WeaponTaskMode.Idle) task = new WeaponTask { Mode = WeaponTaskMode.Idle, Epoch = task.Epoch };
            }
        }

        /// <summary>⭐ WL-3 — rounds of the current turret epoch already spent here (bytes 28–31, after the action's round count).</summary>
        private static unsafe ref int Accounted(ref WeaponChannel channel)
        {
            fixed (byte* s = channel.State)
                return ref Unsafe.AsRef<int>(s + sizeof(Entity) + sizeof(AimState) + sizeof(int));
        }

        // ⭐ CE-3136 (T1) — the weapon row of the unit's ActionStatus: why this action is (not) firing this frame.
        private static void Say(EntityRepository world, Entity unit, ActionReason reason, Entity target, float progress = 0f, float needed = 0f)
        {
            ActionStatusOf.Weapon(world, unit, CombatConstants.ActionIdAimAndFire, reason, target, progress, needed);
            WeaponPostureOf.Report(world, unit, reason, target);   // ⭐ WL-6 (DESIGN_Weapon_Layers W-13b) — the weapon posture
        }

        private static void SayAim(EntityRepository world, Entity unit, Entity target, bool seen, in AimState aim, Entity shooter, int mountIndex)
        {
            if (!seen) Say(world, unit, ActionReason.HoldNotSeen, target);
            else Say(world, unit, ActionReason.Aiming, target, aim.Elapsed, PacedAimSecondsFor(world, shooter, mountIndex, target));
        }

        /// <summary>
        /// ⭐⭐ WS-3 (W-D, §6) — why the weapon system <paramref name="weapon"/> may not fire for <paramref name="unit"/> now: another (or
        /// nobody) controls it, its gunner is gone, it is not deployed, or <paramref name="target"/> is outside its station's arc. Null = fire.
        /// </summary>
        public static ActionReason? SystemHold(EntityRepository world, Entity unit, Entity weapon, Entity target)
        {
            if (WeaponSystems.ControllerOf(world, weapon) != unit) return ActionReason.NotControlled;
            if (!WeaponSystems.CanFire(world, weapon, out var why))
                return why == WeaponHold.NoGunner ? ActionReason.NoGunner : why == WeaponHold.NotDeployed ? ActionReason.NotDeployed : ActionReason.NotControlled;
            if (!target.IsNull && world.IsAlive(target) && world.HasComponent<SimTransform>(target)
                && !WeaponSystems.InStationArc(world, weapon, world.GetComponentRO<SimTransform>(target).Position))
                return ActionReason.OutOfArc;
            return null;
        }

        /// <summary>⭐ <c>CE-3136</c> P-3 — the aim timer, in the channel state after the target (bytes 8–23 of 32).</summary>
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct AimState
        {
            public Entity Target;
            public float  Elapsed;
            public byte   Stance;
            public byte   Ready;
            /// <summary>⭐ WL-4 W-12a — the mount chosen for this engagement (the struct's two former padding bytes: still 16).</summary>
            public byte   ChosenMount;
            /// <summary>0 = no choice; else 1 + the <see cref="Fdp.Toolkit.Behavior.Components.RoeForceLevel"/> on the target the choice was made
            /// under (⭐ CE-2239 part 3, §5k EL-9: a changed level chooses again).</summary>
            public byte   HasChoice;

            /// <summary>A fresh aim (timer and readiness reset) that KEEPS the engagement's mount choice.</summary>
            public readonly AimState Restarted(Entity target, byte stance)
                => new AimState { Target = target, Stance = stance, ChosenMount = ChosenMount, HasChoice = HasChoice };
        }

        /// <summary>
        /// ⭐ WL-4 W-12a (L-4) — the mount for this engagement: the stored choice while the target is the one it was made for and
        /// the mount still has ammunition; otherwise <see cref="WeaponChoice.Choose"/> once more. The engaged target is the
        /// channel state's first field (written by <see cref="OnEnter"/>).
        /// </summary>
        private static unsafe int EngagementMount(EntityRepository world, Entity unit, ref WeaponChannel channel, Entity target, out Entity mountEntity)
        {
            ref var aim = ref AimTimer(ref channel);
            ref Entity engaged = ref EngagedTarget(ref channel);
            mountEntity = unit;
            // ⭐ CE-2239 part 3 (B2, §5k EL-9) — the choice is kept only while the FORCE LEVEL on this target is the one it was made under
            //   (HasChoice = 1 + that level): a man who raises his weapon after an ignored warning gets the rifle, not the next stun grenade;
            //   one who lowers it gets the baton round, not a held rifle
            byte level = (byte)(1 + (byte)ForceLevels.LevelFor(world, unit, target));
            if (aim.HasChoice == level && engaged == target)
            {
                int kept = aim.ChosenMount == 0 ? 0 : MountByIndex(world, unit, aim.ChosenMount, out mountEntity);
                if (kept == aim.ChosenMount && world.HasComponent<WeaponState>(mountEntity) && world.GetComponentRO<WeaponState>(mountEntity).Ammo > 0)
                    return kept;
            }
            int chosen = WeaponChoice.Choose(world, unit, target, out mountEntity);
            aim.ChosenMount = (byte)chosen;
            aim.HasChoice = level;
            engaged = target;
            return chosen;
        }

        /// <summary>The target this action engages (channel state bytes 0–7).</summary>
        private static unsafe ref Entity EngagedTarget(ref WeaponChannel channel)
        {
            fixed (byte* s = channel.State)
                return ref Unsafe.AsRef<Entity>(s);
        }

        /// <summary>⭐ <c>CE-3136</c> B7 — the rounds the CURRENT AimAndFire action has fired (valid once the dispatcher has entered it).</summary>
        public static int RoundsFiredOf(ref WeaponChannel channel) => RoundsFired(ref channel);

        /// <summary>⭐ <c>CE-3136</c> B7 — rounds fired by this action, in the channel state after the aim timer (bytes 24–27).</summary>
        private static unsafe ref int RoundsFired(ref WeaponChannel channel)
        {
            fixed (byte* s = channel.State)
                return ref Unsafe.AsRef<int>(s + sizeof(Entity) + sizeof(AimState));
        }

        private static unsafe ref AimState AimTimer(ref WeaponChannel channel)
        {
            fixed (byte* s = channel.State)
                return ref Unsafe.AsRef<AimState>(s + sizeof(Entity));
        }

        private static Fdp.Toolkit.Tkb.Domain.StanceId ShooterStance(EntityRepository world, Entity e)
            => world.IsComponentTypeRegistered<Hrot.MuscleCharacter.Animation.Components.StanceIntent>()
                ? Hrot.MuscleCharacter.Animation.Components.LogicalStance.Of(world, e)
                : Fdp.Toolkit.Tkb.Domain.StanceId.Standing;

        /// <summary>The fired mount's aim time (TKB, by type), else <c>EngineFallbacks.AimSeconds</c>.</summary>
        /// <summary>⭐ <c>CE-2323</c> (R-2063) — the aim time THIS man needs at <paramref name="target"/>: the mount's, scaled by his own
        /// <see cref="FirePacing.AimFactor"/>. The ONE value the gate waits for and the weapon row reports.</summary>
        public static float PacedAimSecondsFor(EntityRepository world, Entity shooter, int mountIndex, Entity target)
            => AimSecondsFor(world, shooter, mountIndex) * FirePacing.AimFactor(shooter, target);

        public static float AimSecondsFor(EntityRepository world, Entity shooter, int mountIndex)
            => Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.AimSecondsOrFallback(
                   CombatTkb.MountOf(world, shooter, mountIndex)?.AimSeconds ?? 0f);

        /// <summary>The mount child with <paramref name="index"/> (its WeaponState entity); mount 0 on the owner when there is none.</summary>
        private static int MountByIndex(EntityRepository world, Entity owner, int index, out Entity mount)
        {
            mount = owner;
            // ⭐ WS-3 — a controlled weapon system's index names the weapon itself (its own WeaponState)
            if (WeaponSystems.IsControlledIndex(index))
            {
                var controlled = WeaponSystems.ControlledAt(world, owner, index);
                if (controlled.IsNull || !world.HasComponent<WeaponState>(controlled)) return 0;
                mount = controlled;
                return index;
            }
            if (!world.IsComponentTypeRegistered<WeaponMountInfo>()) return 0;
            System.Span<Entity> mounts = stackalloc Entity[8];
            int n = WeaponMountQuery.EnumerateMounts(world, owner, mounts);
            for (int i = 0; i < n; i++)
                if (world.HasComponent<WeaponMountInfo>(mounts[i]) && world.GetComponentRO<WeaponMountInfo>(mounts[i]).MountIndex == index
                    && world.HasComponent<WeaponState>(mounts[i]))
                {
                    mount = mounts[i];
                    return index;
                }
            return 0;
        }

        // OnExit

        public void OnExit(Entity entity, ref WeaponChannel channel, EntityRepository world)
        {
            ReleaseTurrets(world, entity);   // ⭐ WL-3 (W-1) — releasing the channel sets the turret's task to Idle
        }
    }
}
