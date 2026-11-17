using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ CE-10003 (air combat AC-5, AC-8, AC-12, AC-13; docs/designs/air-attack/DESIGN_Air_Combat.md §3, §4) — THE AIR-COMBAT PROFILE of the
    /// one <see cref="AirAttackNodes"/> (R-2015: an Attack order on an AIRCRAFT, flown by a fighter that carries an air-to-air weapon). The
    /// brain picks, every <see cref="DecisionPeriodS"/> after the pilot's reaction, the basic fighter manoeuvre the motion node flies in mode 8
    /// (<see cref="AirCombatSteer"/>, the ONLY order down), shoots a missile only inside the employment zone (<see cref="EmploymentZone"/>, one
    /// at a time per target) and the gun at the ACCELERATING lead within <see cref="GunRangeM"/>, defends against a launch through the one
    /// <see cref="MissileDefence"/>, and disengages winchester or damaged. Its state is its own <see cref="AirCombatState"/> (per entity, R-4212);
    /// it reads the target's replicated motion (tick-start view) and its OWN <see cref="FighterPilotState"/>. Allocation-free.
    /// </summary>
    public static class AirCombatNodes
    {
        /// <summary>The decision rate (s, 2 Hz).</summary>
        public const double DecisionPeriodS = 0.5;
        /// <summary>The gun fires inside this (m) with the nose within <see cref="GunConeDeg"/> of the accelerating lead (AC-8).</summary>
        public const float GunRangeM = 500f, GunConeDeg = 2f;
        /// <summary>Inside this (m) the pilot stops pressing and lags off (AC-13a: jets that touch both crash).</summary>
        public const float BreakAwayM = 250f;
        /// <summary>A gun burst (rounds) — the M61's 0.2 s.</summary>
        public const int GunBurst = 20;
        /// <summary>A missile shot failed (out of its cone, the target unseen) is retried after this (s).</summary>
        public const float RetryShotS = 2f;
        /// <summary>A pilot waits at most this long (s) for his missile's outcome before the next.</summary>
        public const float MissileOutcomeS = 25f;
        /// <summary>The fight's floor without an arena (m above the ground) and the merge throttle.</summary>
        public const float DefaultFloorAgl = 300f;
        /// <summary>Health below this fraction ⇒ disengage (AC-13).</summary>
        public const float DisengageHealth = 0.5f;
        /// <summary>A disengaging fighter is done this far beyond where it disengaged from (m).</summary>
        public const float DisengageOutM = 6000f;

        /// <summary>⭐ AC-12 — reaction to what it sees (s), the g it will pull, by skill (Rookie · Veteran · Ace).</summary>
        public static float ReactionS(PilotSkill s) => s switch { PilotSkill.Ace => 0.3f, PilotSkill.Veteran => 0.7f, _ => 1.5f };
        public static float PullG(PilotSkill s) => s switch { PilotSkill.Ace => 9f, PilotSkill.Veteran => 8f, _ => 6.5f };
        /// <summary>⭐ AC-12 — a Rookie shoots at the zone's max range, a Veteran inside 0.8 of it, an Ace only inside the no-escape range.</summary>
        public static float ShotFraction(PilotSkill s) => s switch { PilotSkill.Veteran => 0.8f, _ => 1f };

        /// <summary>The air-combat profile applies: <paramref name="self"/> is a fixed-wing that carries an air-to-air missile or a forward
        /// gun with an AirToAir mount on its type, and the target is an aircraft in the air.</summary>
        public static bool Applies(EntityRepository world, Entity self, Entity target)
        {
            if (target.IsNull || !world.IsAlive(target) || !Locomotions.IsAircraft(world, target)) return false;
            if (Locomotions.Of(world, self) != LocomotionClass.FixedWing) return false;
            for (int i = 0; i < 8; i++)
            {
                var m = CombatTkb.MountOf(world, self, i);
                if (m == null) break;
                if (m.Category == WeaponCategory.AirToAir) return true;
            }
            return false;
        }

        /// <summary>⭐ CE-10005 (AC-13) — the fighter's own radar: Track on <paramref name="target"/>, else Search; a type with no radar is left alone.
        /// Written only on a change (<see cref="Fdp.Toolkit.Perception.Radar.RadarEmissions.Set"/>).</summary>
        public static void Radar(EntityRepository world, Entity self, Entity target, double now)
        {
            float range = Fdp.Toolkit.Perception.Radar.RadarEmissions.RangeOf(world, self);
            if (range <= 0f) return;
            Fdp.Toolkit.Perception.Radar.RadarEmissions.Set(world, self, target.IsNull ? Fdp.Toolkit.Perception.Radar.RadarMode.Search : Fdp.Toolkit.Perception.Radar.RadarMode.Track,
                target, Fdp.Toolkit.Perception.Radar.RadarEmitterClass.Unknown, range, now);
        }

        /// <summary>⭐ CE-10005 — a fighter: a fixed-wing that carries an air-to-air mount on its type.</summary>
        public static bool IsFighter(EntityRepository world, Entity self)
        {
            if (Locomotions.Of(world, self) != LocomotionClass.FixedWing) return false;
            for (int i = 0; i < 8; i++)
            {
                var m = CombatTkb.MountOf(world, self, i);
                if (m == null) break;
                if (m.Category == WeaponCategory.AirToAir) return true;
            }
            return false;
        }

        /// <summary>One tick of the fight (the profile's whole stage machine). Success: the target is down or the fighter is out; Failure: no weapon.</summary>
        public static NodeStatus Fight(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world, double now)
        {
            if (!world.IsComponentTypeRegistered<AirCombatState>() || !world.IsComponentTypeRegistered<AirCombatSteer>()) return NodeStatus.Failure;
            var before = world.HasComponent<AirCombatState>(self) ? world.GetComponentRO<AirCombatState>(self) : default;
            var cs = before;
            var status = Tick(ref p, ref ws, self, world, now, ref cs);
            if (!SameBytes(in before, in cs))
            {
                if (world.HasComponent<AirCombatState>(self)) world.GetComponentRW<AirCombatState>(self) = cs;
                else world.AddComponent(self, cs);
            }
            return status;
        }

        private static NodeStatus Tick(ref AirAttackParams p, ref AirAttackState ws, Entity self, EntityRepository world, double now, ref AirCombatState cs)
        {
            if (cs.StartedAt == SimInstant.Zero) Begin(world, self, ref cs, now);
            if (!Fdp.Toolkit.Replication.ReplicatedMotion.Of(world, self, out var me, out var myVel)) return NodeStatus.Running;
            var skill = cs.Skill;
            float reaction = ReactionS(skill);

            // ── the target: down ⇒ the kill counts and the attack ends (B14); the fight is flown in mode 8 ──
            bool disengaging = cs.Manoeuvre == Manoeuvre.Disengage;
            if (!disengaging && !Injury.IsInTheFight(world, p.Target))
            {
                StopGun(world, self, ref ws);
                if (cs.Target == p.Target && cs.Kills < 255) cs.Kills++;
                cs.Target = Entity.Null;
                // ⭐ CE-10005 — the order down is cleared to straight and level: 📐 on the cluster run the motion node kept flying the last steer
                //   (a lead pursuit of the falling wreck) and GCAS pulled the winner out of a 9 g dive at the ground again and again
                //   — and in an arena it HOLDS over it (AC-13: "else … Hold over the arena"; 📐 flown straight, the 1v1's winner left 10 km)
                if (world.HasComponent<AirCombatSteer>(self))
                {
                    // the arena it fought in (its steer names it — a winner far out after a drag is still that arena's), else the one around it
                    var hold = world.GetComponentRO<AirCombatSteer>(self).Arena;
                    bool held = !hold.IsNull && world.IsAlive(hold) && world.HasComponent<AirArena>(hold);
                    var holdArena = held ? world.GetComponentRO<AirArena>(hold) : default;
                    if (!held && Fdp.Toolkit.Replication.ReplicatedMotion.Of(world, self, out var at, out _)) held = AirArenaSystem.Containing(world, at, out hold, out holdArena);
                    WriteSteer(world, self, new AirCombatSteer
                    {
                        Manoeuvre = held ? Manoeuvre.ReturnToArena : Manoeuvre.None, Point = held ? holdArena.Centre : Vector3.Zero, Arena = held ? hold : Entity.Null,
                        Throttle = 0.85f, FloorAgl = world.GetComponentRO<AirCombatSteer>(self).FloorAgl,
                    });
                }
                return NodeStatus.Success;
            }
            cs.Target = p.Target;
            EnterModeEight(world, self);

            bool hasTarget = Fdp.Toolkit.Replication.ReplicatedMotion.Of(world, p.Target, out var tp, out var tv);
            // ⭐ CE-10005 (AC-13 / RV-3) — the fighter switches its OWN radar on, tracking its bandit (RS-10's one writer; an aircraft emitter is
            //   exempt from RS-6's dive and RS-7's report). 📐 It was missing: the jets fought with their radars off
            Radar(world, self, hasTarget ? p.Target : Entity.Null, now);
            if (hasTarget) SampleAccel(ref cs, tv, (float)(now - (double)cs.StartedAt));

            // ⭐ CE-10004 (AC-10 / AC-11) — the arena (the serial pre-pass's index): its floor, its shot cap, its soft edge, its stalemate clock
            bool inArena = AirArenaSystem.Containing(world, me, out var arenaE, out var arena);
            float floor = inArena && arena.FloorAglM > 0f ? arena.FloorAglM : DefaultFloorAgl;
            float shotCap = inArena && arena.MaxShotRangeM > 0f ? arena.MaxShotRangeM : AirArena.DefaultMaxShotRangeM;
            Lapses(ref cs, world, self, now, inArena, in arena);

            // ── the missile defence first (AC-9): started by a launch after the reaction (+2 s under a LateWarning lapse) ──
            var dsBefore = MissileDefence.Load(world, self);
            var ds = dsBefore;
            var flight = new MissileDefence.Flight
            {
                Fighter = true, FallbackDir = Vector2.UnitY,
                // AC-12 — a LateWarning sees the launch 2 s late; a Fixation (head down on the gun track) does not see it at all while it lasts
                ReactionS = reaction + (cs.Lapse == PilotLapse.LateWarning ? LateWarningS : cs.Lapse == PilotLapse.Fixation ? LapseS : 0f),
                // ⭐ CE-10005 (AC-12, R-10019) — skill on the DEFENCE: a Rookie never drags and releases fewer decoys; an Ace drags any seeker far out
                DecoyCap = skill == PilotSkill.Rookie ? MissileDefence.RookieDecoys : (byte)0,
                Read = skill switch { PilotSkill.Rookie => MissileDefence.Reading.Rookie, PilotSkill.Ace => MissileDefence.Reading.Ace, _ => MissileDefence.Reading.Veteran },
            };
            bool defending = MissileDefence.Run(world, self, ref ds, now, in flight, out var defence, out bool started);
            if (!AirAttackNodes.Same(in dsBefore, in ds)) MissileDefence.Store(world, self, in ds);

            var m = cs.Manoeuvre;
            float throttle = 0.85f;
            var point = Vector3.Zero;
            bool steerAtPoint = false, waiting = false;
            if (defending)
            {
                if (started) StopGun(world, self, ref ws);
                m = defence;
                throttle = 1f;
                // ⭐ CE-10005 (AC-12, R-10019) — a Rookie defends with a LEVEL turn: a break across the missile's line at his own height
                //   (no descent toward the notch, no drag); the motion node's last-ditch reflex still fires inside 2.5 s
                if (skill == PilotSkill.Rookie && defence == Manoeuvre.Beam && BeamPoint(world, self, me, myVel, out point))
                {
                    m = Manoeuvre.Break;
                    steerAtPoint = true;
                }
            }
            else if (disengaging)
            {
                point = ws.Home;
                throttle = 1f;
                if (Vector2.Distance(new Vector2(me.X, me.Y), new Vector2(point.X, point.Y)) < 1500f) return NodeStatus.Success;
            }
            else if (Winchester(world, self, in cs) || Damaged(world, self))
            {
                // ⭐ AC-13 — out of weapons, or hurt: leave the fight, away from the bandit, at full throttle
                StopGun(world, self, ref ws);
                var away = hasTarget ? Flat(me - tp) : Flat(myVel);
                ws.Home = me + new Vector3(away * DisengageOutM, 0f);
                point = ws.Home;
                m = Manoeuvre.Disengage;
                throttle = 1f;
            }
            else if (inArena && OutsideBy(me, in arena) > 0f && !(cs.Lapse == PilotLapse.Fixation) && cs.Manoeuvre is not (Manoeuvre.None or Manoeuvre.Merge)   // the merge flies IN from outside
                     && !(hasTarget && OnHisTail(me, myVel, tp) && OutsideBy(me, in arena) < SoftEdgeYieldM))   // ⭐ R-10029 — a hot pursuit near the edge goes on
            {
                // ⭐ AC-10 (RV-9) — the SOFT edge: outside the arena it turns back toward the centre; the defence and a disengage override it
                m = Manoeuvre.ReturnToArena;
                point = arena.Centre;
                throttle = 1f;
            }
            else if (cs.Manoeuvre == Manoeuvre.None && (!hasTarget || myVel.LengthSquared() < 1f || tv.LengthSquared() < 1f))
                waiting = true;   // straight on, weapons safe, until both motions are known
            else if (now >= (double)cs.NextDecision && hasTarget)
            {
                // ⭐ CE-10005 (R-10020) — the FIRST decision waits for both motions to be known: 📐 on a cluster the brain node's first ticks
                //   read no replicated velocity yet, the head-on test failed and both jets fired at 6 km (the merge never started)
                m = cs.Lapse == PilotLapse.Fixation ? Manoeuvre.LeadPursuit                       // AC-12 — holds the gun track
                  : inArena && (cs.Manoeuvre == Manoeuvre.Merge || (cs.Manoeuvre == Manoeuvre.None && Vector3.Distance(me, tp) > 4000f && HeadOn(me, myVel, tp, tv))) ? Manoeuvre.Merge   // the start: through the centre, until the pass
                  : Pick(skill, me, myVel, tp, tv, SpeedOf(world, self, myVel), CornerOf(world, self));
                if (m == Manoeuvre.Merge) point = arena.Centre;
                cs.NextDecision = (SimInstant)(now + DecisionPeriodS + (cs.Manoeuvre == Manoeuvre.None ? reaction : 0f));
                throttle = m is Manoeuvre.PurePursuit or Manoeuvre.Break or Manoeuvre.Merge ? 1f : 0.85f;
            }
            else throttle = cs.Manoeuvre is Manoeuvre.PurePursuit or Manoeuvre.Break or Manoeuvre.Merge ? 1f : 0.85f;
            if (m == Manoeuvre.None) m = Manoeuvre.PurePursuit;
            if (m == Manoeuvre.Merge)
            {
                // ⭐ CE-10005 (AC-10, R-10020) — the MERGE is the fight's START: both pilots fly through the centre at corner speed + 20 %, offset
                //   MergeOffsetM to their right (a left-to-left pass, never into each other — AC-13a), and hold their fire until the PASS (the
                //   bandit behind the wing line, or inside MergeEndsM) — then the fight is on
                if (!inArena || !hasTarget || Passed(me, myVel, tp)) { m = Pick(skill, me, myVel, tp, tv, SpeedOf(world, self, myVel), CornerOf(world, self)); cs.NextDecision = (SimInstant)(now + DecisionPeriodS); }
                else
                {
                    point = MergePoint(me, in arena);
                    throttle = SpeedOf(world, self, myVel) > MergeSpeedFactor * CornerOf(world, self) ? 0.3f : 1f;
                }
            }
            if (m == Manoeuvre.ReturnToArena) point = arena.Centre;
            // ⭐ CE-10003 (AC-8, R-10017) — the energy at the gun range: closing fast inside 700 m the pilot pulls the throttle back (no
            //   overshoot, never into the bandit — AC-13a: jets that touch both crash; 📐 measured on ac-skill-decides: both died at once);
            //   an EMPTY gun inside 1.2 km falls back (lag, throttle back) to open the missiles' 500 m minimum again (📐 ac-winchester sat
            //   in trail at 100 m with two missiles left)
            if (hasTarget && !defending && m is Manoeuvre.LeadPursuit or Manoeuvre.PurePursuit or Manoeuvre.LagPursuit)
            {
                var los = tp - me;
                float r = los.Length();
                float closing = r > 1e-3f ? -Vector3.Dot(tv - myVel, los / r) : 0f;
                // ⭐ CE-10006 (AC-8, R-10029) — CLOSE TO GUNS: beyond the gun's reach a bandit holding or opening the range is chased at full
                //   power (📐 ac-dogfight-2v2: the Ace sat 650–1 150 m dead astern of a dragging bandit, lead pursuit at 0.85 throttle, and the
                //   range OPENED from 655 to 1 150 m — no Ace ever reached gun range, so no gun hit)
                if (r > GunRangeM + CloseMarginM && closing < CloseToGunsMps) throttle = 1f;
                if (r < 700f && closing > 15f) throttle = 0.2f;
                if (r < BreakAwayM) { m = Manoeuvre.LagPursuit; throttle = 0.1f; }   // never into it (nor into its falling wreck)
                if (r < 1200f && Rounds(world, self, cs.GunMount) <= 0) { m = Manoeuvre.LagPursuit; throttle = 0.1f; }
            }
            // ⭐ CE-10006 (AC-13a, R-10029) — NOSE TO NOSE the break-off is by TIME, not distance, and it turns OFF the line (a lag pursuit of a
            //   head-on bandit still points at it), held while the two still close: 📐 ac-dogfight-2v2 with close-to-guns — the two Aces met
            //   head-on at 262–380 m/s closing, lagged off at 250 m (< 1 s) and collided (both lost)
            if (hasTarget && !defending && !steerAtPoint && m is Manoeuvre.LeadPursuit or Manoeuvre.PurePursuit or Manoeuvre.LagPursuit or Manoeuvre.Break or Manoeuvre.ReturnToArena)
            {
                var los = tp - me;
                float r = los.Length();
                float closing = r > 1e-3f ? -Vector3.Dot(tv - myVel, los / r) : 0f;
                if (closing > 0f && r < closing * BreakAwayS && AngleDeg(tv, -los) < HeadOnBreakDeg
                    && (AngleDeg(myVel, los) < HeadOnBreakDeg || cs.Manoeuvre == Manoeuvre.Break))
                {
                    var away = Flat(los);
                    point = me + new Vector3(new Vector2(away.Y, -away.X) * 2000f, 0f);   // to my RIGHT: both break right ⇒ left-to-left (R-10020)
                    m = Manoeuvre.Break; steerAtPoint = true; throttle = 1f;
                }
            }
            if (cs.Lapse == PilotLapse.EnergyBleed) throttle = 0.3f;   // AC-12 — a slow turn under corner speed
            // ⭐ CE-10005 (AC-8, R-10024) — a lead pursuit inside the gun's reach flies the GUN's lead: its offset from the bandit (the round's
            //   time of flight, the bandit's acceleration — TargetLead.PointAccelerating, the brain's 2° cone uses the same) rides in the steer's
            //   Point, rounded to 1 m so the replicated steer changes only when the lead moves (📐 ac-guns-turning: with the motion node's own
            //   0.8 s lead the nose never came within 2° and the gun never fired)
            if (m == Manoeuvre.LeadPursuit && hasTarget && !defending && cs.GunMount != AirCombatState.NoMount
                && Vector3.Distance(me, tp) < FixedWingModel.GunLeadRangeM && Rounds(world, self, cs.GunMount) > 0)
            {
                var off = TargetLead.PointAccelerating(me, myVel, AirAttackNodes.RoundSpeedOf(world, self, cs.GunMount), tp, tv, cs.TargetAccel) - tp;
                point = new Vector3(MathF.Round(off.X), MathF.Round(off.Y), MathF.Round(off.Z));
            }
            cs.Manoeuvre = waiting ? Manoeuvre.None : m;

            // ── the weapons: never while defending, leaving or merging ──
            if (!defending && m != Manoeuvre.Disengage && m != Manoeuvre.Merge && hasTarget && !waiting)
            {
                Missile(ref cs, world, self, me, myVel, tp, tv, now, shotCap);
                Gun(ref ws, ref cs, world, self, me, myVel, tp, tv);
            }
            else StopGun(world, self, ref ws);

            WriteSteer(world, self, new AirCombatSteer
            {
                Manoeuvre = m, Lapse = cs.Lapse, Target = m == Manoeuvre.Disengage || steerAtPoint ? Entity.Null : p.Target, Point = point,
                Throttle = throttle, FloorAgl = floor, MaxG = PullG(skill), Arena = inArena ? arenaE : Entity.Null,
            });
            return NodeStatus.Running;
        }

        /// <summary>⭐ CE-10006 (R-10029) — beyond <see cref="GunRangeM"/> + <see cref="CloseMarginM"/> a pursuit closing slower than
        /// <see cref="CloseToGunsMps"/> (m/s) goes to full power; the soft edge yields to a pursuit with the bandit ON THE NOSE
        /// (<see cref="OnHisTail"/>) while less than <see cref="SoftEdgeYieldM"/> outside the arena (AC-10: the pull grows with the distance).</summary>
        public const float CloseToGunsMps = 50f, CloseMarginM = 200f, SoftEdgeYieldM = 1000f, HotPursuitM = 2000f, HotPursuitDeg = 30f;
        /// <summary>⭐ R-10029 — nose to nose (each within <see cref="HeadOnBreakDeg"/> of the other) a pilot breaks off right this long (s) before
        /// the two would meet (AC-13a).</summary>
        public const float BreakAwayS = 2f, HeadOnBreakDeg = 30f;

        /// <summary>⭐ R-10029 — a hot pursuit: the bandit inside <see cref="HotPursuitM"/> and within <see cref="HotPursuitDeg"/> of my nose.</summary>
        public static bool OnHisTail(Vector3 me, Vector3 myVel, Vector3 tp)
        {
            var los = tp - me;
            return los.Length() < HotPursuitM && myVel.LengthSquared() > 1f && AngleDeg(myVel, los) < HotPursuitDeg;
        }

        /// <summary>⭐ CE-10005 (AC-10, R-10020) — the merge passes this far (m) to the right of the arena's centre; it ends inside
        /// <see cref="MergeEndsM"/> of the bandit or once the bandit is behind the wing line; it is flown at corner speed × <see cref="MergeSpeedFactor"/>.</summary>
        public const float MergeOffsetM = 300f, MergeEndsM = 1000f, MergeSpeedFactor = 1.2f;

        /// <summary>The merge's aim point: the arena's centre, <see cref="MergeOffsetM"/> to the right of the line from <paramref name="me"/> to it.</summary>
        public static Vector3 MergePoint(Vector3 me, in AirArena arena)
        {
            var d = new Vector2(arena.Centre.X - me.X, arena.Centre.Y - me.Y);
            if (d.LengthSquared() < 1f) return arena.Centre;
            d = Vector2.Normalize(d);
            var right = new Vector2(d.Y, -d.X);
            return arena.Centre + new Vector3(right * MergeOffsetM, 0f);
        }

        /// <summary>⭐ CE-10005 (R-10020) — a merge is a HEAD-ON start: the two close faster than <see cref="MergeClosingMps"/> (📐 a tail chase
        /// "merging" never passes: ac-radar-chaff-beam's MiG, 5.5 km behind, held its fire for ever).</summary>
        public static bool HeadOn(Vector3 me, Vector3 myVel, Vector3 tp, Vector3 tv)
        {
            var los = tp - me;
            float r = los.Length();
            return r > 1e-3f && -Vector3.Dot(tv - myVel, los / r) > MergeClosingMps;
        }

        /// <summary>The closing speed (m/s) above which the start is a merge.</summary>
        public const float MergeClosingMps = 250f;

        /// <summary>The pass: the bandit is behind <paramref name="me"/>'s wing line, or inside <see cref="MergeEndsM"/>.</summary>
        public static bool Passed(Vector3 me, Vector3 myVel, Vector3 tp)
        {
            var los = tp - me;
            return los.Length() < MergeEndsM || Vector3.Dot(los, myVel) < 0f;
        }

        /// <summary>⭐ CE-10005 (R-10019) — a Rookie's level defence: a point 2 km out, 90° to the line to the missile (the side nearer the
        /// present heading), at his own height. False when no missile is inbound.</summary>
        private static bool BeamPoint(EntityRepository world, Entity self, Vector3 me, Vector3 myVel, out Vector3 point)
        {
            point = default;
            if (!world.IsComponentTypeRegistered<FighterPilotState>() || !world.HasComponent<FighterPilotState>(self)) return false;
            var pilot = world.GetComponentRO<FighterPilotState>(self);
            if (pilot.Inbound == 0) return false;
            float b = pilot.ThreatBearingDeg * (MathF.PI / 180f);
            var toMissile = new Vector2(MathF.Sin(b), MathF.Cos(b));
            var beam = new Vector2(toMissile.Y, -toMissile.X);
            if (Vector2.Dot(beam, Flat(myVel)) < 0f) beam = -beam;
            point = me + new Vector3(beam * 2000f, 0f);
            return true;
        }

        /// <summary>⭐ AC-5 — the manoeuvre from the geometry, by skill. Rookie: pure pursuit and level breaks. Veteran / Ace: lead for the gun,
        /// lag against an overshoot, a break (an Ace with energy: the vertical) when the bandit's nose is on it.</summary>
        public static Manoeuvre Pick(PilotSkill skill, Vector3 me, Vector3 myVel, Vector3 tp, Vector3 tv, float speed, float corner)
        {
            var los = tp - me;
            float r = los.Length();
            var u = r > 1e-3f ? los / r : Vector3.UnitX;
            float ata = AngleDeg(myVel, u);                 // my nose off the line to it
            float itsNose = AngleDeg(tv, -u);               // its nose off the line to me (small = it points at me)
            bool onItsTail = AngleDeg(tv, u) < 60f;         // it flies away from me
            if (!onItsTail && itsNose < 40f && r < 3000f && ata > 60f)   // ⇒ defensive: its nose is on me and mine is not on it
                return skill == PilotSkill.Ace && speed > corner + 30f ? Manoeuvre.Climb : Manoeuvre.Break;
            if (skill == PilotSkill.Rookie) return Manoeuvre.PurePursuit;
            if (r > 4000f) return Manoeuvre.PurePursuit;
            float closing = -Vector3.Dot(tv - myVel, u);
            if (r < 800f && closing > 60f && ata > 20f) return Manoeuvre.LagPursuit;   // overshoot coming
            if (skill == PilotSkill.Ace && speed < corner - 30f && ata > 90f) return Manoeuvre.Extend;   // regain energy before turning again
            return ata < 45f ? Manoeuvre.LeadPursuit : Manoeuvre.PurePursuit;
        }

        // ── ⭐ CE-10004 (AC-12) — lapses: forced by a scenario, or drawn past the arena's stalemate (per entity, deterministic) ─────────

        /// <summary>The draw's chance per second past the stalemate, by skill (Rookie · Veteran · Ace).</summary>
        public static float LapseRate(PilotSkill s) => s switch { PilotSkill.Ace => 0.02f, PilotSkill.Veteran => 0.03f, _ => 0.05f };
        /// <summary>How long a drawn lapse lasts (s); a G-LOC is shorter; a LateWarning adds this to the reaction.</summary>
        public const float LapseS = 10f, GLocS = 3f, LateWarningS = 2f;
        /// <summary>G-LOC after this long (s) above <see cref="GLocG"/>.</summary>
        public const float GLocAfterS = 5f, GLocG = 8f;
        private const int LapseSalt = 0xA1C0;

        private static void Lapses(ref AirCombatState cs, EntityRepository world, Entity self, double now, bool inArena, in AirArena arena)
        {
            if (cs.ForcedLapse != PilotLapse.None) { cs.Lapse = cs.ForcedLapse; return; }
            if (cs.Lapse != PilotLapse.None && now >= (double)cs.LapseUntil) { cs.Lapse = PilotLapse.None; cs.LapseUntil = (SimInstant)(now + 1.0); cs.HighGSeconds = 0f; }
            if (!inArena || arena.LapsesOff != 0 || cs.Lapse != PilotLapse.None) return;
            double quiet = now - Math.Max((double)arena.LastHitAt, (double)cs.StartedAt);
            float stalemate = arena.StalemateSeconds > 0f ? arena.StalemateSeconds : AirArena.DefaultStalemateSeconds;
            if (quiet < stalemate || now < (double)cs.LapseUntil) return;
            cs.LapseUntil = (SimInstant)(Math.Floor(now) + 1.0);   // one draw per second
            float g = world.IsComponentTypeRegistered<FighterPilotState>() && world.HasComponent<FighterPilotState>(self) ? world.GetComponentRO<FighterPilotState>(self).GLoad : 1f;
            cs.HighGSeconds = g > GLocG ? cs.HighGSeconds + 1f : 0f;
            if (cs.HighGSeconds >= GLocAfterS) { cs.Lapse = PilotLapse.GLoc; cs.LapseUntil = (SimInstant)(now + GLocS); return; }
            var rng = Fdp.Toolkit.Determinism.SimRng.FromSim(self.Index, LapseSalt, (float)Math.Floor(now));
            if (rng.NextSingle() >= LapseRate(cs.Skill)) return;
            float pick = rng.NextSingle();
            cs.Lapse = pick < 0.34f ? PilotLapse.Fixation : pick < 0.67f ? PilotLapse.LateWarning : PilotLapse.EnergyBleed;
            cs.LapseUntil = (SimInstant)(now + LapseS);
        }

        private static float Radius(in AirArena a) => a.RadiusM > 0f ? a.RadiusM : AirArena.DefaultRadiusM;

        /// <summary>How far outside the arena's radius <paramref name="at"/> is (m; ≤ 0 inside).</summary>
        public static float OutsideBy(Vector3 at, in AirArena a)
            => Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(a.Centre.X, a.Centre.Y)) - Radius(in a);

        // ── weapons ──────────────────────────────────────────────────────────────────────────────────────

        /// <summary>⭐ AC-8 — one missile at a time per target, only inside the zone (the skill's share of it, never beyond the arena's
        /// <see cref="AirArena.MaxShotRangeM"/>), with the target inside the seeker's half-cone. IR within 3 km, else the radar one.</summary>
        private static void Missile(ref AirCombatState cs, EntityRepository world, Entity self, Vector3 me, Vector3 myVel, Vector3 tp, Vector3 tv, double now, float cap)
        {
            if (now - (double)cs.LastShotAt < RetryShotS) return;
            // one missile at a time per target (AC-8): after HIS shot a pilot waits for its outcome — the target's live inbound count back to 0
            //   (or MissileOutcomeS); another pilot's missile does not hold him
            if (cs.LastShotAt != SimInstant.Zero && now - (double)cs.LastShotAt < MissileOutcomeS
                && world.IsComponentTypeRegistered<FighterPilotState>() && world.HasComponent<FighterPilotState>(cs.Target)
                && world.GetComponentRO<FighterPilotState>(cs.Target).Inbound > 0) return;

            float range = Vector3.Distance(me, tp);
            byte mount = range <= 3000f && Rounds(world, self, cs.IrMount) > 0 ? cs.IrMount
                       : Rounds(world, self, cs.RadarMount) > 0 ? cs.RadarMount
                       : Rounds(world, self, cs.IrMount) > 0 ? cs.IrMount : AirCombatState.NoMount;
            if (mount == AirCombatState.NoMount || range > cap) return;
            var dto = CombatTkb.MountOf(world, self, mount);
            var missile = dto == null ? null : CombatTkb.MissileOf(world, unchecked((long)dto.AmmoGuid));
            if (missile == null) return;
            float cone = missile.Guidance == GuidanceKind.InfraRed ? 45f : 30f;
            if (AngleDeg(myVel, tp - me) > cone) return;
            if (!EmploymentZone.Inside(missile, me, myVel, tp, tv, ShotFraction(cs.Skill), cs.Skill == PilotSkill.Ace)) return;
            if (LineOfFire.BlockedByFriendly(world, self, cs.Target)) return;   // RV-11 — never through a friend
            Launch(world, self, mount, cs.Target, now);
            if (cs.Shots < 255) cs.Shots++;
            cs.LastShotAt = (SimInstant)now;
        }

        /// <summary>
        /// ⭐ CE-10003 — a missile off its rail at a NAMED target: one ordinary <see cref="WeaponFireIntent"/> on the mount (asked through the
        /// mount's <see cref="ShotLedger"/>, spent by the muscle's answer — the executor's own accounting), the rail's interval started. The
        /// aimed-fire executor is not used: its laying / lock gates are a turret's, and a fixed rail on a jet is laid by the pilot's nose
        /// (the cone and the zone above) — 📐 measured: through <c>AimAndFire</c> no AAM left its rail in 2 s (ac-winchester).
        /// </summary>
        private static void Launch(EntityRepository world, Entity self, byte mount, Entity target, double now)
        {
            if (!MountEntity(world, self, mount, out var mountEntity)) return;
            ref var weapon = ref world.GetComponentRW<WeaponState>(mountEntity);
            if (weapon.Ammo == 0 || !Magazine.Ready(in weapon) || weapon.CooldownSecondsRemaining > 0f) return;
            world.Bus.Publish(new WeaponFireIntent
            {
                Shooter = self, Target = target, WeaponIndex = mount, RequestId = ShotLedger.Ask(ref weapon),
                RaisedAt = (SimInstant)HitModel.Now(world),
            });
            weapon.CooldownSecondsRemaining = 1f;
        }

        /// <summary>The entity whose <see cref="WeaponState"/> is mount <paramref name="mount"/> (the owner for 0, a mount child otherwise).</summary>
        private static bool MountEntity(EntityRepository world, Entity self, byte mount, out Entity mountEntity)
        {
            mountEntity = self;
            if (mount == 0) return world.HasComponent<WeaponState>(self);
            if (!world.IsComponentTypeRegistered<WeaponMountInfo>()) return false;
            Span<Entity> mounts = stackalloc Entity[8];
            int n = WeaponMountQuery.EnumerateMounts(world, self, mounts);
            for (int i = 0; i < n; i++)
                if (world.HasComponent<WeaponMountInfo>(mounts[i]) && world.GetComponentRO<WeaponMountInfo>(mounts[i]).MountIndex == mount
                    && world.HasComponent<WeaponState>(mounts[i])) { mountEntity = mounts[i]; return true; }
            return false;
        }

        /// <summary>⭐ AC-8 / RV-10 — the gun: within <see cref="GunRangeM"/>, the nose within <see cref="GunConeDeg"/> of the lead that adds the
        /// target's ACCELERATION (½·a·t², <see cref="TargetLead.PointAccelerating"/>); a burst at that point, re-aimed every tick.</summary>
        private static void Gun(ref AirAttackState ws, ref AirCombatState cs, EntityRepository world, Entity self, Vector3 me, Vector3 myVel, Vector3 tp, Vector3 tv)
        {
            if (cs.GunMount == AirCombatState.NoMount) return;
            float range = Vector3.Distance(me, tp);
            if (Rounds(world, self, cs.GunMount) <= 0) { StopGun(world, self, ref ws); return; }
            float speed = AirAttackNodes.RoundSpeedOf(world, self, cs.GunMount);
            var lead = TargetLead.PointAccelerating(me, myVel, speed, tp, tv, cs.TargetAccel);
            bool inside = range <= GunRangeM && range >= BreakAwayM && AngleDeg(Nose(world, self, myVel), lead - me) <= GunConeDeg;
            if (!inside && ws.Fire.Started == 0) return;
            if (!inside) { StopGun(world, self, ref ws); return; }
            var fire = new FireAtPointNodeParams { Point = lead, Mount = cs.GunMount, Started = ws.Fire.Started, Rounds = GunBurst };
            var status = FireAtPointNodes.FireAtPoint(ref fire, self, world);
            ws.Fire = fire;
            if (status == NodeStatus.Running) AirAttackNodes.RetargetFire(world, self, ws.Fire.Started, lead);
            else ws.Fire = default;
        }

        /// <summary>⭐ CE-10005 (AC-8, R-10024) — where the fixed gun POINTS: the nose, which rides above the velocity by the angle of attack the
        /// pull needs (<see cref="AoaDegPerG"/> × the g, toward the lift) — 📐 measured on ac-guns-turning: in a sustained 5 g turn the
        /// velocity held a steady 2.7–2.9° behind the gun's lead (the integrator's proportional pull), so a velocity-boresighted gun never fired.</summary>
        public static Vector3 Nose(EntityRepository world, Entity self, Vector3 vel)
        {
            float v = vel.Length();
            if (v < 1f || !world.HasComponent<SimTransform>(self)) return vel;
            var f = vel / v;
            var up = Steer3D.Up(world.GetComponentRO<SimTransform>(self).Rotation);
            up -= f * Vector3.Dot(up, f);
            if (up.LengthSquared() < 1e-6f) return vel;
            float g = world.IsComponentTypeRegistered<FighterPilotState>() && world.HasComponent<FighterPilotState>(self)
                ? world.GetComponentRO<FighterPilotState>(self).GLoad : 1f;
            float aoa = AoaDegPerG * MathF.Max(1f, g) * (MathF.PI / 180f);
            return f + Vector3.Normalize(up) * MathF.Tan(aoa);
        }

        /// <summary>The angle of attack per g (°) — the nose above the flight path in a pull.</summary>
        public const float AoaDegPerG = 0.6f;

        private static void StopGun(EntityRepository world, Entity self, ref AirAttackState ws)
        {
            if (ws.Fire.Started != 0 && world.HasComponent<WeaponChannel>(self))
            {
                ref var channel = ref world.GetComponentRW<WeaponChannel>(self);
                if (channel.ActionInstanceId == ws.Fire.Started && channel.ActiveAction == CombatConstants.ActionIdFireAtPoint)
                {
                    channel.ActiveAction = 0;
                    unchecked { channel.ActionInstanceId++; if (channel.ActionInstanceId == 0) channel.ActionInstanceId = 1; }
                }
            }
            ws.Fire = default;
        }

        /// <summary>Rounds left on <paramref name="mount"/>: the replicated <see cref="WeaponAmmo"/> mirror, else the type's load.</summary>
        public static int Rounds(EntityRepository world, Entity self, byte mount)
        {
            if (mount == AirCombatState.NoMount) return 0;
            if (MountEntity(world, self, mount, out var me)) return world.GetComponentRO<WeaponState>(me).Ammo;
            if (world.IsComponentTypeRegistered<WeaponAmmo>() && world.HasComponent<WeaponAmmo>(self))
            {
                var a = world.GetComponentRO<WeaponAmmo>(self);
                if (mount < WeaponAmmo.MaxMounts && mount < a.Count) return a.AmmoOf(mount);
            }
            return CombatTkb.MountOf(world, self, mount)?.InitialAmmunition ?? 0;
        }

        private static bool Winchester(EntityRepository world, Entity self, in AirCombatState cs)
            => Rounds(world, self, cs.GunMount) <= 0 && Rounds(world, self, cs.IrMount) <= 0 && Rounds(world, self, cs.RadarMount) <= 0;

        private static bool Damaged(EntityRepository world, Entity self)
        {
            if (!world.HasComponent<Health>(self)) return false;
            var h = world.GetComponentRO<Health>(self);
            return h.Max > 0f && h.Current < DisengageHealth * h.Max;
        }

        // ── plumbing ──────────────────────────────────────────────────────────────────────────────────────

        /// <summary>The fight's start: the mounts found once (gun = the first forward gun, IR / radar = the first air-to-air mount whose
        /// missile has that seeker).</summary>
        private static void Begin(EntityRepository world, Entity self, ref AirCombatState cs, double now)
        {
            cs.StartedAt = (SimInstant)Math.Max(now, 1e-6);
            cs.GunMount = cs.IrMount = cs.RadarMount = AirCombatState.NoMount;
            cs.Manoeuvre = Manoeuvre.None;
            cs.LastShotAt = SimInstant.Zero;
            for (byte i = 0; i < 8; i++)
            {
                var m = CombatTkb.MountOf(world, self, i);
                if (m == null) break;
                if (m.Category == WeaponCategory.AirToAir)
                {
                    var missile = CombatTkb.MissileOf(world, unchecked((long)m.AmmoGuid));
                    if (missile?.Guidance == GuidanceKind.InfraRed && cs.IrMount == AirCombatState.NoMount) cs.IrMount = i;
                    else if (missile?.Guidance == GuidanceKind.Radar && cs.RadarMount == AirCombatState.NoMount) cs.RadarMount = i;
                }
                else if (cs.GunMount == AirCombatState.NoMount && m.Category != WeaponCategory.Countermeasure
                         && CombatTkb.MissileOf(world, unchecked((long)m.AmmoGuid)) == null) cs.GunMount = i;
            }
        }

        /// <summary>⭐ AC-2 — mode 8, entered once (a new <c>IntentId</c>: the motion bridge applies a mode only on a new id) — the
        /// <c>OperatorControlBehavior.ManualControl</c> precedent.</summary>
        private static void EnterModeEight(EntityRepository world, Entity self)
        {
            if (!world.HasComponent<NavigationIntent>(self)) return;
            ref var intent = ref world.GetComponentRW<NavigationIntent>(self);
            if (intent.Mode == NavigationMode.AirCombat) return;
            intent.Mode = NavigationMode.AirCombat;
            unchecked { intent.IntentId++; }
        }

        /// <summary>The order down, written only when it changed (every change is a sample on the wire).</summary>
        private static void WriteSteer(EntityRepository world, Entity self, in AirCombatSteer s)
        {
            if (world.HasComponent<AirCombatSteer>(self))
            {
                ref var cur = ref world.GetComponentRW<AirCombatSteer>(self);
                if (!SameBytes(in cur, in s)) cur = s;
            }
            else world.AddComponent(self, s);
        }

        /// <summary>The target's acceleration from its velocity, re-sampled every 0.25 s.</summary>
        private static void SampleAccel(ref AirCombatState cs, Vector3 tv, float t)
        {
            float dt = t - cs.TargetVelAt;
            if (cs.TargetVelAt > 0f && dt < 0.25f) return;
            cs.TargetAccel = cs.TargetVelAt > 0f && dt > 1e-3f && dt < 2f ? (tv - cs.TargetVel) / dt : Vector3.Zero;
            cs.TargetVel = tv;
            cs.TargetVelAt = MathF.Max(t, 1e-3f);
        }

        private static float SpeedOf(EntityRepository world, Entity self, Vector3 v)
            => world.IsComponentTypeRegistered<FighterPilotState>() && world.HasComponent<FighterPilotState>(self)
               && world.GetComponentRO<FighterPilotState>(self).SpeedMps > 0f ? world.GetComponentRO<FighterPilotState>(self).SpeedMps : v.Length();

        private static float CornerOf(EntityRepository world, Entity self)
            => world.HasComponent<FlightParams>(self) && world.GetComponentRO<FlightParams>(self).CornerSpeedMps > 0f
               ? world.GetComponentRO<FlightParams>(self).CornerSpeedMps : 170f;

        private static float AngleDeg(Vector3 a, Vector3 b)
        {
            float la = a.Length(), lb = b.Length();
            if (la < 1e-3f || lb < 1e-3f) return 180f;
            return MathF.Acos(Math.Clamp(Vector3.Dot(a, b) / (la * lb), -1f, 1f)) * (180f / MathF.PI);
        }

        private static Vector2 Flat(Vector3 v)
        {
            var f = new Vector2(v.X, v.Y);
            return f.LengthSquared() > 1e-6f ? Vector2.Normalize(f) : Vector2.UnitY;
        }


        internal static bool SameBytes<T>(in T a, in T b) where T : unmanaged
            => System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in a))
                .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in b)));
    }
}
