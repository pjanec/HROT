using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Motion;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.AI.Behaviors.Orders;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ CE-10002 (AC-9, RV-2 / RV-3 / RV-14; docs/designs/air-attack/DESIGN_Air_Combat.md §3) — THE ONE missile defence for every aircraft:
    /// called by the air-combat behaviour (<see cref="AirCombatNodes"/>, a fixed-wing in mode 8) and by the air attack
    /// (<see cref="AirAttackNodes"/>, its evade stage folded in here). ⭐ Started by a LAUNCH only (the brain's <see cref="MissileWarning"/>, seen
    /// after the pilot's reaction); it then reads the LIVE threat from the motion node's <see cref="FighterPilotState"/> (count, seeker, time to
    /// impact, bearing) when the aircraft has one, else the launch snapshot (a helicopter). It ends when no round is inbound.
    /// IR far (&gt; <see cref="DragAboveS"/> to go) ⇒ Drag · IR near ⇒ Beam + flares (§7e E4) · radar ⇒ Beam / notch + chaff in the end-game.
    /// TWO outputs: a <see cref="Manoeuvre"/> for a fighter in mode 8, or a MoveTo point (today's beam, the rotorcraft's drop) issued here.
    /// The state lives in the aircraft's own <see cref="MissileDefenceState"/> (written only here; per-entity, R-4212).
    /// </summary>
    public static class MissileDefence
    {
        /// <summary>An IR missile further than this (s to go) is DRAGGED (turn away, bleed its energy); nearer it is beamed with flares.</summary>
        public const float DragAboveS = 4f;
        /// <summary>A defence holds at least this long (s), and (no live threat) this margin past the snapshot's time to impact.</summary>
        public const float MinEvadeS = AirAttackNodes.MinEvadeS, EvadeMarginS = AirAttackNodes.EvadeMarginS;
        /// <summary>The live threat is read this long after the launch before "nothing inbound" can end the defence (the scan's latency, s).</summary>
        public const float LiveGraceS = 1f;
        /// <summary>The motion node's reflex counts as this defence's last-ditch phase for this long after it fired (s).</summary>
        public const float LastDitchHoldS = 1.5f;

        /// <summary>How the caller flies the defence.</summary>
        public struct Flight
        {
            /// <summary>True: a fighter in mode 8 — the defence answers a <see cref="Manoeuvre"/> (the caller writes its steer). False: a
            /// MoveTo is issued here (the beam break, <see cref="AirAttackNodes.BreakAwayM"/> out).</summary>
            public bool Fighter;
            /// <summary>The MoveTo's absolute height (m); speed (0 ⇒ the aircraft's own).</summary>
            public float MoveZ, Speed;
            /// <summary>The heading to break from when the aircraft has no velocity yet (unit, horizontal).</summary>
            public Vector2 FallbackDir;
            /// <summary>The pilot's reaction to a launch (s; AC-12's skill + a LateWarning lapse's 2 s). 0 = at once.</summary>
            public float ReactionS;
            /// <summary>⭐ CE-10005 (AC-12, R-10019) — the decoys this pilot releases against ONE missile (0 ⇒ <see cref="AirAttackNodes.DecoySalvo"/>):
            /// a Rookie releases <see cref="RookieDecoys"/>.</summary>
            public byte DecoyCap;
            /// <summary>⭐ CE-10005 (AC-12, R-10019) — how well the pilot reads the missile: <see cref="Reading.Rookie"/> never drags (he turns
            /// level across it — a beam); <see cref="Reading.Ace"/> drags ANY seeker while it is far (a max-range radar shot runs out of energy
            /// behind him); the default (a Veteran, any non-fighter) is AC-9's rule: IR far ⇒ drag, else beam.</summary>
            public Reading Read;
        }

        /// <summary>How the pilot reads an incoming missile (AC-12's decision quality on the defence).</summary>
        public enum Reading : byte { Veteran = 0, Rookie = 1, Ace = 2 }

        /// <summary>A Rookie's decoys per missile (AC-12, R-10019).</summary>
        public const byte RookieDecoys = 2;

        /// <summary>The aircraft's defence state (default when it has none yet).</summary>
        public static MissileDefenceState Load(EntityRepository world, Entity self)
            => world.IsComponentTypeRegistered<MissileDefenceState>() && world.HasComponent<MissileDefenceState>(self)
                ? world.GetComponentRO<MissileDefenceState>(self) : default;

        /// <summary>Stores it — added WITH its value the first time (D-31), never added then written.</summary>
        public static void Store(EntityRepository world, Entity self, in MissileDefenceState ds)
        {
            if (!world.IsComponentTypeRegistered<MissileDefenceState>()) return;
            if (world.HasComponent<MissileDefenceState>(self)) world.GetComponentRW<MissileDefenceState>(self) = ds;
            else world.AddComponent(self, ds);
        }

        /// <summary>
        /// One decision. True while a defence runs (the caller's own work waits); <paramref name="manoeuvre"/> is the fighter's answer
        /// (<see cref="Manoeuvre.None"/> for a MoveTo flight). <paramref name="started"/> is true on the frame it began (the caller stops
        /// its fire). Allocation-free.
        /// </summary>
        public static bool Run(EntityRepository world, Entity self, ref MissileDefenceState ds, double now, in Flight flight,
                               out Manoeuvre manoeuvre, out bool started)
        {
            manoeuvre = Manoeuvre.None;
            started = false;
            bool warned = world.IsComponentTypeRegistered<MissileWarning>() && world.HasComponent<MissileWarning>(self);
            var w = warned ? world.GetComponentRO<MissileWarning>(self) : default;
            bool live = world.IsComponentTypeRegistered<FighterPilotState>() && world.HasComponent<FighterPilotState>(self);
            var pilot = live ? world.GetComponentRO<FighterPilotState>(self) : default;

            // ── start: a launch this aircraft has not answered, seen after the pilot's reaction, its missile still in the air ──
            if (warned && w.At > ds.WarnedAt && now >= (double)w.At + flight.ReactionS)
            {
                ds.WarnedAt = w.At;
                bool inAir = live ? pilot.Inbound > 0 || now - (double)w.At < LiveGraceS
                                  : now <= (double)w.At + MathF.Max(w.TimeToImpactS, MinEvadeS) + EvadeMarginS;
                if (inAir)
                {
                    if (ds.Phase == MissileDefencePhase.None)
                    {
                        started = true;
                        ds.StartedAt = (SimInstant)now;
                        ds.Decoys = 0;
                        ds.Count++;
                    }
                    ds.Seeker = w.Seeker;
                    if (flight.Fighter) ds.Phase = PhaseOf(live, in pilot, w.Seeker, Tti(live, in pilot, in w, now), now, flight.Read);
                    else
                    {
                        // a MoveTo flight always BEAMS (§7e E4 — the drag is a fighter's); a second launch re-breaks
                        Break(world, self, in w, in flight, MissileDefencePhase.Beam);
                        ds.Phase = MissileDefencePhase.Beam;
                    }
                }
            }
            if (ds.Phase == MissileDefencePhase.None) return false;

            // ── the threat now: live from the motion node, else the launch snapshot ──
            var seeker = live && pilot.Inbound > 0 ? pilot.ThreatSeeker : ds.Seeker;
            float tti = Tti(live, in pilot, in w, now);
            bool over = live
                ? pilot.Inbound == 0 && now - (double)ds.StartedAt >= MathF.Max(LiveGraceS, flight.Fighter ? LiveGraceS : MinEvadeS)
                : now >= (double)w.At + MathF.Max(w.TimeToImpactS, MinEvadeS) + EvadeMarginS;
            if (over) { ds.Phase = MissileDefencePhase.None; return false; }

            if (flight.Fighter)
            {
                ds.Phase = PhaseOf(live, in pilot, seeker, tti, now, flight.Read);
                manoeuvre = ds.Phase switch
                {
                    MissileDefencePhase.Drag => Manoeuvre.Drag,
                    MissileDefencePhase.LastDitch => Manoeuvre.LastDitch,
                    _ => Manoeuvre.Beam,
                };
            }

            // ── the decoys of the seeker's kind: flares near (beam / last ditch), chaff only in the end-game (RS-3) ──
            int perMissile = flight.DecoyCap > 0 ? flight.DecoyCap : AirAttackNodes.DecoySalvo;
            int cap = perMissile + (perMissile / 2) * Math.Max(0, (live ? pilot.Inbound : 1) - 1);
            if (ds.Decoys < cap && ds.Phase != MissileDefencePhase.Drag && AirAttackNodes.DecoyFor(seeker) is { } kind
                && !(kind == DecoyKind.Chaff && tti > Fdp.Toolkit.Combat.Systems.CountermeasureSystem.ChaffEndGameS)
                && Countermeasures.Release(world, self, kind))
            {
                ds.Decoys++;
                ds.DecoysTotal++;
                ds.LastDecoyAt = (SimInstant)now;
            }
            return true;
        }

        private static float Tti(bool live, in FighterPilotState pilot, in MissileWarning w, double now)
            => live && pilot.Inbound > 0 ? pilot.ThreatTtiS : MathF.Max(0f, w.TimeToImpactS - (float)(now - (double)w.At));

        /// <summary>The fighter's phase: the motion node's reflex fired lately ⇒ LastDitch; far ⇒ Drag (IR; an Ace any seeker; a Rookie never);
        /// else Beam.</summary>
        private static MissileDefencePhase PhaseOf(bool live, in FighterPilotState pilot, GuidanceKind seeker, float tti, double now, Reading read)
        {
            if (live && pilot.LastDitchAt != SimInstant.Zero && now - (double)pilot.LastDitchAt < LastDitchHoldS) return MissileDefencePhase.LastDitch;
            bool drags = read switch { Reading.Rookie => false, Reading.Ace => true, _ => seeker == GuidanceKind.InfraRed };
            return drags && tti > DragAboveS ? MissileDefencePhase.Drag : MissileDefencePhase.Beam;
        }

        /// <summary>⭐ CE-2191 (§7e E4) — the MoveTo break: 90° to the line to the missile on the side nearer the heading, a little away from it,
        /// <see cref="AirAttackNodes.BreakAwayM"/> out at <see cref="Flight.MoveZ"/>.</summary>
        private static void Break(EntityRepository world, Entity self, in MissileWarning w, in Flight flight, MissileDefencePhase phase)
        {
            if (!world.HasComponent<SimTransform>(self)) return;
            var at = world.GetComponentRO<SimTransform>(self).Position;
            float b = w.BearingDeg * (MathF.PI / 180f);
            var toMissile = new Vector2(MathF.Sin(b), MathF.Cos(b));   // 0 = north (+Y), clockwise
            var beam = new Vector2(toMissile.Y, -toMissile.X);
            var heading = Fdp.Toolkit.Replication.ReplicatedMotion.Of(world, self, out _, out var v) && v.X * v.X + v.Y * v.Y > 1f
                ? Vector2.Normalize(new Vector2(v.X, v.Y)) : flight.FallbackDir;
            if (Vector2.Dot(beam, heading) < 0f) beam = -beam;
            var dir = phase == MissileDefencePhase.Drag ? -toMissile : Vector2.Normalize(beam - toMissile * AirAttackNodes.EvadeDragFraction);
            var to = at + new Vector3(dir * AirAttackNodes.BreakAwayM, 0f);
            LocomotionMoveTo.Issue(world, self, new Vector3(to.X, to.Y, flight.MoveZ), flight.Speed, AirOrderNodes.DefaultArrivalRadius, source: MoveSource.Attack);
        }

        // ── ⭐ CE-4617 (RS-6) — the radar escape, folded in here (RV-14); ⭐ RV-3: an AIRCRAFT emitter (a fighter's radar) is exempt ──

        /// <summary>Is <paramref name="self"/> TRACKED by a GROUND radar now (its own <see cref="Fdp.Toolkit.Perception.Radar.RadarWarning"/>)? A
        /// searching radar does not count; ⭐ RV-3 — a radar carried by an aircraft does not either (both fighters track each other all fight).</summary>
        public static bool RadarTracked(EntityRepository world, Entity self)
        {
            if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Radar.RadarWarning>()
                || !world.HasComponent<Fdp.Toolkit.Perception.Radar.RadarWarning>(self)) return false;
            var w = world.GetComponentRO<Fdp.Toolkit.Perception.Radar.RadarWarning>(self);
            if (w.Level != Fdp.Toolkit.Perception.Radar.RadarWarningLevel.Tracked) return false;
            return w.Emitter.IsNull || !world.IsAlive(w.Emitter) || !Locomotions.IsAircraft(world, w.Emitter);
        }

        /// <summary>⭐ CE-4617 — the radar escape leg: away from the radar's bearing (half beam, half away) down to <paramref name="z"/>, below
        /// its clutter floor.</summary>
        public static void RadarEscape(EntityRepository world, Entity self, Vector3 at, float z, float speed, Vector2 fallbackDir)
        {
            var w = world.GetComponentRO<Fdp.Toolkit.Perception.Radar.RadarWarning>(self);
            float b = w.BearingDeg * (MathF.PI / 180f);
            var toRadar = new Vector2(MathF.Sin(b), MathF.Cos(b));   // 0 = north (+Y), clockwise
            var beam = new Vector2(toRadar.Y, -toRadar.X);
            var heading = Fdp.Toolkit.Replication.ReplicatedMotion.Of(world, self, out _, out var v) && v.X * v.X + v.Y * v.Y > 1f
                ? Vector2.Normalize(new Vector2(v.X, v.Y)) : fallbackDir;
            if (Vector2.Dot(beam, heading) < 0f) beam = -beam;
            var dir = Vector2.Normalize(beam - toRadar);
            var to = at + new Vector3(dir * AirAttackNodes.BreakAwayM, 0f);
            LocomotionMoveTo.Issue(world, self, new Vector3(to.X, to.Y, z), speed, AirOrderNodes.DefaultArrivalRadius, source: MoveSource.Flee);
        }
    }
}
