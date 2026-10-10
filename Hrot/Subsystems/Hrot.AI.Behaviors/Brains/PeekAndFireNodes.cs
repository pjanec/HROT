using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Kernel;
using FDP.Eqs;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Executors;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Tkb.Domain;
using Fdp.Toolkit.Utility;
using Hrot.MuscleCharacter.Animation.Stance;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-3136</c> P-6 (D7) — how the unit exposes itself.</summary>
    public enum PeekMode : byte
    {
        /// <summary>⭐ <c>CE-3158</c> G1 — from the POINT (<see cref="EqsResult.Kind"/>): a window ⇒ <see cref="Stance"/> at its stance; a
        /// cover ⇒ hide at its stance and come up one stance taller; a cover already standing, a point that is not cover, or a
        /// unit with no stance ⇒ <see cref="Step"/>. ⛔ SUPERSEDED (CE-3136 D7): keyed on the hide TEMPLATE, so a window found by
        /// another query peeked wrong.</summary>
        Auto = 0,
        /// <summary>The same spot: hide low (prone under the sill), expose at the point's stance.</summary>
        Stance = 1,
        /// <summary>Hide behind cover, step out to a nearby point that sees the enemy.</summary>
        Step = 2,
    }

    /// <summary>
    /// ⭐ <c>CE-3136</c> P-6 — the designer-edited parameters of <see cref="PeekAndFireNodes.PeekAndFire"/> (📄 §8.4). ⭐ A zero means
    /// "the default" (<see cref="PeekAndFireNodes.Effective"/>), so a blackboard nobody filled flies the duel's defaults.
    /// ⚠ The aim time is NOT here: it is the weapon's (D3), so every behaviour aims alike.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PeekAndFireParams
    {
        /// <summary>The cover sensor's template — <see cref="FindWindowFiringPosition"/> (a window) or <see cref="FindCoverFromTarget"/>.</summary>
        public uint HideTemplate;
        /// <summary>How far the cover query looks (m) · how far round the hide point the step-peek query looks (m).</summary>
        public float SearchRadius, PeekSearchRadius;
        /// <summary>See <see cref="PeekMode"/>.</summary>
        public PeekMode Mode;
        /// <summary>The hide stance: 0 = from the point (prone under a window for a stance peek), else <c>StanceId + 1</c>.</summary>
        public byte HideStanceOverride;
        /// <summary>A random wait in this range while hidden (s).</summary>
        public float HideSecondsMin, HideSecondsMax;
        /// <summary>The longest an exposure lasts (s) · the time to SEE the enemy before a blind burst instead (s).</summary>
        public float ExposeSeconds, GraceSeconds;
        /// <summary>A near miss or a hit this recent keeps the unit down (s) (B8, D12).</summary>
        public float SuppressedSeconds;
        /// <summary>Aimed rounds per exposure (B7's count) · rounds of a blind burst.</summary>
        public int RoundsPerExposure, BlindRounds;
        /// <summary>Seconds between rounds.</summary>
        public float FireCooldownSeconds;
        /// <summary>A blind burst flies at the remembered spot raised by this much (m) — the body's middle, not the feet.</summary>
        public float BlindAimHeight;
        /// <summary>Exposures from one hide point before relocating (B4's heat may relocate sooner).</summary>
        public int ExposuresPerPosition;
        /// <summary>B2 — heat an exposure adds · heat being near-missed or hit while exposed adds.</summary>
        public float HeatPerExposure, HeatWhenFiredUpon;
        /// <summary>B4 — burned at · usable again below · a usable warm spot's score penalty per unit of heat.</summary>
        public float BurnHeat, ReuseHeat, HeatPenalty;
        /// <summary>B2/B3 — heat halves in this many seconds · B5 — a candidate within this of a slot IS that slot (m).</summary>
        public float CoolHalfLifeSeconds, MatchRadius;
        /// <summary>Travel speed (m/s) · a new hide point at least this far from the current one (m).</summary>
        public float RelocateSpeed, MinRelocateMetres;
        /// <summary>D12 — 1 = a suppressive burst before relocating (built with P-7).</summary>
        public byte SuppressBeforeRelocate;
        /// <summary>Which forces count as threats for the queries' exposure scoring (0 = every acquired contact).</summary>
        public uint FactionFilter;
        /// <summary>The sensors publish a new answer only when a top score moved by more than this.</summary>
        public float ScoreDeltaThreshold;
        /// <summary>⭐ <c>CE-3158</c> G1 — the stance a STANCE peek comes up to: 0 = from the point (a window's stance; one taller than a
        /// cover's), else <c>StanceId + 1</c> — e.g. "crouch behind a low wall, stand to fire".</summary>
        public byte PeekStanceOverride;
        /// <summary>⭐ <c>CE-3158</c> G2 — with no usable cover point this long the node answers Failure (the posture re-scores);
        /// a step peek with no step-out point this long moves to the next cover (s).</summary>
        public float NoCoverSeconds;
        /// <summary>⭐ <c>CE-3158</c> G4 — a heard shot moves a burst only when it is within this of the locked target's remembered spot
        /// (m); a shot heard elsewhere is another enemy, not this one moving.</summary>
        public float HeardMatchRadius;
    }

    /// <summary>
    /// ⭐ <c>CE-3136</c> P-6 — the node's working state (📄 §8.3): scratch, ⛔ lost on every exit by design — what must outlive an
    /// exit lives in <see cref="FiringPositionMemory"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PeekAndFireState
    {
        public PeekPhase Phase;
        /// <summary>1 = a step peek (else a stance peek) · 1 while stepping out to the peek point · 1 once this exposure was counted.</summary>
        public byte PeekIsStep, Stepping, Counted;
        /// <summary>1 while a move this node issued runs.</summary>
        public byte Moving;
        public StanceId HideStance, PeekStance;
        /// <summary>When the current phase's timer ends · when the unit came up · when the enemy was last seen (sim s).</summary>
        public double PhaseUntil, ExposedAt, LastSeenAt;
        public Vector3 HidePoint, PeekPoint;
        /// <summary>The hide-point sensor (<see cref="PeekAndFireParams.HideTemplate"/>) · the step-peek sensor.</summary>
        public EqsSensorHandle CoverSensor, PeekSensor;
        /// <summary>What both sensors are pointed at (an entity, or a heard contact and its point).</summary>
        public Entity Threat;
        public long HeardId;
        public Vector3 HeardPoint;
        /// <summary>Exposures from the current hide point · exposures of this run (the random wait's salt).</summary>
        public int ExposuresHere, Exposures;
        /// <summary>The aimed fire (the posture's ONE fire step) · the blind burst (<see cref="FireAtPointNodes.FireAtPoint"/>).</summary>
        public EngageState Fire;
        public FireAtPointNodeParams Blind;
        /// <summary>⭐ P-7 D12 — 1 while suppressing before a bound: the next cover is picked (<see cref="NextHide"/>), the unit is up
        /// firing a suppressive burst, and on the way down it runs THERE, not back to the old hide point.</summary>
        public byte Bounding;
        /// <summary>The next cover's stance (<see cref="StanceId"/> + 1; 0 = none) · its kind (<see cref="EqsResult.Kind"/>'s encoding).</summary>
        public byte NextStance, NextKind;
        public Vector3 NextHide;
        /// <summary>⭐ <c>CE-3144</c> (P-8 D14) — where both sensors look: the memory slot (its id) and the spot they were pointed at.</summary>
        public long SensorId;
        public Vector3 SensorAt;
        /// <summary>⭐ <c>CE-3158</c> G2 — 1 while the node waits for a point (no cover answer, or no step-out point) · since when (sim s).
        /// 2 = every answered cover point is burned: the unit fires from where it stands meanwhile.</summary>
        public byte Waiting;
        public double WaitingSince;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3136</c> P-6 (D7 + D8, B1–B8; R-234, R-238) — <c>PeekAndFire</c>: hide, expose briefly, fire, hide again, and move
    /// on when a position is used up. ONE machine flies both soldiers of the window duel — a stance peek (A: prone under the sill,
    /// kneel at it) and a step peek (B: behind a corner, step out) — only the parameters differ. 📄 docs/DESIGN_Peek_And_Fire.md §8.
    /// <para>Reuses the shared steps (R-174): the threat ranking and heard-contact aim (<see cref="EqsTacticsNodes.TopAim"/>), the
    /// sensor keeping (<see cref="PostureNodes.Keep"/>), the ONE aimed fire step (<c>PostureNodes.Fire</c> with B7's round
    /// count — the executor's sight gate and aim time apply), the blind burst (<see cref="FireAtPointNodes.FireAtPoint"/>), the
    /// stance request and the move.</para>
    /// </summary>
    public static class PeekAndFireNodes
    {
        /// <summary>The two sensor sites (with the run's owner stamp they find this run's sensors again).</summary>
        public const int CoverSite = 0x31360001, PeekSite = 0x31360002;

        /// <summary>Arrival radius of the node's moves (m) · a stance change's blend time (s).</summary>
        public const float ArrivalRadius = 0.5f, StanceBlendSeconds = 0.6f;

        /// <summary>The §8.4 default column.</summary>
        public static readonly PeekAndFireParams Defaults = new()
        {
            HideTemplate = FindCoverFromTarget.BlueprintId,
            SearchRadius = 15f, PeekSearchRadius = 3f,
            HideSecondsMin = 2f, HideSecondsMax = 5f,
            ExposeSeconds = 3f, GraceSeconds = 0.6f, SuppressedSeconds = 3f,
            RoundsPerExposure = 3, BlindRounds = 3, FireCooldownSeconds = 0.3f, BlindAimHeight = 1.0f,
            ExposuresPerPosition = 3,
            HeatPerExposure = 1.0f, HeatWhenFiredUpon = 1.5f,
            BurnHeat = 2.5f, ReuseHeat = 1.0f, HeatPenalty = 0.3f,
            CoolHalfLifeSeconds = 45f, MatchRadius = 1.0f,
            RelocateSpeed = 4.5f, MinRelocateMetres = 4f,
            ScoreDeltaThreshold = 0.05f,
            NoCoverSeconds = 3f,
            HeardMatchRadius = 15f,
        };

        /// <summary>The parameters with every zero replaced by its default (the enums, flags and filter stay as authored).</summary>
        public static PeekAndFireParams Effective(in PeekAndFireParams a)
        {
            var d = Defaults;
            var e = a;
            if (e.HideTemplate == 0) e.HideTemplate = d.HideTemplate;
            if (e.SearchRadius <= 0f) e.SearchRadius = d.SearchRadius;
            if (e.PeekSearchRadius <= 0f) e.PeekSearchRadius = d.PeekSearchRadius;
            if (e.HideSecondsMin <= 0f) e.HideSecondsMin = d.HideSecondsMin;
            if (e.HideSecondsMax < e.HideSecondsMin) e.HideSecondsMax = Math.Max(e.HideSecondsMin, d.HideSecondsMax);
            if (e.ExposeSeconds <= 0f) e.ExposeSeconds = d.ExposeSeconds;
            if (e.GraceSeconds <= 0f) e.GraceSeconds = d.GraceSeconds;
            if (e.SuppressedSeconds <= 0f) e.SuppressedSeconds = d.SuppressedSeconds;
            if (e.RoundsPerExposure <= 0) e.RoundsPerExposure = d.RoundsPerExposure;
            if (e.BlindRounds <= 0) e.BlindRounds = d.BlindRounds;
            if (e.FireCooldownSeconds <= 0f) e.FireCooldownSeconds = d.FireCooldownSeconds;
            if (e.BlindAimHeight <= 0f) e.BlindAimHeight = d.BlindAimHeight;
            if (e.ExposuresPerPosition <= 0) e.ExposuresPerPosition = d.ExposuresPerPosition;
            if (e.HeatPerExposure <= 0f) e.HeatPerExposure = d.HeatPerExposure;
            if (e.HeatWhenFiredUpon <= 0f) e.HeatWhenFiredUpon = d.HeatWhenFiredUpon;
            if (e.BurnHeat <= 0f) e.BurnHeat = d.BurnHeat;
            if (e.ReuseHeat <= 0f) e.ReuseHeat = d.ReuseHeat;
            if (e.HeatPenalty <= 0f) e.HeatPenalty = d.HeatPenalty;
            if (e.CoolHalfLifeSeconds <= 0f) e.CoolHalfLifeSeconds = d.CoolHalfLifeSeconds;
            if (e.MatchRadius <= 0f) e.MatchRadius = d.MatchRadius;
            if (e.RelocateSpeed <= 0f) e.RelocateSpeed = d.RelocateSpeed;
            if (e.MinRelocateMetres <= 0f) e.MinRelocateMetres = d.MinRelocateMetres;
            if (e.ScoreDeltaThreshold <= 0f) e.ScoreDeltaThreshold = d.ScoreDeltaThreshold;
            if (e.NoCoverSeconds <= 0f) e.NoCoverSeconds = d.NoCoverSeconds;
            if (e.HeardMatchRadius <= 0f) e.HeardMatchRadius = d.HeardMatchRadius;
            return e;
        }

        /// <summary>B2–B5's numbers out of the node's parameters.</summary>
        public static HeatRules Rules(in PeekAndFireParams p) => new()
        {
            CoolHalfLifeSeconds = p.CoolHalfLifeSeconds, BurnHeat = p.BurnHeat, ReuseHeat = p.ReuseHeat,
            HeatPenalty = p.HeatPenalty, MatchRadius = p.MatchRadius,
        };

        /// <summary>
        /// Runs the cycle (§8.2) against the unit's top threat. Running while it has one; Success when nothing is remembered;
        /// Failure when the unit cannot move or fire.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus PeekAndFire(ref PeekAndFireParams authored, ref PeekAndFireState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<LocomotionChannel>(self) || !world.HasComponent<WeaponChannel>(self)) return NodeStatus.Failure;
            var p = Effective(in authored);
            double now = Now(world);

            var was = new ThreatAim(ws.Threat, ws.HeardId, ws.HeardPoint);
            if (!EqsTacticsNodes.TopAim(world, self, in was, out var threat))
            {
                Release(ref ws, self, world);
                return NodeStatus.Success;   // nothing left to fight
            }
            KeepSensors(ref ws, in p, self, world, in was, in threat);

            ref var mem = ref UnitMemory.Ref<FiringPositionMemory>(world, self);
            switch (ws.Phase)
            {
                case PeekPhase.Choose:
                    // ⭐ CE-3158 G2 — no cover for NoCoverSeconds: say so, the posture picks something else
                    if (!ChooseOrWait(ref ws, in p, ref mem, self, world, now, in threat))
                    {
                        Release(ref ws, self, world);
                        return NodeStatus.Failure;
                    }
                    break;
                case PeekPhase.MoveToHide: MoveToHide(ref ws, in p, ref mem, self, world, now); break;
                case PeekPhase.Hidden:     Hidden(ref ws, in p, ref mem, self, world, now, in threat); break;
                case PeekPhase.Expose:     Expose(ref ws, in p, ref mem, self, world, now, in threat); break;
                case PeekPhase.Aimed:      Aimed(ref ws, in p, ref mem, self, world, now, in threat); break;
                case PeekPhase.Blind:      Blind(ref ws, in p, ref mem, self, world, now); break;
                case PeekPhase.Recover:    RecoverMove(ref ws, in p, self, world, now); break;
            }

            mem.Phase = (byte)ws.Phase;
            mem.PhaseAt = now;
            mem.PhaseUntil = ws.PhaseUntil;
            mem.HidePoint = ws.HidePoint;
            mem.PeekPoint = ws.PeekPoint;
            return NodeStatus.Running;
        }

        /// <summary>Leaving the node: its sensors go, its fire and its move stop. ⭐ The position memory stays (B1).</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.PeekAndFireNodes.PeekAndFire")]
        public static void Deactivate_PeekAndFire(ref PeekAndFireParams p, ref PeekAndFireState ws, Entity self, EntityRepository world)
            => Release(ref ws, self, world);

        // ── the phases ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ⭐ <c>CE-3158</c> G2 (📄 docs/DESIGN_Peek_And_Fire.md §10.5) — the Choose phase with its honest outcomes: a usable point ⇒ go;
        /// answered points but every one burned ⇒ AIMED FIRE FROM WHERE IT STANDS while they cool (never silent); no answer at all
        /// for <c>NoCoverSeconds</c> ⇒ false (the node answers Failure).
        /// </summary>
        private static bool ChooseOrWait(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, Entity self,
                                         EntityRepository world, double now, in ThreatAim threat)
        {
            if (PickHide(ref ws, in p, ref mem, world, now, relocating: false, out var point, out var stance, out byte kind))
            {
                if (ws.Waiting == 2) PostureNodes.StopFiring(world, self, ref ws.Fire);
                ws.Waiting = 0;
                GoToHide(ref ws, in p, self, world, point, stance, kind);
                return true;
            }
            if (Answered(in ws, world))
            {
                // every point burned (or claimed): fight from here — the aimed step, only at a SEEN enemy (D4)
                ws.Waiting = 2;
                var target = LockTarget(world, self, in threat);
                if (!target.IsNull && SightNow.Sees(world, self, target))
                    PostureNodes.Fire(world, self, ref ws.Fire, p.FireCooldownSeconds, 0, target);
                else PostureNodes.StopFiring(world, self, ref ws.Fire);
                return true;
            }
            if (ws.Waiting != 1) { ws.Waiting = 1; ws.WaitingSince = now; }
            return now - ws.WaitingSince < p.NoCoverSeconds;
        }

        /// <summary>The cover sensor has answered with at least one point.</summary>
        private static bool Answered(in PeekAndFireState ws, EntityRepository world)
        {
            if (!ws.CoverSensor.IsValid || !world.IsAlive(ws.CoverSensor.ChildId)
                || !world.HasComponent<EqsCognitiveBuffer>(ws.CoverSensor.ChildId)) return false;
            ref readonly var buffer = ref world.GetComponentRO<EqsCognitiveBuffer>(ws.CoverSensor.ChildId);
            return buffer.IsReady && buffer.Count > 0;
        }

        private static void Choose(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, Entity self,
                                   EntityRepository world, double now, bool relocating)
        {
            if (!PickHide(ref ws, in p, ref mem, world, now, relocating, out var point, out var stance, out byte kind)) return;   // no answer yet
            GoToHide(ref ws, in p, self, world, point, stance, kind);
        }

        /// <summary>Takes <paramref name="point"/> as the hide point (its stances by the peek mode) and walks there.</summary>
        private static void GoToHide(ref PeekAndFireState ws, in PeekAndFireParams p, Entity self, EntityRepository world,
                                     Vector3 point, StanceId? stance, byte kind)
        {
            ws.PeekIsStep = (byte)(ChoosePeek(in p, stance, kind, StanceRequest.CanCarry(world, self), out var hide, out var peek) ? 1 : 0);
            ws.HideStance = hide;
            ws.PeekStance = peek;
            ws.Waiting = 0;
            ws.HidePoint = point;
            ws.PeekPoint = point;
            ws.ExposuresHere = 0;
            ws.Bounding = 0;
            ws.Moving = (byte)(LocomotionMoveTo.Issue(world, self, point, p.RelocateSpeed, ArrivalRadius) ? 1 : 0);
            ws.Phase = PeekPhase.MoveToHide;
        }

        /// <summary>
        /// ⭐ <c>CE-3158</c> G1 (📄 docs/DESIGN_Peek_And_Fire.md §10.5) — HOW THE UNIT EXPOSES, from the point it hides at: true = a
        /// step peek (hide at <paramref name="hide"/>, the peek stance is chosen at the peek point), false = a stance peek (hide at
        /// <paramref name="hide"/>, come up to <paramref name="peek"/> on the same spot). <paramref name="kind"/> is
        /// <see cref="EqsResult.Kind"/>'s encoding; <paramref name="canStance"/> = the unit carries a stance at all.
        /// </summary>
        internal static bool ChoosePeek(in PeekAndFireParams p, StanceId? stance, byte kind, bool canStance, out StanceId hide, out StanceId peek)
        {
            StanceId? hideOverride = p.HideStanceOverride != 0 ? (StanceId)(p.HideStanceOverride - 1) : null;
            StanceId? peekOverride = p.PeekStanceOverride != 0 ? (StanceId)(p.PeekStanceOverride - 1) : null;
            var mode = p.Mode;
            if (mode == PeekMode.Auto)
            {
                var r = new EqsResult { Kind = kind };
                if (!canStance || !r.TryGetKind(out var k)) mode = PeekMode.Step;   // no stance, or not a cover point: step out
                else if (k == CoverKind.WindowFiring) mode = PeekMode.Stance;       // a window: up at it, at its stance
                else
                {
                    // a cover: hide at ITS stance, come up one taller — a cover already standing has nothing taller ⇒ step out
                    hide = hideOverride ?? stance ?? StanceId.Crouched;
                    if (peekOverride is { } po ? Taller(po, hide) : Taller(StanceId.Standing, hide))
                    {
                        peek = peekOverride ?? OneTaller(hide);
                        return false;
                    }
                    mode = PeekMode.Step;
                }
            }
            if (mode == PeekMode.Step)
            {
                hide = hideOverride ?? stance ?? StanceId.Crouched;
                peek = StanceId.Standing;   // chosen at the peek point
                return true;
            }
            peek = peekOverride ?? stance ?? StanceId.Crouched;   // the window's stance (P-1: crouched at a sill)
            hide = hideOverride ?? StanceId.Prone;
            return false;
        }

        /// <summary>True when <paramref name="a"/> is a taller stance than <paramref name="b"/> (Standing &gt; Crouched &gt; Prone).</summary>
        private static bool Taller(StanceId a, StanceId b) => Height(a) > Height(b);

        private static int Height(StanceId s) => s switch { StanceId.Prone => 0, StanceId.Crouched => 1, _ => 2 };

        private static StanceId OneTaller(StanceId s) => s == StanceId.Prone ? StanceId.Crouched : StanceId.Standing;

        private static void MoveToHide(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, Entity self,
                                       EntityRepository world, double now)
        {
            var status = LocomotionMoveTo.Status(world, self);
            if (status == NodeStatus.Running) return;
            ws.Moving = 0;
            if (status == NodeStatus.Success) { EnterHidden(ref ws, in p, self, world, now); return; }
            // ⭐ CE-3158 G2 — the move failed: the point is (maybe) unreachable — heat it by UnreachableHeat × BurnHeat, so a second
            //   failure burns it and it is never picked again while it stays hot (one failure may be a path not ready yet)
            Unreachable(ref mem, ws.HidePoint, in p, now);
            ws.Phase = PeekPhase.Choose;
        }

        /// <summary>⭐ <c>CE-3158</c> G2 — the share of <c>BurnHeat</c> one failed move adds: two failures burn the point.</summary>
        public const float UnreachableHeat = 0.6f;

        private static void Unreachable(ref FiringPositionMemory mem, Vector3 point, in PeekAndFireParams p, double now)
            => PositionHeat.Add(ref mem, point, UnreachableHeat * p.BurnHeat, now, Rules(in p), exposure: false);

        private static void Hidden(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, Entity self,
                                   EntityRepository world, double now, in ThreatAim threat)
        {
            StanceRequest.Set(world, self, ws.HideStance, StanceBlendSeconds);

            // B4/D8 — a used-up position: move on if there is somewhere else (a burned one with nowhere to go waits to cool)
            bool burned = PositionHeat.IsBurnedAt(ref mem, ws.HidePoint, now, Rules(in p));
            if (ws.Bounding == 0 && (burned || ws.ExposuresHere >= p.ExposuresPerPosition))
            {
                if (p.SuppressBeforeRelocate != 0)
                {
                    // ⭐ P-7 D12 — SUPPRESS AND BOUND: pick the next cover now, but first come up and fire a suppressive burst at the
                    //   freshest evidence (below, through the exposure); the run to the new cover starts when the burst ends (Recover).
                    if (PickHide(ref ws, in p, ref mem, world, now, relocating: true, out var next, out var nextStance, out byte nextKind))
                    {
                        ws.Bounding = 1;
                        ws.NextHide = next;
                        ws.NextStance = nextStance is { } ns ? (byte)((byte)ns + 1) : (byte)0;
                        ws.NextKind = nextKind;
                        ws.PhaseUntil = now;   // no further wait: the bound is the reason to come up
                    }
                    else if (burned) return;   // nowhere to go: a burned spot waits to cool
                }
                else
                {
                    Choose(ref ws, in p, ref mem, self, world, now, relocating: true);   // nothing elsewhere ⇒ changes nothing
                    if (ws.Phase == PeekPhase.MoveToHide || burned) return;
                }
            }

            // B8 — reloading or under fire: stay down (the dispatcher runs the reload; an empty magazine starts one here)
            bool reloading = ReloadingOrEmpty(world, self);
            // ⭐ CE-3158 G3 — the ROE decides whether to come up at all: HoldFire never exposes (hide only); ReturnFire exposes
            //   only inside the return-fire window, and then WITHOUT the random wait (the window is the reason to come up)
            if (!AimAndFireExecutor.RoePermitsFire(world, self)) return;
            bool returning = world.IsComponentTypeRegistered<Roe>() && RoeOf.Fire(world, self) == RoeFire.ReturnFire;
            if (reloading || Suppressed(world, self, in p) || (!returning && now < ws.PhaseUntil)) return;

            if (ws.PeekIsStep == 0)
            {
                ws.PeekPoint = ws.HidePoint;
                StanceRequest.Set(world, self, ws.PeekStance, StanceBlendSeconds);
                BeginExposure(ref ws, in p, ref mem, now);
                ws.Phase = PeekPhase.Expose;
                return;
            }

            if (!PickPeek(ref ws, in p, ref mem, world, now, out var peek, out var peekStance))
            {
                // ⭐ CE-3158 G2 — no step-out point for NoCoverSeconds: this cover cannot be fought from — to the next one
                if (ws.Waiting != 1) { ws.Waiting = 1; ws.WaitingSince = now; }
                else if (now - ws.WaitingSince >= p.NoCoverSeconds)
                {
                    PositionHeat.Add(ref mem, ws.HidePoint, p.BurnHeat, now, Rules(in p), exposure: false);
                    ws.Waiting = 0;
                    ws.Phase = PeekPhase.Choose;
                }
                return;
            }
            ws.Waiting = 0;
            ws.PeekPoint = peek;
            ws.PeekStance = peekStance ?? StanceId.Standing;
            ws.Stepping = 1;
            ws.Counted = 0;
            ws.Moving = (byte)(LocomotionMoveTo.Issue(world, self, peek, p.RelocateSpeed, ArrivalRadius) ? 1 : 0);
            ws.Phase = PeekPhase.Expose;
        }

        private static void Expose(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, Entity self,
                                   EntityRepository world, double now, in ThreatAim threat)
        {
            if (ws.Stepping == 1)
            {
                var status = LocomotionMoveTo.Status(world, self);
                if (status == NodeStatus.Running) return;
                ws.Stepping = 0;
                ws.Moving = 0;
                if (status != NodeStatus.Success) { Recover(ref ws, in p, ref mem, self, world, now, firedUpon: false); return; }
                StanceRequest.Set(world, self, ws.PeekStance, StanceBlendSeconds);
                BeginExposure(ref ws, in p, ref mem, now);
            }

            if (ws.Bounding == 1)
            {
                // ⭐ P-7 D12 — the suppressive burst: no aim time, no grace — at the freshest evidence, seen or not
                if (!StartBurst(ref ws, in p, self, world, in threat)) Recover(ref ws, in p, ref mem, self, world, now, false);
                return;
            }

            var target = LockTarget(world, self, in threat);
            if (!target.IsNull && SightNow.Sees(world, self, target))
            {
                ws.LastSeenAt = now;
                ws.Fire = default;   // a fresh aimed action each exposure (B7's count starts at 0)
                // ⭐ CE-3158 G4 — the target is LOCKED for the exposure: the one whose sight was just checked is the one shot at
                PostureNodes.Fire(world, self, ref ws.Fire, p.FireCooldownSeconds, p.RoundsPerExposure, target);
                ws.Phase = PeekPhase.Aimed;
                return;
            }
            if (now - ws.ExposedAt < p.GraceSeconds) return;

            // 4.2 — not seen in time: a blind burst at the FRESHEST evidence (D13), raised to the body's middle (no aim time)
            if (!StartBurst(ref ws, in p, self, world, in threat)) Recover(ref ws, in p, ref mem, self, world, now, false);
        }

        /// <summary>
        /// ⭐ <c>CE-3158</c> G4 — the target of this exposure: the squad's ASSIGNED target (<see cref="SquadAssignment"/>) when the unit has
        /// one that is alive and seen, else the top threat's entity; <see cref="Entity.Null"/> when the threat is only a heard point.
        /// </summary>
        internal static Entity LockTarget(EntityRepository world, Entity self, in ThreatAim threat)
        {
            if (SquadAssignment.TargetOf(world, self, out long handle))
            {
                var assigned = new Entity((ulong)handle);
                if (world.IsAlive(assigned) && SightNow.Sees(world, self, assigned)) return assigned;
            }
            return threat.IsPoint ? Entity.Null : threat.Entity;
        }

        /// <summary>A burst of <c>BlindRounds</c> at the freshest evidence (D13) + <c>BlindAimHeight</c>; false = nowhere known.</summary>
        private static bool StartBurst(ref PeekAndFireState ws, in PeekAndFireParams p, Entity self, EntityRepository world, in ThreatAim threat)
        {
            if (!FreshestEvidence(world, self, in threat, out var at, out _, p.HeardMatchRadius)) return false;
            ws.Blind = new FireAtPointNodeParams
            {
                Point = at + new Vector3(0f, 0f, p.BlindAimHeight), CooldownSeconds = p.FireCooldownSeconds, Rounds = p.BlindRounds,
            };
            FireAtPointNodes.FireAtPoint(ref ws.Blind, self, world);
            ws.Phase = PeekPhase.Blind;
            return true;
        }

        /// <summary>
        /// ⭐ P-7 D13 — THE FRESHEST EVIDENCE WINS for a burst: the newest (by <see cref="TargetMemory.LastSeenTick"/>) of the threat's
        /// own slot and every HEARD (anonymous) contact. 📐 A shot heard more than ≈ 6 m from the remembered spot makes a NEW anonymous
        /// slot (<c>TargetMemory.HearContact</c>) and the ranking prefers the identified one — without this the burst goes where the
        /// enemy WAS. Ties keep the threat's own slot. ⚠ An aimed shot still needs sight (D4); this is only where a burst goes.
        /// </summary>
        internal static bool FreshestEvidence(EntityRepository world, Entity self, in ThreatAim threat, out Vector3 at)
            => FreshestEvidence(world, self, in threat, out at, out _);

        /// <summary>As above, and <paramref name="slotId"/> = the memory slot the evidence came from (the threat's own id when no
        /// heard slot is newer).</summary>
        internal static unsafe bool FreshestEvidence(EntityRepository world, Entity self, in ThreatAim threat, out Vector3 at, out long slotId,
                                                     float matchRadius = 0f)
        {
            bool have = EqsTacticsNodes.ThreatPosition(world, self, in threat, out at);
            // ⭐ CE-3158 G4 — with a radius, a heard slot counts only near where THIS threat is remembered (another enemy's shot does
            //   not drag the burst away from the locked target); a heard-only threat has no spot of its own to measure from
            var origin = at;
            bool filter = matchRadius > 0f && have && !threat.IsPoint;
            long id = threat.IsPoint ? threat.HeardId : (long)threat.Entity.PackedValue;
            slotId = id;
            if (!world.HasComponent<TargetMemory>(self)) return have;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(self);
            uint newest = 0;
            for (int i = 0; i < mem.Count; i++)
                if (mem.EntityIds[i] == id) { newest = mem.LastSeenTick[i]; break; }
            for (int i = 0; i < mem.Count; i++)
            {
                if (!TargetMemory.IsAnonymous(in mem, i) || mem.LastSeenTick[i] <= newest) continue;
                var heard = new Vector3(mem.PositionsX[i], mem.PositionsY[i], mem.PositionsZ[i]);
                if (filter && Vector3.Distance(heard, origin) > matchRadius) continue;
                newest = mem.LastSeenTick[i];
                at = heard;
                slotId = mem.EntityIds[i];
                have = true;
            }
            return have;
        }

        private static void Aimed(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, Entity self,
                                  EntityRepository world, double now, in ThreatAim threat)
        {
            if (!ws.Fire.Threat.IsNull && SightNow.Sees(world, self, ws.Fire.Threat)) ws.LastSeenAt = now;   // the LOCKED target
            ref readonly var channel = ref world.GetComponentRO<WeaponChannel>(self);
            bool done = channel.ActiveAction == CombatConstants.ActionIdAimAndFire && channel.Status != NodeStatus.Running;
            bool firedUpon = FiredUponSince(world, self, ws.ExposedAt);
            if (done || firedUpon || now - ws.ExposedAt >= p.ExposeSeconds || now - ws.LastSeenAt > p.GraceSeconds)
                Recover(ref ws, in p, ref mem, self, world, now, firedUpon);
        }

        private static void Blind(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, Entity self,
                                  EntityRepository world, double now)
        {
            var status = FireAtPointNodes.FireAtPoint(ref ws.Blind, self, world);
            bool firedUpon = FiredUponSince(world, self, ws.ExposedAt);
            if (status != NodeStatus.Running || firedUpon || now - ws.ExposedAt >= p.ExposeSeconds)
                Recover(ref ws, in p, ref mem, self, world, now, firedUpon);
        }

        private static void RecoverMove(ref PeekAndFireState ws, in PeekAndFireParams p, Entity self, EntityRepository world, double now)
        {
            var status = LocomotionMoveTo.Status(world, self);
            if (status == NodeStatus.Running) return;
            ws.Moving = 0;
            if (status == NodeStatus.Success) EnterHidden(ref ws, in p, self, world, now);
            else ws.Phase = PeekPhase.Choose;
        }

        // ── the steps ───────────────────────────────────────────────────────────────────────────────

        /// <summary>The exposure counts once it is real (up at the window, or arrived at the peek point): a use and B2's heat.</summary>
        private static void BeginExposure(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, double now)
        {
            ws.ExposedAt = now;
            ws.LastSeenAt = 0;   // not seen this exposure (a time before any exposure) — ⛔ never an infinity: the block is dumped as JSON
            if (ws.Counted == 1) return;
            ws.Counted = 1;
            ws.ExposuresHere++;
            ws.Exposures++;
            PositionHeat.Add(ref mem, ws.HidePoint, p.HeatPerExposure, now, Rules(in p), exposure: true);
        }

        /// <summary>The exposure ends: the fire stops, being shot at heats the position more (B2), and the unit goes back down.</summary>
        private static void Recover(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem, Entity self,
                                    EntityRepository world, double now, bool firedUpon)
        {
            PostureNodes.StopFiring(world, self, ref ws.Fire);
            StopBlind(ref ws, self, world);
            if (firedUpon) PositionHeat.Add(ref mem, ws.HidePoint, p.HeatWhenFiredUpon, now, Rules(in p), exposure: false);
            ws.Counted = 0;
            if (ws.Bounding == 1)
            {
                // ⭐ P-7 D12 — the burst is over: RUN to the next cover (never back to the used one)
                GoToHide(ref ws, in p, self, world, ws.NextHide, ws.NextStance != 0 ? (StanceId)(ws.NextStance - 1) : null, ws.NextKind);
                return;
            }
            if (ws.PeekIsStep == 1 && Vector3.Distance(ws.PeekPoint, ws.HidePoint) > ArrivalRadius)
            {
                StanceRequest.Set(world, self, ws.HideStance, StanceBlendSeconds);
                ws.Moving = (byte)(LocomotionMoveTo.Issue(world, self, ws.HidePoint, p.RelocateSpeed, ArrivalRadius) ? 1 : 0);
                ws.Phase = PeekPhase.Recover;
                return;
            }
            EnterHidden(ref ws, in p, self, world, now);
        }

        private static void EnterHidden(ref PeekAndFireState ws, in PeekAndFireParams p, Entity self, EntityRepository world, double now)
        {
            StanceRequest.Set(world, self, ws.HideStance, StanceBlendSeconds);
            var rng = SimRng.FromSim(self.Index, CoverSite ^ (ws.Exposures * 7919), (float)now);
            ws.PhaseUntil = now + p.HideSecondsMin + rng.NextSingle() * (p.HideSecondsMax - p.HideSecondsMin);
            ws.Phase = PeekPhase.Hidden;
        }

        /// <summary>B6 — the best hide point of the cover sensor's answer span by <c>score − heat penalty</c>: burned ones skipped,
        /// and when relocating none closer than <c>MinRelocateMetres</c> to the current one.</summary>
        private static unsafe bool PickHide(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem,
                                            EntityRepository world, double now, bool relocating, out Vector3 point, out StanceId? stance,
                                            out byte kind)
        {
            point = default; stance = null; kind = 0;
            if (!ws.CoverSensor.IsValid || !world.IsAlive(ws.CoverSensor.ChildId)
                || !world.HasComponent<EqsCognitiveBuffer>(ws.CoverSensor.ChildId)) return false;
            ref readonly var buffer = ref world.GetComponentRO<EqsCognitiveBuffer>(ws.CoverSensor.ChildId);
            if (!buffer.IsReady || buffer.Count == 0) return false;

            var span = buffer.GetSpanRO()[..buffer.Count];   // ⭐ CE-3144 — the span is all 16 slots; past Count they are (0,0,0) score 0
            float best = float.NegativeInfinity;
            for (int i = 0; i < span.Length; i++)
            {
                var r = span[i];
                var at = new Vector3(r.PositionX, r.PositionY, r.PositionZ);
                if (relocating && Vector3.Distance(at, ws.HidePoint) < p.MinRelocateMetres) continue;
                float score = r.Score - PositionHeat.Penalty(ref mem, at, now, Rules(in p));
                if (score <= best) continue;   // ties keep the first: the sensor's order, deterministic
                best = score;
                point = at;
                stance = r.TryGetStance(out var s) ? s : null;
                kind = r.Kind;
            }
            return !float.IsNegativeInfinity(best);
        }

        /// <summary>The step-out point: the peek sensor's best answer within <c>PeekSearchRadius</c> of the hide point.</summary>
        private static bool PickPeek(ref PeekAndFireState ws, in PeekAndFireParams p, ref FiringPositionMemory mem,
                                     EntityRepository world, double now, out Vector3 point, out StanceId? stance)
        {
            point = default; stance = null;
            if (!ws.PeekSensor.IsValid || !world.IsAlive(ws.PeekSensor.ChildId)
                || !world.HasComponent<EqsCognitiveBuffer>(ws.PeekSensor.ChildId)) return false;
            ref readonly var buffer = ref world.GetComponentRO<EqsCognitiveBuffer>(ws.PeekSensor.ChildId);
            if (!buffer.IsReady || buffer.Count == 0) return false;
            var span = buffer.GetSpanRO()[..buffer.Count];   // ⭐ CE-3144 — the span is all 16 slots; past Count they are (0,0,0) score 0
            for (int i = 0; i < span.Length; i++)
            {
                var r = span[i];
                var at = new Vector3(r.PositionX, r.PositionY, r.PositionZ);
                if (Vector3.Distance(at, ws.HidePoint) > p.PeekSearchRadius + ArrivalRadius) continue;
                point = at;
                stance = r.TryGetStance(out var s) ? s : null;
                return true;   // the sensor's order is its ranking
            }
            return false;
        }

        /// <summary>The cover sensor always; the step-peek sensor once a step peek is chosen — both pointed at the threat.</summary>
        private static void KeepSensors(ref PeekAndFireState ws, in PeekAndFireParams p, Entity self, EntityRepository world,
                                        in ThreatAim was, in ThreatAim threat)
        {
            // ⭐ CE-3144 (P-8 D14) — the sensors look from and for where the enemy is REMEMBERED (the freshest evidence, D13), never
            //   where he is now: a hidden enemy's true position is seen from nowhere, so both soldiers' searches answered nothing and
            //   the duel stood still (measured live). Re-pointed when that spot moves by RepointMetres or another slot becomes freshest.
            var aim = SensorAim(world, self, in threat);
            bool retarget = ws.SensorId == 0 || EqsTacticsNodes.Moved(new ThreatAim(Entity.Null, ws.SensorId, ws.SensorAt), in aim);
            var cover = new PostureSensorsParams { SearchRadius = p.SearchRadius, ScoreDeltaThreshold = p.ScoreDeltaThreshold, FactionFilter = p.FactionFilter };
            ws.CoverSensor = PostureNodes.Keep(ws.CoverSensor, world, self, CoverSite, p.HideTemplate, in cover, in aim, retarget);
            if (ws.PeekIsStep == 1)
            {
                var peek = cover;
                peek.SearchRadius = p.PeekSearchRadius;
                ws.PeekSensor = PostureNodes.Keep(ws.PeekSensor, world, self, PeekSite, FindOpenFiringPosition.BlueprintId, in peek, in aim, retarget);
            }
            if (retarget) { ws.SensorId = aim.HeardId; ws.SensorAt = aim.Point; }
            retarget = EqsTacticsNodes.Moved(in was, in threat);
            if (retarget || ws.HeardId != threat.HeardId) ws.HeardPoint = threat.Point;
            ws.Threat = threat.Entity;
            ws.HeardId = threat.HeardId;
        }

        /// <summary>
        /// ⭐ <c>CE-3144</c> (P-8 D14) — the sensors' aim: the freshest evidence (<c>FreshestEvidence</c>) as a POINT, keyed by
        /// the memory slot it came from (the threat's own, or a heard one). Falls back to <paramref name="threat"/> when nothing is
        /// remembered as a spot.
        /// </summary>
        internal static ThreatAim SensorAim(EntityRepository world, Entity self, in ThreatAim threat)
            => FreshestEvidence(world, self, in threat, out var at, out long id) && id != 0 ? new ThreatAim(Entity.Null, id, at) : threat;

        /// <summary>B8 — the unit's weapon is reloading, or its magazine is empty (then the reload starts here).</summary>
        private static bool ReloadingOrEmpty(EntityRepository world, Entity self)
        {
            if (!world.HasComponent<WeaponState>(self)) return false;
            ref var weapon = ref world.GetComponentRW<WeaponState>(self);
            if (Magazine.Reloading(weapon)) return true;
            if (weapon.MagazineSize > 0 && weapon.MagazineRounds == 0 && weapon.Ammo > 0) { Magazine.StartIfEmpty(ref weapon); return true; }
            return false;
        }

        /// <summary>B8/D12 — near-missed or hit within <c>SuppressedSeconds</c>.</summary>
        private static bool Suppressed(EntityRepository world, Entity self, in PeekAndFireParams p)
            => RecentSensesOf.Within(world, self, SensorChange.NearMiss, p.SuppressedSeconds)
            || RecentSensesOf.Within(world, self, SensorChange.Hit, p.SuppressedSeconds);

        /// <summary>B2 — near-missed or hit since the unit came up.</summary>
        private static bool FiredUponSince(EntityRepository world, Entity self, double since)
        {
            double window = Now(world) - since + 1e-6;
            return RecentSensesOf.WithinSince(world, self, SensorChange.NearMiss, window, since)
                || RecentSensesOf.WithinSince(world, self, SensorChange.Hit, window, since);
        }

        private static void StopBlind(ref PeekAndFireState ws, Entity self, EntityRepository world)
        {
            if (ws.Blind.Started != 0 && world.HasComponent<WeaponChannel>(self))
            {
                ref var channel = ref world.GetComponentRW<WeaponChannel>(self);
                if (channel.ActiveAction == CombatConstants.ActionIdFireAtPoint && channel.ActionInstanceId == ws.Blind.Started)
                {
                    channel.ActiveAction = 0;
                    unchecked { channel.ActionInstanceId++; if (channel.ActionInstanceId == 0) channel.ActionInstanceId = 1; }
                }
            }
            ws.Blind = default;
        }

        private static void Release(ref PeekAndFireState ws, Entity self, EntityRepository world)
        {
            PostureNodes.StopFiring(world, self, ref ws.Fire);
            StopBlind(ref ws, self, world);
            PostureNodes.Drop(world, ws.CoverSensor);
            PostureNodes.Drop(world, ws.PeekSensor);
            if (ws.Moving == 1) LocomotionMoveTo.Stop(world, self);
            ws = default;
        }

        private static double Now(EntityRepository world)
            => world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
    }
}
